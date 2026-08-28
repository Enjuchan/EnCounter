using EnCounter.Core;
using Xunit;

namespace EnCounter.Core.Tests;

/// <summary>
/// Regeln, die nur bei bestimmten Personen anspringen. Verglichen wird der Vorname,
/// weil nur der gespeichert wird — eingetippt werden darf trotzdem der volle Name.
/// </summary>
public class PersonFilterTests
{
    private static Rule Regel(params string[] namen) => new()
    {
        Name = "Test",
        Id = "test",
        Enabled = true,
        Channels = [ChatChannel.Say],
        TriggerTexts = [],
        OnlyFrom = [.. namen],
    };

    private static IncomingEvent Von(string name) =>
        new(name, 67, ChatChannel.Say, TargetedAtMe: true, FromSelf: false, Text: "hallo");

    [Fact]
    public void Leere_Liste_laesst_jeden_durch()
    {
        Assert.True(RuleMatcher.Matches(Regel(), Von("Enju"), "Stella"));
    }

    [Fact]
    public void Eingetragene_Person_zaehlt()
    {
        Assert.True(RuleMatcher.Matches(Regel("Enju"), Von("Enju"), "Stella"));
    }

    [Fact]
    public void Fremde_Person_zaehlt_nicht()
    {
        Assert.False(RuleMatcher.Matches(Regel("Enju"), Von("Mara"), "Stella"));
    }

    [Fact]
    public void Gross_und_Kleinschreibung_egal()
    {
        Assert.True(RuleMatcher.Matches(Regel("enju"), Von("Enju"), "Stella"));
    }

    /// <summary>
    /// Gespeichert wird nur der Vorname. Wer den vollen Namen eintraegt, meint
    /// dieselbe Person - sonst zaehlt die Regel nie und niemand versteht warum.
    /// </summary>
    [Fact]
    public void Voller_Name_trifft_den_Vornamen()
    {
        Assert.True(RuleMatcher.Matches(Regel("Enju Kun"), Von("Enju"), "Stella"));
    }

    [Fact]
    public void Mehrere_Namen_sind_ein_Oder()
    {
        var regel = Regel("Enju", "Mara");

        Assert.True(RuleMatcher.Matches(regel, Von("Mara"), "Stella"));
        Assert.False(RuleMatcher.Matches(regel, Von("Rayleigh"), "Stella"));
    }

    [Fact]
    public void Leerer_Eintrag_laesst_niemanden_durch()
    {
        Assert.False(RuleMatcher.Matches(Regel("  "), Von("Enju"), "Stella"));
    }
}
