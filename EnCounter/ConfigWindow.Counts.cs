using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Plugin.Services;
using EnCounter.Core;
using EnCounter.Ui;

namespace EnCounter;

public sealed partial class ConfigWindow
{
    // ---------------------------------------------------------------- Counts

    private enum Zaehlbereich { Totals, Received, Given }

    private static readonly Zaehlbereich[] Zaehlbereiche = [Zaehlbereich.Totals, Zaehlbereich.Received, Zaehlbereich.Given];

    private sealed record ZaehlerEintrag(string Basis, string Name, int Anzahl);

    private sealed record ZaehlerPerson(
        CounterKey Person, string Vorname, string Welt, string Id, List<ZaehlerEintrag> Eintraege, int Summe);

    private sealed record TotalsZeile(string Basis, string Name, int Empfangen, int Gegeben);

    /// <summary>Gefilterte Anzeigedaten der Counts; wird nur bei geänderten Daten oder Suchtext neu gebaut.</summary>
    private sealed class ZaehlerModell
    {
        public int Signatur;
        public string Suche = string.Empty;
        public List<TotalsZeile> Totals = [];
        public List<ZaehlerPerson> Empfangen = [];
        public List<ZaehlerPerson> Gegeben = [];
        public int TotalsAlle;
        public int EmpfangenAlle;
        public int GegebenAlle;
    }

    private Zaehlbereich zaehlbereich;
    private string zaehlerSuche = string.Empty;
    private ZaehlerModell? zaehlerModell;
    private readonly HashSet<string> aufgeklappt = [];

    /// <summary>Prüfsumme über Stände und Regelnamen: Ändert sie sich, stimmt das Modell nicht mehr.</summary>
    private int ZaehlerSignatur()
    {
        var hash = new HashCode();
        foreach (var (schluessel, staende) in counters.Entries)
        {
            hash.Add(schluessel);
            foreach (var (person, anzahl) in staende)
            {
                hash.Add(person);
                hash.Add(anzahl);
            }
        }

        foreach (var regel in config.Rules)
            hash.Add(regel.Name);

        // Die Namen im Modell sind übersetzt gebaut.
        hash.Add(Loc.Current);

        return hash.ToHashCode();
    }

    private ZaehlerModell HoleZaehlerModell()
    {
        var signatur = ZaehlerSignatur();
        if (zaehlerModell is { } m && m.Signatur == signatur && m.Suche == zaehlerSuche)
            return m;

        var suche = zaehlerSuche.Trim();
        bool Passt(string text) =>
            suche.Length == 0 || text.Contains(suche, StringComparison.CurrentCultureIgnoreCase);

        var alleTotals = counters.Entries.Keys
            .Select(Basisschluessel)
            .Distinct()
            .Select(b => new TotalsZeile(b, Beschreibe(b).Name,
                                         counters.GetTotal(b),
                                         counters.GetTotal(b + CounterScope.GivenSuffix)))
            .Where(z => z.Empfangen > 0 || z.Gegeben > 0)
            .OrderByDescending(z => z.Empfangen + z.Gegeben)
            .ToList();

        var empfangen = PersonenListe(gegeben: false);
        var gegeben = PersonenListe(gegeben: true);

        bool PersonPasst(ZaehlerPerson p) => Passt(p.Person.Name) || p.Eintraege.Any(e => Passt(e.Name));

        return zaehlerModell = new ZaehlerModell
        {
            Signatur = signatur,
            Suche = zaehlerSuche,
            Totals = [.. alleTotals.Where(z => Passt(z.Name))],
            Empfangen = [.. empfangen.Where(PersonPasst)],
            Gegeben = [.. gegeben.Where(PersonPasst)],
            TotalsAlle = alleTotals.Count,
            EmpfangenAlle = empfangen.Count,
            GegebenAlle = gegeben.Count,
        };
    }

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
        var modell = HoleZaehlerModell();
        var abstand = ImGui.GetStyle().ItemSpacing.X;
        var suchbreite = Widgets.S(200);

        string Beschriftung(Zaehlbereich b) => b switch
        {
            Zaehlbereich.Totals => Loc.T("Totals", "Gesamt"),
            Zaehlbereich.Received => $"{Loc.T("Received", "Empfangen")} · {Personen(modell.EmpfangenAlle)}",
            _ => $"{Loc.T("Given", "Gegeben")} · {Personen(modell.GegebenAlle)}",
        };

        Widgets.Segmented("zaehlbereich", Zaehlbereiche, Beschriftung, ref zaehlbereich,
            Math.Max(Widgets.S(300), ImGui.GetContentRegionAvail().X - suchbreite - abstand));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(suchbreite);
        ImGui.InputTextWithHint("##zaehlersuche", Loc.T("Search", "Suchen"), ref zaehlerSuche, 64);
        ImGui.Spacing();

