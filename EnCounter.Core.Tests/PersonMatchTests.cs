using EnCounter.Core;
using Xunit;

namespace EnCounter.Core.Tests;

/// <summary>
/// Der Personenvergleich ist oeffentlich, weil ihn zwei Stellen brauchen: die
/// Regelpruefung und die Oberflaeche, die den Stand einer Regel summiert.
///
/// Ohne den zweiten Fall zeigte die Regelliste bei einer Regel "nur fuer X" den Stand
/// **aller** - der Zaehler haengt am Ereignis und wird geteilt.
/// </summary>
public class PersonMatchTests
{
    [Fact]
    public void Vollstaendiger_Eintrag_trifft_die_Person()
    {
        Assert.True(RuleMatcher.Matches("Stella Unibell@Shiva", "Stella Unibell", "Shiva"));
    }

    [Fact]
    public void Gleicher_Vorname_anderer_Nachname_trifft_nicht()
    {
        Assert.False(RuleMatcher.Matches("Stella Unibell@Shiva", "Stella Lovebell", "Shiva"));
    }

    [Fact]
    public void Andere_Welt_trifft_nicht()
    {
        Assert.False(RuleMatcher.Matches("Stella Unibell@Shiva", "Stella Unibell", "Odin"));
    }

    [Fact]
    public void Gross_und_Kleinschreibung_egal()
    {
        Assert.True(RuleMatcher.Matches("stella unibell@shiva", "Stella Unibell", "Shiva"));
    }

    [Theory]
    [InlineData("Stella")]
    [InlineData("Stella Unibell")]
    [InlineData("Stella@Shiva")]
    [InlineData(null)]
    [InlineData("")]
    public void Unvollstaendiges_trifft_nie(string? eintrag)
    {
        Assert.False(RuleMatcher.Matches(eintrag, "Stella Unibell", "Shiva"));
    }

    /// <summary>
    /// Der gemeldete Fall in Zahlen: Eine Pet-Regel nur fuer eine Person soll deren
    /// Anteil zeigen, nicht die Summe aller.
    /// </summary>
    [Fact]
    public void Nur_die_genannte_Person_zaehlt()
    {
        (string Name, string Welt, int Anzahl)[] staende =
        [
            ("Stella Unibell", "Shiva", 12),
            ("Rayleigh Snoww", "Shiva", 30),
            ("Stella Lovebell", "Shiva", 5),
        ];

        var summe = staende
            .Where(s => RuleMatcher.Matches("Stella Unibell@Shiva", s.Name, s.Welt))
            .Sum(s => s.Anzahl);

        Assert.Equal(12, summe);
    }
}
