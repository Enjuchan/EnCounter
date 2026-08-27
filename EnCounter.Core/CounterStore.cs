namespace EnCounter.Core;

/// <summary>
/// Hält die Zähler, je Regel eine Tabelle Person zu Anzahl.
///
/// Gezählt wird pro Person, auch wenn nur die Summe angezeigt wird: Aus
/// Einzelzählern lässt sich summieren, umgekehrt nicht.
/// </summary>
public sealed class CounterStore
{
    private readonly Dictionary<string, Dictionary<CounterKey, int>> counters = [];

    /// <summary>Für die Speicherung: alles, was gezählt wurde.</summary>
    public IReadOnlyDictionary<string, Dictionary<CounterKey, int>> Entries => counters;

    /// <summary>Erhöht den Zähler und gibt den neuen Stand dieser Person zurück.</summary>
    public int Increment(string ruleName, CounterKey key)
    {
        if (!counters.TryGetValue(ruleName, out var perPerson))
            counters[ruleName] = perPerson = [];

        perPerson.TryGetValue(key, out var count);
        return perPerson[key] = count + 1;
    }

    public int GetPersonal(string ruleName, CounterKey key) =>
        counters.TryGetValue(ruleName, out var p) && p.TryGetValue(key, out var c) ? c : 0;

    public int GetTotal(string ruleName) =>
        counters.TryGetValue(ruleName, out var p) ? p.Values.Sum() : 0;

    /// <summary>Beim Laden aus der Konfiguration.</summary>
    public void Load(Dictionary<string, Dictionary<CounterKey, int>> data)
    {
        counters.Clear();
        foreach (var (rule, perPerson) in data)
            counters[rule] = new Dictionary<CounterKey, int>(perPerson);
    }
}