        if (modell.TotalsAlle == 0 && modell.EmpfangenAlle == 0 && modell.GegebenAlle == 0)
        {
            ImGui.TextColored(Theme.Muted, Loc.T("Nothing counted yet. Counts appear here as soon as a rule counts something.",
                                                 "Noch nichts gezählt. Zähler erscheinen hier, sobald eine Regel etwas zählt."));
            return;
        }

        var hilfe = Loc.T("Counters belong to the event and are shared by all rules.\n" +
                          "Totals counts events; Received and Given list people.\n" +
                          "Counts can only be reset, not typed in.",
                          "Zähler gehören dem Event und werden von allen Regeln geteilt.\n" +
                          "Gesamt zählt Events; Empfangen und Gegeben listen Personen.\n" +
                          "Zähler lassen sich nur zurücksetzen, nicht eintippen.");
        Widgets.SectionHeader(zaehlbereich switch
        {
            Zaehlbereich.Totals => Loc.T("Totals", "Gesamt"),
            Zaehlbereich.Received => Loc.T("Received", "Empfangen"),
            _ => Loc.T("Given", "Gegeben"),
        }, hilfe);

        ImGui.BeginChild("zaehlerinhalt", new Vector2(0, 0));
        switch (zaehlbereich)
        {
            case Zaehlbereich.Totals:
                DrawGesamtzahlen(modell);
                break;
            case Zaehlbereich.Received:
                DrawZaehlerPersonen(modell.Empfangen, modell.EmpfangenAlle, gegeben: false);
                break;
            default:
                DrawZaehlerPersonen(modell.Gegeben, modell.GegebenAlle, gegeben: true);
                break;
        }

