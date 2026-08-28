using System.Text.Json;
using System.Text.RegularExpressions;
using EnCounter.Core;

namespace EnCounter;

/// <summary>
/// Übernimmt Regeln und Zählerstände aus PatMe und PatMeHonorific.
///
/// **Was übernommen werden kann und was nicht:** PatMeHonorific hält Regeln, die den
/// unseren ähneln - Emote-Nummern, Vorlage, Prefix, Richtung. PatMe hält die Zahlen,
/// aber nur als **Summe je Emote und eigenem Charakter**; wer wie oft geemotet hat,
/// weiß es nicht. Importierte Stände landen deshalb unter einer Sammelperson, statt
/// eine Aufschlüsselung vorzutäuschen, die es nie gab.
/// </summary>
public static partial class PatMeImport
{
    /// <summary>Unter diesem Namen liegen importierte Summen im Zähler.</summary>
    public const string SammelName = "(imported)";

    /// <summary>
    /// PatMe benennt seine Zähler mit englischen Emote-Namen. Die Zuordnung fest
    /// einzutragen ist verlässlicher, als sie über die Spieltabelle zu suchen: Bei
    /// einem deutschen Client heißt der Emote „Streicheln" und würde nie gefunden.
    ///
    /// „Hug" umfasst zwei Emotes - 112 `/hug` und 113 `/embrace`. PatMeHonorific
    /// zählt beide zusammen, und das ist richtig so.
    /// </summary>
    private static readonly Dictionary<string, uint[]> PatMeEmotes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Pet"] = [105],
        ["Dote"] = [146],
        ["Hug"] = [112, 113],
    };

    [GeneratedRegex(@"\{\{\s*(\w+)\s*\}\}")]
    private static partial Regex ScribanPlatzhalter();

    /// <summary>
    /// Das Ergebnis beider Quellen. Sie sind **zwei getrennte Plugins**: Die Regeln
    /// stehen in PatMeHonorific, die Zahlen in PatMe. Wer nur eines davon installiert
    /// hat, soll sehen, welches fehlt, statt zu rätseln, warum die Hälfte leer ist.
    /// </summary>
    /// <summary>Ein Charakter aus PatMe mit seinen Ständen.</summary>
    public sealed record Charakter(ulong Cid, string Name, Dictionary<string, int> Zaehler)
    {
        public int Gesamt => Zaehler.Values.Sum();
    }

    public sealed record Fund(
        List<Rule> Regeln,
        List<Charakter> Charaktere,
        ulong EigeneCid,
        List<string> Hinweise,
        bool HonorificGefunden,
        bool PatMeGefunden)
    {
        /// <summary>
        /// Der gerade gespielte Charakter, falls PatMe ihn kennt. Null heißt: nicht
        /// eingeloggt, oder dieser Charakter hat dort noch nie gezählt.
        /// </summary>
        public Charakter? Eigener =>
            EigeneCid == 0 ? null : Charaktere.FirstOrDefault(c => c.Cid == EigeneCid);

        public bool Leer => Regeln.Count == 0 && Charaktere.Count == 0;

        /// <summary>
        /// Die Stände, die übernommen werden. Voreingestellt ist der eigene Charakter:
        /// EnCounter zählt für den, der gerade gespielt wird, und die Stände fremder
        /// Charaktere hier hineinzurechnen wäre eine stille Fälschung.
        /// </summary>
        public Dictionary<string, int> SummenFuer(bool alleCharaktere)
        {
            var quelle = alleCharaktere
                ? Charaktere
                : Eigener is { } eigener ? [eigener] : new List<Charakter>();

            var summen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var c in quelle)
                foreach (var (name, wert) in c.Zaehler)
                    summen[name] = summen.GetValueOrDefault(name) + wert;

            return summen;
        }
    }

    /// <summary>
    /// Liest beide Dateien, ohne etwas zu verändern. Fehlende Dateien sind kein
    /// Fehler - dann gibt es eben nichts zu holen.
    /// </summary>
    /// <summary>
    /// Liest beide Dateien, ohne etwas zu verändern.
    ///
    /// <paramref name="eigeneCid"/> ist die ContentId des gerade gespielten
    /// Charakters, 0 wenn nicht eingeloggt.
    /// </summary>
    public static Fund Lesen(string konfigOrdner, ulong eigeneCid)
    {
        var hinweise = new List<string>();

        var honorificDatei = Path.Combine(konfigOrdner, "PatMeHonorific.json");
        var patMeDatei = Path.Combine(konfigOrdner, "PatMe.json");

        var regeln = LiesRegeln(honorificDatei, hinweise);
        var charaktere = LiesCharaktere(patMeDatei, hinweise);

        var fund = new Fund(regeln, charaktere, eigeneCid, hinweise,
                            File.Exists(honorificDatei), File.Exists(patMeDatei));

        // Zaehler ohne passende Regel waeren verloren. Fuer die bekannten Emotes laesst
        // sich eine Regel nachlegen, damit die Zahl ein Zuhause hat. Geprueft wird
        // gegen alle Charaktere, damit die Regel auch dann entsteht, wenn nur ein
        // anderer Charakter dort Staende hat.
        foreach (var (emote, anzahl) in fund.SummenFuer(alleCharaktere: true))
        {
            if (anzahl <= 0 || !PatMeEmotes.TryGetValue(emote, out var ids))
                continue;

            if (regeln.Any(r => r.EmoteIds.Intersect(ids).Any()))
                continue;

            regeln.Add(EmoteRegel(emote, ids));
            hinweise.Add($"No PatMe Honorific rule for {emote} - a plain one was created.");
        }

        if (charaktere.Count > 0 && eigeneCid == 0)
            hinweise.Add("Not logged in - the current character cannot be told apart.");
        else if (charaktere.Count > 0 && fund.Eigener is null)
            hinweise.Add("PatMe has no counts for the character you are playing.");

        return fund;
    }

    /// <summary>
    /// Trägt den Fund ein. Vorhandene Regeln bleiben unberührt.
    ///
    /// Nimmt bewusst die Regelliste statt der ganzen Konfiguration - so kommt diese
    /// Klasse ohne Dalamud aus und lässt sich gegen echte Dateien prüfen.
    /// </summary>
    public static int Anwenden(Fund fund, List<Rule> regeln, CounterStore counters,
                               bool alleCharaktere)
    {
        var summen = fund.SummenFuer(alleCharaktere);
        var uebernommen = 0;

        foreach (var regel in fund.Regeln)
        {
            regel.EnsureId();
            regeln.Add(regel);
            uebernommen++;

            var anzahl = PassendeSumme(summen, regel.EmoteIds);
            if (anzahl > 0)
                counters.Set(regel.Id, new CounterKey(SammelName, 0), anzahl);
        }

        return uebernommen;
    }

    private static int PassendeSumme(Dictionary<string, int> summen, List<uint> emoteIds)
    {
        foreach (var (name, anzahl) in summen)
        {
            if (!PatMeEmotes.TryGetValue(name, out var ids))
                continue;

            if (emoteIds.Intersect(ids).Any())
                return anzahl;
        }

        return 0;
    }

    // ------------------------------------------------------- PatMeHonorific

    private static List<Rule> LiesRegeln(string pfad, List<string> hinweise)
    {
        var regeln = new List<Rule>();

        if (!File.Exists(pfad))
            return regeln;

        try
        {
            using var datei = JsonDocument.Parse(File.ReadAllText(pfad));

            if (!datei.RootElement.TryGetProperty("EmoteConfigs", out var liste))
                return regeln;

            var farbenVerloren = false;

            foreach (var eintrag in liste.EnumerateArray())
            {
                var regel = AlsRegel(eintrag, hinweise, ref farbenVerloren);
                if (regel is not null)
                    regeln.Add(regel);
            }

            if (farbenVerloren)
                hinweise.Add("Colours were not carried over - set them again under Colours.");
        }
        catch (Exception e)
        {
            hinweise.Add($"PatMeHonorific.json could not be read: {e.GetType().Name}");
        }

        return regeln;
    }

    private static Rule? AlsRegel(JsonElement e, List<string> hinweise, ref bool farbenVerloren)
    {
        var name = Text(e, "Name") ?? "Imported rule";
        var vorlage = Text(e, "TitleTemplate") ?? string.Empty;

        var emoteIds = new List<uint>();
        if (e.TryGetProperty("EmoteIds", out var ids) && ids.ValueKind == JsonValueKind.Array)
            emoteIds.AddRange(ids.EnumerateArray().Select(i => i.GetUInt32()));

        if (emoteIds.Count == 0)
        {
            hinweise.Add($"\"{name}\" has no emotes and was skipped.");
            return null;
        }

        if (Farbe(e, "Color") || Farbe(e, "Glow"))
            farbenVerloren = true;

        var (uebersetzt, sonderfall) = UebersetzeVorlage(vorlage);
        if (sonderfall)
            hinweise.Add($"\"{name}\" used a session or combo count - it now shows the total.");

        var regel = new Rule
        {
            Name = name,
            Enabled = Wahrheit(e, "Enabled") ?? true,
            Channels = [ChatChannel.StandardEmote, ChatChannel.CustomEmote],
            EmoteIds = emoteIds,
            Direction = Richtung(e),
            IsPrefix = Wahrheit(e, "IsPrefix") ?? true,

            // PatMeHonorific kennt keine Namen in Vorlagen - es gibt dort nur
            // Gesamtzahlen. Also die namenlose Vorlage, und das bewusst.
            ShowName = false,
            GeneralTemplate = uebersetzt,
            PersonalTemplate = uebersetzt,
        };

        regel.EnsureId();
        return regel;
    }

    /// <summary>
    /// Scriban-Platzhalter in unsere übersetzen. PatMeHonorific kennt drei:
    /// `total_count`, `session_count` und `combo_count` - wir nur die Gesamtzahl.
    /// </summary>
    private static (string Vorlage, bool Sonderfall) UebersetzeVorlage(string vorlage)
    {
        var sonderfall = false;

        var neu = ScribanPlatzhalter().Replace(vorlage, treffer =>
        {
            var feld = treffer.Groups[1].Value;

            if (!feld.Equals("total_count", StringComparison.OrdinalIgnoreCase))
                sonderfall = true;

            return "[Count]";
        });

        return (neu.Trim(), sonderfall);
    }

    /// <summary>
    /// PatMeHonorific zählt seine Richtungen in derselben Reihenfolge wie wir:
    /// 0 empfangen, 1 gegeben, alles andere beides. Bei den Voreinstellungen dort
    /// steht 0, und die heißen „Receiving ...".
    /// </summary>
    private static CountDirection Richtung(JsonElement e)
    {
        if (!e.TryGetProperty("Direction", out var d) || !d.TryGetInt32(out var wert))
            return CountDirection.Received;

        return wert switch
        {
            0 => CountDirection.Received,
            1 => CountDirection.Given,
            _ => CountDirection.Both,
        };
    }

    // ----------------------------------------------------------------- PatMe

    /// <summary>
    /// Liest die Stände **getrennt je Charakter**. PatMe führt sie so, und die Wahl,
    /// welche davon übernommen werden, gehört dem Nutzer - zusammengerechnet wären
    /// sie nicht mehr auseinanderzuhalten.
    /// </summary>
    private static List<Charakter> LiesCharaktere(string pfad, List<string> hinweise)
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
                var zaehler = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                if (besitzer.TryGetProperty("Counters", out var liste2))
                {
                    foreach (var z in liste2.EnumerateArray())
                    {
                        var name = Text(z, "Name");
                        if (string.IsNullOrEmpty(name)) continue;
                        if (!z.TryGetProperty("Value", out var v) || !v.TryGetInt32(out var wert)) continue;

                        zaehler[name] = zaehler.GetValueOrDefault(name) + wert;

                        if (wert > 0 && !PatMeEmotes.ContainsKey(name))
                            unbekannt.Add(name);
                    }
                }

                var cid = besitzer.TryGetProperty("CID", out var c) && c.TryGetUInt64(out var wertCid)
                    ? wertCid
                    : 0ul;

                charaktere.Add(new Charakter(cid, Text(besitzer, "Name") ?? "?", zaehler));
            }

            foreach (var k in unbekannt)
                hinweise.Add($"PatMe counter \"{k}\" is unknown here and was skipped.");
        }
        catch (Exception e)
        {
            hinweise.Add($"PatMe.json could not be read: {e.GetType().Name}");
        }

        return charaktere;
    }

    private static Rule EmoteRegel(string emote, uint[] ids)
    {
        var regel = new Rule
        {
            Name = emote,
            Enabled = true,
            Channels = [ChatChannel.StandardEmote, ChatChannel.CustomEmote],
            EmoteIds = [.. ids],
            ShowName = false,
            GeneralTemplate = $"{emote.ToLowerInvariant()}ted [Count]x",
            PersonalTemplate = $"[Name] {emote.ToLowerInvariant()}ted me [Count]x",
        };

        regel.EnsureId();
        return regel;
    }

    // ------------------------------------------------------------- Werkzeug

    private static string? Text(JsonElement e, string feld) =>
        e.TryGetProperty(feld, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool? Wahrheit(JsonElement e, string feld) =>
        e.TryGetProperty(feld, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean()
            : null;

    private static bool Farbe(JsonElement e, string feld) =>
        e.TryGetProperty(feld, out var v) && v.ValueKind is not JsonValueKind.Null;
}
