using EnCounter.Core;
using Xunit;

namespace EnCounter.Core.Tests;

/// <summary>
/// Bei „beides" fallen empfangene und gegebene Emotes in dieselbe Regel. Ohne eigene
/// Vorlage stuende „[Name] hugged me" auch dann da, wenn man selbst umarmt hat.
/// </summary>
public class GivenTemplateTests
{
    private static Rule Regel() => new()
    {
        Name = "Hug",
        Id = "hug",
        ShowName = true,
        Direction = CountDirection.Both,
        PersonalTemplate = "[Name] hugged me [Count]x",
        GeneralTemplate = "hugged [Count]x",
        GivenPersonalTemplate = "hugged [Name] [Count]x",
        GivenGeneralTemplate = "hugged others [Count]x",
    };

    [Fact]
    public void Empfangen_nimmt_die_normale_Vorlage()
    {
        Assert.Equal("Enju hugged me 3x",
            TitleTemplate.Build(Regel(), "Enju", 3, 9, outgoing: false));
    }

    [Fact]
    public void Gegeben_nimmt_die_Gegenvorlage()
    {
        Assert.Equal("hugged Enju 3x",
            TitleTemplate.Build(Regel(), "Enju", 3, 9, outgoing: true));
    }

    [Fact]
    public void Ohne_Namen_gilt_dasselbe_getrennt()
    {
        var regel = Regel();
        regel.ShowName = false;

        Assert.Equal("hugged 9x", TitleTemplate.Build(regel, "Enju", 3, 9, outgoing: false));
        Assert.Equal("hugged others 9x", TitleTemplate.Build(regel, "Enju", 3, 9, outgoing: true));
    }

    /// <summary>
    /// Der wichtigste Fall fuer bestehende Regeln: Wer keine Gegenvorlage gepflegt
    /// hat, bekommt weiterhin die normale - nicht einen leeren Titel.
    /// </summary>
    [Fact]
    public void Leere_Gegenvorlage_faellt_auf_die_normale_zurueck()
    {
        var regel = Regel();
        regel.GivenPersonalTemplate = string.Empty;
        regel.GivenGeneralTemplate = string.Empty;

        Assert.Equal("Enju hugged me 3x",
            TitleTemplate.Build(regel, "Enju", 3, 9, outgoing: true));
    }

    [Fact]
    public void Nur_Leerzeichen_zaehlt_als_leer()
    {
        var regel = Regel();
        regel.GivenPersonalTemplate = "   ";

        Assert.Equal("Enju hugged me 3x",
            TitleTemplate.Build(regel, "Enju", 3, 9, outgoing: true));
    }

    /// <summary>
    /// Der Rueckfall bei zu langem Namen muss die Vorlage **derselben** Richtung
    /// nehmen - sonst wechselt die Meldung mitten im Satz die Perspektive.
    /// </summary>
    [Fact]
    public void Rueckfall_bei_Ueberlaenge_bleibt_in_der_Richtung()
    {
        var regel = Regel();
        regel.GivenPersonalTemplate = "hugged [Name] very warmly indeed [Count]x";

        Assert.Equal("hugged others 9x",
            TitleTemplate.Build(regel, "Wilhelmina", 3, 9, outgoing: true));
    }

    // --- nur eine Richtung ----------------------------------------------------

    /// <summary>
    /// Der gemeldete Fall: Eine Both-Regel wird dupliziert, das Duplikat auf eine
    /// Richtung gestellt. Die Gegenvorlage kommt beim Kopieren mit, ist in der
    /// Oberflaeche aber nicht mehr zu sehen - sie darf deshalb auch nicht mehr
    /// benutzt werden. Sonst aendert man einen Text, der nicht gelesen wird.
    /// </summary>
    [Fact]
    public void Bei_nur_Given_gilt_die_normale_Vorlage()
    {
        var regel = Regel();
        regel.Direction = CountDirection.Given;
        regel.PersonalTemplate = "das hier soll erscheinen";

        Assert.Equal("das hier soll erscheinen",
            TitleTemplate.Build(regel, "Enju", 3, 9, outgoing: true));
    }

    [Fact]
    public void Bei_nur_Received_ebenso()
    {
        var regel = Regel();
        regel.Direction = CountDirection.Received;
        regel.PersonalTemplate = "das hier soll erscheinen";

        Assert.Equal("das hier soll erscheinen",
            TitleTemplate.Build(regel, "Enju", 3, 9, outgoing: false));
    }

    /// <summary>
    /// Die Gegenvorlage bleibt gespeichert - wer zurueck auf "beides" stellt, hat sie
    /// wieder. Weggeworfen wird nichts.
    /// </summary>
    [Fact]
    public void Zurueck_auf_Both_bringt_die_Gegenvorlage_wieder()
    {
        var regel = Regel();

        regel.Direction = CountDirection.Given;
        Assert.Equal("Enju hugged me 3x", TitleTemplate.Build(regel, "Enju", 3, 9, outgoing: true));

        regel.Direction = CountDirection.Both;
        Assert.Equal("hugged Enju 3x", TitleTemplate.Build(regel, "Enju", 3, 9, outgoing: true));
    }
}
