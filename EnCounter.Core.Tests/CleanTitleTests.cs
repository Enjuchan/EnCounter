using EnCounter.Core;
using Xunit;

namespace EnCounter.Core.Tests;

/// <summary>
/// Honorific verwirft **jeden** Titel mit Steuerzeichen - `CustomTitle.IsValid` gibt
/// dann false, und es erscheint gar nichts. Von aussen sieht das aus, als haette die
/// Regel nicht gegriffen; genau so wurde es am 29.08.2026 gemeldet.
///
/// Am echten Honorific 1.7.5.0 gemessen: 0x02, Tabulator und Zeilenumbruch fuehren zu
/// `IsValid=false`, Sterne, Emoji und FFXIVs eigene Symbole nicht.
/// </summary>
public class CleanTitleTests
{
    [Theory]
    [InlineData(" Raphy patted me ", " Raphy patted me ")]   // Auto-Translate-Reste
    [InlineData("\t Raphy \t", " Raphy ")]
    [InlineData("Zeile\nUmbruch", "ZeileUmbruch")]
    public void Steuerzeichen_werden_entfernt(string roh, string erwartet)
    {
        Assert.Equal(erwartet, TitleTemplate.Clean(roh));
    }

    /// <summary>
    /// Sichtbare Deko muss bleiben - sie ist der halbe Reiz an eigenen Titeln, und
    /// Honorific nimmt sie anstandslos.
    /// </summary>
    [Theory]
    [InlineData("★ Raphy patted me ★")]     // Stern
    [InlineData("♥ Raphy ♥")]               // Herz
    [InlineData(" Raphy ")]               // FFXIV-Privatzeichen
    [InlineData("\U0001F49C Raphy \U0001F49C")]       // Emoji
    [InlineData("  Raphy  ")]               // geschuetztes Leerzeichen
    public void Sichtbare_Deko_bleibt(string roh)
    {
        Assert.Equal(roh, TitleTemplate.Clean(roh));
    }

    [Fact]
    public void Sauberer_Text_bleibt_unveraendert()
    {
        const string text = "Raphy patted me [Count]x";
        Assert.Equal(text, TitleTemplate.Clean(text));
        Assert.False(TitleTemplate.HasControlCharacters(text));
    }

    [Fact]
    public void Leeres_bleibt_leer()
    {
        Assert.Equal(string.Empty, TitleTemplate.Clean(null));
        Assert.Equal(string.Empty, TitleTemplate.Clean(""));
    }

    [Fact]
    public void Steuerzeichen_werden_erkannt()
    {
        Assert.True(TitleTemplate.HasControlCharacters(" Raphy"));
        Assert.False(TitleTemplate.HasControlCharacters("★ Raphy"));
    }

    /// <summary>
    /// Der gemeldete Fall: Die Vorlage traegt Steuerzeichen, der Titel muss trotzdem
    /// erscheinen - gereinigt statt gar nicht.
    /// </summary>
    [Fact]
    public void Build_liefert_einen_gueltigen_Titel_trotz_Steuerzeichen()
    {
        var regel = new Rule
        {
            ShowName = true,
            PersonalTemplate = " Raphy patted me ",
            GeneralTemplate = "Pet Counter [Count]",
        };

        var titel = TitleTemplate.Build(regel, "Raphtalia Redpanda", 5, 12);

        Assert.Equal(" Raphy patted me ", titel);
        Assert.False(TitleTemplate.HasControlCharacters(titel));
    }

    /// <summary>Auch der Rueckfall auf die namenlose Vorlage muss sauber sein.</summary>
    [Fact]
    public void Auch_die_namenlose_Vorlage_wird_gereinigt()
    {
        var regel = new Rule
        {
            ShowName = false,
            GeneralTemplate = "\tPet Counter [Count]",
        };

        Assert.Equal("Pet Counter 12", TitleTemplate.Build(regel, "Wer", 5, 12));
    }
}
