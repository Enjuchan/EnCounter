using System.Collections;
using System.Reflection;
using EnCounter.Core;
using Xunit;

namespace EnCounter.Core.Tests;

/// <summary>
/// Die Kopiermethode wurde beim Erweitern des Modells viermal vergessen. Der Test
/// prueft deshalb **ueber Reflection alle Felder**, nicht eine handgepflegte Liste -
/// sonst waere er beim naechsten neuen Feld genauso blind wie der Code.
/// </summary>
public class RuleCopyTests
{
    /// <summary>Eine Regel, in der jedes Feld vom Standardwert abweicht.</summary>
    private static Rule Bunt()
    {
        var r = new Rule
        {
            Name = "Original",
            Enabled = false,
            Channels = [ChatChannel.Say, ChatChannel.Cwls3],
            TriggerTexts = ["<3", "love"],
            RequiresOwnName = true,
            EmoteIds = [112, 113],
            Direction = CountDirection.Both,
            CooldownSeconds = 42,
            ShowName = false,
            PersonalTemplate = "p",
            GeneralTemplate = "g",
            GivenPersonalTemplate = "gp",
            GivenGeneralTemplate = "gg",
            OnlyFrom = ["Rayleigh@Shiva"],
            IsPrefix = false,
            DisplaySeconds = 33,
            UseColours = true,
            ColourR = 0.1f, ColourG = 0.2f, ColourB = 0.3f,
            GlowR = 0.4f, GlowG = 0.5f, GlowB = 0.6f,
            UseGradient = true,
            Colour3R = 0.7f, Colour3G = 0.8f, Colour3B = 0.9f,
            GradientStyle = 2,
        };

        r.EnsureId();
        return r;
    }

    [Fact]
    public void Kopie_uebernimmt_jedes_Feld()
    {
        var original = Bunt();
        var kopie = original.Copy();

        foreach (var p in typeof(Rule).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // Die Id muss sich unterscheiden - sonst teilten sich beide den Zaehler.
            if (p.Name == nameof(Rule.Id))
                continue;

            var a = p.GetValue(original);
            var b = p.GetValue(kopie);

            if (a is IEnumerable listeA and not string)
            {
                Assert.Equal(listeA.Cast<object>(), ((IEnumerable)b!).Cast<object>());
                continue;
            }

            Assert.Equal(a, b);
        }
    }

    [Fact]
    public void Kopie_bekommt_eine_eigene_Id()
    {
        var original = Bunt();
        var kopie = original.Copy();

        Assert.NotEqual(original.Id, kopie.Id);
        Assert.NotEmpty(kopie.Id);
    }

    /// <summary>
    /// Flache Kopien der Listen waeren der unauffaelligste Fehler: Ein Haekchen in der
    /// Kopie wuerde still auch das Original aendern.
    /// </summary>
    [Fact]
    public void Listen_sind_eigenstaendig()
    {
        var original = Bunt();
        var kopie = original.Copy();

        kopie.Channels.Add(ChatChannel.Yell);
        kopie.TriggerTexts.Add("neu");
        kopie.EmoteIds.Add(146);
        kopie.OnlyFrom.Add("Wer Auchimmer");

        Assert.Equal(2, original.Channels.Count);
        Assert.Equal(2, original.TriggerTexts.Count);
        Assert.Equal(2, original.EmoteIds.Count);
        Assert.Single(original.OnlyFrom);
    }

    [Fact]
    public void Name_laesst_sich_beim_Kopieren_setzen()
    {
        Assert.Equal("Original (copy)", Bunt().Copy("Original (copy)").Name);
    }
}
