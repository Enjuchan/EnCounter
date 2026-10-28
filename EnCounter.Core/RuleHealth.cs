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

/// <summary>Core kennt keine Spracheinstellung; die Oberfläche wählt zwischen <see cref="Message"/> (Englisch) und <see cref="MessageDe"/>.</summary>
public sealed record HealthFinding(HealthSeverity Severity, string Message, string MessageDe);

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
            Add(befunde, HealthSeverity.Blockierend, "No channels - the rule never counts.",
                "Keine Kanäle - die Regel zählt nie.");

        // Mischregeln aus älteren Fassungen: Die Chatkanäle sind in der Oberfläche
        // unsichtbar, zählen aber weiter.
        if (emote && chatkanaele > 0)
            Add(befunde, HealthSeverity.Warnung,
                $"Also listens to {chatkanaele} chat channel(s) - emote rules should not.",
                chatkanaele == 1
                    ? "Hört auch auf 1 Chatkanal - Emote-Regeln sollten das nicht."
                    : $"Hört auch auf {chatkanaele} Chatkanäle - Emote-Regeln sollten das nicht.");

        PruefeOnlyFrom(rule, befunde);

        // Der Matcher gibt bei Emotes vor den Textbedingungen true zurück, die Reste
        // verhindern das Zählen also nicht.
        if (emote && (rule.TriggerTexts.Count > 0 || rule.RequiresOwnName))
            Add(befunde, HealthSeverity.Hinweis,
                "Trigger words or the name check are left over from a chat rule and ignored for emotes.",
                "Triggerwörter oder die Namensprüfung stammen noch von einer Chatregel und werden bei Emotes ignoriert.");

        // Leere Wörter treffen nie; stehen nur solche da, gilt nicht „egal", sondern
        // „nichts" - anders als bei einer leeren Liste.
        if (!emote && chatkanaele > 0 && rule.TriggerTexts.Count > 0 &&
            rule.TriggerTexts.All(string.IsNullOrWhiteSpace))
            Add(befunde, HealthSeverity.Blockierend, "All trigger words are empty - the rule never counts.",
                "Alle Triggerwörter sind leer - die Regel zählt nie.");

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
                "Every 'Only from' entry is incomplete - the rule never counts.",
                "Jeder Eintrag unter „Nur von“ ist unvollständig - die Regel zählt nie.");
        else
            Add(befunde, HealthSeverity.Warnung,
                $"{unvollstaendig} 'Only from' entr{(unvollstaendig == 1 ? "y is" : "ies are")} incomplete and never match{(unvollstaendig == 1 ? "es" : string.Empty)}.",
                unvollstaendig == 1
                    ? "1 Eintrag unter „Nur von“ ist unvollständig und trifft nie zu."
                    : $"{unvollstaendig} Einträge unter „Nur von“ sind unvollständig und treffen nie zu.");
    }

    private static void PruefeVorlagen(Rule rule, List<HealthFinding> befunde)
    {
        var received = TitleTemplate.VorlagenFuer(rule, outgoing: false);
        PruefeVorlagenpaar(rule, received, string.Empty, string.Empty, befunde);

        if (rule.Direction != CountDirection.Both || !IstEmoteRegel(rule))
            return;

        var given = TitleTemplate.VorlagenFuer(rule, outgoing: true);

        if (given != received)
            PruefeVorlagenpaar(rule, given, " (given)", " (gegeben)", befunde);
    }

    private static void PruefeVorlagenpaar(Rule rule, (string WithName, string WithoutName) vorlagen,
                                           string zusatz, string zusatzDe, List<HealthFinding> befunde)
    {
        // Steuerzeichen entfernt Build vor dem Anzeigen; der Titel erscheint also, aber
        // anders als geschrieben.
        if (TitleTemplate.HasControlCharacters(vorlagen.WithoutName) ||
            (rule.ShowName && TitleTemplate.HasControlCharacters(vorlagen.WithName)))
            Add(befunde, HealthSeverity.Warnung,
                $"A template{zusatz} contains invisible control characters; they are removed from the title.",
                $"Eine Vorlage{zusatzDe} enthält unsichtbare Steuerzeichen; sie werden aus dem Titel entfernt.");

        var ohne = TitleTemplate.FreeTextLength(TitleTemplate.Clean(vorlagen.WithoutName));
        var ohneZuLang = ohne > TitleTemplate.MaxTitleLength;

        if (!rule.ShowName)
        {
            if (ohneZuLang)
                Add(befunde, HealthSeverity.Blockierend,
                    $"The template without name{zusatz} is too long - Honorific never shows the title.",
                    $"Die Vorlage ohne Namen{zusatzDe} ist zu lang - Honorific zeigt den Titel nie an.");

            return;
        }

        var mitNamen = TitleTemplate.Clean(vorlagen.WithName);
        var passtFuerKeinenNamen = TitleTemplate.MaxNameLengthFor(mitNamen) == 0;

        if (passtFuerKeinenNamen && ohneZuLang)
            Add(befunde, HealthSeverity.Blockierend,
                $"Both templates{zusatz} are too long - Honorific never shows the title.",
                $"Beide Vorlagen{zusatzDe} sind zu lang - Honorific zeigt den Titel nie an.");
        else if (ohneZuLang)
            Add(befunde, HealthSeverity.Warnung,
                $"The fallback template without name{zusatz} is too long; titles with long names are not shown.",
                $"Die Rückfall-Vorlage ohne Namen{zusatzDe} ist zu lang; Titel mit langen Namen werden nicht angezeigt.");
        else if (passtFuerKeinenNamen)
            Add(befunde, HealthSeverity.Warnung,
                $"The template with name{zusatz} fits no name; the one without name is always used.",
                $"Die Vorlage mit Namen{zusatzDe} passt zu keinem Namen; es gilt immer die ohne Namen.");
    }

    private static void Add(List<HealthFinding> befunde, HealthSeverity schwere, string text, string textDe) =>
        befunde.Add(new HealthFinding(schwere, text, textDe));

    private static bool IstEmoteKanal(ChatChannel kanal) =>
        kanal is ChatChannel.StandardEmote or ChatChannel.CustomEmote;

    private static bool IstEmoteRegel(Rule rule) => rule.Channels.Any(IstEmoteKanal);
}
