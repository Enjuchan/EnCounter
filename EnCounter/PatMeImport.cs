using System.Text.Json;
using EnCounter.Core;

namespace EnCounter;

/// <summary>
/// Übernimmt Zählerstände aus PatMe und PatMe Honorific.
///
/// **Nur Zahlen, keine Regeln.** Der Import trägt in die gemeinsame Liste ein, was
/// dort gezählt wurde; welche Regeln daraus etwas machen, entscheidet man selbst.
///
/// **Zwei Quellen, die sich überschneiden.** PatMe zählt seit jeher, aber nur nach
/// Emote-*Namen* und ohne Richtung. PatMe Honorific zählt erst seit seiner
/// Aktivierung, dafür nach Emote-Nummer und Richtung getrennt - es kennt damit Emotes,
/// die PatMe zusammenwirft. Bei Überschneidung gilt der **höhere** Stand: Beide haben
/// dasselbe gezählt, nur unterschiedlich lange.
///
/// **Wer geemotet hat, weiß keine der beiden.** Importiertes landet deshalb unter
/// einer Sammelperson, statt eine Aufschlüsselung vorzutäuschen, die es nie gab.
/// </summary>
public static class PatMeImport
{
    /// <summary>Unter diesem Namen liegen importierte Summen im Zähler.</summary>
    public const string SammelName = "(imported)";

