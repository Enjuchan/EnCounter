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
    private readonly HonorificBridge honorific;
    private readonly ChatWatcher watcher;
    private readonly EmoteWatcher emotes;
    private readonly TitlePresenter presenter;
    private readonly ConfigWindow window;

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        if (config.EnsureDefaults())
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

        window = new ConfigWindow(config, counters, Data, () => PluginInterface.SavePluginConfig(config));

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
            Chat.PrintError($"[EnCounter] Honorific wird benoetigt: {honorific.LastError}");
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

        foreach (var regel in config.Rules)
        {
            if (!RuleMatcher.Matches(regel, e, eigenerName))
                continue;

            var schluessel = new CounterKey(e.SenderName, e.SenderWorldId);
            var persoenlich = counters.Increment(regel.Name, schluessel);
            var gesamt = counters.GetTotal(regel.Name);

            presenter.Show(
                TitleTemplate.Build(regel, e.SenderName, persoenlich, gesamt),
                regel);

            config.FromStore(counters);
            PluginInterface.SavePluginConfig(config);
            return;   // eine Meldung je Ereignis, die erste passende gewinnt
        }
    }

    /// <summary>
    /// Ein Emote wurde ausgeloest. Nur Emotes anderer, die auf uns zielen, zaehlen.
    /// </summary>
    private void HandleEmote(uint urheber, uint ziel, uint emoteId)
    {
        if (!PlayerState.IsLoaded)
            return;

        var eigene = PlayerState.EntityId;

        if (urheber == eigene || ziel != eigene)
            return;

        var (name, welt) = Spieler(urheber);
        if (string.IsNullOrEmpty(name))
            return;

        Handle(new IncomingEvent(
            SenderName: name,
            SenderWorldId: welt,
            Channel: ChatChannel.StandardEmote,
            TargetedAtMe: true,
            FromSelf: false,
            Text: string.Empty,
            EmoteId: emoteId));
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

    private void Tick(IFramework _) => presenter.Tick();

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

        config.FromStore(counters);
        PluginInterface.SavePluginConfig(config);
    }
}
