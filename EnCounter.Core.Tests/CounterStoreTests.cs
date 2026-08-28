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

    // --- Uebersicht und Berichtigung ------------------------------------------

    [Fact]
    public void Uebersicht_zeigt_die_hoechsten_Staende_zuerst()
    {
        var s = new CounterStore();
        s.Increment("streicheln", Mara);
        s.Increment("streicheln", Enju);
        s.Increment("streicheln", Enju);

        var liste = s.GetAll("streicheln");

        Assert.Equal(2, liste.Count);
        Assert.Equal(Enju, liste[0].Person);
        Assert.Equal(2, liste[0].Count);
        Assert.Equal(Mara, liste[1].Person);
    }

    [Fact]
    public void Uebersicht_einer_unbekannten_Regel_ist_leer()
    {
        Assert.Empty(new CounterStore().GetAll("gibtsnicht"));
    }

    [Fact]
    public void Stand_laesst_sich_von_Hand_setzen()
    {
        var s = new CounterStore();
        s.Increment("streicheln", Enju);
        s.Set("streicheln", Enju, 42);

        Assert.Equal(42, s.GetPersonal("streicheln", Enju));
    }

    /// <summary>
    /// Null bedeutet weg, nicht "steht auf null" - ein Eintrag ohne Zaehlung waere in
    /// der Uebersicht nur Rauschen.
    /// </summary>
    [Fact]
    public void Auf_null_gesetzt_verschwindet_der_Eintrag()
    {
        var s = new CounterStore();
        s.Increment("streicheln", Enju);
        s.Set("streicheln", Enju, 0);

        Assert.Equal(0, s.PersonCount("streicheln"));
    }

    [Fact]
    public void Einzelne_Person_laesst_sich_entfernen()
    {
        var s = new CounterStore();
        s.Increment("streicheln", Enju);
        s.Increment("streicheln", Mara);
        s.Remove("streicheln", Enju);

        Assert.Equal(1, s.GetTotal("streicheln"));
        Assert.Equal(0, s.GetPersonal("streicheln", Enju));
    }

    [Fact]
    public void Ganze_Regel_laesst_sich_zuruecksetzen()
    {
        var s = new CounterStore();
        s.Increment("streicheln", Enju);
        s.Increment("umarmen", Enju);
        s.Reset("streicheln");

        Assert.Equal(0, s.GetTotal("streicheln"));
        Assert.Equal(1, s.GetTotal("umarmen"));
    }

    // --- Wandern alter Staende ------------------------------------------------

    [Fact]
    public void Rekey_traegt_die_Staende_unter_die_neue_Id()
    {
        var s = new CounterStore();
        s.Increment("Liebe", Enju);
        s.Increment("Liebe", Enju);
        s.Rekey("Liebe", "abc123");

        Assert.Equal(2, s.GetTotal("abc123"));
        Assert.Equal(0, s.GetTotal("Liebe"));
    }

    /// <summary>
    /// Liegt unter der Ziel-Id schon etwas, darf es nicht ueberschrieben werden -
    /// sonst kostet ein Namensdreher den echten Stand.
    /// </summary>
    [Fact]
    public void Rekey_ueberschreibt_vorhandene_Staende_nicht()
    {
        var s = new CounterStore();
        s.Increment("alt", Enju);
        s.Increment("neu", Mara);
        s.Increment("neu", Mara);

        s.Rekey("alt", "neu");

        Assert.Equal(2, s.GetTotal("neu"));
    }
}