        ImGui.EndChild();
    }

    // Deutsch ohne Substantiv, damit die Beschriftungen der Umschaltleiste in ihre Felder passen.
    private static string Personen(int anzahl) =>
        Loc.Current == Language.German ? anzahl.ToString() : anzahl == 1 ? "1 person" : $"{anzahl} people";

    private static void RechtsText(string text, Vector4 farbe)
    {
        var pos = ImGui.GetCursorScreenPos();
        var breite = ImGui.CalcTextSize(text).X;
        ImGui.SetCursorScreenPos(new Vector2(pos.X + ImGui.GetContentRegionAvail().X - breite, pos.Y));
        ImGui.TextColored(farbe, text);
    }

    /// <summary>Ein Ereignis je Zeile, empfangen und gegeben nebeneinander.</summary>
    private void DrawGesamtzahlen(ZaehlerModell modell)
    {
        if (modell.Totals.Count == 0)
        {
            ImGui.TextColored(Theme.Muted, Loc.T("No matches.", "Keine Treffer."));
            return;
        }

        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.PadOuterX;
        if (!ImGui.BeginTable("gesamt", 4, flags))
            return;

        ImGui.TableSetupColumn("Event", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(Loc.T("Received", "Empfangen"), ImGuiTableColumnFlags.WidthFixed, Widgets.S(90));
        ImGui.TableSetupColumn(Loc.T("Given", "Gegeben"), ImGuiTableColumnFlags.WidthFixed, Widgets.S(90));
        ImGui.TableSetupColumn("##reset", ImGuiTableColumnFlags.WidthFixed, Widgets.S(36));
        ImGui.TableHeadersRow();

        string? zuruecksetzen = null;

        foreach (var zeile in modell.Totals)
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(zeile.Name);

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            RechtsText(zeile.Empfangen > 0 ? zeile.Empfangen.ToString() : "-",
                zeile.Empfangen > 0 ? Theme.Text : Theme.Muted);

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            RechtsText(zeile.Gegeben > 0 ? zeile.Gegeben.ToString() : "-",
                zeile.Gegeben > 0 ? Theme.Text : Theme.Muted);

            ImGui.TableNextColumn();
            if (Widgets.ShiftButton($"ges{zeile.Basis}", FontAwesomeIcon.Undo, new("reset this event for everyone", "dieses Event für alle zurückzusetzen")))
                zuruecksetzen = zeile.Basis;
        }

        ImGui.EndTable();

        if (zuruecksetzen is not { } basis)
            return;

        counters.Reset(basis);
        counters.Reset(basis + CounterScope.GivenSuffix);

        // Ohne das schluckt die Bremse den naechsten Treffer, und es sieht aus,
        // als wuerde nach dem Zuruecksetzen gar nicht mehr gezaehlt.
        cooldowns.Clear(basis);
        cooldowns.Clear(basis + CounterScope.GivenSuffix);

        save();
    }

    /// <summary>
    /// Wer in dieser Richtung gezählt wurde, mit den Ereignissen dahinter - die
    /// höchsten Stände zuerst.
    ///
    /// Getrennt vom Zeichnen, weil die Anzahl schon für die Umschaltleiste gebraucht
    /// wird, bevor feststeht, welcher Bereich offen ist.
    /// </summary>
    private List<ZaehlerPerson> PersonenListe(bool gegeben)
    {
        var proPerson = new Dictionary<CounterKey, List<ZaehlerEintrag>>();

        foreach (var (schluessel, staende) in counters.Entries)
        {
            if (IstGegeben(schluessel) != gegeben)
                continue;

            var basis = Basisschluessel(schluessel);
            var name = Beschreibe(basis).Name;

            foreach (var (person, anzahl) in staende)
            {
                if (anzahl <= 0)
                    continue;

                if (!proPerson.TryGetValue(person, out var liste))
                    proPerson[person] = liste = [];

                liste.Add(new ZaehlerEintrag(basis, name, anzahl));
            }
        }

        var kuerzel = gegeben ? "g" : "r";

        return [.. proPerson
            .Select(p => new ZaehlerPerson(
                p.Key,
                TitleTemplate.FirstName(p.Key.Name),
                WorldCatalog.NameOf(data, p.Key.WorldId),
                $"{kuerzel}{p.Key.Name}{p.Key.WorldId}",
                [.. p.Value.OrderByDescending(e => e.Anzahl)],
                p.Value.Sum(e => e.Anzahl)))
            .OrderByDescending(p => p.Summe)];
    }

    private void DrawZaehlerPersonen(List<ZaehlerPerson> personen, int alle, bool gegeben)
    {
        if (alle == 0)
        {
            ImGui.TextColored(Theme.Muted, gegeben
                ? Loc.T("You have not emoted anyone yet.", "Du hast noch kein Emote auf jemanden gerichtet.")
                : Loc.T("Nobody has emoted you yet.", "Noch niemand hat ein Emote auf dich gerichtet."));

            return;
        }

        if (personen.Count == 0)
        {
            ImGui.TextColored(Theme.Muted, Loc.T("No matches.", "Keine Treffer."));
            return;
        }

        foreach (var person in personen)
        {
            var offen = aufgeklappt.Contains(person.Id);

            var geklickt = Widgets.Card(person.Id, breite =>
            {
                var start = ImGui.GetCursorScreenPos();
                ImGui.TextUnformatted(person.Vorname);
                if (person.Welt.Length > 0)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(Theme.Muted, person.Welt);
                }

                var summe = person.Summe.ToString();
                ImGui.SameLine();
                ImGui.SetCursorScreenPos(new Vector2(start.X + breite - ImGui.CalcTextSize(summe).X, start.Y));
                ImGui.TextUnformatted(summe);

                if (!offen)
                    return;

                ImGui.Spacing();

                var knopf = Widgets.IconButtonWidth(FontAwesomeIcon.Undo);
                foreach (var eintrag in person.Eintraege)
                {
                    var zeile = ImGui.GetCursorScreenPos();
                    ImGui.AlignTextToFramePadding();

                    var zahl = eintrag.Anzahl.ToString();
                    ImGui.SetCursorScreenPos(new Vector2(zeile.X + Widgets.S(40) - ImGui.CalcTextSize(zahl).X, zeile.Y));
                    ImGui.TextUnformatted(zahl);

                    ImGui.SetCursorScreenPos(new Vector2(zeile.X + Widgets.S(52), zeile.Y));
                    ImGui.TextUnformatted(eintrag.Name);

                    ImGui.SetCursorScreenPos(new Vector2(zeile.X + breite - knopf, zeile.Y));
                    if (!Widgets.ShiftButton($"x{eintrag.Basis}", FontAwesomeIcon.Undo, new("reset this person for this event", "diese Person für dieses Event zurückzusetzen")))
                        continue;

                    var schluessel = gegeben ? eintrag.Basis + CounterScope.GivenSuffix : eintrag.Basis;
                    counters.Remove(schluessel, person.Person);
                    cooldowns.Clear(schluessel, person.Person);
                    save();
                }
            }, selected: offen);

            if (geklickt && !aufgeklappt.Remove(person.Id))
                aufgeklappt.Add(person.Id);

            ImGui.Spacing();
        }
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
            return (Regelname(basis[legacy.Length..], Loc.T("any emote", "beliebiges Emote")), gegeben);

        if (basis.StartsWith(CounterScope.EmotePrefix, StringComparison.Ordinal))
        {
            var rest = basis[CounterScope.EmotePrefix.Length..];

            return (uint.TryParse(rest, out var id) ? EmoteCatalog.NameOf(data, id) : rest,
                    gegeben);
        }

        if (basis.StartsWith(CounterScope.TextPrefix, StringComparison.Ordinal))
            return ($"\"{basis[CounterScope.TextPrefix.Length..]}\"", gegeben);

        if (basis.StartsWith(CounterScope.RulePrefix, StringComparison.Ordinal))
            return (Regelname(basis[CounterScope.RulePrefix.Length..], Loc.T("any message", "beliebige Nachricht")), gegeben);

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
            ? $"{Loc.T("deleted rule", "gelöschte Regel")} ({zusatz})"
            : $"{regel.Name} ({zusatz})";
    }
}
