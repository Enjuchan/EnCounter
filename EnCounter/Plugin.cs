using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using EnCounter.Core;
using EnCounter.Ui;

namespace EnCounter;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IChatGui Chat { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider Interop { get; private set; } = null!;
    [PluginService] internal static IObjectTable Objects { get; private set; } = null!;
    [PluginService] internal static ICommandManager Commands { get; private set; } = null!;
    [PluginService] internal static IDataManager Data { get; private set; } = null!;
    [PluginService] internal static ITargetManager Targets { get; private set; } = null!;
    [PluginService] internal static ITextureProvider Textures { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;

    private readonly Configuration config;
    private readonly CounterStore counters;
    private readonly CooldownGate cooldowns = new();
    private readonly HonorificBridge honorific;
    private readonly ChatWatcher watcher;
    private readonly EmoteWatcher emotes;
    private readonly TitlePresenter presenter;
    private const string Command = "/encounter";

    private readonly ConfigWindow window;
    private WelcomeWindow welcomeWindow = null!;
    private ChangelogWindow changelogWindow = null!;
    private readonly Welcome welcome;
    private readonly WindowSystem windows = new("EnCounter");

    /// <summary>Das zuletzt gezählte Ereignis; null, bis etwas gezählt wurde.</summary>
    private LastEvent? letztes;

    /// <summary>
    /// Es liegt etwas an, das noch nicht auf der Platte steht.
    ///
    /// Frueher wurde bei jedem einzelnen Treffer geschrieben - bei einem Makro, das im
    /// Sekundentakt feuert, ebenso wie beim Ziehen eines Reglers in der Oberflaeche.
    /// Gesammelt wird deshalb und im Puls geschrieben.
    /// </summary>
    private bool ungespeichert;
    private DateTime naechstesSpeichern = DateTime.MinValue;
    private static readonly TimeSpan Speicherabstand = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Schreibt jeden Schritt der Emote-Verarbeitung in Log und Chat.
    ///
    /// Fuer den Fall, dass ein Emote bei einer bestimmten Person nicht zaehlt und bei
    /// anderen schon: Nur so ist zu sehen, ob das Paket ankommt, welche Ziel-Kennung
    /// darin steht, ob die Objekttabelle sie aufloest und welche Regel greift.
    ///
    /// Bewusst nicht dauerhaft an - im Gebrauch waere es Laerm.
    /// </summary>
    private bool diagnose;


    public Plugin()
    {
        // ECommons braucht das für DalamudReflector, das der Dependencies-Tab zum Installieren nutzt.
        ECommons.ECommonsMain.Init(PluginInterface, this);

        // Null heißt: Dalamud hatte nie eine Konfiguration dieses Plugins, also eine Neuinstallation.
        var savedConfig = PluginInterface.GetPluginConfig();
        config = savedConfig as Configuration ?? new Configuration();
        Loc.Init(PluginInterface, config.Language);

        // Reihenfolge zaehlt: Erst Regeln anlegen, damit auch die frischen eine Id
        // bekommen, dann wandern, dann laden. Nach Migrate liegen alle Zaehler unter
        // Ids - der laufende Betrieb kennt den alten Fall gar nicht mehr.
        var neuAngelegt = config.EnsureDefaults();
        var gewandert = config.Migrate();

        if (neuAngelegt || gewandert)
            PluginInterface.SavePluginConfig(config);

        counters = config.ToStore();

        honorific = new HonorificBridge(PluginInterface);
        presenter = new TitlePresenter(honorific);

        watcher = new ChatWatcher(Chat);
        watcher.OnEvent += Handle;

        // Emotes kommen nicht zuverlaessig ueber den Chat - der Absender entscheidet,
        // ob eine Meldung erzeugt wird. Deshalb direkt an der Paketverarbeitung.
        emotes = new EmoteWatcher(Interop);
        emotes.OnEmote += HandleEmote;

        // Der Ordner mit allen Plugin-Konfigurationen - dort liegen auch die von
        // PatMe und PatMeHonorific, aus denen sich importieren laesst.
        var konfigOrdner = PluginInterface.ConfigFile.Directory?.FullName ?? string.Empty;

        // Die ContentId trennt die eigenen Charaktere - PatMe fuehrt seine Staende so.
        Ui.Widgets.Init(Textures);
        var version = PluginInterface.Manifest.AssemblyVersion.ToString(3);
        var dependencies = new Dependencies(PluginInterface, Log, EnCounterDependencies.Groups);
        window = new ConfigWindow(config, counters, cooldowns, Data, konfigOrdner,
                                  () => PlayerState.IsLoaded ? PlayerState.ContentId : 0ul,
                                  AktuellesZiel,
                                  MerkeAenderung,
                                  () => letztes,
                                  () => diagnose,
                                  SetzeDiagnose,
                                  honorific,
                                  presenter,
                                  version,
                                  dependencies,
                                  EnCounterAbout.Create(version),
                                  () => welcomeWindow.IsOpen = true,
                                  () => changelogWindow.IsOpen = true,
                                  Log);
        windows.AddWindow(window);

        // Gespeichert wird über den gesammelten Weg, weil er die Zähler mitschreibt.
        var saveLanguage = new Action<string>(code =>
        {
            config.Language = code;
            MerkeAenderung();
        });
        welcome = new Welcome(ClientState, PluginInterface.Manifest.AssemblyVersion.ToString(), savedConfig is not null,
                              config.LastSeenVersion, seen =>
        {
            config.LastSeenVersion = seen;
            MerkeAenderung();
        });
        changelogWindow = new ChangelogWindow("EnCounter", EnCounterChangelog.Entries, welcome.IsNew, saveLanguage);
        welcomeWindow = new WelcomeWindow(EnCounterWelcome.Create(version, window.DrawWelcomeSettings),
                                          dependencies,
                                          () => window.IsOpen = true, () => changelogWindow.IsOpen = true, saveLanguage);
        windows.AddWindow(welcomeWindow);
        windows.AddWindow(changelogWindow);

        PluginInterface.UiBuilder.Draw += Draw;

        // Dalamud kennt zwei Einstiege: das Zahnrad fuer Einstellungen und den Knopf
        // fuer das Hauptfenster. Bei EnCounter ist beides dasselbe - die
        // Regelverwaltung ist der einzige Einstieg, den es gibt. Ohne OpenMainUi
        // mahnt der Plugin-Installer das an.
        PluginInterface.UiBuilder.OpenConfigUi += Oeffnen;
        PluginInterface.UiBuilder.OpenMainUi += Oeffnen;

        // Dalamud übernimmt den Hilfetext bei der Anmeldung, deshalb wird der Befehl beim Sprachwechsel neu angemeldet.
        RegisterCommand();
        Loc.Changed += RegisterCommand;

        Framework.Update += Tick;

        welcome.Start(() => welcomeWindow.IsOpen = true, () => changelogWindow.IsOpen = true);

        // Anderen Plugins - etwa einem eigenen Titel-Wechsler - mitteilen, wann
        // EnCounter den Titel belegt.
        PluginInterface.GetIpcProvider<bool>("EnCounter.IsShowing")
                       .RegisterFunc(() => presenter.IsShowing);

        if (!honorific.IsAvailable)
        {
            Chat.PrintError($"[EnCounter] {Loc.T($"Honorific is required: {honorific.LastError}", $"Honorific wird benötigt: {honorific.LastError}")}");
        }
        else
        {
            var namen = string.Join(", ", config.Rules.Where(r => r.Enabled).Select(r => r.Name));
            Log.Information($"EnCounter bereit. Aktive Regeln: {namen}");
        }
    }

    private void RegisterCommand()
    {
        Commands.RemoveHandler(Command);
        Commands.AddHandler(Command, new Dalamud.Game.Command.CommandInfo(AufBefehl)
        {
            HelpMessage = $"{EnCounterAbout.ToggleEffect} " +
                          Loc.T("/encounter debug turns diagnostics on or off.", "/encounter debug schaltet die Diagnose ein oder aus."),
        });
    }

    private void Handle(IncomingEvent e)
    {
        // IClientState.LocalPlayer gibt es nicht mehr - der Name kommt aus
        // IPlayerState und ist dort schon eine Zeichenkette.
        if (!PlayerState.IsLoaded)
            return;

        if (config.Paused)
        {
            if (diagnose)
                Melde(new("  dropped: counting is paused", "  verworfen: Pause ist an"));

            return;
        }

        var eigenerName = Vorname(PlayerState.CharacterName);
        if (string.IsNullOrEmpty(eigenerName))
            return;

        var passende = config.Rules.Where(r => RuleMatcher.Matches(r, e, eigenerName)).ToList();

        if (diagnose)
        {
            var regelnamen = string.Join(", ", passende.Select(r => r.Name));
            Melde(passende.Count == 0
                ? new("  no rule matches", "  keine Regel passt")
                : new($"  matching rules: {regelnamen}", $"  passende Regeln: {regelnamen}"));
        }

        if (passende.Count == 0)
            return;

        var person = new CounterKey(e.SenderName, e.SenderWorldId);
        var jetzt = DateTime.UtcNow;

        // Der Zaehler gehoert dem Ereignis, nicht der Regel: Ein Dote landet unter
        // `e:146`, gleich wie viele Regeln ihn aufgreifen. Deshalb wird je Schluessel
        // **einmal** erhoeht - sonst zaehlte eine allgemeine und eine persoenliche
        // Regel denselben Dote doppelt.
        var schluessel = passende
            .Select(r => CounterScope.ForEvent(r, e))
            .Distinct()
            .ToList();

        var gezaehlt = new HashSet<string>();

        foreach (var s in schluessel)
        {
            // Der kuerzeste Cooldown unter den beteiligten Regeln gewinnt - eine Regel
            // mit langer Sperre darf eine mit kurzer nicht ausbremsen.
            var sperre = passende
                .Where(r => CounterScope.ForEvent(r, e) == s)
                .Min(r => r.CooldownSeconds);

            if (!cooldowns.Allow(s, person, sperre, jetzt))
            {
                if (diagnose)
                    Melde(new($"  {s} throttled: the same person counts again after {sperre}s",
                              $"  {s} gebremst: dieselbe Person zählt erst nach {sperre} s wieder"));

                continue;
            }

            // Staende aus der Zeit, als nur der Vorname gespeichert wurde.
            counters.MergeShortName(s, person);
            var stand = counters.Increment(s, person);
            gezaehlt.Add(s);

            if (diagnose)
                Melde(new($"  counted under {s}: {person.Name} is now at {stand}",
                          $"  gezählt unter {s}: {person.Name} steht jetzt bei {stand}"));
        }

        if (gezaehlt.Count == 0)
            return;

        MerkeAenderung();

        // Angezeigt wird nur eine Meldung - die der **genauesten** Regel, die
        // tatsaechlich gezaehlt hat. Eine Regel nur fuer eine bestimmte Person
        // gewinnt damit gegen die allgemeine, die dasselbe Emote abdeckt.
        // Siehe RulePriority.
        var zeigende = RulePriority.Winner(
            passende.Where(r => gezaehlt.Contains(CounterScope.ForEvent(r, e))));

        if (zeigende is null)
            return;

        var auswahl = CounterScope.ForRule(zeigende, e.Outgoing);

        var personalStand = counters.GetPersonal(auswahl, person);

        letztes = new LastEvent(
            Person: string.IsNullOrEmpty(e.SenderWorld) ? e.SenderName : $"{e.SenderName}@{e.SenderWorld}",
            EventName: IstEmote(e.Channel) ? EmoteCatalog.NameOf(Data, e.EmoteId) : $"Chat ({e.Channel})",
            Count: personalStand,
            RuleName: zeigende.Name,
            Time: jetzt);

        presenter.Show(
            TitleTemplate.Build(zeigende, e.SenderName, personalStand,
                                counters.GetTotal(auswahl),
                                e.Outgoing),
            zeigende);
    }

    private static bool IstEmote(ChatChannel kanal) =>
        kanal is ChatChannel.StandardEmote or ChatChannel.CustomEmote;

    /// <summary>Es hat sich etwas geändert; geschrieben wird im nächsten Puls.</summary>
    private void MerkeAenderung() => ungespeichert = true;

    private void SpeichereJetzt()
    {
        config.FromStore(counters);
        PluginInterface.SavePluginConfig(config);

        ungespeichert = false;
        naechstesSpeichern = DateTime.UtcNow + Speicherabstand;
    }

    /// <summary>
    /// Ein Emote wurde ausgeloest.
    ///
    /// Der Hook sieht beide Richtungen: was andere auf uns richten und was wir auf
    /// andere richten. Welche davon zaehlt, entscheidet die Regel. Gezaehlt wird in
    /// beiden Faellen die **andere** Person - ein Zaehler auf den eigenen Namen waere
    /// sinnlos.
    ///
    /// Ungerichtete Emotes bleiben aussen vor: Wer neben uns tanzt, meint niemanden,
    /// und ohne Gegenueber gibt es keinen Schluessel zum Zaehlen.
    /// </summary>
    private void HandleEmote(uint urheber, uint ziel, uint emoteId)
    {
        if (!PlayerState.IsLoaded)
            return;

        var eigene = PlayerState.EntityId;
        if (eigene == 0)
            return;

        var ausgehend = urheber == eigene;
        var andere = ausgehend ? ziel : urheber;

        // Dasselbe Emote kommt je nach Groesse des Gegenuebers unter verschiedenen
        // Nummern an - siehe EmoteVariants. Ohne diese Ruecksetzung zaehlt eine Regel
        // bei manchen Personen und bei anderen nicht.
        var roh = emoteId;
        emoteId = EmoteVariants.Normalise(Data, emoteId);

        if (diagnose)
        {
            var variante = roh != emoteId ? new LocText($" (variant of {emoteId})", $" (Variante von {emoteId})") : new LocText(string.Empty, string.Empty);

            Melde(new($"emote {roh}{variante.En}: from {urheber}, to {ziel}, I am {eigene} " +
                      $"({(ausgehend ? "outgoing" : "incoming")})",
                      $"Emote {roh}{variante.De}: von {urheber}, an {ziel}, ich bin {eigene} " +
                      $"({(ausgehend ? "ausgehend" : "empfangen")})"));
        }

        if (andere == 0 || andere == eigene)
        {
            if (diagnose)
                Melde(andere == 0
                    ? new("  dropped: no target - the emote was aimed at nobody", "  verworfen: kein Gegenüber - das Emote war auf niemanden gerichtet")
                    : new("  dropped: the target is myself", "  verworfen: Gegenüber bin ich selbst"));

            return;
        }

        // Fremde Emotes auf Dritte gehen uns nichts an.
        if (!ausgehend && ziel != eigene)
        {
            if (diagnose)
                Melde(new("  dropped: aimed at someone else", "  verworfen: galt jemand anderem"));

            return;
        }

        var (name, welt) = Spieler(andere);

        if (string.IsNullOrEmpty(name))
        {
            if (diagnose)
                Melde(new($"  dropped: ID {andere} is not in the object table", $"  verworfen: Kennung {andere} ist in der Objekttabelle nicht zu finden"));

            return;
        }

        if (diagnose)
            Melde(new($"  recognised as {name}@{WorldCatalog.NameOf(Data, welt)}", $"  erkannt als {name}@{WorldCatalog.NameOf(Data, welt)}"));

        Handle(new IncomingEvent(
            SenderName: name,
            SenderWorldId: welt,
            Channel: ChatChannel.StandardEmote,
            TargetedAtMe: !ausgehend,
            FromSelf: ausgehend,
            Text: string.Empty,
            EmoteId: emoteId,
            Outgoing: ausgehend,
            SenderWorld: WorldCatalog.NameOf(Data, welt)));
    }

    /// <summary>
    /// Das gerade anvisierte Ziel als `Vorname@Welt`, oder null.
    ///
    /// Nur Spieler: Ein anvisiertes Mobil oder ein NSC kann keine Regel auslösen, und
    /// dessen Namen anzubieten wäre nur irreführend.
    /// </summary>
    private static string? AktuellesZiel()
    {
        if (Targets.Target is not Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter spieler)
            return null;

        var name = spieler.Name.TextValue;
        if (string.IsNullOrEmpty(name))
            return null;

        var welt = WorldCatalog.NameOf(Data, spieler.HomeWorld.RowId);

        return string.IsNullOrEmpty(welt) ? name : $"{name}@{welt}";
    }

    /// <summary>Name und Heimatwelt zu einer EntityId aus der Objektliste.</summary>
    private static (string Name, uint World) Spieler(uint entityId)
    {
        foreach (var o in Objects)
        {
            if (o.EntityId != entityId)
                continue;

            if (o is Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter spieler)
                return (spieler.Name.TextValue, spieler.HomeWorld.RowId);

            return (o.Name.TextValue, 0u);
        }

        return (string.Empty, 0u);
    }

    private void Draw()
    {
        // Im Ladebildschirm wird nichts gezeichnet. Visible bleibt unberührt, damit das Fenster danach von selbst wiederkommt.
        // Eine Prüfung auf den Login fehlt mit Absicht: Das Fenster ist zugleich das Einstellungsfenster und soll
        // vom Titelbildschirm aus über den Plugin-Installer erreichbar sein.
        if (Condition[ConditionFlag.BetweenAreas] || Condition[ConditionFlag.BetweenAreas51])
            return;

        windows.Draw();
    }

    private void Oeffnen() => window.Toggle();

    private void AufBefehl(string _, string argumente)
    {
        if (!argumente.Trim().Equals("debug", StringComparison.OrdinalIgnoreCase))
        {
            Oeffnen();
            return;
        }

        SetzeDiagnose(!diagnose);
    }

    /// <summary>Gemeinsamer Weg für Befehl und Oberfläche, damit beide dieselbe Meldung auslösen.</summary>
    private void SetzeDiagnose(bool an)
    {
        diagnose = an;

        Chat.Print(diagnose
            ? $"[EnCounter] {Loc.T("Diagnostics on. Every emote is logged until you turn it off.", "Diagnose an. Jedes Emote wird protokolliert, bis du sie ausschaltest.")}"
            : $"[EnCounter] {Loc.T("Diagnostics off.", "Diagnose aus.")}");
    }

    /// <summary>Eine Zeile Diagnose, in Log und Chat.</summary>
    private void Melde(LocText text)
    {
        Log.Information($"[EnCounter/diag] {text.En}");
        Chat.Print($"[EnCounter] {text}");
    }

    private void Tick(IFramework _)
    {
        presenter.Tick();

        if (ungespeichert && DateTime.UtcNow >= naechstesSpeichern)
            SpeichereJetzt();
    }

    private static string Vorname(string? voll)
    {
        if (string.IsNullOrEmpty(voll))
            return string.Empty;

        var leer = voll.IndexOf(' ');
        return leer > 0 ? voll[..leer] : voll;
    }

    public void Dispose()
    {
        welcome.Dispose();
        Loc.Changed -= RegisterCommand;
        Loc.Dispose();
        Commands.RemoveHandler(Command);
        windows.RemoveAllWindows();
        PluginInterface.UiBuilder.Draw -= Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= Oeffnen;
        PluginInterface.UiBuilder.OpenMainUi -= Oeffnen;

        Framework.Update -= Tick;
        watcher.OnEvent -= Handle;
        watcher.Dispose();

        emotes.OnEmote -= HandleEmote;
        emotes.Dispose();
        presenter.Clear();

        // Beim Abschalten immer schreiben, egal wie kurz der letzte Puls her ist.
        SpeichereJetzt();
        ECommons.ECommonsMain.Dispose();
    }
}
