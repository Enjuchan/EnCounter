using System.Text.RegularExpressions;

namespace EnCounter.Core;

/// <summary>
/// Füllt Vorlagen und wacht über die Länge.
///
/// Die 32 Zeichen sind Honorifics Grenze, nicht unsere: Ein längerer Titel wird dort
/// nicht gekürzt, sondern gar nicht angezeigt. Deshalb wird gegen den Extremfall
/// gerechnet - längstmöglicher Vorname, Zahl bis 9999 - statt gegen den Durchschnitt.
/// </summary>
public static partial class TitleTemplate
{
    public const int MaxTitleLength = 32;

    /// <summary>Längstmöglicher Vorname in FFXIV.</summary>
    private const int MaxFirstName = 15;

    /// <summary>Vier Stellen, also Zähler bis 9999.</summary>
    private const int MaxDigits = 4;

    /// <summary>Was bei persönlichen Vorlagen an freiem Text bleibt: 32 - 15 - 4.</summary>
    public const int MaxPersonalFreeText = MaxTitleLength - MaxFirstName - MaxDigits;

    /// <summary>
    /// `[Anzahl]` ist die alte, deutsche Schreibweise. Sie wird weiter erkannt, damit
    /// von Hand geschriebene Vorlagen aus früheren Fassungen nicht still kaputtgehen -
    /// gespeicherte werden beim Laden auf `[Count]` umgeschrieben.
    /// </summary>
    [GeneratedRegex(@"\[Name\]|\[Count\]|\[Anzahl\]")]
    private static partial Regex Placeholders();

    /// <summary>Schreibt die alte Schreibweise auf die neue um.</summary>
    public static string Modernise(string template) =>
        (template ?? string.Empty).Replace("[Anzahl]", "[Count]");

    /// <summary>Kürzestmöglicher Vorname in FFXIV.</summary>
    public const int MinFirstName = 2;

    /// <summary>Länge der Vorlage ohne die Platzhalter.</summary>
    public static int FreeTextLength(string template) =>
        Placeholders().Replace(template ?? string.Empty, string.Empty).Length;

    /// <summary>
    /// Bis zu welcher Vornamenlänge diese Vorlage in den Titel passt.
    ///
    /// Die feste Grenze von <see cref="MaxPersonalFreeText"/> rechnet mit dem
    /// längstmöglichen Namen. Wer kürzere Namen erwartet, darf mehr Text schreiben -
    /// dann greift bei langen Namen eben der Rückfall auf die namenlose Vorlage.
    ///
    /// Ergebnis 0 heißt: passt für keinen Namen, die Vorlage kommt nie zum Zug.
    /// </summary>
    public static int MaxNameLengthFor(string template)
    {
        var rest = MaxTitleLength - FreeTextLength(template) - MaxDigits;
        return rest < MinFirstName ? 0 : Math.Min(rest, MaxFirstName);
    }

    /// <summary>Der Vorname aus einem vollen Namen. Für die Anzeige im Titel.</summary>
    public static string FirstName(string? voll)
    {
        if (string.IsNullOrEmpty(voll))
            return string.Empty;

        var leer = voll.IndexOf(' ');
        return leer > 0 ? voll[..leer] : voll;
    }

    /// <summary>
    /// Entfernt Steuerzeichen aus einem Titel.
    ///
    /// **Honorific verwirft jeden Titel, der welche enthält** - `CustomTitle.IsValid`
    /// gibt dann `false`, und es erscheint nichts. Von außen sieht das aus, als hätte
    /// die Regel nicht gegriffen.
    ///
    /// Sie geraten leicht hinein: Wer Text aus dem Spiel kopiert, schleppt Reste eines
    /// Auto-Translate-Payloads mit (0x02 leitet einen ein), und ein Tabulator oder
    /// Zeilenumbruch ist im Eingabefeld unsichtbar. Am Fund vom 29.08.2026 gemessen,
    /// indem `IsValid` aus Honorific 1.7.5.0 direkt aufgerufen wurde.
    ///
    /// Sichtbare Deko - Sterne, Herzen, Emoji, FFXIVs eigene Symbole - ist dagegen
    /// unbedenklich und bleibt unangetastet.
    /// </summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        if (!text.Any(char.IsControl))
            return text;

        return new string([.. text.Where(c => !char.IsControl(c))]);
    }

    /// <summary>Ob eine Vorlage Zeichen enthält, die Honorific den Titel verwerfen lassen.</summary>
    public static bool HasControlCharacters(string? text) =>
        text is not null && text.Any(char.IsControl);

    public static string Render(string template, string? name, int count) =>
        (template ?? string.Empty)
            .Replace("[Name]", name ?? string.Empty)
            .Replace("[Count]", count.ToString())
            .Replace("[Anzahl]", count.ToString());

    /// <summary>
    /// Baut die anzuzeigende Meldung.
    ///
    /// Welche Vorlage genommen wird, entscheidet die Regel: mit Namen zeigt sie den
    /// Zaehler der ausloesenden Person, ohne Namen die Summe ueber alle.
    ///
    /// Passt die persoenliche Vorlage nicht in die Titelgrenze, wird ersatzweise die
    /// allgemeine genommen - gekuerzt wird nie, weil ein abgeschnittener Titel
    /// schlechter ist als ein kuerzerer.
    /// </summary>
    public static string Build(Rule rule, string name, int personal, int total,
                               bool outgoing = false)
    {
        var (mitNamen, ohneNamen) = VorlagenFuer(rule, outgoing);

        // Steuerzeichen wuerden Honorific den ganzen Titel verwerfen lassen - siehe
        // Clean. Lieber ohne sie anzeigen als gar nicht.
        if (!rule.ShowName)
            return Clean(Render(ohneNamen, null, total));

        // Gespeichert wird der **volle** Name, damit zwei „Rayleigh" auf derselben
        // Welt auseinandergehalten werden. Im Titel steht trotzdem nur der Vorname -
        // für beides zusammen reichen die 32 Zeichen nie.
        var persoenlich = Clean(Render(mitNamen, FirstName(name), personal));

        if (persoenlich.Length <= MaxTitleLength)
            return persoenlich;

        return Clean(Render(ohneNamen, null, total));
    }

    /// <summary>
    /// Welche Vorlagen gelten - die normalen oder die für die Gegenrichtung.
    ///
    /// Eine leere Gegenvorlage fällt auf die normale zurück. Damit bleiben Regeln aus
    /// älteren Fassungen unverändert gültig, und wer nur eine Richtung zählt, muss
    /// nichts doppelt pflegen.
    /// </summary>
    public static (string WithName, string WithoutName) VorlagenFuer(Rule rule, bool outgoing)
    {
        if (!outgoing)
            return (rule.PersonalTemplate, rule.GeneralTemplate);

        var mitNamen = string.IsNullOrWhiteSpace(rule.GivenPersonalTemplate)
            ? rule.PersonalTemplate
            : rule.GivenPersonalTemplate;

        var ohneNamen = string.IsNullOrWhiteSpace(rule.GivenGeneralTemplate)
            ? rule.GeneralTemplate
            : rule.GivenGeneralTemplate;

        return (mitNamen, ohneNamen);
    }
}
