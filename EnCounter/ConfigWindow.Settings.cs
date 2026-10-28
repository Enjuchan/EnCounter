using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Plugin.Services;
using EnCounter.Core;
using EnCounter.Ui;

namespace EnCounter;

public sealed partial class ConfigWindow
{
    private void DrawEinstellungen()
    {
        Abschnitt("Counting", "Pause stops all counting without touching your rules. Diagnostics is the same switch as /encounter debug.", breite =>
        {
            var pause = config.Paused;
            if (Widgets.ToggleRow("Pause counting", ref pause, breite,
                    "Nothing is counted and no title is shown. Your rules stay as they are."))
            {
                config.Paused = pause;
                save();
            }

            ImGui.Spacing();

            var diagnose = diagnoseLesen();
            if (Widgets.ToggleRow("Diagnostics", ref diagnose, breite,
                    "Writes every step of emote and rule processing to the log and to chat. Also shows the draw time of this window in the status line and logs it every 10 seconds."))
                diagnoseSetzen(diagnose);
        });

        DrawImport();

        Abschnitt("Privacy", null, breite =>
            Umbruch(Theme.Muted, "The list of names and all counters are stored on this computer only.", breite));
    }

    private static void Umbruch(Vector4 farbe, string text, float breite)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, farbe);
        ImGui.PushTextWrapPos(ImGui.GetCursorPos().X + breite);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();
    }


    /// <summary>
    /// Zeigt erst, was gefunden wurde, und übernimmt erst auf Knopfdruck. Ein Import,
    /// der ungefragt Regeln anlegt, ist schwer rückgängig zu machen.
    /// </summary>
    private void DrawImport()
    {
        Abschnitt("Import from PatMe", null, breite =>
        {
            ImGui.TextColored(Theme.Muted, "Adds counts to the shared list. No rules are created.");
            ImGui.Spacing();

            if (ImGui.Button("Read PatMe"))
            {
                importFund = PatMeImport.Lesen(konfigOrdner, eigeneCid());

                // Ohne erkennbaren eigenen Charakter bliebe sonst gar nichts uebrig.
                importAlle = importFund.Eigener is null;
                importErgebnis = null;
            }

            if (importFund is null)
            {
                if (importErgebnis is { } n)
                {
                    ImGui.Spacing();
                    ImGui.TextColored(Theme.Success, n == 1 ? "1 entry imported." : $"{n} entries imported.");
                }

                return;
            }

            var fund = importFund;
            ImGui.Spacing();

            DrawQuelle("PatMe", fund.PatMeGefunden, fund.HatAus("PatMe"));
            ImGui.SameLine();
            DrawQuelle("PatMe Honorific", fund.HonorificGefunden, fund.HatAus("PatMe Honorific"));

            // Beide zaehlen dasselbe, nur unterschiedlich lange - Honorific erst seit
            // seiner Aktivierung. Der hoehere Stand gilt, sonst zaehlte ein Dote doppelt.
            if (fund.HatAus("PatMe") && fund.HatAus("PatMe Honorific"))
                ImGui.TextColored(Theme.Muted, "Both count the same things - the higher number wins.");

            ImGui.Spacing();
            var uebernehmbar = DrawImportZahlen(fund, breite);

            if (fund.Leer)
                ImGui.TextColored(Theme.Muted, "Nothing to import.");

            foreach (var hinweis in fund.Hinweise)
                Umbruch(Theme.Warning, $"! {hinweis}", breite);

            ImGui.Spacing();

            if (Widgets.PrimaryButton("Import", FontAwesomeIcon.Download, uebernehmbar > 0))
            {
                var anzahl = PatMeImport.Anwenden(fund, counters, importAlle);
                if (anzahl > 0)
                    save();

                importErgebnis = anzahl;
                importFund = null;
                return;
            }

            ImGui.SameLine();

            if (ImGui.Button("Discard"))
                importFund = null;
        });
    }

    /// <summary>
    /// Die Zahlen aus PatMe, samt der Wahl, wessen Stände gelten. Gibt die Zahl der
    /// übernehmbaren Posten zurück.
    ///
    /// PatMe führt je Charakter Buch. Alles zusammenzurechnen wäre bequem und falsch:
    /// EnCounter zählt für den Charakter, der gerade gespielt wird.
    /// </summary>
    private int DrawImportZahlen(PatMeImport.Fund fund, float breite)
    {
        var eigener = fund.Eigener;

        if (eigener is null)
        {
            ImGui.TextColored(Theme.Warning, "Current character not found in PatMe.");
        }
        else
        {
            var an = !importAlle;
            if (Widgets.ToggleRow($"Only {eigener.Name}", ref an, breite))
                importAlle = !an;
        }

        if (fund.Charaktere.Count > 1)
        {
            var alle = importAlle;
            if (Widgets.ToggleRow($"All {fund.Charaktere.Count} characters, summed", ref alle, breite))
                importAlle = eigener is null || alle;
        }

        ImGui.Spacing();

        var uebernehmbar = fund.Uebernehmbar(importAlle).ToList();

        if (uebernehmbar.Count == 0)
        {
            ImGui.TextColored(Theme.Muted, "Nothing counted yet.");
            return 0;
        }

        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.PadOuterX;
        if (ImGui.BeginTable("importzahlen", 3, flags))
        {
            ImGui.TableSetupColumn("Emote", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Received", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Given", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            foreach (var emote in uebernehmbar.GroupBy(p => p.EmoteId))
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(EmoteCatalog.NameOf(data, emote.Key));

                foreach (var gegeben in new[] { false, true })
                {
                    ImGui.TableNextColumn();
                    var posten = emote.FirstOrDefault(p => p.Gegeben == gegeben);
                    if (posten is null)
                    {
                        ImGui.TextColored(Theme.Muted, "-");
                        continue;
                    }

                    ImGui.TextUnformatted(posten.Anzahl.ToString());

                    // Sind die Quellen sich uneinig, steht dahinter, was jede sagt - die Wahl
                    // des hoeheren Werts ist eine Wette, und die soll man sehen koennen.
                    if (!posten.Uneinig)
                        continue;

                    ImGui.SameLine();
                    ImGui.TextColored(Theme.Warning,
                        "(" + string.Join(", ", posten.Quellen.Select(q => $"{q.Quelle} {q.Wert}")) + ")");
                }
            }

            ImGui.EndTable();
        }

        ImGui.Spacing();
        Umbruch(Theme.Warning,
            $"PatMe does not record who did it, so these arrive as one entry named {PatMeImport.SammelName}.", breite);

        ImGui.TextColored(Theme.Muted, "Importing again replaces that entry - it never adds up twice.");
        return uebernehmbar.Count;
    }

    /// <summary>
    /// Eine Importquelle: ob ihre Datei da ist und ob etwas darin steht. Beides
    /// getrennt, weil „installiert, zählt aber nicht" der häufigere Fall ist - PatMe
    /// Honorific führt erst Buch, seit es läuft.
    /// </summary>
    private static void DrawQuelle(string plugin, bool gefunden, bool hatZahlen)
    {
        var (punkt, tooltip) = !gefunden
            ? (Theme.Danger, "not installed")
            : hatZahlen
                ? (Theme.Success, "found")
                : (Theme.Warning, "found, but has not counted anything");

        Widgets.StatusPill(plugin, punkt);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);
    }
}
