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
        Abschnitt(Loc.T("Counting", "Zählen"),
            Loc.T("Pause stops all counting without touching your rules. Diagnostics is the same switch as /encounter debug.",
                  "Pause stoppt das Zählen, ohne deine Regeln anzufassen. Diagnose ist derselbe Schalter wie /encounter debug."), breite =>
        {
            var pause = config.Paused;
            if (Widgets.ToggleRow(Loc.T("Pause counting", "Zählen pausieren"), ref pause, breite,
                    Loc.T("Nothing is counted and no title is shown. Your rules stay as they are.",
                          "Es wird nichts gezählt und kein Titel angezeigt. Deine Regeln bleiben, wie sie sind.")))
            {
                config.Paused = pause;
                save();
            }

            ImGui.Spacing();

            var diagnose = diagnoseLesen();
            if (Widgets.ToggleRow(Loc.T("Diagnostics", "Diagnose"), ref diagnose, breite,
                    Loc.T("Writes every step of emote and rule processing to the log and to chat. Also shows the draw time of this window in the status line and logs it every 10 seconds.",
                          "Schreibt jeden Schritt der Emote- und Regelverarbeitung ins Log und in den Chat. Zeigt außerdem die Zeichenzeit dieses Fensters in der Statuszeile und schreibt sie alle 10 Sekunden ins Log.")))
                diagnoseSetzen(diagnose);
        });

        DrawImport();

        Abschnitt(Loc.T("Privacy", "Datenschutz"), null, breite =>
            Umbruch(Theme.Muted, Loc.T("The list of names and all counters are stored on this computer only.",
                                       "Die Namensliste und alle Zähler liegen nur auf diesem Computer."), breite));
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
        Abschnitt(Loc.T("Import from PatMe", "Import aus PatMe"), null, DrawImportInhalt);
    }

    /// <summary>
    /// Die Einstellungen im Willkommensfenster: derselbe Import wie hier und ein Knopf,
    /// der die Regeln öffnet, damit man die erste anlegt oder eine der mitgelieferten ändert.
    /// </summary>
    public void DrawWelcomeSettings(float breite)
    {
        ImGui.TextColored(Theme.AccentLight, Loc.T("Import from PatMe", "Import aus PatMe"));
        DrawImportInhalt(breite);
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (Widgets.PrimaryButton(Loc.T("Open rules", "Regeln öffnen"), FontAwesomeIcon.ListUl))
            OpenTab(TabRules);
    }

    private void DrawImportInhalt(float breite)
    {
        ImGui.TextColored(Theme.Muted, Loc.T("Adds counts to the shared list. No rules are created.",
                                             "Fügt Zähler zur gemeinsamen Liste hinzu. Es werden keine Regeln angelegt."));
        ImGui.Spacing();

        if (ImGui.Button(Loc.T("Read PatMe", "PatMe lesen")))
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
                ImGui.TextColored(Theme.Success, n == 1 ? Loc.T("1 entry imported.", "1 Eintrag importiert.") : Loc.T($"{n} entries imported.", $"{n} Einträge importiert."));
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
            ImGui.TextColored(Theme.Muted, Loc.T("Both count the same things - the higher number wins.", "Beide zählen dasselbe - die höhere Zahl gilt."));

        ImGui.Spacing();
        var uebernehmbar = DrawImportZahlen(fund, breite);

        if (fund.Leer)
            ImGui.TextColored(Theme.Muted, Loc.T("Nothing to import.", "Nichts zu importieren."));

        foreach (var hinweis in fund.Hinweise)
            Umbruch(Theme.Warning, $"! {hinweis}", breite);

        ImGui.Spacing();

        if (Widgets.PrimaryButton(Loc.T("Import", "Importieren"), FontAwesomeIcon.Download, uebernehmbar > 0))
        {
            var anzahl = PatMeImport.Anwenden(fund, counters, importAlle);
            if (anzahl > 0)
                save();

            importErgebnis = anzahl;
            importFund = null;
            return;
        }

        ImGui.SameLine();

        if (ImGui.Button(Loc.T("Discard", "Verwerfen")))
            importFund = null;
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
            ImGui.TextColored(Theme.Warning, Loc.T("Current character not found in PatMe.", "Aktueller Charakter in PatMe nicht gefunden."));
        }
        else
        {
            var an = !importAlle;
            if (Widgets.ToggleRow(Loc.T($"Only {eigener.Name}", $"Nur {eigener.Name}"), ref an, breite))
                importAlle = !an;
        }

        if (fund.Charaktere.Count > 1)
        {
            var alle = importAlle;
            if (Widgets.ToggleRow(Loc.T($"All {fund.Charaktere.Count} characters, summed", $"Alle {fund.Charaktere.Count} Charaktere, zusammengerechnet"), ref alle, breite))
                importAlle = eigener is null || alle;
        }

        ImGui.Spacing();

        var uebernehmbar = fund.Uebernehmbar(importAlle).ToList();

        if (uebernehmbar.Count == 0)
        {
            ImGui.TextColored(Theme.Muted, Loc.T("Nothing counted yet.", "Noch nichts gezählt."));
            return 0;
        }

        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.PadOuterX;
        if (ImGui.BeginTable("importzahlen", 3, flags))
        {
            ImGui.TableSetupColumn("Emote", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn(Loc.T("Received", "Empfangen"), ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn(Loc.T("Given", "Gegeben"), ImGuiTableColumnFlags.WidthStretch);
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
            Loc.T($"PatMe does not record who did it, so these arrive as one entry named {PatMeImport.SammelName}.",
                  $"PatMe speichert nicht, wer es getan hat, deshalb landen diese Stände unter einem Eintrag namens {PatMeImport.SammelName}."), breite);

        ImGui.TextColored(Theme.Muted, Loc.T("Importing again replaces that entry - it never adds up twice.",
                                             "Erneutes Importieren ersetzt diesen Eintrag - nichts wird doppelt addiert."));
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
            ? (Theme.Danger, Loc.T("not installed", "nicht installiert"))
            : hatZahlen
                ? (Theme.Success, Loc.T("found", "gefunden"))
                : (Theme.Warning, Loc.T("found, but has not counted anything", "gefunden, hat aber noch nichts gezählt"));

        Widgets.StatusPill(plugin, punkt);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);
    }
}
