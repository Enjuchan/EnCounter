using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using EnCounter.Core;

namespace EnCounter;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IChatGui Chat { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider Interop { get; private set; } = null!;
    [PluginService] internal static IObjectTable Objects { get; private set; } = null!;
    [PluginService] internal static ICommandManager Commands { get; private set; } = null!;
    [PluginService] internal static IDataManager Data { get; private set; } = null!;

    private readonly Configuration config;
    private readonly CounterStore counters;
    private readonly CooldownGate cooldowns = new();
    private readonly HonorificBridge honorific;
    private readonly ChatWatcher watcher;
    private readonly EmoteWatcher emotes;
    private readonly TitlePresenter presenter;
    private readonly ConfigWindow window;

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

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

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
        window = new ConfigWindow(config, counters, Data, konfigOrdner,
                                  () => PlayerState.IsLoaded ? PlayerState.ContentId : 0ul,
                                  MerkeAenderung);

        PluginInterface.UiBuilder.Draw += window.Draw;

        // Dalamud kennt zwei Einstiege: das Zahnrad fuer Einstellungen und den Knopf
        // fuer das Hauptfenster. Bei EnCounter ist beides dasselbe - die
        // Regelverwaltung ist der einzige Einstieg, den es gibt. Ohne OpenMainUi
        // mahnt der Plugin-Installer das an.
        PluginInterface.UiBuilder.OpenConfigUi += Oeffnen;
        PluginInterface.UiBuilder.OpenMainUi += Oeffnen;

        Commands.AddHandler("/encounter", new Dalamud.Game.Command.CommandInfo((_, _) => Oeffnen())
        {
            HelpMessage = "Opens the EnCounter settings.",
        });

        Framework.Update += Tick;

        // Anderen Plugins - etwa einem eigenen Titel-Wechsler - mitteilen, wann
        // EnCounter den Titel belegt.
        PluginInterface.GetIpcProvider<bool>("EnCounter.IsShowing")
                       .RegisterFunc(() => presenter.IsShowing);

        if (!honorific.IsAvailable)
        {
            Chat.PrintError($"[EnCounter] Honorific is required: {honorific.LastError}");
        }
        else
        {
            var namen = string.Join(", ", config.Rules.Where(r => r.Enabled).Select(r => r.Name));
            Log.Information($"EnCounter bereit. Aktive Regeln: {namen}");
        }
    }

    private void Handle(IncomingEvent e)
    {
        // IClientState.LocalPlayer gibt es nicht mehr - der Name kommt aus
        // IPlayerState und ist dort schon eine Zeichenkette.
        if (!PlayerState.IsLoaded)
            return;

        var eigenerName = Vorname(PlayerState.CharacterName);
        if (string.IsNullOrEmpty(eigenerName))
            return;

        var passende = config.Rules.Where(r => RuleMatcher.Matches(r, e, eigenerName)).ToList();
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
                continue;

            counters.Increment(s, person);
            gezaehlt.Add(s);
        }

        if (gezaehlt.Count == 0)
            return;

        MerkeAenderung();

        // Angezeigt wird nur eine Meldung: die der ersten Regel, die tatsaechlich
        // gezaehlt hat. Was sie zeigt, liest sie aus dem gemeinsamen Stand - eine
        // neue Regel kennt damit sofort, was vor ihr gezaehlt wurde.
        var zeigende = passende.FirstOrDefault(r => gezaehlt.Contains(CounterScope.ForEvent(r, e)));
        if (zeigende is null)
            return;

        var auswahl = CounterScope.ForRule(zeigende, e.Outgoing);

        presenter.Show(
            TitleTemplate.Build(zeigende, e.SenderName,
                                counters.GetPersonal(auswahl, person),
                                counters.GetTotal(auswahl),
                                e.Outgoing),
            zeigende);
    }

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

        if (andere == 0 || andere == eigene)
            return;

        // Fremde Emotes auf Dritte gehen uns nichts an.
        if (!ausgehend && ziel != eigene)
            return;

        var (name, welt) = Spieler(andere);
        if (string.IsNullOrEmpty(name))
            return;

        Handle(new IncomingEvent(
            SenderName: name,
            SenderWorldId: welt,
            Channel: ChatChannel.StandardEmote,
            TargetedAtMe: !ausgehend,
            FromSelf: ausgehend,
            Text: string.Empty,
            EmoteId: emoteId,
            Outgoing: ausgehend));
    }

    /// <summary>Name und Heimatwelt zu einer EntityId aus der Objektliste.</summary>
    private static (string Name, uint World) Spieler(uint entityId)
    {
        foreach (var o in Objects)
        {
            if (o.EntityId != entityId)
                continue;

            if (o is Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter spieler)
                return (Vorname(spieler.Name.TextValue), spieler.HomeWorld.RowId);

            return (Vorname(o.Name.TextValue), 0u);
        }

        return (string.Empty, 0u);
    }

    private void Oeffnen() => window.Visible = !window.Visible;

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
        Commands.RemoveHandler("/encounter");
        PluginInterface.UiBuilder.Draw -= window.Draw;
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
    }
}
