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

    [GeneratedRegex(@"\[Name\]|\[Anzahl\]")]
    private static partial Regex Placeholders();

    /// <summary>Länge der Vorlage ohne die Platzhalter.</summary>
    public static int FreeTextLength(string template) =>
        Placeholders().Replace(template ?? string.Empty, string.Empty).Length;

    public static string Render(string template, string? name, int count) =>
        (template ?? string.Empty)
            .Replace("[Name]", name ?? string.Empty)
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
    public static string Build(Rule rule, string name, int personal, int total)
    {
        if (!rule.ShowName)
            return Render(rule.GeneralTemplate, null, total);

        var persoenlich = Render(rule.PersonalTemplate, name, personal);

        if (persoenlich.Length <= MaxTitleLength)
            return persoenlich;

        return Render(rule.GeneralTemplate, null, total);
    }
}
