using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using EnCounter.Core;
using EnCounter.Ui;

namespace EnCounter;

/// <summary>
/// Regeln verwalten.
///
/// Zweispaltig: links die Regeln, rechts der Editor der ausgewählten. Beides
/// gleichzeitig sichtbar, damit man beim Vergleichen nicht springen muss.
///
/// **Leitgedanke der Aufteilung:** Eine Regel hört entweder auf Emotes oder auf Chat.
/// Das stand früher nur implizit in den Kanalhäkchen, und die Oberfläche wechselte
/// unerklärlich ihr Aussehen. Jetzt ist es die erste, bewusste Entscheidung; alles
/// Weitere richtet sich danach.
///
/// Die Bindings heißen `Dalamud.Bindings.ImGui`, nicht ImGuiNET - Beispiele aus dem
/// Netz passen meist nicht.
/// </summary>
public sealed partial class ConfigWindow : Window
{
    private readonly Configuration config;
    private readonly CounterStore counters;
    private readonly CooldownGate cooldowns;
    private readonly IDataManager data;
    private readonly string konfigOrdner;
    private readonly Func<ulong> eigeneCid;
    private readonly Func<string?> aktuellesZiel;
    private readonly Action save;
    private readonly Func<LastEvent?> letztesEreignis;
    private readonly Func<bool> diagnoseLesen;
    private readonly Action<bool> diagnoseSetzen;
    private readonly HonorificBridge honorific;
    private readonly TitlePresenter presenter;
    private readonly string version;
    private readonly Dependencies dependencies;
    private readonly AboutInfo about;
    private readonly Action openWelcome;
    private readonly Action openChangelog;
    private readonly FrameTiming timing;

    private Theme.Scope? themeScope;

    public ConfigWindow(
        Configuration config,
        CounterStore counters,
        CooldownGate cooldowns,
        IDataManager data,
        string konfigOrdner,
        Func<ulong> eigeneCid,
        Func<string?> aktuellesZiel,
        Action save,
        Func<LastEvent?> letztesEreignis,
        Func<bool> diagnoseLesen,
        Action<bool> diagnoseSetzen,
        HonorificBridge honorific,
        TitlePresenter presenter,
        string version,
        Dependencies dependencies,
        AboutInfo about,
        Action openWelcome,
        Action openChangelog,
        IPluginLog log)
        // Der Titel bleibt "EnCounter", damit gespeicherte Fensterlage und -größe erhalten bleiben.
        : base("EnCounter")
    {
        this.config = config;
        this.counters = counters;
        this.cooldowns = cooldowns;
        this.data = data;
        this.konfigOrdner = konfigOrdner;
        this.eigeneCid = eigeneCid;
        this.aktuellesZiel = aktuellesZiel;
        this.save = save;
        this.letztesEreignis = letztesEreignis;
        this.diagnoseLesen = diagnoseLesen;
        this.diagnoseSetzen = diagnoseSetzen;
        this.honorific = honorific;
        this.presenter = presenter;
        this.version = version;
        this.dependencies = dependencies;
        this.about = about;
        this.openWelcome = openWelcome;
        this.openChangelog = openChangelog;
        timing = new FrameTiming(log, "Draw", TimeSpan.FromSeconds(5), diagnoseLesen);

        Size = new Vector2(860, 620);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(720, 480), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };

