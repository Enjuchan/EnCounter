using EnCounter.Core;
using Xunit;

namespace EnCounter.Core.Tests;

public class TitleTemplateTests
{
    [Fact]
    public void Fuellt_Namen_und_Anzahl_ein()
    {
        var t = TitleTemplate.Render("[Name] petted you [Anzahl]x", "Enju", 5);
        Assert.Equal("Enju petted you 5x", t);
    }

    [Fact]
    public void Kommt_ohne_Namen_aus()
    {
        var t = TitleTemplate.Render("petted [Anzahl]x", null, 47);
        Assert.Equal("petted 47x", t);
    }

    [Fact]
    public void Freier_Text_zaehlt_ohne_Platzhalter()
    {
        // " petted you " plus "x" = 13
        Assert.Equal(13, TitleTemplate.FreeTextLength("[Name] petted you [Anzahl]x"));
        Assert.Equal(8, TitleTemplate.FreeTextLength("petted [Anzahl]x"));
    }

    [Fact]
    public void Faellt_auf_allgemeine_Vorlage_zurueck_wenn_zu_lang()
    {
        var r = new Rule
        {
            PersonalTemplate = "[Name] hat dich sehr oft gestreichelt [Anzahl]",
            GeneralTemplate = "petted [Anzahl]x",
        };

        var t = TitleTemplate.Build(r, "Ariadne", personal: 12, total: 47);
        Assert.Equal("petted 47x", t);
    }

    [Fact]
    public void Nimmt_die_persoenliche_Vorlage_wenn_sie_passt()
    {
        var r = new Rule
        {
            PersonalTemplate = "[Name] petted you [Anzahl]x",
            GeneralTemplate = "petted [Anzahl]x",
        };

        var t = TitleTemplate.Build(r, "Enju", personal: 5, total: 47);
        Assert.Equal("Enju petted you 5x", t);
    }

    [Fact]
    public void Kuerzt_niemals_sondern_weicht_aus()
    {
        var r = new Rule
        {
            PersonalTemplate = "[Name] petted you [Anzahl]x",
            GeneralTemplate = "petted [Anzahl]x",
        };

        // 14 Zeichen Name: 14 + 13 + 1 = 28, passt noch
        var t = TitleTemplate.Build(r, "Maximiliananna", personal: 5, total: 47);
        Assert.True(t.Length <= TitleTemplate.MaxTitleLength);
        Assert.DoesNotContain("\u2026", t);
    }

    [Fact]
    public void Ohne_Namen_wird_die_Summe_gezeigt()
    {
        var r = new Rule
        {
            ShowName = false,
            PersonalTemplate = "[Name] petted you [Anzahl]x",
            GeneralTemplate = "petted [Anzahl]x",
        };

        // Der persoenliche Zaehler waere 5, die Summe ist 47.
        var t = TitleTemplate.Build(r, "Enju", personal: 5, total: 47);
        Assert.Equal("petted 47x", t);
    }

    [Fact]
    public void Maximale_Namenslaenge_haengt_an_der_Vorlage()
    {
        // 13 Zeichen freier Text: reicht fuer jeden Namen.
        Assert.Equal(15, TitleTemplate.MaxNameLengthFor("[Name] petted you [Anzahl]x"));

        // Laenger: reicht nur noch fuer kuerzere Namen.
        // " hugged you warmly " sind 19 Zeichen: 32 - 19 - 4 = 9
        Assert.Equal(9, TitleTemplate.MaxNameLengthFor("[Name] hugged you warmly [Anzahl]"));

        // Zu lang fuer jeden Namen - Vornamen haben mindestens zwei Zeichen.
        Assert.Equal(0, TitleTemplate.MaxNameLengthFor("[Name] hugged you very warmly indeed [Anzahl]"));
    }

    [Fact]
    public void Grenzwerte_stimmen()
    {
        Assert.Equal(32, TitleTemplate.MaxTitleLength);
        Assert.Equal(13, TitleTemplate.MaxPersonalFreeText);
    }
}
