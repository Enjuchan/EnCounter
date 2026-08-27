using EnCounter.Core;
using Xunit;

namespace EnCounter.Core.Tests;

public class CounterStoreTests
{
    private static readonly CounterKey Enju = new("Enju", 67);
    private static readonly CounterKey Mara = new("Mara", 67);

    [Fact]
    public void Zaehlt_pro_Person_hoch()
    {
        var s = new CounterStore();

        Assert.Equal(1, s.Increment("streicheln", Enju));
        Assert.Equal(2, s.Increment("streicheln", Enju));
        Assert.Equal(1, s.Increment("streicheln", Mara));
    }

    [Fact]
    public void Summe_zaehlt_alle_Personen()
    {
        var s = new CounterStore();
        s.Increment("streicheln", Enju);
        s.Increment("streicheln", Enju);
        s.Increment("streicheln", Mara);

        Assert.Equal(3, s.GetTotal("streicheln"));
        Assert.Equal(2, s.GetPersonal("streicheln", Enju));
    }

    [Fact]
    public void Regeln_zaehlen_getrennt()
    {
        var s = new CounterStore();
        s.Increment("streicheln", Enju);
        s.Increment("umarmen", Enju);

        Assert.Equal(1, s.GetTotal("streicheln"));
        Assert.Equal(1, s.GetTotal("umarmen"));
    }

    [Fact]
    public void Gleiche_Namen_auf_verschiedenen_Welten_bleiben_getrennt()
    {
        var s = new CounterStore();
        s.Increment("streicheln", new CounterKey("Enju", 67));
        s.Increment("streicheln", new CounterKey("Enju", 40));

        Assert.Equal(1, s.GetPersonal("streicheln", new CounterKey("Enju", 67)));
        Assert.Equal(2, s.GetTotal("streicheln"));
    }

    [Fact]
    public void Unbekannte_Eintraege_liefern_null()
    {
        var s = new CounterStore();

        Assert.Equal(0, s.GetTotal("gibtsnicht"));
        Assert.Equal(0, s.GetPersonal("gibtsnicht", Enju));
    }
}
