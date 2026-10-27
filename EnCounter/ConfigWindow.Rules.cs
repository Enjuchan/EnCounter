using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Plugin.Services;
using EnCounter.Core;
using EnCounter.Ui;

namespace EnCounter;

public sealed partial class ConfigWindow
{
    private void DrawRegelseite()
    {
        DrawLinkeSpalte();

        ImGui.SameLine(0, Widgets.S(14));

        ImGui.BeginChild("editor", new(0, 0));

        if (Gewaehlt is { } regel)
            DrawEditor(regel);
        else
            ImGui.TextColored(Theme.Muted, "No rule selected. Create one with New rule.");

        ImGui.EndChild();
    }

    private Rule? Gewaehlt =>
        ausgewaehlt >= 0 && ausgewaehlt < config.Rules.Count ? config.Rules[ausgewaehlt] : null;

    /// <summary>
    /// Ob eine Regel diese Richtung überhaupt zählt.
    ///
    /// Chatregeln kennen keine Richtung — dort gibt es keinen verlässlichen Empfänger,
    /// gezählt wird immer als empfangen.
    /// </summary>
    private static bool ZaehltRichtung(Rule regel, bool outgoing)
    {
        if (!IstEmoteRegel(regel))
            return !outgoing;

        return regel.Direction switch
        {
            CountDirection.Received => !outgoing,
            CountDirection.Given => outgoing,
            _ => true,
        };
    }

    /// <summary>
    /// Was diese Regel in einer Richtung gezählt hat.
    ///
    /// **Bei einer Regel mit Namensliste zählen nur die genannten Personen.** Der
    /// Zähler hängt am Ereignis und wird mit jeder Regel geteilt, die dasselbe
    /// abdeckt; die schlichte Summe zeigte deshalb bei einer Regel „nur für X" den
    /// Stand aller.
    /// </summary>
    private int StandDerRegel(Rule regel, bool outgoing)
    {
        var auswahl = CounterScope.ForRule(regel, outgoing);

        if (regel.OnlyFrom.Count == 0)
            return counters.GetTotal(auswahl);

        var summe = 0;

        foreach (var (person, anzahl) in counters.GetAll(auswahl))
        {
            var welt = WorldCatalog.NameOf(data, person.WorldId);

            if (regel.OnlyFrom.Any(e => RuleMatcher.Matches(e, person.Name, welt)))
                summe += anzahl;
        }

        return summe;
    }

    /// <summary>
    /// Die Zeile unter dem Regelnamen.
    ///
    /// **Zeigt nur, was die Regel auch zählt.** Eine reine „ich emote sie"-Regel darf
    /// nicht das Empfangene mit ausweisen — sie hat es nie gezählt, und der geteilte
    /// Zähler enthält es trotzdem.
    /// </summary>
    private string Standzeile(Rule regel)
    {
        var empfangen = ZaehltRichtung(regel, false) ? StandDerRegel(regel, false) : -1;
        var gegeben = ZaehltRichtung(regel, true) ? StandDerRegel(regel, true) : -1;

        // Chat kennt keine Richtung - dort wäre „received" nur Ballast.
        if (!IstEmoteRegel(regel))
            return $"{empfangen} counted · chat";

        if (empfangen >= 0 && gegeben >= 0)
            return $"{empfangen} received · {gegeben} given";

        return empfangen >= 0 ? $"{empfangen} received" : $"{gegeben} given";
    }

    private void Tausche(int a, int b)
    {
        (config.Rules[a], config.Rules[b]) = (config.Rules[b], config.Rules[a]);
        ausgewaehlt = b;
        save();
    }

    // ------------------------------------------------------------------ links

    private void DrawLinkeSpalte()
    {
        ImGui.BeginChild("rules", new Vector2(Widgets.S(260), 0));

        Widgets.SectionHeader($"Rules · {config.Rules.Count}");
        if (Widgets.PrimaryButton("New rule", FontAwesomeIcon.Plus))
        {
            var neu = new Rule { Name = $"Rule {config.Rules.Count + 1}" };
            neu.EnsureId();
            config.Rules.Add(neu);
            ausgewaehlt = config.Rules.Count - 1;
            save();
        }

        ImGui.Spacing();

        // Eigener Scrollbereich, damit Kopf und Knopf bei vielen Regeln stehen bleiben.
        ImGui.BeginChild("rulelist", new Vector2(0, 0));
        for (var i = 0; i < config.Rules.Count; i++)
            DrawRegelkarte(i, config.Rules[i]);
        ImGui.EndChild();

        ImGui.EndChild();
    }

