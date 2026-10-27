using EnCounter.Core;
using Xunit;

namespace EnCounter.Core.Tests;

public class RuleHealthTests
{
    private static Rule Emote() => new()
    {
        Channels = [ChatChannel.StandardEmote, ChatChannel.CustomEmote],
        PersonalTemplate = "[Name] petted me [Count]x",
        GeneralTemplate = "petted [Count]x",
    };

    private static Rule Chat() => new()
    {
        Channels = [ChatChannel.Say],
        TriggerTexts = ["love you"],
        PersonalTemplate = "[Name] loves me [Count]x",
        GeneralTemplate = "got love [Count]x",
    };

    private static HealthSeverity? Worst(Rule r) => RuleHealth.Worst(RuleHealth.Check(r));

    [Fact]
    public void Gesunde_Emote_Regel_hat_keine_Befunde() =>
        Assert.Empty(RuleHealth.Check(Emote()));

    [Fact]
    public void Gesunde_Chat_Regel_hat_keine_Befunde() =>
        Assert.Empty(RuleHealth.Check(Chat()));

    [Fact]
    public void Regel_ohne_Kanaele_blockiert()
    {
        var r = Chat();
        r.Channels.Clear();
        Assert.Equal(HealthSeverity.Blockierend, Worst(r));
    }

    [Fact]
    public void Emote_Regel_mit_Chatkanal_warnt()
    {
        var r = Emote();
        r.Channels.Add(ChatChannel.Say);
        Assert.Equal(HealthSeverity.Warnung, Worst(r));
    }

    [Fact]
    public void Nur_unvollstaendige_OnlyFrom_blockieren()
    {
        var r = Emote();
        r.OnlyFrom = ["Stella", "Stella Unibell"];
        Assert.Equal(HealthSeverity.Blockierend, Worst(r));
    }

    [Fact]
    public void Teilweise_unvollstaendige_OnlyFrom_warnen()
    {
        var r = Emote();
        r.OnlyFrom = ["Stella", "Stella Unibell@Shiva"];
        Assert.Equal(HealthSeverity.Warnung, Worst(r));
    }

    [Fact]
    public void Vollstaendige_OnlyFrom_sind_gesund()
    {
        var r = Emote();
        r.OnlyFrom = ["Stella Unibell@Shiva"];
        Assert.Empty(RuleHealth.Check(r));
    }

    [Fact]
    public void Textreste_bei_Emote_Regel_sind_nur_ein_Hinweis_und_die_Regel_zaehlt()
    {
        var r = Emote();
        r.TriggerTexts = ["love you"];
        r.RequiresOwnName = true;
        Assert.Equal(HealthSeverity.Hinweis, Worst(r));

        var e = new IncomingEvent("Stella Unibell", 1, ChatChannel.StandardEmote,
                                  TargetedAtMe: true, FromSelf: false, Text: "", EmoteId: 105);
        Assert.True(RuleMatcher.Matches(r, e, "Enju"));
    }

    [Fact]
    public void Nur_leere_Triggerwoerter_blockieren_Chat_Regel()
    {
        var r = Chat();
        r.TriggerTexts = ["", "  "];
        Assert.Equal(HealthSeverity.Blockierend, Worst(r));
    }

    [Fact]
    public void Namenlose_Vorlage_ueber_der_Grenze_blockiert_ohne_Namensanzeige()
    {
        var r = Chat();
        r.ShowName = false;
        r.GeneralTemplate = new string('x', TitleTemplate.MaxTitleLength + 1) + " [Count]";
        Assert.Equal(HealthSeverity.Blockierend, Worst(r));
    }

    [Fact]
    public void Zu_lange_namenlose_Vorlage_warnt_wenn_die_persoenliche_passt()
    {
        var r = Chat();
        r.GeneralTemplate = new string('x', TitleTemplate.MaxTitleLength + 1);
        Assert.Equal(HealthSeverity.Warnung, Worst(r));
    }

    [Fact]
    public void Beide_Vorlagen_zu_lang_blockiert()
    {
        var r = Chat();
        r.PersonalTemplate = new string('x', 40) + " [Name]";
        r.GeneralTemplate = new string('x', 40);
        Assert.Equal(HealthSeverity.Blockierend, Worst(r));
    }

    [Fact]
    public void Persoenliche_Vorlage_die_nie_passt_warnt_wenn_der_Rueckfall_passt()
    {
        var r = Chat();
        r.PersonalTemplate = new string('x', 30) + " [Name] [Count]";
        Assert.Equal(HealthSeverity.Warnung, Worst(r));
    }

    [Fact]
    public void Steuerzeichen_in_der_Vorlage_warnen()
    {
        var r = Chat();
        r.GeneralTemplate = "got\tlove [Count]x";
        Assert.Equal(HealthSeverity.Warnung, Worst(r));
    }

    [Fact]
    public void Gegenvorlage_wird_nur_bei_Both_geprueft()
    {
        var r = Emote();
        r.GivenGeneralTemplate = new string('x', 40);
        Assert.Empty(RuleHealth.Check(r));

        r.Direction = CountDirection.Both;
        Assert.Equal(HealthSeverity.Warnung, Worst(r));
    }
}