        // Das Fenster war bisher ein einfaches ImGui-Fenster ohne Escape-Schließen, Töne und Einblenden.
        RespectCloseHotkey = false;
        DisableWindowSounds = true;
        DisableFadeInFadeOut = true;
    }

    /// <summary>
    /// Die Kanäle einzeln, in drei Spalten nach Zusammengehörigkeit. Gruppenhäkchen
    /// gab es vorher - sie standen auf „aus", sobald ein Kanal der Gruppe fehlte, und
    /// verschwiegen damit genau das, was sie zeigen sollten.
    /// </summary>
    private static (string Beschriftung, ChatChannel Kanal)[][] Kanalspalten =>
    [
        [
            (Loc.T("Say", "Sagen"), ChatChannel.Say),
            (Loc.T("Yell", "Rufen"), ChatChannel.Yell),
            (Loc.T("Shout", "Schreien"), ChatChannel.Shout),
            (Loc.T("Tell", "Flüstern"), ChatChannel.TellIncoming),
        ],
        [
            (Loc.T("Party", "Gruppe"), ChatChannel.Party),
            (Loc.T("Alliance", "Allianz"), ChatChannel.Alliance),
            (Loc.T("Cross-world party", "Weltenübergreifende Gruppe"), ChatChannel.CrossParty),
            (Loc.T("PvP team", "PvP-Team"), ChatChannel.PvpTeam),
        ],
        [
            (Loc.T("Free Company", "Freie Gesellschaft"), ChatChannel.FreeCompany),
            (Loc.T("Novice Network", "Neulingskanal"), ChatChannel.NoviceNetwork),
        ],
    ];

    private static readonly ChatChannel[] Linkshells =
    [
        ChatChannel.Ls1, ChatChannel.Ls2, ChatChannel.Ls3, ChatChannel.Ls4,
        ChatChannel.Ls5, ChatChannel.Ls6, ChatChannel.Ls7, ChatChannel.Ls8,
    ];

    private static readonly ChatChannel[] WeltLinkshells =
    [
        ChatChannel.Cwls1, ChatChannel.Cwls2, ChatChannel.Cwls3, ChatChannel.Cwls4,
        ChatChannel.Cwls5, ChatChannel.Cwls6, ChatChannel.Cwls7, ChatChannel.Cwls8,
    ];

    private static readonly ChatChannel[] Emotekanaele =
    [
        ChatChannel.StandardEmote, ChatChannel.CustomEmote,
    ];

    private int ausgewaehlt;
    private string neuerTrigger = string.Empty;
    private string neuerName = string.Empty;
    private string emoteSuche = string.Empty;

    /// <summary>Gelesener, noch nicht übernommener Import. Null: kein Fenster offen.</summary>
    private PatMeImport.Fund? importFund;

    /// <summary>
    /// Ob die Stände aller Charaktere zusammengerechnet werden. Aus, weil EnCounter
    /// für den zählt, der gerade gespielt wird.
    /// </summary>
    private bool importAlle;

    private static (FontAwesomeIcon Icon, string Label)[] Tabs() =>
    [
        (FontAwesomeIcon.ListUl, Loc.T("Rules", "Regeln")),
        (FontAwesomeIcon.ChartBar, Loc.T("Counts", "Zähler")),
        (FontAwesomeIcon.Cog, Loc.T("Settings", "Einstellungen")),
        (FontAwesomeIcon.PuzzlePiece, Loc.T("Dependencies", "Abhängigkeiten")),
        (FontAwesomeIcon.InfoCircle, Loc.T("About", "Info")),
    ];

    /// <summary>Index des Tabs Regeln, siehe <see cref="Tabs"/>.</summary>
    public const int TabRules = 0;

    private int tab;

    /// <summary>Öffnet das Fenster auf dem gewählten Tab, etwa aus dem Willkommensfenster.</summary>
    public void OpenTab(int index)
    {
        tab = index;
        IsOpen = true;
    }

    // Theme muss vor dem Begin des Fensters gesetzt sein, weil Fensterhintergrund und Titelleiste davon abhängen.
    public override void PreDraw() => themeScope = Theme.Push();

    public override void PostDraw()
    {
        themeScope?.Dispose();
        themeScope = null;
    }

    public override void Draw()
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        DrawWindow();
        timing.Add(System.Diagnostics.Stopwatch.GetElapsedTime(started));
    }

    private void DrawWindow()
    {
        Decor.Background();
        Decor.HeaderBand(ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y + ImGui.GetTextLineHeight());
        DrawKopf();
        ImGui.Dummy(new Vector2(0, 6));

        Widgets.PillTabs("tabs", Tabs(), ref tab);
        ImGui.Dummy(new Vector2(0, 8));

        // Die Staende stehen in einem eigenen Tab, nicht in jeder Regel: Sie gehoeren dem
        // Ereignis und werden von allen Regeln geteilt, die dasselbe abdecken.
        switch (tab)
        {
            case 0:
                DrawRegelseite();
                break;
            case 1:
                DrawGlobaleZaehler();
                break;
            case 2:
                DrawEinstellungen();
                break;
            case 3:
                DependenciesView.Draw(dependencies);
                break;
            default:
                AboutView.Draw(about, openWelcome, openChangelog);
                break;
        }
    }

    private void DrawKopf()
    {
        ImGui.AlignTextToFramePadding();
        Widgets.Icon(FontAwesomeIcon.HandHoldingHeart, Theme.AccentLight);
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Theme.AccentLight, "EnCounter");
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Theme.Muted, version);

        DrawStatusMarken();
        DrawStatuszeile();
    }

    private bool honorificDa;
    private DateTime honorificGeprueft = DateTime.MinValue;

    /// <summary>
    /// Fragt Honorific alle zwei Sekunden statt in jedem Frame, weil jede Prüfung ein IPC-Aufruf ist.
    /// So fällt auch auf, wenn Honorific zur Laufzeit ausgeschaltet wird.
    /// </summary>
    private bool HonorificDa()
    {
        var jetzt = DateTime.UtcNow;
        if (jetzt - honorificGeprueft > TimeSpan.FromSeconds(2))
        {
            honorificDa = honorific.IsAvailable;
            honorificGeprueft = jetzt;
        }

        return honorificDa;
    }

    private void DrawStatusMarken()
    {
        var marken = new List<(string Text, Vector4 Punkt, string Tooltip)>();

        if (presenter.IsShowing)
            marken.Add(($"{Loc.T("Showing", "Zeigt")} · {presenter.CurrentText} · {Math.Ceiling(presenter.Remaining.TotalSeconds)} s", Theme.Accent,
                Loc.T("The title above your name right now.", "Der Titel, der gerade über deinem Namen steht.")));

        if (config.Paused)
            marken.Add((Loc.T("Paused", "Pausiert"), Theme.Warning,
                Loc.T("Counting is paused. Turn it back on in Settings.", "Zählen ist pausiert. Schalte es in den Einstellungen wieder ein.")));

        var da = HonorificDa();
        marken.Add(("Honorific", da ? Theme.Success : Theme.Danger,
            da ? Loc.T("Honorific is loaded. Titles are shown and synced.", "Honorific ist geladen. Titel werden angezeigt und synchronisiert.")
               : Loc.T($"Titles can't be shown: {honorific.LastError}", $"Titel können nicht angezeigt werden: {honorific.LastError}")));

        var abstand = ImGui.GetStyle().ItemSpacing.X;
        var breite = marken.Sum(m => Widgets.PillWidth(m.Text, true)) + abstand * (marken.Count - 1)
            + abstand + Widgets.LanguagePillWidth();

        ImGui.SameLine(ImGui.GetContentRegionMax().X - breite);
        for (var i = 0; i < marken.Count; i++)
        {
            if (i > 0)
                ImGui.SameLine();

            Widgets.StatusPill(marken[i].Text, marken[i].Punkt);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(marken[i].Tooltip);
        }

        ImGui.SameLine();
        Widgets.LanguagePill(SpracheSpeichern);
    }

    private void SpracheSpeichern(string code)
    {
        config.Language = code;
        save();
    }

    private void DrawStatuszeile()
    {
        string text;
        var farbe = Theme.Muted;

        if (config.Paused)
        {
            text = Loc.T("Paused - nothing is counted.", "Pausiert - es wird nichts gezählt.");
            farbe = Theme.Warning;
        }
        else if (letztesEreignis() is not { } e)
        {
            text = Loc.T("Nothing counted since the plugin started.", "Seit dem Start des Plugins wurde nichts gezählt.");
        }
        else
        {
            // Gespeichert ist Name@Welt, angezeigt wird wie im Titel nur der Vorname.
            var vorname = e.Person.Split('@')[0].Split(' ')[0];
            text = $"{Loc.T("Last", "Zuletzt")}: {vorname} · {e.EventName} · {e.Count} · {Vor(DateTime.UtcNow - e.Time)} · {e.RuleName}";
        }

        var start = ImGui.GetCursorPosX();
        ImGui.TextColored(farbe, text);

        if (diagnoseLesen())
        {
            // Rechtsbündig, aber nie über den Statustext hinweg.
            var zeit = timing.Summary();
            ImGui.SameLine(Math.Max(start + ImGui.CalcTextSize(text).X + ImGui.GetStyle().ItemSpacing.X * 2,
                ImGui.GetContentRegionMax().X - ImGui.CalcTextSize(zeit).X));
            ImGui.TextColored(Theme.Muted, zeit);
        }
    }

    private static string Vor(TimeSpan dauer) =>
        dauer.TotalSeconds < 60 ? Loc.T($"{(int)dauer.TotalSeconds} s ago", $"vor {(int)dauer.TotalSeconds} s")
        : dauer.TotalMinutes < 60 ? Loc.T($"{(int)dauer.TotalMinutes} min ago", $"vor {(int)dauer.TotalMinutes} min")
        : Loc.T($"{(int)dauer.TotalHours} h ago", $"vor {(int)dauer.TotalHours} h");

    private int? importErgebnis;

}
