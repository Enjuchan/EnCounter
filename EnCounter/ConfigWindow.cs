using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
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
public sealed partial class ConfigWindow(
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
    string version)
{
    /// <summary>
    /// Die Kanäle einzeln, in drei Spalten nach Zusammengehörigkeit. Gruppenhäkchen
    /// gab es vorher - sie standen auf „aus", sobald ein Kanal der Gruppe fehlte, und
    /// verschwiegen damit genau das, was sie zeigen sollten.
    /// </summary>
    private static readonly (string Beschriftung, ChatChannel Kanal)[][] Kanalspalten =
    [
        [
            ("Say", ChatChannel.Say),
            ("Yell", ChatChannel.Yell),
            ("Shout", ChatChannel.Shout),
            ("Tell", ChatChannel.TellIncoming),
        ],
        [
            ("Party", ChatChannel.Party),
            ("Alliance", ChatChannel.Alliance),
            ("Cross-world party", ChatChannel.CrossParty),
            ("PvP team", ChatChannel.PvpTeam),
        ],
        [
            ("Free Company", ChatChannel.FreeCompany),
            ("Novice Network", ChatChannel.NoviceNetwork),
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

    public bool Visible;

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

    private static readonly (FontAwesomeIcon Icon, string Label)[] Tabs =
    [
        (FontAwesomeIcon.ListUl, "Rules"),
        (FontAwesomeIcon.ChartBar, "Counts"),
        (FontAwesomeIcon.Cog, "Settings"),
    ];

    private int tab;

    public void Draw()
    {
        if (!Visible)
            return;

        using var theme = Theme.Push();

        ImGui.SetNextWindowSize(new(860, 620), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(720, 480), new Vector2(float.MaxValue, float.MaxValue));

        // Der Titel bleibt "EnCounter", damit gespeicherte Fensterlage und -größe erhalten bleiben.
        if (!ImGui.Begin("EnCounter", ref Visible))
        {
            ImGui.End();
            return;
        }

        Decor.Background();
        Decor.HeaderBand(ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y + ImGui.GetTextLineHeight());
        DrawKopf();
        ImGui.Dummy(new Vector2(0, 6));

        Widgets.PillTabs("tabs", Tabs, ref tab);
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
            default:
                DrawEinstellungen();
                break;
        }

        ImGui.End();
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
            marken.Add(($"Showing · {presenter.CurrentText} · {Math.Ceiling(presenter.Remaining.TotalSeconds)} s", Theme.Accent,
                "The title above your name right now."));

        if (config.Paused)
            marken.Add(("Paused", Theme.Warning, "Counting is paused. Turn it back on in Settings."));

        var da = HonorificDa();
        marken.Add(("Honorific", da ? Theme.Success : Theme.Danger,
            da ? "Honorific is loaded. Titles are shown and synced." : $"Titles can't be shown: {honorific.LastError}"));

        var abstand = ImGui.GetStyle().ItemSpacing.X;
        var breite = marken.Sum(m => Widgets.PillWidth(m.Text, true)) + abstand * (marken.Count - 1);

        ImGui.SameLine(ImGui.GetContentRegionMax().X - breite);
        for (var i = 0; i < marken.Count; i++)
        {
            if (i > 0)
                ImGui.SameLine();

            Widgets.StatusPill(marken[i].Text, marken[i].Punkt);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(marken[i].Tooltip);
        }
    }

    private void DrawStatuszeile()
    {
        if (config.Paused)
        {
            ImGui.TextColored(Theme.Warning, "Paused - nothing is counted.");
            return;
        }

        if (letztesEreignis() is not { } e)
        {
            ImGui.TextColored(Theme.Muted, "Nothing counted since the plugin started.");
            return;
        }

        // Gespeichert ist Name@Welt, angezeigt wird wie im Titel nur der Vorname.
        var vorname = e.Person.Split('@')[0].Split(' ')[0];
        ImGui.TextColored(Theme.Muted, $"Last: {vorname} · {e.EventName} · {e.Count} · {Vor(DateTime.UtcNow - e.Time)} · {e.RuleName}");
    }

    private static string Vor(TimeSpan dauer) =>
        dauer.TotalSeconds < 60 ? $"{(int)dauer.TotalSeconds} s ago"
        : dauer.TotalMinutes < 60 ? $"{(int)dauer.TotalMinutes} min ago"
        : $"{(int)dauer.TotalHours} h ago";

    private int? importErgebnis;

}
