using EnCounter.Core;
using Xunit;

namespace EnCounter.Core.Tests;

/// <summary>
/// Die Richtung entscheidet, ob ein Emote zaehlt: empfangen, gegeben oder beides.
/// Gezaehlt wird immer die andere Person - deshalb steht ihr Name in beiden Faellen
/// im Ereignis.
/// </summary>
public class DirectionTests
{
    private static Rule Regel(CountDirection richtung) => new()
    {
        Name = "Hug",
        Id = "hug",
        Enabled = true,
        Channels = [ChatChannel.StandardEmote],
        TriggerTexts = [],
        EmoteIds = [112],
        Direction = richtung,
    };

    /// <summary>Jemand umarmt uns.</summary>
    private static IncomingEvent Empfangen() =>
        new("Enju", 67, ChatChannel.StandardEmote,
            TargetedAtMe: true, FromSelf: false, Text: "", EmoteId: 112, Outgoing: false);

    /// <summary>Wir umarmen jemanden - der Name ist der des Ziels.</summary>
    private static IncomingEvent Gegeben() =>
        new("Enju", 67, ChatChannel.StandardEmote,
            TargetedAtMe: false, FromSelf: true, Text: "", EmoteId: 112, Outgoing: true);

    [Fact]
    public void Empfangen_zaehlt_bei_Received()
    {
        Assert.True(RuleMatcher.Matches(Regel(CountDirection.Received), Empfangen(), "Stella"));
    }

    [Fact]
    public void Gegeben_zaehlt_nicht_bei_Received()
    {
        Assert.False(RuleMatcher.Matches(Regel(CountDirection.Received), Gegeben(), "Stella"));
    }

    [Fact]
    public void Gegeben_zaehlt_bei_Given()
    {
        Assert.True(RuleMatcher.Matches(Regel(CountDirection.Given), Gegeben(), "Stella"));
    }

    [Fact]
    public void Empfangen_zaehlt_nicht_bei_Given()
    {
        Assert.False(RuleMatcher.Matches(Regel(CountDirection.Given), Empfangen(), "Stella"));
    }

    [Fact]
    public void Beide_Richtungen_zaehlen_bei_Both()
    {
        var regel = Regel(CountDirection.Both);

        Assert.True(RuleMatcher.Matches(regel, Empfangen(), "Stella"));
        Assert.True(RuleMatcher.Matches(regel, Gegeben(), "Stella"));
    }

    /// <summary>
    /// Der Emote-Filter muss auch fuer ausgehende gelten - sonst zaehlt jedes eigene
    /// Emote in einer Regel, die nur auf /hug hoert.
    /// </summary>
    [Fact]
    public void Falsche_Emote_Nummer_zaehlt_auch_ausgehend_nicht()
    {
        var e = Gegeben() with { EmoteId = 105 };
        Assert.False(RuleMatcher.Matches(Regel(CountDirection.Given), e, "Stella"));
    }

    /// <summary>
    /// Ein empfangenes Emote, das jemand anderem galt, zaehlt nie - unabhaengig von
    /// der Richtung.
    /// </summary>
    [Fact]
    public void Emote_an_Dritte_zaehlt_nicht()
    {
        var e = Empfangen() with { TargetedAtMe = false };
        Assert.False(RuleMatcher.Matches(Regel(CountDirection.Both), e, "Stella"));
    }

    /// <summary>
    /// Im Chat gibt es keine Richtung: Eigene Nachrichten bleiben aussen vor, auch
    /// wenn die Regel auf „gegeben" steht.
    /// </summary>
    [Fact]
    public void Eigene_Chatnachricht_zaehlt_auch_bei_Given_nicht()
    {
        var regel = Regel(CountDirection.Given);
        regel.Channels = [ChatChannel.Say];

        var e = new IncomingEvent("Enju", 67, ChatChannel.Say,
            TargetedAtMe: false, FromSelf: true, Text: "hallo");

        Assert.False(RuleMatcher.Matches(regel, e, "Stella"));
    }
}
