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
    public int Version { get; set; } = 1;

    public List<Rule> Rules { get; set; } = [];

    public List<CounterEntry> Counters { get; set; } = [];

    public sealed class CounterEntry
    {
        public string RuleName { get; set; } = string.Empty;
        public string PlayerName { get; set; } = string.Empty;
        public uint WorldId { get; set; }
        public int Count { get; set; }
    }

    /// <summary>
    /// Legt beim ersten Start zwei Regeln an, damit sofort etwas passiert.
    ///
    /// Solange es keine Oberfläche gibt, wäre das Plugin sonst stumm - es gäbe nichts,
    /// worauf es hören könnte.
    /// </summary>
    public bool EnsureDefaults()
    {
        if (Rules.Count > 0)
            return false;

        Rules.Add(new Rule
        {
            Name = "Emotes",
            Enabled = true,
            Channels = [ChatChannel.StandardEmote, ChatChannel.CustomEmote],
            TriggerTexts = [],
            RequiresOwnName = false,
            PersonalTemplate = "[Name] emoted [Anzahl]x",
            GeneralTemplate = "emoted at [Anzahl]x",
            DisplaySeconds = 10,
        });

        Rules.Add(new Rule
        {
            Name = "Liebe",
            Enabled = true,
            Channels = [ChatChannel.Say, ChatChannel.TellIncoming, ChatChannel.Party],
            TriggerTexts = ["liebe dich", "love you", "<3"],
            RequiresOwnName = false,
            PersonalTemplate = "[Name] loves you [Anzahl]x",
            GeneralTemplate = "got love [Anzahl]x",
            DisplaySeconds = 10,
        });

        return true;
    }

    public CounterStore ToStore()
    {
        var daten = new Dictionary<string, Dictionary<CounterKey, int>>();

        foreach (var e in Counters)
        {
            if (!daten.TryGetValue(e.RuleName, out var proPerson))
                daten[e.RuleName] = proPerson = [];

            proPerson[new CounterKey(e.PlayerName, e.WorldId)] = e.Count;
        }

        var store = new CounterStore();
        store.Load(daten);
        return store;
    }

    public void FromStore(CounterStore store)
    {
        Counters = [.. store.Entries.SelectMany(regel =>
            regel.Value.Select(person => new CounterEntry
            {
                RuleName = regel.Key,
                PlayerName = person.Key.Name,
                WorldId = person.Key.WorldId,
                Count = person.Value,
            }))];
    }
}
