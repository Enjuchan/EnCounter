using EnCounter.Core;
using Xunit;

namespace EnCounter.Core.Tests;

public class CooldownGateTests
{
    private static readonly CounterKey Enju = new("Enju", 67);
    private static readonly CounterKey Mara = new("Mara", 67);
    private static readonly DateTime Start = new(2026, 8, 28, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Erster_Treffer_geht_immer_durch()
    {
        var g = new CooldownGate();
        Assert.True(g.Allow("regel", Enju, 3, Start));
    }

    [Fact]
    public void Zweiter_Treffer_innerhalb_der_Sperre_wird_gebremst()
    {
        var g = new CooldownGate();
        g.Allow("regel", Enju, 3, Start);

        Assert.False(g.Allow("regel", Enju, 3, Start.AddSeconds(1)));
    }

    [Fact]
    public void Nach_Ablauf_geht_es_wieder()
    {
        var g = new CooldownGate();
        g.Allow("regel", Enju, 3, Start);

        Assert.True(g.Allow("regel", Enju, 3, Start.AddSeconds(3)));
    }

    /// <summary>
    /// Der entscheidende Fall: Dass A gerade gezaehlt hat, darf B nicht blockieren.
    /// Sonst wuerde ein einzelner Vielemoter alle anderen aussperren.
    /// </summary>
    [Fact]
    public void Verschiedene_Personen_bremsen_sich_nicht_gegenseitig()
    {
        var g = new CooldownGate();
        g.Allow("regel", Enju, 30, Start);

        Assert.True(g.Allow("regel", Mara, 30, Start));
    }

    [Fact]
    public void Verschiedene_Regeln_bremsen_sich_nicht_gegenseitig()
    {
        var g = new CooldownGate();
        g.Allow("hug", Enju, 30, Start);

        Assert.True(g.Allow("pet", Enju, 30, Start));
    }

    [Fact]
    public void Null_Sekunden_schaltet_die_Bremse_ab()
    {
        var g = new CooldownGate();

        Assert.True(g.Allow("regel", Enju, 0, Start));
        Assert.True(g.Allow("regel", Enju, 0, Start));
        Assert.True(g.Allow("regel", Enju, 0, Start));
    }

    /// <summary>
    /// Eine abgewiesene Anfrage darf die Sperre nicht verlaengern - sonst haelt ein
    /// Dauerfeuer den Zaehler fuer immer geschlossen.
    /// </summary>
    [Fact]
    public void Abgewiesene_Versuche_verschieben_die_Sperre_nicht()
    {
        var g = new CooldownGate();
        g.Allow("regel", Enju, 3, Start);

        Assert.False(g.Allow("regel", Enju, 3, Start.AddSeconds(1)));
        Assert.False(g.Allow("regel", Enju, 3, Start.AddSeconds(2)));
        Assert.True(g.Allow("regel", Enju, 3, Start.AddSeconds(3)));
    }
}
