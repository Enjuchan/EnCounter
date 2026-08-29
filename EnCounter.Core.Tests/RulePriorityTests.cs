using EnCounter.Core;
using Xunit;

namespace EnCounter.Core.Tests;

/// <summary>
/// Gezaehlt wird von allen passenden Regeln, angezeigt nur von einer. Welcher, das
/// entscheidet die Genauigkeit - sonst ueberdeckt eine allgemeine Regel jede
/// persoenliche, die man danebenstellt.
/// </summary>
public class RulePriorityTests
{
    private static Rule Regel(string name, string[]? nur = null, uint[]? emotes = null)
    {
        var r = new Rule
        {
            Name = name,
            Channels = [ChatChannel.StandardEmote],
            OnlyFrom = [.. nur ?? []],
            EmoteIds = [.. emotes ?? []],
        };

        r.EnsureId();
        return r;
    }

    /// <summary>
    /// Der gemeldete Fall: „streicheln fuer alle" plus eine Regel nur fuer eine
    /// Person. Vorher gewann immer die allgemeine, weil sie zuerst in der Liste stand.
    /// </summary>
    [Fact]
    public void Persoenliche_Regel_gewinnt_gegen_allgemeine()
    {
        var allgemein = Regel("Pet", emotes: [105]);
        var persoenlich = Regel("Pet Ray", ["Rayleigh@Shiva"], [105]);

        Assert.Equal(persoenlich, RulePriority.Winner([allgemein, persoenlich]));
    }

    /// <summary>Auch dann, wenn die allgemeine Regel weiter oben steht.</summary>
    [Fact]
    public void Reihenfolge_schlaegt_die_Genauigkeit_nicht()
    {
        var allgemein = Regel("Pet", emotes: [105]);
        var persoenlich = Regel("Pet Ray", ["Rayleigh"], [105]);

        Assert.Equal(persoenlich, RulePriority.Winner([allgemein, persoenlich]));
        Assert.Equal(persoenlich, RulePriority.Winner([persoenlich, allgemein]));
    }

    [Fact]
    public void Emote_Auswahl_ist_genauer_als_alle_Emotes()
    {
        var alle = Regel("Alles");
        var nurDote = Regel("Dote", emotes: [146]);

        Assert.Equal(nurDote, RulePriority.Winner([alle, nurDote]));
    }

    /// <summary>Eine Person zu benennen wiegt schwerer als ein Emote zu waehlen.</summary>
    [Fact]
    public void Namensliste_wiegt_schwerer_als_Emote_Auswahl()
    {
        var nurDote = Regel("Dote", emotes: [146]);
        var nurRay = Regel("Ray", ["Rayleigh"]);

        Assert.Equal(nurRay, RulePriority.Winner([nurDote, nurRay]));
    }

    [Fact]
    public void Bei_gleicher_Genauigkeit_gewinnt_die_erste()
    {
        var a = Regel("A", ["Rayleigh"], [105]);
        var b = Regel("B", ["Azura"], [105]);

        Assert.Equal(a, RulePriority.Winner([a, b]));
    }

    [Fact]
    public void Ohne_passende_Regel_gibt_es_keinen_Gewinner()
    {
        Assert.Null(RulePriority.Winner([]));
    }
}
