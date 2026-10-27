namespace EnCounter.Core;

/// <summary>
/// Wie schwer ein Befund wiegt.
///
/// <see cref="Hinweis"/> verhindert nichts, ist aber ein Rest, der beim nächsten Blick
/// in die Regel verwirrt.
/// </summary>
public enum HealthSeverity
{
    Hinweis = 0,

    /// <summary>Die Regel zählt oder zeigt nur teilweise.</summary>
    Warnung = 1,

    /// <summary>Die Regel zählt oder zeigt nie.</summary>
    Blockierend = 2,
}

public sealed record HealthFinding(HealthSeverity Severity, string Message);

/// <summary>
/// Gründe, warum eine Regel nie zählt oder nie einen Titel zeigt.
///
/// Fasst zusammen, was die Oberfläche bisher verstreut an den einzelnen Feldern
/// anzeigt, damit auch die Regelliste eine Regel beanstanden kann, ohne dass man sie
/// öffnet. Die Prüfungen nutzen <see cref="RuleMatcher"/> und
/// <see cref="TitleTemplate"/>, damit Anzeige und Verhalten nicht auseinanderlaufen.
/// </summary>
public static class RuleHealth
{
    public static IReadOnlyList<HealthFinding> Check(Rule rule)
    {
        var befunde = new List<HealthFinding>();

        var emote = IstEmoteRegel(rule);
        var chatkanaele = rule.Channels.Count(k => !IstEmoteKanal(k));

        if (rule.Channels.Count == 0)
            Add(befunde, HealthSeverity.Blockierend, "No channels - the rule never counts.");

        // Mischregeln aus älteren Fassungen: Die Chatkanäle sind in der Oberfläche
        // unsichtbar, zählen aber weiter.
        if (emote && chatkanaele > 0)
            Add(befunde, HealthSeverity.Warnung,
                $"Also listens to {chatkanaele} chat channel(s) - emote rules should not.");

        PruefeOnlyFrom(rule, befunde);

        // Der Matcher gibt bei Emotes vor den Textbedingungen true zurück, die Reste
        // verhindern das Zählen also nicht.
        if (emote && (rule.TriggerTexts.Count > 0 || rule.RequiresOwnName))
            Add(befunde, HealthSeverity.Hinweis,
                "Trigger words or the name check are left over from a chat rule and ignored for emotes.");

        // Leere Wörter treffen nie; stehen nur solche da, gilt nicht „egal", sondern
        // „nichts" - anders als bei einer leeren Liste.
        if (!emote && chatkanaele > 0 && rule.TriggerTexts.Count > 0 &&
            rule.TriggerTexts.All(string.IsNullOrWhiteSpace))
            Add(befunde, HealthSeverity.Blockierend, "All trigger words are empty - the rule never counts.");

        PruefeVorlagen(rule, befunde);

        return befunde;
    }

    /// <summary>Der schwerste Befund, oder null bei einer gesunden Regel.</summary>
    public static HealthSeverity? Worst(IReadOnlyList<HealthFinding> befunde) =>
        befunde.Count == 0 ? null : befunde.Max(b => b.Severity);

    private static void PruefeOnlyFrom(Rule rule, List<HealthFinding> befunde)
    {
        if (rule.OnlyFrom.Count == 0)
            return;

        var unvollstaendig = rule.OnlyFrom.Count(e => !RuleMatcher.IstVollstaendig(e));

        if (unvollstaendig == 0)
            return;

        if (unvollstaendig == rule.OnlyFrom.Count)
            Add(befunde, HealthSeverity.Blockierend,
                "Every 'Only from' entry is incomplete - the rule never counts.");
        else
            Add(befunde, HealthSeverity.Warnung,
                $"{unvollstaendig} 'Only from' entr{(unvollstaendig == 1 ? "y is" : "ies are")} incomplete and never match{(unvollstaendig == 1 ? "es" : string.Empty)}.");
    }

    private static void PruefeVorlagen(Rule rule, List<HealthFinding> befunde)
    {
        var received = TitleTemplate.VorlagenFuer(rule, outgoing: false);
        PruefeVorlagenpaar(rule, received, string.Empty, befunde);

        if (rule.Direction != CountDirection.Both || !IstEmoteRegel(rule))
            return;

        var given = TitleTemplate.VorlagenFuer(rule, outgoing: true);

        if (given != received)
            PruefeVorlagenpaar(rule, given, " (given)", befunde);
    }

    private static void PruefeVorlagenpaar(Rule rule, (string WithName, string WithoutName) vorlagen,
                                           string zusatz, List<HealthFinding> befunde)
    {
        // Steuerzeichen entfernt Build vor dem Anzeigen; der Titel erscheint also, aber
        // anders als geschrieben.
        if (TitleTemplate.HasControlCharacters(vorlagen.WithoutName) ||
            (rule.ShowName && TitleTemplate.HasControlCharacters(vorlagen.WithName)))
            Add(befunde, HealthSeverity.Warnung,
                $"A template{zusatz} contains invisible control characters; they are removed from the title.");

        var ohne = TitleTemplate.FreeTextLength(TitleTemplate.Clean(vorlagen.WithoutName));
        var ohneZuLang = ohne > TitleTemplate.MaxTitleLength;

        if (!rule.ShowName)
        {
            if (ohneZuLang)
                Add(befunde, HealthSeverity.Blockierend,
                    $"The template without name{zusatz} is too long - Honorific never shows the title.");

            return;
        }

        var mitNamen = TitleTemplate.Clean(vorlagen.WithName);
        var passtFuerKeinenNamen = TitleTemplate.MaxNameLengthFor(mitNamen) == 0;

        if (passtFuerKeinenNamen && ohneZuLang)
            Add(befunde, HealthSeverity.Blockierend,
                $"Both templates{zusatz} are too long - Honorific never shows the title.");
        else if (ohneZuLang)
            Add(befunde, HealthSeverity.Warnung,
                $"The fallback template without name{zusatz} is too long; titles with long names are not shown.");
        else if (passtFuerKeinenNamen)
            Add(befunde, HealthSeverity.Warnung,
                $"The template with name{zusatz} fits no name; the one without name is always used.");
    }

    private static void Add(List<HealthFinding> befunde, HealthSeverity schwere, string text) =>
        befunde.Add(new HealthFinding(schwere, text));

    private static bool IstEmoteKanal(ChatChannel kanal) =>
        kanal is ChatChannel.StandardEmote or ChatChannel.CustomEmote;

    private static bool IstEmoteRegel(Rule rule) => rule.Channels.Any(IstEmoteKanal);
}
