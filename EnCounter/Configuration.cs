using Dalamud.Configuration;
using EnCounter.Core;

namespace EnCounter;

/// <summary>
/// Regeln und Zähler, gespeichert in den Plugin-Einstellungen.
///
/// Die Zähler werden als flache Liste abgelegt, weil sich CounterKey als
/// Wörterbuchschlüssel nicht sauber serialisieren lässt.
/// </summary>
[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    /// <summary>
    /// 1: Zähler hingen am Regelnamen, Platzhalter hieß `[Anzahl]`.
    /// 2: Zähler hängen an <see cref="Rule.Id"/>, Platzhalter heißt `[Count]`.
    /// 3: Zähler hängen am Ereignis (`e:146`, `t:&lt;3`), nicht mehr an der Regel.
    /// 4: Reste des PatMe-Imports entfernt, der noch unter Regel-Ids ablegte.
    /// </summary>
    public const int CurrentVersion = 4;

    public int Version { get; set; } = CurrentVersion;

    public List<Rule> Rules { get; set; } = [];

    /// <summary>
    /// Pause: Solange sie gesetzt ist, wird weder gezählt noch ein Titel gezeigt.
    /// Fehlt das Feld in einer älteren Datei, bleibt es aus - eine neue Fassung
    /// ist dafür nicht nötig.
    /// </summary>
    public bool Paused { get; set; }

    public List<CounterEntry> Counters { get; set; } = [];

    public sealed class CounterEntry
    {
        /// <summary>Der stabile Schlüssel. Leer bei Ständen aus Fassung 1.</summary>
        public string RuleId { get; set; } = string.Empty;

        /// <summary>Nur noch zum Wandern alter Stände da.</summary>
        public string RuleName { get; set; } = string.Empty;

        public string PlayerName { get; set; } = string.Empty;
        public uint WorldId { get; set; }
        public int Count { get; set; }
    }

    /// <summary>
    /// Bringt eine geladene Konfiguration auf den heutigen Stand. Gibt zurück, ob sich
    /// etwas geändert hat und gespeichert werden muss.
    ///
    /// Läuft **vor** <see cref="ToStore"/>: Danach sind alle Zähler unter Ids
    /// abgelegt, und nichts im laufenden Betrieb muss den alten Fall noch kennen.
    /// </summary>
    public bool Migrate()
    {
        var geaendert = false;

        foreach (var regel in Rules)
        {
            if (regel.EnsureId())
                geaendert = true;

            var persoenlich = TitleTemplate.Modernise(regel.PersonalTemplate);
            if (persoenlich != regel.PersonalTemplate)
            {
                regel.PersonalTemplate = persoenlich;
                geaendert = true;
            }

            var allgemein = TitleTemplate.Modernise(regel.GeneralTemplate);
            if (allgemein != regel.GeneralTemplate)
            {
                regel.GeneralTemplate = allgemein;
                geaendert = true;
            }
        }

        // Alte Stände hängen am Namen. Sie jetzt auf die Id umzuschreiben ist die
        // einzige Gelegenheit, bei der die Zuordnung noch eindeutig ist - sobald
        // jemand umbenennt, wäre sie verloren.
        foreach (var eintrag in Counters)
        {
            if (!string.IsNullOrEmpty(eintrag.RuleId))
                continue;

            var regel = Rules.FirstOrDefault(r => r.Name == eintrag.RuleName);
            if (regel is null)
                continue;

            eintrag.RuleId = regel.Id;
            geaendert = true;
        }

        if (Version < 3 && AufEreignisschluessel())
            geaendert = true;

        if (Version < 4 && EntferneImportReste())
            geaendert = true;

        // Aufgeräumt wird hier bewusst **nicht** mehr. Seit die Zähler am Ereignis
        // hängen, gehört ein Stand keiner Regel exklusiv: `e:146` überlebt das
        // Löschen einer Dote-Regel und ist wieder da, sobald jemand eine neue anlegt.
        // Das ist der Sinn der Sache - und ein Aufräumer hätte genau das zerstört.

        if (Version != CurrentVersion)
        {
            Version = CurrentVersion;
            geaendert = true;
        }

        return geaendert;
    }

    /// <summary>
    /// Entfernt Stände, deren Schlüssel keiner ist.
    ///
    /// In 0.3.0.0 legte der PatMe-Import weiterhin unter der Id einer Regel ab, obwohl
    /// die Schlüssel da schon am Ereignis hingen. Solche Einträge lassen sich keinem
    /// Ereignis mehr zuordnen und erscheinen in der Übersicht als roher Guid.
    ///
    /// Das ist die einzige Stelle, an der überhaupt noch Stände entfernt werden -
    /// hier geht es um nachweislich falsche Einträge, nicht um vermeintlich
    /// verwaiste.
    /// </summary>
    private bool EntferneImportReste()
    {
        var entfernt = Counters.RemoveAll(e =>
            !e.RuleId.StartsWith(CounterScope.EmotePrefix, StringComparison.Ordinal) &&
            !e.RuleId.StartsWith(CounterScope.TextPrefix, StringComparison.Ordinal) &&
            !e.RuleId.StartsWith(CounterScope.RulePrefix, StringComparison.Ordinal));

        return entfernt > 0;
    }

    /// <summary>
    /// Trägt Stände von den Regel-Ids auf die Ereignisschlüssel um.
    ///
    /// **Zwei Regeln können auf denselben Schlüssel treffen** - eine allgemeine
    /// „alle Dotes" und eine „nur Rayleigh". Beide haben dasselbe Ereignis gezählt,
    /// nur unterschiedlich lange. Übernommen wird deshalb der **höhere** Stand: Er
    /// ist die vollständigere Zählung, und die Summe wäre eine Verdopplung.
    /// </summary>
    private bool AufEreignisschluessel()
    {
        if (Counters.Count == 0)
            return false;

        var abbildung = new Dictionary<string, string>();

        foreach (var regel in Rules)
        {
            abbildung[regel.LegacyCounterId(false)] = Zielschluessel(regel, false);
            abbildung[regel.LegacyCounterId(true)] = Zielschluessel(regel, true);
        }

        var neu = new Dictionary<(string Schluessel, string Person, uint Welt), int>();

        foreach (var eintrag in Counters)
        {
            if (!abbildung.TryGetValue(eintrag.RuleId, out var ziel))
                continue;   // Rest einer gelöschten Regel - nicht mehr zuzuordnen

            var schluessel = (ziel, eintrag.PlayerName, eintrag.WorldId);
            neu[schluessel] = Math.Max(neu.GetValueOrDefault(schluessel), eintrag.Count);
        }

        Counters = [.. neu.Select(e => new CounterEntry
        {
            RuleId = e.Key.Schluessel,
            PlayerName = e.Key.Person,
            WorldId = e.Key.Welt,
            Count = e.Value,
        })];

        return true;
    }

    /// <summary>
    /// Wohin der alte Stand einer Regel wandert.
    ///
    /// Deckt die Regel mehrere Emotes ab, landet alles beim ersten - feiner war die
    /// alte Zählung nie aufgelöst. Deckt sie *alle* ab, gibt es keinen einzelnen
    /// Schlüssel; dann bleibt der Stand unter einem eigenen, der aber dasselbe
    /// Präfix trägt und damit weiterhin mitgezählt wird.
    /// </summary>
    private static string Zielschluessel(Rule regel, bool outgoing)
    {
        var auswahl = CounterScope.ForRule(regel, outgoing);

        if (auswahl.Prefix is not null)
            return auswahl.Prefix + "legacy:" + regel.Id + auswahl.Suffix;

        return auswahl.Keys.Count > 0
            ? auswahl.Keys[0]
            : CounterScope.RulePrefix + regel.Id + auswahl.Suffix;
    }

    /// <summary>
    /// Legt beim ersten Start ein paar Regeln an, damit sofort etwas passiert.
    ///
    /// Getrennt nach Emote statt einer Sammelregel „Emotes": Nur so kann jede ihren
    /// eigenen Text tragen - „hugged" statt „emoted" - und das ist der eigentliche
    /// Reiz an der Sache.
    /// </summary>
    public bool EnsureDefaults()
    {
        if (Rules.Count > 0)
            return false;

        // Nummern aus der Emote-Tabelle des Spiels, nicht geraten: Der Emote mit der
        // Nummer 105 heisst „Pet", sein Textbefehl ist /stroke. Umarmt wird auf zwei
        // Arten - 112 ist /hug, 113 ist /embrace; beide zaehlen als Umarmung.
        Rules.Add(EmoteRegel("Hug", [112, 113], "hugged"));
        Rules.Add(EmoteRegel("Dote", [146], "doted"));
        Rules.Add(EmoteRegel("Pet", [105], "petted"));

        Rules.Add(Neu(new Rule
        {
            Name = "Love",
            Enabled = true,

            // Ueberall, wo Menschen miteinander reden. Alliance und Novice Network
            // bleiben aussen vor - dort geht es um Inhalte, nicht um Zuneigung.
            Channels =
            [
                ChatChannel.Say, ChatChannel.Yell, ChatChannel.Shout,
                ChatChannel.TellIncoming, ChatChannel.Party, ChatChannel.CrossParty,
                ChatChannel.FreeCompany,
            ],
            TriggerTexts = ["love you", "<3"],

            // Sonst zaehlt jedes Gespraech in Hoerweite mit. Im Tell greift die
            // Bedingung ohnehin nicht - dort ist die Nachricht an einen gerichtet.
            RequiresOwnName = true,
            PersonalTemplate = "[Name] loves me [Count]x",
            GeneralTemplate = "got love [Count]x",
            DisplaySeconds = 10,
        }));

        return true;
    }

    private static Rule EmoteRegel(string name, uint[] emoteIds, string verb) =>
        Neu(new Rule
        {
            Name = name,
            Enabled = true,
            Channels = [ChatChannel.StandardEmote, ChatChannel.CustomEmote],
            EmoteIds = [.. emoteIds],
            TriggerTexts = [],
            RequiresOwnName = false,
            PersonalTemplate = $"[Name] {verb} me [Count]x",
            GeneralTemplate = $"{verb} [Count]x",

            // Fuer den Fall, dass jemand auf "beides" umstellt: Dann steht schon da,
            // was sonst verkehrt herum erschiene.
            GivenPersonalTemplate = $"{verb} [Name] [Count]x",
            GivenGeneralTemplate = $"{verb} others [Count]x",
            DisplaySeconds = 10,
        });

    private static Rule Neu(Rule regel)
    {
        regel.EnsureId();
        return regel;
    }

    public CounterStore ToStore()
    {
        var daten = new Dictionary<string, Dictionary<CounterKey, int>>();

        foreach (var e in Counters)
        {
            if (string.IsNullOrEmpty(e.RuleId))
                continue;

            if (!daten.TryGetValue(e.RuleId, out var proPerson))
                daten[e.RuleId] = proPerson = [];

            proPerson[new CounterKey(e.PlayerName, e.WorldId)] = e.Count;
        }

        var store = new CounterStore();
        store.Load(daten);
        return store;
    }

    /// <summary>
    /// `RuleId` trägt seit Fassung 3 den **Ereignisschlüssel** (`e:146`, `t:&lt;3`),
    /// nicht mehr die Id einer Regel. `RuleName` bleibt leer - der Stand gehört
    /// keiner Regel mehr, die man benennen könnte.
    /// </summary>
    public void FromStore(CounterStore store)
    {
        Counters = [.. store.Entries.SelectMany(raum =>
            raum.Value.Select(person => new CounterEntry
            {
                RuleId = raum.Key,
                PlayerName = person.Key.Name,
                WorldId = person.Key.WorldId,
                Count = person.Value,
            }))];
    }
}
