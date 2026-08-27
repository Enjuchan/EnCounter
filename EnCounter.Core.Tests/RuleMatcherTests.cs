using EnCounter.Core;
using Xunit;

namespace EnCounter.Core.Tests;

public class RuleMatcherTests
{
    private static IncomingEvent Ereignis(
        ChatChannel kanal = ChatChannel.Say,
        bool anMich = true,
        bool vonMir = false,
        string text = "hallo Stella") =>
        new("Enju", 67, kanal, anMich, vonMir, text);

    private static Rule Regel() => new()
    {
        Name = "Test",
        Enabled = true,
        Channels = [ChatChannel.Say],
        TriggerTexts = ["hallo"],
        RequiresOwnName = false,
    };

    [Fact]
    public void Greift_bei_passendem_Text_und_Kanal()
    {
        Assert.True(RuleMatcher.Matches(Regel(), Ereignis(), "Stella"));
    }

    [Fact]
    public void Greift_nicht_bei_falschem_Kanal()
    {
        var e = Ereignis(kanal: ChatChannel.Party);
        Assert.False(RuleMatcher.Matches(Regel(), e, "Stella"));
    }

    [Fact]
    public void Greift_nicht_bei_eigener_Nachricht()
    {
        var e = Ereignis(vonMir: true);
        Assert.False(RuleMatcher.Matches(Regel(), e, "Stella"));
    }

    [Fact]
    public void Greift_nicht_wenn_ausgeschaltet()
    {
        var r = Regel();
        r.Enabled = false;
        Assert.False(RuleMatcher.Matches(r, Ereignis(), "Stella"));
    }

    [Fact]
    public void Verlangt_eigenen_Namen_wenn_eingestellt()
    {
        var r = Regel();
        r.RequiresOwnName = true;

        Assert.True(RuleMatcher.Matches(r, Ereignis(text: "hallo Stella"), "Stella"));
        Assert.False(RuleMatcher.Matches(r, Ereignis(text: "hallo Leute"), "Stella"));
    }

    [Fact]
    public void Im_Tell_wird_kein_eigener_Name_verlangt()
    {
        var r = Regel();
        r.RequiresOwnName = true;
        r.Channels = [ChatChannel.Say, ChatChannel.TellIncoming];

        // Im Say greift die Bedingung.
        var say = Ereignis(kanal: ChatChannel.Say, text: "hallo Leute");
        Assert.False(RuleMatcher.Matches(r, say, "Stella"));

        // Im Tell nicht - die Nachricht ist ohnehin an uns gerichtet.
        var tell = Ereignis(kanal: ChatChannel.TellIncoming, text: "hallo Leute");
        Assert.True(RuleMatcher.Matches(r, tell, "Stella"));
    }

    [Fact]
    public void Text_wird_ohne_Ruecksicht_auf_Grossschreibung_geprueft()
    {
        Assert.True(RuleMatcher.Matches(Regel(), Ereignis(text: "HALLO alle"), "Stella"));
    }

    [Fact]
    public void Emote_greift_ueber_TargetKind_ohne_Text()
    {
        var r = Regel();
        r.Channels = [ChatChannel.StandardEmote];
        r.TriggerTexts = [];

        var anMich = Ereignis(kanal: ChatChannel.StandardEmote, anMich: true, text: "Enju streichelt Stella.");
        var anAndere = Ereignis(kanal: ChatChannel.StandardEmote, anMich: false, text: "Enju streichelt Mara.");

        Assert.True(RuleMatcher.Matches(r, anMich, "Stella"));
        Assert.False(RuleMatcher.Matches(r, anAndere, "Stella"));
    }
}
