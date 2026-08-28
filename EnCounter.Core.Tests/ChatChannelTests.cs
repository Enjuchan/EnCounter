using EnCounter.Core;
using Xunit;

namespace EnCounter.Core.Tests;

/// <summary>
/// Die Werte stammen aus Dalamuds XivChatType und sind dort nicht durchweg
/// regelmaessig. Diese Tests halten die Ueberraschungen fest, damit sie niemand
/// spaeter "glattzieht".
/// </summary>
public class ChatChannelTests
{
    /// <summary>
    /// Der ChatWatcher maskiert den rohen Wert mit 0x7F, weil darueber Flags des
    /// Spiels stehen. Ein Kanal ab 128 kaeme dadurch als ein anderer an.
    /// </summary>
    [Fact]
    public void Alle_Kanaele_ueberstehen_die_Maskierung()
    {
        foreach (var kanal in Enum.GetValues<ChatChannel>())
        {
            var wert = (int)kanal;
            Assert.Equal(wert, wert & 0x7F);
        }
    }

    /// <summary>
    /// Die weltuebergreifenden Linkshells laufen **nicht** fortlaufend: Die erste
    /// steht bei 37, die uebrigen sieben erst bei 101 bis 107.
    /// </summary>
    [Fact]
    public void Weltuebergreifende_Linkshells_haben_eine_Luecke()
    {
        Assert.Equal(37, (int)ChatChannel.Cwls1);
        Assert.Equal(101, (int)ChatChannel.Cwls2);
        Assert.Equal(107, (int)ChatChannel.Cwls8);
    }

    [Fact]
    public void Gewoehnliche_Linkshells_laufen_fortlaufend()
    {
        Assert.Equal(16, (int)ChatChannel.Ls1);
        Assert.Equal(23, (int)ChatChannel.Ls8);
    }

    [Fact]
    public void Kanaele_sind_eindeutig()
    {
        var werte = Enum.GetValues<ChatChannel>().Select(k => (int)k).ToList();
        Assert.Equal(werte.Count, werte.Distinct().Count());
    }
}
