using EnCounter.Core;
using Xunit;

namespace EnCounter.Core.Tests;

/// <summary>
/// `Only from` verlangt die vollstaendige Form `Vorname Nachname@Welt`.
///
/// Kuerzere Formen waren zwischenzeitlich erlaubt und sind wieder abgeschafft: Wer nur
/// „Stella" eintraegt, meint eine Person, traefe aber jede mit diesem Vornamen - und
/// wuerde es nie bemerken, weil eine zu hohe Zahl nach nichts aussieht.
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

    private static IncomingEvent Von(string name, string welt = "Shiva") =>
        new(name, 0, ChatChannel.Say, TargetedAtMe: true, FromSelf: false,
            Text: "hallo", SenderWorld: welt);

    [Fact]
    public void Leere_Liste_laesst_jeden_durch()
    {
        Assert.True(RuleMatcher.Matches(Regel(), Von("Stella Unibell"), "Enju"));
    }

    [Fact]
    public void Vollstaendiger_Eintrag_trifft()
    {
        Assert.True(RuleMatcher.Matches(
            Regel("Stella Unibell@Shiva"), Von("Stella Unibell"), "Enju"));
    }

    [Fact]
    public void Fremde_Person_zaehlt_nicht()
    {
        Assert.False(RuleMatcher.Matches(
            Regel("Stella Unibell@Shiva"), Von("Rayleigh Snoww"), "Enju"));
    }

    /// <summary>Der Fall, um den es geht: gleicher Vorname, anderer Nachname.</summary>
    [Fact]
    public void Gleicher_Vorname_anderer_Nachname_zaehlt_nicht()
    {
        Assert.False(RuleMatcher.Matches(
            Regel("Stella Unibell@Shiva"), Von("Stella Lovebell"), "Enju"));
    }

    [Fact]
    public void Andere_Welt_zaehlt_nicht()
    {
        Assert.False(RuleMatcher.Matches(
            Regel("Stella Unibell@Shiva"), Von("Stella Unibell", "Odin"), "Enju"));
    }

    [Fact]
    public void Gross_und_Kleinschreibung_egal()
    {
        Assert.True(RuleMatcher.Matches(
            Regel("stella unibell@shiva"), Von("Stella Unibell"), "Enju"));
    }

    [Fact]
    public void Leerzeichen_um_das_At_stoeren_nicht()
    {
        Assert.True(RuleMatcher.Matches(
            Regel(" Stella Unibell @ Shiva "), Von("Stella Unibell"), "Enju"));
    }

    [Fact]
    public void Mehrere_Namen_sind_ein_Oder()
    {
        var regel = Regel("Stella Unibell@Shiva", "Rayleigh Snoww@Odin");

        Assert.True(RuleMatcher.Matches(regel, Von("Rayleigh Snoww", "Odin"), "Enju"));
        Assert.False(RuleMatcher.Matches(regel, Von("Rayleigh Snoww", "Shiva"), "Enju"));
    }

    // --- unvollstaendige Eintraege --------------------------------------------

    [Theory]
    [InlineData("Stella")]                 // kein Nachname, keine Welt
    [InlineData("Stella Unibell")]         // keine Welt
    [InlineData("Stella@Shiva")]           // kein Nachname
    [InlineData("@Shiva")]                 // kein Name
    [InlineData("Stella Unibell@")]        // Welt leer
    [InlineData("   ")]
    public void Unvollstaendiges_trifft_nie(string eintrag)
    {
        Assert.False(RuleMatcher.IstVollstaendig(eintrag));

        Assert.False(RuleMatcher.Matches(
            Regel(eintrag), Von("Stella Unibell"), "Enju"));
    }

    [Fact]
    public void Vollstaendig_erkennt_die_richtige_Form()
    {
        Assert.True(RuleMatcher.IstVollstaendig("Stella Unibell@Shiva"));
        Assert.True(RuleMatcher.IstVollstaendig(" Stella Unibell @ Shiva "));
    }

    /// <summary>
    /// Ein unvollstaendiger Eintrag darf die anderen nicht mitreissen - sonst faellt
    /// eine ganze Regel aus, weil sich jemand einmal vertippt hat.
    /// </summary>
    [Fact]
    public void Ein_kaputter_Eintrag_blockiert_die_anderen_nicht()
    {
        var regel = Regel("Stella", "Rayleigh Snoww@Odin");

        Assert.True(RuleMatcher.Matches(regel, Von("Rayleigh Snoww", "Odin"), "Enju"));
    }
}
