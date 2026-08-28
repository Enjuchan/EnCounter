using EnCounter.Core;
using Xunit;

namespace EnCounter.Core.Tests;

/// <summary>
/// Der Zaehler gehoert dem Ereignis, nicht der Regel: Zwei Regeln fuer dasselbe Emote
/// lesen denselben Stand. Empfangenes und Gegebenes bleiben trotzdem getrennt.
/// </summary>
public class CounterIdTests
{
    private static readonly CounterKey Ray = new("Rayleigh", 67);

    private static Rule EmoteRegel(params uint[] emotes)
    {
        var r = new Rule
        {
            Name = "Dote",
            Channels = [ChatChannel.StandardEmote, ChatChannel.CustomEmote],
            EmoteIds = [.. emotes],
            Direction = CountDirection.Both,
        };

        r.EnsureId();
        return r;
    }

    private static IncomingEvent Dote(bool outgoing = false) =>
        new("Rayleigh", 67, ChatChannel.StandardEmote,
            TargetedAtMe: !outgoing, FromSelf: outgoing, Text: "", EmoteId: 146, Outgoing: outgoing);

    // --- geteilte Staende ------------------------------------------------------

    /// <summary>
    /// Der Kern: Eine allgemeine und eine persoenliche Regel fuer dasselbe Emote
    /// muessen auf denselben Schluessel zeigen. Sonst faengt die neue Regel bei null
    /// an, obwohl dieselbe Person laengst gezaehlt wurde.
    /// </summary>
    [Fact]
    public void Zwei_Regeln_fuer_dasselbe_Emote_teilen_den_Stand()
    {
        var allgemein = EmoteRegel(146);
        var nurRay = EmoteRegel(146);
        nurRay.OnlyFrom = ["Rayleigh"];

        Assert.Equal(CounterScope.ForEvent(allgemein, Dote()),
                     CounterScope.ForEvent(nurRay, Dote()));
    }

    [Fact]
    public void Neue_Regel_sieht_den_bisherigen_Stand()
    {
        var s = new CounterStore();
        var alt = EmoteRegel(146);

        for (var i = 0; i < 11; i++)
            s.Increment(CounterScope.ForEvent(alt, Dote()), Ray);

        // Spaeter angelegt, eigene Id, trotzdem derselbe Stand.
        var neu = EmoteRegel(146);
        Assert.Equal(11, s.GetTotal(CounterScope.ForRule(neu, outgoing: false)));
    }

    [Fact]
    public void Verschiedene_Emotes_bleiben_getrennt()
    {
        Assert.NotEqual(CounterScope.ForEvent(EmoteRegel(146), Dote()),
                        CounterScope.ForEvent(EmoteRegel(112), Dote() with { EmoteId = 112 }));
    }

    // --- Richtungen ------------------------------------------------------------

    [Fact]
    public void Beide_Richtungen_haben_verschiedene_Raeume()
    {
        var r = EmoteRegel(146);

        Assert.NotEqual(CounterScope.ForEvent(r, Dote(outgoing: false)),
                        CounterScope.ForEvent(r, Dote(outgoing: true)));
    }

    /// <summary>
    /// Der Fall aus dem Spiel: Ray dotet mich elfmal, ich ihn zwoelfmal. Vorher stand
    /// danach 23 da - jetzt zwei Staende.
    /// </summary>
    [Fact]
    public void Staende_laufen_getrennt_hoch()
    {
        var r = EmoteRegel(146);
        var s = new CounterStore();

        for (var i = 0; i < 11; i++)
            s.Increment(CounterScope.ForEvent(r, Dote(outgoing: false)), Ray);

        for (var i = 0; i < 12; i++)
            s.Increment(CounterScope.ForEvent(r, Dote(outgoing: true)), Ray);

        Assert.Equal(11, s.GetTotal(CounterScope.ForRule(r, outgoing: false)));
        Assert.Equal(12, s.GetTotal(CounterScope.ForRule(r, outgoing: true)));
    }

    /// <summary>
    /// Eine Regel ohne Emote-Auswahl deckt alles ab. Ihre Sicht darf trotzdem nicht
    /// das Gegebene mit einsammeln - das Praefix `e:` passt sonst auch auf `e:146:g`.
    /// </summary>
    [Fact]
    public void Regel_ohne_Auswahl_trennt_die_Richtungen_trotzdem()
    {
        var r = EmoteRegel();
        var s = new CounterStore();

        s.Increment(CounterScope.ForEvent(r, Dote(outgoing: false)), Ray);
        s.Increment(CounterScope.ForEvent(r, Dote(outgoing: true)), Ray);
        s.Increment(CounterScope.ForEvent(r, Dote(outgoing: true)), Ray);

        Assert.Equal(1, s.GetTotal(CounterScope.ForRule(r, outgoing: false)));
        Assert.Equal(2, s.GetTotal(CounterScope.ForRule(r, outgoing: true)));
    }

    [Fact]
    public void Regel_ohne_Auswahl_summiert_ueber_alle_Emotes()
    {
        var r = EmoteRegel();
        var s = new CounterStore();

        s.Increment(CounterScope.ForEvent(r, Dote()), Ray);
        s.Increment(CounterScope.ForEvent(r, Dote() with { EmoteId = 112 }), Ray);

        Assert.Equal(2, s.GetTotal(CounterScope.ForRule(r, outgoing: false)));
    }

    /// <summary>Mehrere Emotes in einer Regel werden zusammengezaehlt.</summary>
    [Fact]
    public void Regel_mit_zwei_Emotes_summiert_beide()
    {
        var hug = EmoteRegel(112, 113);
        var s = new CounterStore();

        s.Increment(CounterScope.ForEvent(hug, Dote() with { EmoteId = 112 }), Ray);
        s.Increment(CounterScope.ForEvent(hug, Dote() with { EmoteId = 113 }), Ray);

        Assert.Equal(2, s.GetTotal(CounterScope.ForRule(hug, outgoing: false)));
    }

    // --- Chat ------------------------------------------------------------------

    [Fact]
    public void Chat_zaehlt_unter_dem_getroffenen_Triggerwort()
    {
        var a = new Rule { Channels = [ChatChannel.Say], TriggerTexts = ["<3", "love you"] };
        var b = new Rule { Channels = [ChatChannel.Party], TriggerTexts = ["Love You"] };
        a.EnsureId();
        b.EnsureId();

        var e = new IncomingEvent("Ray", 67, ChatChannel.Say, true, false, "ich love you");

        // Gross- und Kleinschreibung darf keine zwei Staende ergeben.
        Assert.Equal(CounterScope.ForEvent(a, e), CounterScope.ForEvent(b, e));
    }

    [Fact]
    public void Chat_ohne_Triggerwoerter_bleibt_bei_der_Regel()
    {
        var r = new Rule { Channels = [ChatChannel.Say], TriggerTexts = [] };
        r.EnsureId();

        var e = new IncomingEvent("Ray", 67, ChatChannel.Say, true, false, "hallo");

        Assert.StartsWith(CounterScope.RulePrefix, CounterScope.ForEvent(r, e));
    }
}
