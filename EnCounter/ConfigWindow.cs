using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using EnCounter.Core;

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
public sealed class ConfigWindow(
    Configuration config,
    CounterStore counters,
    IDataManager data,
    string konfigOrdner,
    Func<ulong> eigeneCid,
    Func<string?> aktuellesZiel,
    Func<string> zuletztGesehen,
    Action save)
{
    private const uint Warnfarbe = 0xFF3333FFu;

    /// <summary>Gelb: geht noch, aber nicht fuer jeden Namen.</summary>
    private const uint Hinweisfarbe = 0xFF66CCFFu;
    private const uint Graufarbe = 0xFF999999u;

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

    public void Draw()
    {
        if (!Visible)
            return;

        ImGui.SetNextWindowSize(new(800, 580), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("EnCounter", ref Visible))
        {
            ImGui.End();
            return;
        }

        // Die Staende stehen oben, nicht in jeder Regel: Sie gehoeren dem Ereignis und
        // werden von allen Regeln geteilt, die dasselbe abdecken.
        if (ImGui.BeginTabBar("haupt"))
        {
            if (ImGui.BeginTabItem("Rules"))
            {
                DrawRegelseite();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Counts"))
            {
                DrawGlobaleZaehler();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        ImGui.End();
    }

    private void DrawRegelseite()
    {
        DrawLinkeSpalte();

        ImGui.SameLine();

        ImGui.BeginChild("editor", new(0, 0));

        if (Gewaehlt is { } regel)
            DrawEditor(regel);
        else
            ImGui.TextColored(Graufarbe, "No rule selected.");

        ImGui.EndChild();
    }

    private Rule? Gewaehlt =>
        ausgewaehlt >= 0 && ausgewaehlt < config.Rules.Count ? config.Rules[ausgewaehlt] : null;

    /// <summary>
    /// Regel nach oben oder unten schieben.
    ///
    /// Nötig, weil bei **gleich genauen** Regeln die Reihenfolge entscheidet, welche
    /// die Meldung stellt. Wer zwei Regeln für dieselbe Person und dasselbe Emote
    /// baut, kann sonst nicht bestimmen, welcher Text erscheint.
    /// </summary>
    private void DrawReihenfolge()
    {
        var kannHoch = ausgewaehlt > 0;
        var kannRunter = ausgewaehlt >= 0 && ausgewaehlt < config.Rules.Count - 1;

        if (!kannHoch)
            ImGui.BeginDisabled();

        var hoch = ImGui.Button("Up", new(95, 0)) && kannHoch;

        if (!kannHoch)
            ImGui.EndDisabled();

        ImGui.SameLine();

        if (!kannRunter)
            ImGui.BeginDisabled();

        var runter = ImGui.Button("Down", new(95, 0)) && kannRunter;

        if (!kannRunter)
            ImGui.EndDisabled();

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Among equally specific rules, the upper one shows its text.");

        if (hoch)
            Tausche(ausgewaehlt, ausgewaehlt - 1);
        else if (runter)
            Tausche(ausgewaehlt, ausgewaehlt + 1);
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
        ImGui.BeginChild("rules", new(215, 0), true);

        ImGui.TextUnformatted("Rules");
        ImGui.Separator();

        for (var i = 0; i < config.Rules.Count; i++)
        {
            var regel = config.Rules[i];

            var an = regel.Enabled;
            if (ImGui.Checkbox($"##on{i}", ref an))
            {
                regel.Enabled = an;
                save();
            }

            ImGui.SameLine();

            if (ImGui.Selectable($"{regel.Name}##sel{i}", ausgewaehlt == i))
                ausgewaehlt = i;

            // Zaehler und Art stehen klein darunter statt in Klammern dahinter - so
            // bleibt der Name lesbar, auch wenn er lang ist.
            var art = IstEmoteRegel(regel) ? "emotes" : "chat";
            var gesamt = counters.GetTotal(CounterScope.ForRule(regel, false))
                       + counters.GetTotal(CounterScope.ForRule(regel, true));

            ImGui.TextColored(Graufarbe, $"      {gesamt} counted · {art}");
        }

        ImGui.Separator();

        DrawReihenfolge();

        if (ImGui.Button("Add", new(95, 0)))
        {
            var neu = new Rule { Name = $"Rule {config.Rules.Count + 1}" };
            neu.EnsureId();
            config.Rules.Add(neu);
            ausgewaehlt = config.Rules.Count - 1;
            save();
        }

        ImGui.SameLine();

        if (ImGui.Button("Duplicate", new(95, 0)) && Gewaehlt is { } vorlage)
        {
            config.Rules.Insert(ausgewaehlt + 1, vorlage.Copy($"{vorlage.Name} (copy)"));
            ausgewaehlt++;
            save();
        }

        // SetItemTooltip gibt es in diesen Bindings nicht - nur IsItemHovered.
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Same settings, own counter, starting at zero.");

        // Loeschen verlangt Shift. Eine Regel ist schnell gebaut und schnell weg;
        // ein Fehlklick hier kostet alle Einstellungen darin.
        var shift = ImGui.GetIO().KeyShift;

        if (!shift)
            ImGui.BeginDisabled();

        var geloescht = ImGui.Button("Delete", new(196, 0)) && shift && Gewaehlt is not null;

        if (!shift)
            ImGui.EndDisabled();

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Hold Shift to delete.");

        if (geloescht)
        {
            // Der Zaehler bleibt stehen: Er gehoert dem Ereignis, nicht dieser Regel.
            // Eine neue Dote-Regel findet ihren Stand spaeter wieder vor.
            config.Rules.RemoveAt(ausgewaehlt);
            ausgewaehlt = Math.Min(ausgewaehlt, config.Rules.Count - 1);
            save();
        }

        ImGui.Separator();

        if (ImGui.Button("Import...", new(196, 0)))
        {
            importFund = PatMeImport.Lesen(konfigOrdner, eigeneCid());

            // Ohne erkennbaren eigenen Charakter bliebe sonst gar nichts uebrig.
            importAlle = importFund.Eigener is null;

            ImGui.OpenPopup("import");
        }

        DrawImport();

        ImGui.EndChild();
    }

    /// <summary>
    /// Zeigt erst, was gefunden wurde, und übernimmt erst auf Knopfdruck. Ein Import,
    /// der ungefragt Regeln anlegt, ist schwer rückgängig zu machen.
    /// </summary>
    private void DrawImport()
    {
        var offen = importFund is not null;
        if (!offen)
            return;

        ImGui.SetNextWindowSize(new(520, 0), ImGuiCond.Always);

        if (!ImGui.BeginPopupModal("import", ref offen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            // Ueber das Kreuz geschlossen.
            if (!offen)
                importFund = null;

            return;
        }

        var fund = importFund!;

        ImGui.TextColored(Graufarbe, "Adds counts to the shared list. No rules are created.");
        ImGui.Spacing();

        DrawQuelle("PatMe", fund.PatMeGefunden, fund.HatAus("PatMe"));
        DrawQuelle("PatMe Honorific", fund.HonorificGefunden, fund.HatAus("PatMe Honorific"));

        // Beide zaehlen dasselbe, nur unterschiedlich lange - Honorific erst seit
        // seiner Aktivierung. Der hoehere Stand gilt, sonst zaehlte ein Dote doppelt.
        if (fund.HatAus("PatMe") && fund.HatAus("PatMe Honorific"))
            ImGui.TextColored(Graufarbe, "Both count the same things - the higher number wins.");

        ImGui.Spacing();
        DrawImportZahlen(fund);

        if (fund.Leer)
        {
            ImGui.Spacing();
            ImGui.TextColored(Graufarbe, "Nothing to import.");
        }

        foreach (var hinweis in fund.Hinweise)
            ImGui.TextColored(Hinweisfarbe, $"! {hinweis}");

        ImGui.Separator();

        if (ImGui.Button("Import", new(120, 0)))
        {
            if (PatMeImport.Anwenden(fund, counters, importAlle) > 0)
                save();

            importFund = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();

        if (ImGui.Button("Cancel", new(120, 0)))
        {
            importFund = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    /// <summary>
    /// Die Zahlen aus PatMe, samt der Wahl, wessen Stände gelten.
    ///
    /// PatMe führt je Charakter Buch. Alles zusammenzurechnen wäre bequem und falsch:
    /// EnCounter zählt für den Charakter, der gerade gespielt wird.
    /// </summary>
    private void DrawImportZahlen(PatMeImport.Fund fund)
    {
        var eigener = fund.Eigener;

        if (eigener is null)
        {
            ImGui.TextColored(Hinweisfarbe, "      current character not found in PatMe");
        }
        else
        {
            var an = !importAlle;
            if (ImGui.Checkbox($"Only {eigener.Name}##nurEigener", ref an))
                importAlle = !an;
        }

        if (fund.Charaktere.Count > 1)
        {
            var alle = importAlle;
            if (ImGui.Checkbox($"All {fund.Charaktere.Count} characters, summed##alle", ref alle))
                importAlle = eigener is null || alle;
        }

        ImGui.Spacing();

        var uebernehmbar = fund.Uebernehmbar(importAlle).ToList();

        if (uebernehmbar.Count == 0)
        {
            ImGui.TextColored(Graufarbe, "      nothing counted yet");
            return;
        }

        foreach (var posten in uebernehmbar)
        {
            var richtung = posten.Gegeben ? "given" : "received";

            ImGui.TextColored(Graufarbe,
                $"      {EmoteCatalog.NameOf(data, posten.EmoteId)}: {posten.Anzahl} {richtung}");

            // Sind die Quellen sich uneinig, steht dahinter, was jede sagt - die Wahl
            // des hoeheren Werts ist eine Wette, und die soll man sehen koennen.
            if (!posten.Uneinig)
                continue;

            ImGui.SameLine();
            ImGui.TextColored(Hinweisfarbe,
                "(" + string.Join(", ", posten.Quellen.Select(q => $"{q.Quelle} {q.Wert}")) + ")");
        }

        ImGui.Spacing();
        ImGui.TextColored(Hinweisfarbe,
            $"PatMe does not record who did it, so these arrive as one entry named {PatMeImport.SammelName}.");

        ImGui.TextColored(Graufarbe, "Importing again replaces that entry - it never adds up twice.");
    }

    /// <summary>
    /// Eine Importquelle: ob ihre Datei da ist und ob etwas darin steht. Beides
    /// getrennt, weil „installiert, zählt aber nicht" der häufigere Fall ist - PatMe
    /// Honorific führt erst Buch, seit es läuft.
    /// </summary>
    private static void DrawQuelle(string plugin, bool gefunden, bool hatZahlen)
    {
        ImGui.TextUnformatted($"{plugin}");
        ImGui.SameLine();

        if (!gefunden)
        {
            ImGui.TextColored(Hinweisfarbe, "not installed");
            return;
        }

        if (hatZahlen)
            ImGui.TextColored(Graufarbe, "found");
        else
            ImGui.TextColored(Hinweisfarbe, "found, but has not counted anything");
    }

    // ----------------------------------------------------------------- rechts

    private void DrawEditor(Rule regel)
    {
        var name = regel.Name;
        ImGui.SetNextItemWidth(300);
        if (ImGui.InputText("Name", ref name, 64))
        {
            regel.Name = name;
            save();
        }

        ImGui.Spacing();

        if (!ImGui.BeginTabBar("tabs"))
            return;

        if (ImGui.BeginTabItem("Listen to"))
        {
            DrawAusloeser(regel);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Text"))
        {
            DrawVorlagen(regel);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Colours"))
        {
            DrawFarben(regel);
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private static bool IstEmoteRegel(Rule regel) =>
        regel.Channels.Contains(ChatChannel.StandardEmote) ||
        regel.Channels.Contains(ChatChannel.CustomEmote);

    // ------------------------------------------------------------- Listen to

    private void DrawAusloeser(Rule regel)
    {
        ImGui.Spacing();

        var istEmote = IstEmoteRegel(regel);
        DrawArtwahl(regel, istEmote);

        ImGui.Separator();

        if (istEmote)
        {
            DrawRichtung(regel);
            ImGui.Spacing();
            DrawEmoteAuswahl(regel);
        }
        else
        {
            DrawKanaele(regel);
            ImGui.Spacing();
            DrawTrigger(regel);
        }

        ImGui.Separator();
        DrawPersonen(regel);

        ImGui.Separator();
        DrawBremse(regel);
    }

    /// <summary>
    /// Namensliste: Nur diese Personen lösen die Regel aus. Verglichen wird der
    /// Vorname — eingetippt werden darf trotzdem der volle Name.
    /// </summary>
    private void DrawPersonen(Rule regel)
    {
        ImGui.TextUnformatted("Only from");

        ImGui.TextColored(Graufarbe, regel.OnlyFrom.Count == 0
            ? "Empty: anyone counts."
            : "Only these people count - and this rule's text wins over a general one.");

        // Nur die vollstaendige Form ist eindeutig: Vornamen wiederholen sich, und
        // Namen sind in FFXIV ohnehin nur pro Welt eindeutig.
        ImGui.TextColored(Graufarbe, "Full name and world, for example:");
        ImGui.TextColored(Graufarbe, "Stella Unibell@Shiva");

        // Was EnCounter zuletzt tatsaechlich erkannt hat. Ohne diese Zeile raet man
        // beim Eintragen - ein Eintrag, der nicht greift, sieht aus wie einer, der
        // greift.
        var gesehen = zuletztGesehen();

        if (!string.IsNullOrEmpty(gesehen))
        {
            ImGui.Spacing();
            ImGui.TextColored(Graufarbe, "Last recognised:");
            ImGui.SameLine();
            ImGui.TextColored(Hinweisfarbe, gesehen);

            if (!gesehen.Contains("(no world)") &&
                !regel.OnlyFrom.Contains(gesehen, StringComparer.CurrentCultureIgnoreCase))
            {
                ImGui.SameLine();

                if (ImGui.Button("Add##gesehen"))
                {
                    regel.OnlyFrom.Add(gesehen);
                    save();
                }
            }
        }

        ImGui.Spacing();

        for (var i = 0; i < regel.OnlyFrom.Count; i++)
        {
            if (ImGui.Button($"x##per{i}"))
            {
                regel.OnlyFrom.RemoveAt(i);
                save();
                return;
            }

            ImGui.SameLine();

            var vollstaendig = RuleMatcher.IstVollstaendig(regel.OnlyFrom[i]);

            ImGui.TextColored(vollstaendig ? 0xFFFFFFFFu : Warnfarbe, regel.OnlyFrom[i]);

            if (vollstaendig)
                continue;

            ImGui.SameLine();
            ImGui.TextColored(Warnfarbe, "incomplete - never matches");
        }

        ImGui.SetNextItemWidth(220);
        ImGui.InputTextWithHint("##newperson", "Firstname Lastname@World", ref neuerName, 64);

        ImGui.SameLine();

        if (ImGui.Button("Add##person") && !string.IsNullOrWhiteSpace(neuerName))
        {
            regel.OnlyFrom.Add(neuerName.Trim());
            neuerName = string.Empty;
            save();
            return;
        }

        ImGui.SameLine();

        // Bequemer Weg zum richtigen Namen: Wer im Spiel steht, tippt sich nicht
        // vertippt - und die Welt kommt gleich mit.
        var ziel = aktuellesZiel();

        if (ziel is null)
        {
            ImGui.BeginDisabled();
            ImGui.Button("Current target");
            ImGui.EndDisabled();

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip("Target a player in game to use this.");

            return;
        }

        if (!ImGui.Button("Current target"))
        {
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Add {ziel}");

            return;
        }

        if (!regel.OnlyFrom.Contains(ziel, StringComparer.CurrentCultureIgnoreCase))
        {
            regel.OnlyFrom.Add(ziel);
            save();
        }
    }

    /// <summary>
    /// Emote- oder Chatregel. Die Wahl setzt die Kanäle passend - sie ist keine
    /// zusätzliche Angabe, sondern eine lesbare Sicht auf dieselbe Auswahl.
    /// </summary>
    private void DrawArtwahl(Rule regel, bool istEmote)
    {
        ImGui.SetNextItemWidth(220);

        if (ImGui.BeginCombo("Listens to", istEmote ? "Emotes" : "Chat messages"))
        {
            if (ImGui.Selectable("Chat messages", !istEmote) && istEmote)
            {
                foreach (var k in Emotekanaele)
                    regel.Channels.Remove(k);

                // Ohne einen Kanal hoert die Regel auf gar nichts. Say ist der
                // naheliegende Anfang.
                if (regel.Channels.Count == 0)
                    regel.Channels.Add(ChatChannel.Say);

                save();
            }

            if (ImGui.Selectable("Emotes", istEmote) && !istEmote)
            {
                regel.Channels.Clear();
                regel.Channels.AddRange(Emotekanaele);
                save();
            }

            ImGui.EndCombo();
        }

        ImGui.TextColored(Graufarbe, istEmote
            ? "Counts emotes like /hug or /pet, taken straight from the game."
            : "Counts chat messages containing one of your trigger words.");

        // Mischregeln aus aelteren Fassungen: Die Chatkanaele waeren hier unsichtbar,
        // wuerden aber weiter zaehlen.
        if (!istEmote)
            return;

        var uebrig = regel.Channels.Count(k => !Emotekanaele.Contains(k));
        if (uebrig == 0)
            return;

        ImGui.TextColored(Warnfarbe, $"This rule also still listens to {uebrig} chat channel(s).");

        if (!ImGui.Button("Remove them"))
            return;

        regel.Channels.RemoveAll(k => !Emotekanaele.Contains(k));
        save();
    }

    /// <summary>
    /// Die Kanäle einzeln, in Spalten. Eine einzelne lange Liste zwingt zum Scrollen
    /// und lässt Zusammengehöriges auseinanderfallen.
    /// </summary>
    private void DrawKanaele(Rule regel)
    {
        ImGui.TextUnformatted("Channels");

        if (ImGui.BeginTable("kanaele", Kanalspalten.Length))
        {
            for (var i = 0; i < Kanalspalten.Length; i++)
                ImGui.TableSetupColumn($"##sp{i}");

            // Zeilenweise fuellen: In jeder Zeile ein Eintrag je Spalte, damit die
            // Spalten nebeneinander stehen statt untereinander.
            var zeilen = Kanalspalten.Max(s => s.Length);

            for (var zeile = 0; zeile < zeilen; zeile++)
            {
                ImGui.TableNextRow();

                foreach (var spalte in Kanalspalten)
                {
                    ImGui.TableNextColumn();

                    if (zeile >= spalte.Length)
                        continue;

                    var (beschriftung, kanal) = spalte[zeile];
                    Kanalhaken(regel, beschriftung, kanal);
                }
            }

            ImGui.EndTable();
        }

        ImGui.Spacing();

        DrawLinkshellGruppe(regel, "Linkshells", Linkshells, "ls");
        DrawLinkshellGruppe(regel, "Cross-world linkshells", WeltLinkshells, "cwls");
    }

    /// <summary>
    /// Sechzehn Linkshells würden die Seite erschlagen. Zugeklappt steht nur, wie
    /// viele davon an sind - die Angabe, auf die es beim Überfliegen ankommt.
    /// </summary>
    private void DrawLinkshellGruppe(Rule regel, string titel, ChatChannel[] kanaele, string kuerzel)
    {
        var aktiv = kanaele.Count(regel.Channels.Contains);
        var beschriftung = aktiv > 0 ? $"{titel}  ({aktiv} on)###{kuerzel}" : $"{titel}###{kuerzel}";

        if (!ImGui.CollapsingHeader(beschriftung))
            return;

        if (!ImGui.BeginTable($"tbl{kuerzel}", 4))
            return;

        for (var i = 0; i < kanaele.Length; i++)
        {
            if (i % 4 == 0)
                ImGui.TableNextRow();

            ImGui.TableNextColumn();
            Kanalhaken(regel, $"{i + 1}##{kuerzel}{i}", kanaele[i]);
        }

        ImGui.EndTable();
    }

    private void Kanalhaken(Rule regel, string beschriftung, ChatChannel kanal)
    {
        var an = regel.Channels.Contains(kanal);

        if (!ImGui.Checkbox(beschriftung, ref an))
            return;

        if (an)
            regel.Channels.Add(kanal);
        else
            regel.Channels.Remove(kanal);

        save();
    }

    private void DrawTrigger(Rule regel)
    {
        ImGui.TextUnformatted("Trigger words");
        ImGui.TextColored(Graufarbe, "Any of these must appear. Empty: every message counts.");

        for (var i = 0; i < regel.TriggerTexts.Count; i++)
        {
            if (ImGui.Button($"x##trg{i}"))
            {
                regel.TriggerTexts.RemoveAt(i);
                save();
                return;
            }

            ImGui.SameLine();
            ImGui.TextUnformatted(regel.TriggerTexts[i]);
        }

        ImGui.SetNextItemWidth(220);
        ImGui.InputTextWithHint("##newtrigger", "add a word or phrase...", ref neuerTrigger, 128);
        ImGui.SameLine();

        if (ImGui.Button("Add##trigger") && !string.IsNullOrWhiteSpace(neuerTrigger))
        {
            regel.TriggerTexts.Add(neuerTrigger.Trim());
            neuerTrigger = string.Empty;
            save();
        }

        ImGui.Spacing();

        var eigenerName = regel.RequiresOwnName;
        if (ImGui.Checkbox("Message must contain my name", ref eigenerName))
        {
            regel.RequiresOwnName = eigenerName;
            save();
        }

        ImGui.TextColored(Graufarbe, "Ignored in Tell - those are addressed to you anyway.");
    }

    /// <summary>
    /// Empfangen, gegeben oder beides. Steht nur bei Emotes, weil es im Chat keinen
    /// verlässlichen Empfänger gibt - wer in Say etwas sagt, sagt es an alle.
    /// </summary>
    private void DrawRichtung(Rule regel)
    {
        ImGui.SetNextItemWidth(220);

        if (ImGui.BeginCombo("Direction", Richtungsname(regel.Direction)))
        {
            foreach (var richtung in new[] { CountDirection.Received, CountDirection.Given, CountDirection.Both })
            {
                if (!ImGui.Selectable(Richtungsname(richtung), regel.Direction == richtung))
                    continue;

                regel.Direction = richtung;
                save();
            }

            ImGui.EndCombo();
        }

        ImGui.TextColored(Graufarbe, "The counter is always kept per other person, never for yourself.");
    }

    private static string Richtungsname(CountDirection richtung) => richtung switch
    {
        CountDirection.Received => "They emote me",
        CountDirection.Given => "I emote them",
        _ => "Both directions",
    };

    private void DrawBremse(Rule regel)
    {
        var sekunden = regel.CooldownSeconds;
        ImGui.SetNextItemWidth(220);

        if (ImGui.SliderInt("Cooldown per person", ref sekunden, 0, 60, sekunden == 0 ? "off" : "%d s"))
        {
            regel.CooldownSeconds = sekunden;
            save();
        }

        ImGui.TextColored(Graufarbe, sekunden == 0
            ? "Every single event counts - a macro can run the number up."
            : $"The same person counts again after {sekunden}s at the earliest.");
    }

    /// <summary>
    /// Emotes über ihren Namen auswählen. Gespeichert wird die Nummer, weil die
    /// sprachunabhängig ist - angezeigt der Name in der Client-Sprache.
    /// </summary>
    private void DrawEmoteAuswahl(Rule regel)
    {
        ImGui.TextUnformatted("Emotes");

        ImGui.TextColored(Graufarbe, regel.Direction == CountDirection.Given
            ? "Empty: every emote you aim at someone counts."
            : "Empty: every emote aimed at you counts.");

        foreach (var id in regel.EmoteIds.ToList())
        {
            if (ImGui.Button($"x##emo{id}"))
            {
                regel.EmoteIds.Remove(id);
                save();
                return;
            }

            ImGui.SameLine();
            ImGui.TextUnformatted(EmoteCatalog.NameOf(data, id));
        }

        ImGui.SetNextItemWidth(220);
        ImGui.InputTextWithHint("##emotesuche", "search emote...", ref emoteSuche, 64);

        if (string.IsNullOrWhiteSpace(emoteSuche))
            return;

        ImGui.BeginChild("emoteliste", new(0, 180), true);

        var treffer = 0;
        foreach (var emote in EmoteCatalog.All(data))
        {
            if (regel.EmoteIds.Contains(emote.Id))
                continue;

            if (emote.Name.Contains(emoteSuche, StringComparison.CurrentCultureIgnoreCase) is false &&
                emote.Command.Contains(emoteSuche, StringComparison.CurrentCultureIgnoreCase) is false)
                continue;

            if (++treffer > 40)
            {
                ImGui.TextColored(Graufarbe, "...more matches, refine the search.");
                break;
            }

            if (!ImGui.Selectable($"{emote.Name}  {emote.Command}##pick{emote.Id}"))
                continue;

            regel.EmoteIds.Add(emote.Id);
            emoteSuche = string.Empty;
            save();
            break;
        }

        if (treffer == 0)
            ImGui.TextColored(Graufarbe, "nothing found");

        ImGui.EndChild();
    }

    // ------------------------------------------------------------------ Text

    private void DrawVorlagen(Rule regel)
    {
        ImGui.Spacing();
        ImGui.SetNextItemWidth(280);

        // Der Unterschied ist nicht nur der Name: Mit Namen zeigt die Meldung den
        // Zaehler dieser Person, ohne Namen die Summe ueber alle.
        if (ImGui.BeginCombo("Shows", regel.ShowName ? "One person's count" : "The total across everyone"))
        {
            if (ImGui.Selectable("One person's count", regel.ShowName) && !regel.ShowName)
            {
                regel.ShowName = true;
                save();
            }

            if (ImGui.Selectable("The total across everyone", !regel.ShowName) && regel.ShowName)
            {
                regel.ShowName = false;
                save();
            }

            ImGui.EndCombo();
        }

        ImGui.TextColored(Graufarbe, regel.ShowName
            ? "How often THIS person did it."
            : "How often ANYONE did it.");

        ImGui.Separator();
        ImGui.TextColored(Graufarbe, "[Name] and [Count] are filled in.");

        var istEmote = IstEmoteRegel(regel);
        var beideRichtungen = istEmote && regel.Direction == CountDirection.Both;

        // Bei ausgehenden Emotes ist [Name] nicht der Urheber, sondern das Ziel - eine
        // Vorlage wie "[Name] hugged me" stuende dann genau verkehrt herum.
        if (istEmote && regel.Direction == CountDirection.Given)
            ImGui.TextColored(Hinweisfarbe, "[Name] is the person you emoted, not the sender.");

        ImGui.Spacing();

        if (beideRichtungen)
            ImGui.TextUnformatted("When they emote you");

        DrawVorlagenpaar(regel,
            () => regel.PersonalTemplate, v => regel.PersonalTemplate = v,
            () => regel.GeneralTemplate, v => regel.GeneralTemplate = v,
            kuerzel: string.Empty, ersatz: null);

        // Bei "beides" faellt sonst eine der beiden Meldungen verkehrt herum aus.
        // Leer bleiben duerfen die Felder trotzdem - dann gilt die Vorlage von oben.
        if (beideRichtungen)
        {
            ImGui.Separator();
            ImGui.TextUnformatted("When you emote them");
            ImGui.TextColored(Graufarbe, "Leave empty to use the templates above.");
            ImGui.Spacing();

            DrawVorlagenpaar(regel,
                () => regel.GivenPersonalTemplate, v => regel.GivenPersonalTemplate = v,
                () => regel.GivenGeneralTemplate, v => regel.GivenGeneralTemplate = v,
                kuerzel: "##given", ersatz: regel);
        }

        ImGui.Separator();

        var sekunden = regel.DisplaySeconds;
        ImGui.SetNextItemWidth(220);
        if (ImGui.SliderInt("Seconds shown", ref sekunden, 1, 60))
        {
            regel.DisplaySeconds = sekunden;
            save();
        }

        // Ueber oder unter dem Namen - Honorific kann beides.
        ImGui.SetNextItemWidth(220);

        if (!ImGui.BeginCombo("Position", regel.IsPrefix ? "Above the name" : "Below the name"))
            return;

        if (ImGui.Selectable("Above the name", regel.IsPrefix) && !regel.IsPrefix)
        {
            regel.IsPrefix = true;
            save();
        }

        if (ImGui.Selectable("Below the name", !regel.IsPrefix) && regel.IsPrefix)
        {
            regel.IsPrefix = false;
            save();
        }

        ImGui.EndCombo();
    }

    /// <summary>
    /// Die zwei Vorlagen einer Richtung. <paramref name="ersatz"/> ist gesetzt, wenn
    /// leere Felder auf die Vorlagen der Gegenrichtung zurückfallen - dann steht
    /// darunter, was stattdessen erscheint.
    /// </summary>
    private void DrawVorlagenpaar(Rule regel,
                                  Func<string> mitNamenLesen, Action<string> mitNamenSetzen,
                                  Func<string> ohneNamenLesen, Action<string> ohneNamenSetzen,
                                  string kuerzel, Rule? ersatz)
    {
        if (regel.ShowName)
        {
            DrawVorlage($"With name{kuerzel}", mitNamenLesen(), TitleTemplate.MaxPersonalFreeText,
                        mitNamenSetzen, mitNamen: true, ersatz: ersatz?.PersonalTemplate);

            ImGui.Spacing();
            ImGui.TextColored(Graufarbe, "Fallback if the name makes it too long:");
        }

        DrawVorlage($"Without name{kuerzel}", ohneNamenLesen(), TitleTemplate.MaxTitleLength,
                    ohneNamenSetzen, mitNamen: false, ersatz: ersatz?.GeneralTemplate);
    }

    /// <summary>
    /// Vorlage mit Zeichenzähler.
    ///
    /// Die feste Grenze rechnet mit dem längstmöglichen Vornamen. Sie zu überschreiten
    /// ist kein Fehler: Bei kürzeren Namen passt die Meldung weiterhin, bei längeren
    /// greift der Rückfall auf die namenlose Vorlage. Deshalb steht hier, **bis zu
    /// welcher Namenslänge** es reicht, statt einer pauschalen Warnung.
    /// </summary>
    private void DrawVorlage(string label, string wert, int grenze, Action<string> setzen,
                             bool mitNamen, string? ersatz = null)
    {
        var text = wert;
        ImGui.SetNextItemWidth(320);
        if (ImGui.InputText(label, ref text, 128))
        {
            setzen(text);
            save();
        }

        // Leeres Feld mit Rueckfall: Der Zeichenzaehler waere hier nur verwirrend,
        // gezeigt wird stattdessen, was tatsaechlich erscheint.
        if (ersatz is not null && string.IsNullOrWhiteSpace(text))
        {
            ImGui.TextColored(Graufarbe, $"      uses: {ersatz}");
            return;
        }

        // Steuerzeichen sind im Eingabefeld unsichtbar, lassen Honorific aber den
        // ganzen Titel verwerfen. Angezeigt wird trotzdem etwas - die gereinigte
        // Fassung -, aber wer sie loswird, hat auch die Vorlage wieder im Griff.
        if (TitleTemplate.HasControlCharacters(text))
        {
            ImGui.TextColored(Warnfarbe, "      Contains invisible control characters.");
            ImGui.SameLine();

            if (ImGui.Button($"Clean up##{label}"))
            {
                setzen(TitleTemplate.Clean(text));
                save();
            }
        }

        var benutzt = TitleTemplate.FreeTextLength(text);

        if (!mitNamen)
        {
            // Ohne Namen ist die Grenze hart - hier gibt es keinen Rueckfall mehr.
            var zuLang = benutzt > grenze;
            ImGui.TextColored(zuLang ? Warnfarbe : Graufarbe, $"      {benutzt} / {grenze}");

            if (zuLang)
            {
                ImGui.SameLine();
                ImGui.TextColored(Warnfarbe, "too long, Honorific will not show it");
            }

            return;
        }

        var maxName = TitleTemplate.MaxNameLengthFor(text);

        if (maxName >= 15)
        {
            ImGui.TextColored(Graufarbe, $"      {benutzt} / {grenze}  fits any name");
        }
        else if (maxName > 0)
        {
            ImGui.TextColored(Hinweisfarbe,
                $"      {benutzt} / {grenze}  fits names up to {maxName} characters");
            ImGui.TextColored(Graufarbe, "      Longer names fall back to the template below.");
        }
        else
        {
            ImGui.TextColored(Warnfarbe,
                $"      {benutzt} / {grenze}  too long for any name");
            ImGui.TextColored(Warnfarbe, "      The template below will always be used instead.");
        }
    }

    // --------------------------------------------------------------- Colours

    private void DrawFarben(Rule regel)
    {
        ImGui.Spacing();

        var eigene = regel.UseColours;
        if (ImGui.Checkbox("Use own colours", ref eigene))
        {
            regel.UseColours = eigene;
            save();
        }

        ImGui.TextColored(Graufarbe, "Off: the game's default title styling is used.");

        if (!regel.UseColours)
            return;

        ImGui.Separator();

        var farbe = new Vector3(regel.ColourR, regel.ColourG, regel.ColourB);
        ImGui.SetNextItemWidth(220);
        if (ImGui.ColorEdit3("Text", ref farbe))
        {
            (regel.ColourR, regel.ColourG, regel.ColourB) = (farbe.X, farbe.Y, farbe.Z);
            save();
        }

        var glow = new Vector3(regel.GlowR, regel.GlowG, regel.GlowB);
        ImGui.SetNextItemWidth(220);
        if (ImGui.ColorEdit3("Glow", ref glow))
        {
            (regel.GlowR, regel.GlowG, regel.GlowB) = (glow.X, glow.Y, glow.Z);
            save();
        }

        ImGui.Separator();

        var verlauf = regel.UseGradient;
        if (ImGui.Checkbox("Gradient", ref verlauf))
        {
            regel.UseGradient = verlauf;
            save();
        }

        if (!regel.UseGradient)
            return;

        var farbe3 = new Vector3(regel.Colour3R, regel.Colour3G, regel.Colour3B);
        ImGui.SetNextItemWidth(220);
        if (ImGui.ColorEdit3("Second colour", ref farbe3))
        {
            (regel.Colour3R, regel.Colour3G, regel.Colour3B) = (farbe3.X, farbe3.Y, farbe3.Z);
            save();
        }

        ImGui.SetNextItemWidth(220);

        if (!ImGui.BeginCombo("Style", Verlaufsname(regel.GradientStyle)))
            return;

        for (var stil = 0; stil <= 2; stil++)
        {
            if (!ImGui.Selectable(Verlaufsname(stil), regel.GradientStyle == stil))
                continue;

            regel.GradientStyle = stil;
            save();
        }

        ImGui.EndCombo();
    }

    /// <summary>Die Reihenfolge stammt aus Honorific: 0 Pulse, 1 Wave, 2 Static.</summary>
    private static string Verlaufsname(int stil) => stil switch
    {
        0 => "Pulse",
        1 => "Wave",
        _ => "Static",
    };

    // ---------------------------------------------------------------- Counts

    /// <summary>
    /// Alle Stände, regelübergreifend.
    ///
    /// Gezählt wird am Ereignis: Ein Dote steht unter „Dote", gleich wie viele Regeln
    /// ihn aufgreifen. Deshalb steht diese Liste hier oben und nicht in jeder Regel -
    /// dort wäre viermal dasselbe zu sehen.
    ///
    /// Nur zum Ansehen und Zurücksetzen. Eine tippbare Zahl lädt dazu ein, den Stand
    /// zu erfinden, und dann steht über dem Kopf eine Behauptung statt einer Zählung.
    /// </summary>
    private void DrawGlobaleZaehler()
    {
        ImGui.Spacing();

        if (counters.Entries.All(r => r.Value.Count == 0))
        {
            ImGui.TextColored(Graufarbe, "Nothing counted yet.");
            return;
        }

        ImGui.BeginChild("globalzaehler", new(0, 0));

        DrawUeberschrift("Totals");
        ImGui.TextColored(Graufarbe, "Every event, both directions at a glance.");
        ImGui.Spacing();
        DrawGesamtzahlen();

        ImGui.Spacing();
        DrawUeberschrift("Received  —  per person");
        DrawPersonenliste(gegeben: false);

        ImGui.Spacing();
        DrawUeberschrift("Given  —  per person");
        DrawPersonenliste(gegeben: true);

        ImGui.EndChild();
    }

    /// <summary>
    /// Deutlich abgesetzte Abschnittsüberschrift. `SeparatorText` gibt es in diesen
    /// Bindings nicht, also Linie und Text von Hand.
    /// </summary>
    private static void DrawUeberschrift(string text)
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted(text);
        ImGui.Separator();
        ImGui.Spacing();
    }

    /// <summary>Ein Ereignis je Zeile, empfangen und gegeben nebeneinander.</summary>
    private void DrawGesamtzahlen()
    {
        var basen = counters.Entries.Keys
            .Select(Basisschluessel)
            .Distinct()
            .Select(b => (Basis: b,
                          Empfangen: counters.GetTotal(b),
                          Gegeben: counters.GetTotal(b + CounterScope.GivenSuffix)))
            .Where(z => z.Empfangen > 0 || z.Gegeben > 0)
            .OrderByDescending(z => z.Empfangen + z.Gegeben)
            .ToList();

        if (!ImGui.BeginTable("gesamt", 4, ImGuiTableFlags.RowBg))
            return;

        ImGui.TableSetupColumn("##name");
        ImGui.TableSetupColumn("##recv");
        ImGui.TableSetupColumn("##giv");
        ImGui.TableSetupColumn("##reset");

        foreach (var (basis, empfangen, gegeben) in basen)
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(Beschreibe(basis).Name);

            ImGui.TableNextColumn();
            if (empfangen > 0)
                ImGui.TextUnformatted($"{empfangen} received");
            else
                ImGui.TextColored(Graufarbe, "-");

            ImGui.TableNextColumn();
            if (gegeben > 0)
                ImGui.TextUnformatted($"{gegeben} given");
            else
                ImGui.TextColored(Graufarbe, "-");

            ImGui.TableNextColumn();

            if (!ShiftKnopf($"Reset##ges{basis}", "reset this event for everyone"))
                continue;

            counters.Reset(basis);
            counters.Reset(basis + CounterScope.GivenSuffix);
            save();
            break;
        }

        ImGui.EndTable();
    }

    /// <summary>
    /// Wer wie oft - über alle Ereignisse einer Richtung zusammengefasst, aufklappbar
    /// bis auf das einzelne Emote.
    /// </summary>
    private void DrawPersonenliste(bool gegeben)
    {
        var proPerson = new Dictionary<CounterKey, List<(string Basis, int Anzahl)>>();

        foreach (var (schluessel, staende) in counters.Entries)
        {
            if (IstGegeben(schluessel) != gegeben)
                continue;

            foreach (var (person, anzahl) in staende)
            {
                if (anzahl <= 0)
                    continue;

                if (!proPerson.TryGetValue(person, out var liste))
                    proPerson[person] = liste = [];

                liste.Add((Basisschluessel(schluessel), anzahl));
            }
        }

        if (proPerson.Count == 0)
        {
            ImGui.TextColored(Graufarbe, gegeben
                ? "You have not emoted anyone yet."
                : "Nobody has emoted you yet.");

            return;
        }

        var kuerzel = gegeben ? "g" : "r";

        foreach (var (person, eintraege) in proPerson.OrderByDescending(p => p.Value.Sum(e => e.Anzahl)))
        {
            var welt = WorldCatalog.NameOf(data, person.WorldId);
            var wo = string.IsNullOrEmpty(welt) ? string.Empty : $" @ {welt}";
            var summe = eintraege.Sum(e => e.Anzahl);

            // Die Kennung hinter ### haelt den Aufklappzustand fest, auch wenn sich
            // die Zahl im Titel aendert.
            if (!ImGui.CollapsingHeader(
                    $"{person.Name}{wo}   {summe}###{kuerzel}{person.Name}{person.WorldId}"))
                continue;

            ImGui.Indent();

            foreach (var (basis, anzahl) in eintraege.OrderByDescending(e => e.Anzahl))
            {
                var schluessel = gegeben ? basis + CounterScope.GivenSuffix : basis;

                if (ShiftKnopf($"x##{kuerzel}{schluessel}{person.Name}{person.WorldId}",
                               "reset this person for this event"))
                {
                    counters.Remove(schluessel, person);
                    save();
                    break;
                }

                ImGui.SameLine();
                ImGui.TextUnformatted($"{anzahl,5}   {Beschreibe(basis).Name}");
            }

            ImGui.Unindent();
        }
    }

    /// <summary>
    /// Ein Knopf, der Shift verlangt.
    ///
    /// Für alles, was gesammelte Zählungen wegwirft: Die sind über Wochen entstanden
    /// und mit einem Klick nicht wiederherzustellen. Triggerwörter und Emote-Auswahlen
    /// tippt man dagegen in Sekunden neu - dort wäre die Hürde nur lästig.
    /// </summary>
    private static bool ShiftKnopf(string beschriftung, string was)
    {
        var shift = ImGui.GetIO().KeyShift;

        if (!shift)
            ImGui.BeginDisabled();

        var gedrueckt = ImGui.Button(beschriftung) && shift;

        if (!shift)
            ImGui.EndDisabled();

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip($"Hold Shift to {was}");

        return gedrueckt;
    }

    private static bool IstGegeben(string schluessel) =>
        schluessel.EndsWith(CounterScope.GivenSuffix, StringComparison.Ordinal);

    private static string Basisschluessel(string schluessel) =>
        IstGegeben(schluessel)
            ? schluessel[..^CounterScope.GivenSuffix.Length]
            : schluessel;

    /// <summary>
    /// Aus dem Schlüssel wird wieder etwas Lesbares: `e:146` ist „Dote", `t:&lt;3`
    /// das Wort selbst. Gespeichert wird die Nummer, weil sie sprachunabhängig ist -
    /// angezeigt der Name in der Client-Sprache.
    /// </summary>
    private (string Name, bool Gegeben) Beschreibe(string schluessel)
    {
        var gegeben = schluessel.EndsWith(CounterScope.GivenSuffix, StringComparison.Ordinal);

        var basis = gegeben
            ? schluessel[..^CounterScope.GivenSuffix.Length]
            : schluessel;

        const string legacy = CounterScope.EmotePrefix + "legacy:";

        if (basis.StartsWith(legacy, StringComparison.Ordinal))
            return (Regelname(basis[legacy.Length..], "any emote"), gegeben);

        if (basis.StartsWith(CounterScope.EmotePrefix, StringComparison.Ordinal))
        {
            var rest = basis[CounterScope.EmotePrefix.Length..];

            return (uint.TryParse(rest, out var id) ? EmoteCatalog.NameOf(data, id) : rest,
                    gegeben);
        }

        if (basis.StartsWith(CounterScope.TextPrefix, StringComparison.Ordinal))
            return ($"\"{basis[CounterScope.TextPrefix.Length..]}\"", gegeben);

        if (basis.StartsWith(CounterScope.RulePrefix, StringComparison.Ordinal))
            return (Regelname(basis[CounterScope.RulePrefix.Length..], "any message"), gegeben);

        return (basis, gegeben);
    }

    /// <summary>
    /// Name der Regel, an der ein Stand noch hängt. Gelöschte Regeln behalten ihren
    /// Stand - er ist nur nicht mehr zu benennen.
    /// </summary>
    private string Regelname(string id, string zusatz)
    {
        var regel = config.Rules.FirstOrDefault(r => r.Id == id);

        return regel is null
            ? $"deleted rule ({zusatz})"
            : $"{regel.Name} ({zusatz})";
    }
}
