using EnCounter.Core;
using Xunit;

namespace EnCounter.Core.Tests;

/// <summary>
/// Textbedingungen duerfen bei Emotes nicht greifen: Ein Emote traegt keinen Text, sie
/// wuerden also immer scheitern und die Regel stumm schalten.
///
/// Genau so gemeldet am 29.08.2026 - eine Chatregel war dupliziert und auf Emotes
/// umgestellt worden, Triggerwoerter und Namensbezug blieben stehen. Die Oberflaeche
/// blendet sie bei Emote-Regeln aus, also war nichts davon zu sehen.
/// </summary>
public class EmoteTextConditionTests
{
    private static Rule LoveEmote() => new()
    {
        Id = "love", Name = "Love Emote", Enabled = true,
        Channels = [ChatChannel.StandardEmote, ChatChannel.CustomEmote],
        EmoteIds = [274],
        Direction = CountDirection.Both,

        // Die Reste aus der Chatregel.
        TriggerTexts = ["love you", "<3"],
        RequiresOwnName = true,
    };

    private static IncomingEvent Herzpose(bool outgoing = false) =>
        new("Azura Zechs", 21, ChatChannel.StandardEmote,
            TargetedAtMe: !outgoing, FromSelf: outgoing, Text: "",
            EmoteId: 274, Outgoing: outgoing, SenderWorld: "Shiva");

    [Fact]
    public void Triggerwoerter_blockieren_ein_Emote_nicht()
    {
        Assert.True(RuleMatcher.Matches(LoveEmote(), Herzpose(), "Enju"));
    }

    [Fact]
    public void Auch_ausgehend_nicht()
    {
        Assert.True(RuleMatcher.Matches(LoveEmote(), Herzpose(outgoing: true), "Enju"));
    }

    /// <summary>Was weiterhin gelten muss: die Emote-Auswahl.</summary>
    [Fact]
    public void Falsches_Emote_zaehlt_weiterhin_nicht()
    {
        var e = Herzpose() with { EmoteId = 146 };
        Assert.False(RuleMatcher.Matches(LoveEmote(), e, "Enju"));
    }

    /// <summary>Und die Namensliste - die braucht keinen Text.</summary>
    [Fact]
    public void Namensliste_gilt_weiterhin()
    {
        var regel = LoveEmote();
        regel.OnlyFrom = ["Rayleigh Snoww@Shiva"];

        Assert.False(RuleMatcher.Matches(regel, Herzpose(), "Enju"));
    }

    /// <summary>Im Chat bleiben die Bedingungen selbstverstaendlich wirksam.</summary>
    [Fact]
    public void Im_Chat_gelten_Triggerwoerter_weiter()
    {
        var regel = LoveEmote();
        regel.Channels = [ChatChannel.Say];
        regel.RequiresOwnName = false;

        var passt = new IncomingEvent("Azura Zechs", 21, ChatChannel.Say,
            TargetedAtMe: true, FromSelf: false, Text: "love you Enju", SenderWorld: "Shiva");

        var passtNicht = passt with { Text = "hallo" };

        Assert.True(RuleMatcher.Matches(regel, passt, "Enju"));
        Assert.False(RuleMatcher.Matches(regel, passtNicht, "Enju"));
    }
}
