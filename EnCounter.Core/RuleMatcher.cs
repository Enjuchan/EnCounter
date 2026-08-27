namespace EnCounter.Core;

/// <summary>Entscheidet, ob ein Ereignis eine Regel auslöst.</summary>
public static class RuleMatcher
{
    public static bool Matches(Rule rule, IncomingEvent e, string ownFirstName)
    {
        if (!rule.Enabled)
            return false;

        // Eigene Nachrichten zaehlen nie - sonst treibt man den eigenen Zaehler hoch.
        if (e.FromSelf)
            return false;

        if (!rule.Channels.Contains(e.Channel))
            return false;

        // Bei Emotes liefert das Spiel die Zuordnung strukturell. Wer nicht gemeint
        // war, zaehlt nicht.
        if (IsEmote(e.Channel))
        {
            if (!e.TargetedAtMe)
                return false;

            // Leere Liste heisst: jedes Emote zaehlt.
            if (rule.EmoteIds.Count > 0 && !rule.EmoteIds.Contains(e.EmoteId))
                return false;
        }

        // Der Namensbezug gilt nicht fuer Kanaele, die ohnehin an einen gerichtet
        // sind. In einem Tell den eigenen Namen zu verlangen, kostet nur Treffer.
        if (rule.RequiresOwnName && !IsDirected(e.Channel) &&
            !e.Text.Contains(ownFirstName, StringComparison.CurrentCultureIgnoreCase))
            return false;

        // Keine Triggertexte heisst: der Inhalt ist egal.
        if (rule.TriggerTexts.Count == 0)
            return true;

        return rule.TriggerTexts.Any(t =>
            !string.IsNullOrWhiteSpace(t) &&
            e.Text.Contains(t, StringComparison.CurrentCultureIgnoreCase));
    }

    private static bool IsEmote(ChatChannel channel) =>
        channel is ChatChannel.StandardEmote or ChatChannel.CustomEmote;

    /// <summary>
    /// Kanaele, die von sich aus an eine bestimmte Person gehen. Dort ist ein
    /// Namensbezug ueberfluessig.
    /// </summary>
    private static bool IsDirected(ChatChannel channel) =>
        channel is ChatChannel.TellIncoming;
}