    private void DrawRegelkarte(int index, Rule regel)
    {
        var befunde = RuleHealth.Check(regel);
        var schlimmste = RuleHealth.Worst(befunde);

        var geklickt = Widgets.Card($"rule{index}", breite =>
        {
            var start = ImGui.GetCursorScreenPos();
            var zeile = ImGui.GetTextLineHeight();

            ImGui.SetCursorScreenPos(start + new Vector2(0, (zeile - 16) / 2 + 1));
            var an = regel.Enabled;
            if (Widgets.Toggle($"on{index}", ref an))
            {
                regel.Enabled = an;
                save();
            }

            // Rechts die Art und, falls nötig, ein Warnzeichen; der Name bekommt den Rest der Breite.
            var art = IstEmoteRegel(regel) ? "Emote" : "Chat";
            var artBreite = Widgets.PillWidth(art, false);
            var rechts = start.X + breite;
            var warnBreite = 0f;

            if (schlimmste is >= HealthSeverity.Warnung)
            {
                ImGui.PushFont(UiBuilder.IconFont);
                warnBreite = ImGui.CalcTextSize(FontAwesomeIcon.ExclamationTriangle.ToIconString()).X + 6;
                ImGui.PopFont();
            }

            ImGui.SetCursorScreenPos(new Vector2(start.X + 38, start.Y));
            var nameFarbe = regel.Enabled ? Theme.Text : Theme.Muted;
            ImGui.PushClipRect(new Vector2(start.X + 38, start.Y), new Vector2(rechts - artBreite - warnBreite - 6, start.Y + zeile + 2), true);
            ImGui.TextColored(nameFarbe, regel.Name);
            ImGui.PopClipRect();

            if (warnBreite > 0)
            {
                ImGui.SetCursorScreenPos(new Vector2(rechts - artBreite - warnBreite, start.Y));
                Widgets.Icon(FontAwesomeIcon.ExclamationTriangle, schlimmste == HealthSeverity.Blockierend ? Theme.Danger : Theme.Warning);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(string.Join("\n", befunde.Where(b => b.Severity >= HealthSeverity.Warnung).Select(b => b.Message)));
            }

            ImGui.SetCursorScreenPos(new Vector2(rechts - artBreite, start.Y - 2));
            Widgets.Pill(art, Theme.Border, Theme.Muted);

            ImGui.SetCursorScreenPos(new Vector2(start.X + 38, start.Y + zeile + 3));
            ImGui.TextColored(Theme.Muted, Standzeile(regel));
        }, ausgewaehlt == index, 10);

        if (geklickt)
            ausgewaehlt = index;
    }

    // ------------------------------------------------------------------ Editor

    private void DrawEditorKopf(Rule regel)
    {
        var abstand = ImGui.GetStyle().ItemSpacing.X;
        var knoepfe = Widgets.IconButtonWidth(FontAwesomeIcon.ArrowUp) + Widgets.IconButtonWidth(FontAwesomeIcon.ArrowDown)
                    + Widgets.IconButtonWidth(FontAwesomeIcon.Copy) + Widgets.IconButtonWidth(FontAwesomeIcon.Trash) + abstand * 4;

        var name = regel.Name;
        ImGui.SetNextItemWidth(-knoepfe);
        if (ImGui.InputTextWithHint("##name", "Rule name", ref name, 64))
        {
            regel.Name = name;
            save();
        }

        // Bei gleich genauen Regeln entscheidet die Reihenfolge, welche den Titel stellt.
        const string reihenfolge = "Among equally specific rules, the upper one shows its title.";
        var kannHoch = ausgewaehlt > 0;
        var kannRunter = ausgewaehlt < config.Rules.Count - 1;

        ImGui.SameLine();
        ImGui.BeginDisabled(!kannHoch);
        var hoch = Widgets.IconButton("up", FontAwesomeIcon.ArrowUp);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip($"Move up. {reihenfolge}");

        ImGui.SameLine();
        ImGui.BeginDisabled(!kannRunter);
        var runter = Widgets.IconButton("down", FontAwesomeIcon.ArrowDown);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip($"Move down. {reihenfolge}");

        ImGui.SameLine();
        if (Widgets.IconButton("dup", FontAwesomeIcon.Copy, "Duplicate: same settings, own counter, starting at zero."))
        {
            config.Rules.Insert(ausgewaehlt + 1, regel.Copy($"{regel.Name} (copy)"));
            ausgewaehlt++;
            save();
            return;
        }

        ImGui.SameLine();

        // Löschen verlangt Shift: Eine Regel ist schnell weg, und ein Fehlklick kostet alle Einstellungen darin.
        // Der Zähler bleibt, er gehört dem Ereignis; eine neue Regel findet ihren Stand wieder vor.
        if (Widgets.ShiftButton("del", FontAwesomeIcon.Trash, "delete this rule"))
        {
            config.Rules.RemoveAt(ausgewaehlt);
            ausgewaehlt = Math.Min(ausgewaehlt, config.Rules.Count - 1);
            save();
            return;
        }

        if (hoch && kannHoch)
            Tausche(ausgewaehlt, ausgewaehlt - 1);
        else if (runter && kannRunter)
            Tausche(ausgewaehlt, ausgewaehlt + 1);
    }
}
