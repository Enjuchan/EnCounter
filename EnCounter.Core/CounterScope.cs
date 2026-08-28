namespace EnCounter.Core;

/// <summary>
/// Bestimmt, unter welchem Schlüssel gezählt wird.
///
/// **Der Zähler gehört dem Ereignis, nicht der Regel.** Ein Dote von Rayleigh landet
/// unter `e:146`, gleich welche Regel ihn aufgreift. Legt man später eine zweite Regel
/// nur für Rayleigh an, zeigt sie sofort seine bisherigen Dotes - sie liest denselben
/// Stand, statt bei null anzufangen.
///
/// Vorher hielt jede Regel ihren eigenen Stand. Zwei Regeln für dasselbe Emote führten
/// damit zwei Wahrheiten über dieselbe Person, und keine davon war vollständig.
///
/// Gezählt wird trotzdem nur, was mindestens eine Regel abdeckt - EnCounter legt keine
/// Liste über alles an, was im Umkreis passiert.
/// </summary>
public static class CounterScope
{
    /// <summary>Emote, nach Nummer aus den Spieldaten.</summary>
    public const string EmotePrefix = "e:";

    /// <summary>Chat, nach getroffenem Triggerwort.</summary>
    public const string TextPrefix = "t:";

    /// <summary>
    /// Chat ohne Triggerwörter. Dann gibt es nichts Gemeinsames, worauf sich zwei
    /// Regeln einigen könnten - also bleibt der Stand bei der Regel.
    /// </summary>
    public const string RulePrefix = "r:";

    /// <summary>Angehängt an alles, was man selbst getan hat.</summary>
    public const string GivenSuffix = ":g";

    /// <summary>
    /// Der Schlüssel, unter dem dieses Ereignis zählt. Die Regel entscheidet nur bei
    /// Chatnachrichten mit, weil erst ihre Triggerwörter das Ereignis benennen.
    /// </summary>
    public static string ForEvent(Rule rule, IncomingEvent e)
    {
        var basis = IsEmote(e.Channel)
            ? EmotePrefix + e.EmoteId
            : TextSchluessel(rule, e);

        return e.Outgoing ? basis + GivenSuffix : basis;
    }

    /// <summary>
    /// Welche Stände zu einer Regel gehören - für Anzeige und Zurücksetzen.
    ///
    /// <see cref="Selection.Prefix"/> ist gesetzt, wenn die Regel nicht auf einzelne
    /// Emotes eingeschränkt ist: Dann gehört ihr alles, was je gezählt wurde.
    /// </summary>
    public static Selection ForRule(Rule rule, bool outgoing)
    {
        var suffix = outgoing ? GivenSuffix : string.Empty;

        if (IstEmoteRegel(rule))
        {
            if (rule.EmoteIds.Count == 0)
                return new Selection([], EmotePrefix, suffix);

            return new Selection([.. rule.EmoteIds.Select(id => EmotePrefix + id + suffix)], null, suffix);
        }

        if (rule.TriggerTexts.Count == 0)
            return new Selection([RulePrefix + rule.Id + suffix], null, suffix);

        return new Selection(
            [.. rule.TriggerTexts
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => TextPrefix + Normalisiere(t) + suffix)],
            null, suffix);
    }

    /// <summary>
    /// Die Stände einer Regel: entweder eine Handvoll Schlüssel oder alles unter einem
    /// Präfix. <see cref="Suffix"/> grenzt dabei die Richtung ab.
    /// </summary>
    public sealed record Selection(IReadOnlyList<string> Keys, string? Prefix, string Suffix);

    private static string TextSchluessel(Rule rule, IncomingEvent e)
    {
        // Das erste zutreffende Triggerwort benennt das Ereignis. Mehrere Treffer in
        // einer Nachricht bleiben eine Zaehlung - gesagt wurde es ja einmal.
        foreach (var wort in rule.TriggerTexts)
        {
            if (string.IsNullOrWhiteSpace(wort))
                continue;

            if (e.Text.Contains(wort, StringComparison.CurrentCultureIgnoreCase))
                return TextPrefix + Normalisiere(wort);
        }

        return RulePrefix + rule.Id;
    }

    /// <summary>
    /// Triggerwörter sind frei getippt. „Love You" und „love you" meinen dasselbe und
    /// dürfen nicht zwei Stände ergeben.
    /// </summary>
    public static string Normalisiere(string wort) => wort.Trim().ToLowerInvariant();

    private static bool IstEmoteRegel(Rule rule) =>
        rule.Channels.Contains(ChatChannel.StandardEmote) ||
        rule.Channels.Contains(ChatChannel.CustomEmote);

    private static bool IsEmote(ChatChannel channel) =>
        channel is ChatChannel.StandardEmote or ChatChannel.CustomEmote;
}
