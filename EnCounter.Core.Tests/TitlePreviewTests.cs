using EnCounter.Core;
using Xunit;

namespace EnCounter.Core.Tests;

public class TitlePreviewTests
{
    private static Rule Regel() => new()
    {
        PersonalTemplate = "[Name] petted me [Count]x",
        GeneralTemplate = "petted [Count]x",
        GivenPersonalTemplate = "petted [Name] [Count]x",
        GivenGeneralTemplate = "petted others [Count]x",
    };

    [Fact]
    public void Zeigt_Vornamen_und_persoenlichen_Stand()
    {
        var text = TitleTemplate.Preview(Regel());
        Assert.Equal($"Stella petted me {TitleTemplate.ExamplePersonalCount}x", text);
    }

    [Fact]
    public void Faellt_auf_die_Vorlage_ohne_Namen_zurueck_wenn_es_zu_lang_wird()
    {
        var r = Regel();
        r.PersonalTemplate = "[Name] petted me so very very much [Count]x";

        Assert.Equal($"petted {TitleTemplate.ExampleTotalCount}x", TitleTemplate.Preview(r));
    }

    [Fact]
    public void Ohne_Namensanzeige_gilt_die_Summe()
    {
        var r = Regel();
        r.ShowName = false;
        Assert.Equal($"petted {TitleTemplate.ExampleTotalCount}x", TitleTemplate.Preview(r));
    }

    [Fact]
    public void Both_liefert_beide_Richtungen()
    {
        var r = Regel();
        r.Direction = CountDirection.Both;

        Assert.Equal($"Stella petted me {TitleTemplate.ExamplePersonalCount}x",
                     TitleTemplate.Preview(r, outgoing: false));
        Assert.Equal($"petted Stella {TitleTemplate.ExamplePersonalCount}x",
                     TitleTemplate.Preview(r, outgoing: true));
    }

    [Fact]
    public void Ohne_Both_gilt_auch_ausgehend_die_normale_Vorlage()
    {
        var r = Regel();
        Assert.Equal(TitleTemplate.Preview(r), TitleTemplate.Preview(r, outgoing: true));
    }
}