    /// <summary>
    /// PatMe benennt seine Zähler mit englischen Emote-Namen. Die Zuordnung fest
    /// einzutragen ist verlässlicher, als sie über die Spieltabelle zu suchen: Bei
    /// einem deutschen Client heißt der Emote „Streicheln" und würde nie gefunden.
    ///
    /// „Hug" steht dort für zwei Emotes - 112 `/hug` und 113 `/embrace`. Aufteilen
    /// lässt sich die Summe nicht, sie landet ganz beim ersten. PatMe Honorific führt
    /// beide getrennt und ist dort die bessere Quelle.
    /// </summary>
    private static readonly Dictionary<string, uint> PatMeEmotes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Pet"] = 105,
        ["Dote"] = 146,
        ["Hug"] = 112,
    };

    /// <summary>Ein gefundener Stand, schon auf Emote-Nummern gebracht.</summary>
    public sealed record Stand(uint EmoteId, bool Gegeben, ulong Cid, int Anzahl, string Quelle);

    /// <summary>Ein Charakter, wie ihn PatMe benennt.</summary>
    public sealed record Charakter(ulong Cid, string Name);

    public sealed record Fund(
        List<Stand> Staende,
        List<Charakter> Charaktere,
        ulong EigeneCid,
        List<string> Hinweise,
        bool HonorificGefunden,
        bool PatMeGefunden)
    {
        public Charakter? Eigener =>
            EigeneCid == 0 ? null : Charaktere.FirstOrDefault(c => c.Cid == EigeneCid);

        public bool Leer => Staende.All(s => s.Anzahl <= 0);

        /// <summary>Welche Quellen tatsächlich etwas beigetragen haben.</summary>
        public bool HatAus(string quelle) => Staende.Any(s => s.Quelle == quelle && s.Anzahl > 0);

        /// <summary>Was übernommen würde, samt dem, was jede Quelle dazu sagt.</summary>
        public sealed record Posten(uint EmoteId, bool Gegeben, int Anzahl,
                                    IReadOnlyList<(string Quelle, int Wert)> Quellen)
        {
            /// <summary>Sind sich die Quellen uneinig? Dann lohnt ein Blick.</summary>
            public bool Uneinig => Quellen.Count > 1 && Quellen.Select(q => q.Wert).Distinct().Count() > 1;
        }

        /// <summary>
        /// Was übernommen würde: je Emote und Richtung eine Zahl.
        ///
        /// Innerhalb einer Quelle werden die gewählten Charaktere summiert, über die
        /// Quellen hinweg gilt der **höhere** Wert.
        ///
        /// Richtig ist das nicht immer: Lief PatMe eine Weile nicht und Honorific
        /// schon, fehlt dem einen genau dieser Zeitraum, und das Maximum liegt zu
        /// niedrig. Die Summe wäre aber falscher, sobald beide gleichzeitig liefen -
        /// dann zählte jedes Emote doppelt. Zeitstempel gibt es in keiner der Dateien,
        /// also bleibt eine Wette. Die auf die kleinere Zahl erfindet wenigstens
        /// nichts.
        /// </summary>
        public IEnumerable<Posten> Uebernehmbar(bool alleCharaktere)
        {
            var gewaehlt = alleCharaktere
                ? null
                : Eigener?.Cid ?? (EigeneCid == 0 ? null : EigeneCid);

            var jeQuelle = Staende
                .Where(s => s.Anzahl > 0 && (gewaehlt is null || s.Cid == gewaehlt))
                .GroupBy(s => (s.EmoteId, s.Gegeben, s.Quelle))
                .Select(g => (g.Key.EmoteId, g.Key.Gegeben, g.Key.Quelle, Summe: g.Sum(s => s.Anzahl)));

            return jeQuelle
                .GroupBy(x => (x.EmoteId, x.Gegeben))
                .Select(g => new Posten(
                    g.Key.EmoteId,
                    g.Key.Gegeben,
                    g.Max(x => x.Summe),
                    [.. g.Select(x => (x.Quelle, x.Summe)).OrderBy(x => x.Quelle)]))
                .OrderBy(p => p.Gegeben)
                .ThenByDescending(p => p.Anzahl);
        }
    }

    public static Fund Lesen(string konfigOrdner, ulong eigeneCid)
    {
        var hinweise = new List<string>();

        var honorificDatei = Path.Combine(konfigOrdner, "PatMeHonorific.json");
        var patMeDatei = Path.Combine(konfigOrdner, "PatMe.json");

        var staende = new List<Stand>();
        var charaktere = LiesPatMe(patMeDatei, staende, hinweise);

        LiesHonorific(honorificDatei, staende, hinweise);

        if (staende.Count > 0 && eigeneCid == 0)
            hinweise.Add("Not logged in - the current character cannot be told apart.");
        else if (staende.Count > 0 && staende.All(s => s.Cid != eigeneCid))
            hinweise.Add("Neither plugin has counts for the character you are playing.");

        return new Fund(staende, charaktere, eigeneCid, hinweise,
                        File.Exists(honorificDatei), File.Exists(patMeDatei));
    }

    /// <summary>
    /// Trägt die Stände unter der Sammelperson ein - **gesetzt, nicht addiert**.
    /// Zweimal importieren verdoppelt damit nichts, und was echte Personen beigetragen
    /// haben, bleibt unberührt.
    /// </summary>
    public static int Anwenden(Fund fund, CounterStore counters, bool alleCharaktere)
    {
        var person = new CounterKey(SammelName, 0);
        var eingetragen = 0;

        foreach (var posten in fund.Uebernehmbar(alleCharaktere))
        {
            var schluessel = CounterScope.EmotePrefix + posten.EmoteId +
                             (posten.Gegeben ? CounterScope.GivenSuffix : string.Empty);

            counters.Set(schluessel, person, posten.Anzahl);
            eingetragen++;
        }

        return eingetragen;
    }

    // ----------------------------------------------------------------- PatMe

    private static List<Charakter> LiesPatMe(string pfad, List<Stand> staende, List<string> hinweise)
    {
        var charaktere = new List<Charakter>();

        if (!File.Exists(pfad))
            return charaktere;

        try
        {
            using var datei = JsonDocument.Parse(File.ReadAllText(pfad));

            if (!datei.RootElement.TryGetProperty("EmoteData", out var liste))
                return charaktere;

            var unbekannt = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var besitzer in liste.EnumerateArray())
            {
                var cid = Zahl(besitzer, "CID");
                charaktere.Add(new Charakter(cid, Text(besitzer, "Name") ?? "?"));

                if (!besitzer.TryGetProperty("Counters", out var eintraege))
                    continue;

                foreach (var z in eintraege.EnumerateArray())
                {
                    var name = Text(z, "Name");
                    if (string.IsNullOrEmpty(name)) continue;
                    if (!z.TryGetProperty("Value", out var v) || !v.TryGetInt32(out var wert)) continue;
                    if (wert <= 0) continue;

                    if (!PatMeEmotes.TryGetValue(name, out var emoteId))
                    {
                        unbekannt.Add(name);
                        continue;
                    }

                    // PatMe kennt keine Richtung - es zaehlt nur Empfangenes.
                    staende.Add(new Stand(emoteId, false, cid, wert, "PatMe"));
                }
            }

            foreach (var k in unbekannt)
                hinweise.Add($"PatMe counter \"{k}\" has no known emote here and was skipped.");
        }
        catch (Exception e)
        {
            hinweise.Add($"PatMe.json could not be read: {e.GetType().Name}");
        }

        return charaktere;
    }

    // -------------------------------------------------------- PatMeHonorific

    /// <summary>
    /// Liest `Counters`. Der Schlüssel trägt Emote-Nummer, Richtung und Charakter -
    /// alles, was gebraucht wird, ohne Namen raten zu müssen.
    ///
    /// Die Liste bleibt leer, solange das Plugin deaktiviert ist; es zählt erst, wenn
    /// es läuft.
    /// </summary>
    private static void LiesHonorific(string pfad, List<Stand> staende, List<string> hinweise)
    {
        if (!File.Exists(pfad))
            return;

        try
        {
            using var datei = JsonDocument.Parse(File.ReadAllText(pfad));

            if (!datei.RootElement.TryGetProperty("Counters", out var liste))
                return;

            foreach (var eintrag in liste.EnumerateArray())
            {
                if (!eintrag.TryGetProperty("Key", out var schluessel)) continue;
                if (!eintrag.TryGetProperty("Value", out var v) || !v.TryGetInt32(out var wert)) continue;
                if (wert <= 0) continue;

                var emoteId = (uint)Zahl(schluessel, "EmoteId");
                if (emoteId == 0) continue;

                // Richtung wie bei uns: 0 empfangen, alles andere gegeben. Die
                // Voreinstellungen dort heissen "Receiving ..." und stehen auf 0.
                var gegeben = Zahl(schluessel, "Direction") != 0;

                staende.Add(new Stand(emoteId, gegeben, Zahl(schluessel, "CharacterId"),
                                      wert, "PatMe Honorific"));
            }
        }
        catch (Exception e)
        {
            hinweise.Add($"PatMeHonorific.json could not be read: {e.GetType().Name}");
        }
    }

    // ------------------------------------------------------------- Werkzeug

    private static string? Text(JsonElement e, string feld) =>
        e.TryGetProperty(feld, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static ulong Zahl(JsonElement e, string feld) =>
        e.TryGetProperty(feld, out var v) && v.TryGetUInt64(out var wert) ? wert : 0ul;
}
