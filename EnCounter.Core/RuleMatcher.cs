namespace EnCounter.Core;

/// <summary>Entscheidet, ob ein Ereignis eine Regel auslöst.</summary>
public static class RuleMatcher
{
    public static bool Matches(Rule rule, IncomingEvent e, string ownFirstName)
    {
        if (!rule.Enabled)
            return false;

        // Eigene Chatnachrichten zaehlen nie - sonst treibt man den eigenen Zaehler
        // hoch. Ausgehende Emotes sind der Sonderfall: Dort ist "von mir" gerade der
        // Sinn der Sache, und gezaehlt wird ohnehin die andere Person.
        if (e.FromSelf && !e.Outgoing)
            return false;

        if (!rule.Channels.Contains(e.Channel))
            return false;

        // Bei Emotes liefert das Spiel die Zuordnung strukturell. Wer nicht gemeint
        // war, zaehlt nicht.
        if (IsEmote(e.Channel))
        {
            if (!MatchesDirection(rule.Direction, e.Outgoing))
                return false;

            // Nur fuer empfangene Emotes: Bei ausgehenden hat der Aufrufer schon
            // geprueft, dass ueberhaupt jemand gemeint war.
            if (!e.Outgoing && !e.TargetedAtMe)
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

        // Namensliste: Nur diese Personen zaehlen. Verglichen wird der Vorname, weil
        // nur der gespeichert wird - eingetippt werden darf trotzdem der volle Name.
        if (rule.OnlyFrom.Count > 0 && !rule.OnlyFrom.Any(n => IstDieselbePerson(n, e)))
            return false;

        // Keine Triggertexte heisst: der Inhalt ist egal.
        if (rule.TriggerTexts.Count == 0)
            return true;

        return rule.TriggerTexts.Any(t =>
            !string.IsNullOrWhiteSpace(t) &&
            e.Text.Contains(t, StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>
    /// Die Richtung wird nur bei Emotes geprueft. Im Chat gibt es keinen
    /// verlaesslichen Empfaenger - wer in Say etwas sagt, sagt es an alle, und ein
    /// Zaehler braucht eine Person.
    /// </summary>
    private static bool MatchesDirection(CountDirection wanted, bool outgoing) => wanted switch
    {
        CountDirection.Received => !outgoing,
        CountDirection.Given => outgoing,
        _ => true,
    };

    /// <summary>
    /// Vergleicht einen Eintrag aus <see cref="Rule.OnlyFrom"/> mit dem Absender.
    ///
    /// Zwei Schreibweisen sind erlaubt:
    ///
    /// Verlangt wird die **vollständige** Form `Vorname Nachname@Welt`, etwa
    /// `Stella Unibell@Shiva`.
    ///
    /// Kürzere Formen waren zwischenzeitlich erlaubt und sind wieder abgeschafft: Wer
    /// nur „Stella" einträgt, meint eine Person, träfe aber jede mit diesem Vornamen -
    /// und würde es nie bemerken, weil eine zu hohe Zahl nach nichts aussieht.
    /// Vornamen wiederholen sich, und Namen sind in FFXIV ohnehin nur *pro Welt*
    /// eindeutig.
    ///
    /// Unvollständige Einträge treffen deshalb nie. Die Oberfläche kennzeichnet sie.
    /// </summary>
    private static bool IstDieselbePerson(string eingetragen, IncomingEvent e)
    {
        if (!IstVollstaendig(eingetragen))
            return false;

        var eintrag = eingetragen.Trim();
        var at = eintrag.IndexOf('@');

        var name = eintrag[..at].Trim();
        var welt = eintrag[(at + 1)..].Trim();

        return string.Equals(name, e.SenderName, StringComparison.CurrentCultureIgnoreCase) &&
               string.Equals(welt, e.SenderWorld, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>
    /// Ob ein Eintrag eine Person eindeutig benennt: Vor- und Nachname, dann `@Welt`.
    /// Auch für die Oberfläche, damit sie unbrauchbare Einträge anzeigen kann.
    /// </summary>
    public static bool IstVollstaendig(string? eintrag)
    {
        if (string.IsNullOrWhiteSpace(eintrag))
            return false;

        var at = eintrag.IndexOf('@');
        if (at <= 0 || at == eintrag.Length - 1)
            return false;

        var name = eintrag[..at].Trim();
        var welt = eintrag[(at + 1)..].Trim();

        if (welt.Length == 0)
            return false;

        // Ein Leerzeichen mitten im Namen heisst: Vor- und Nachname sind da.
        var leer = name.IndexOf(' ');

        return leer > 0 && leer < name.Length - 1;
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
