namespace EnCounter.Core;

/// <summary>
/// Hält die Zähler, je Regel eine Tabelle Person zu Anzahl.
///
/// Gezählt wird pro Person, auch wenn nur die Summe angezeigt wird: Aus
/// Einzelzählern lässt sich summieren, umgekehrt nicht.
///
/// Der Schlüssel ist die **Id** der Regel, nicht ihr Name. Am Namen hing der Stand
/// früher an einer Zeichenkette, die sich jederzeit ändern kann - eine Umbenennung
/// hat den Zähler gelöscht.
/// </summary>
public sealed class CounterStore
{
    private readonly Dictionary<string, Dictionary<CounterKey, int>> counters = [];

    /// <summary>Für die Speicherung: alles, was gezählt wurde.</summary>
    public IReadOnlyDictionary<string, Dictionary<CounterKey, int>> Entries => counters;

    /// <summary>Erhöht den Zähler und gibt den neuen Stand dieser Person zurück.</summary>
    public int Increment(string ruleId, CounterKey key)
    {
        if (!counters.TryGetValue(ruleId, out var perPerson))
            counters[ruleId] = perPerson = [];

        perPerson.TryGetValue(key, out var count);
        return perPerson[key] = count + 1;
    }

    public int GetPersonal(string ruleId, CounterKey key) =>
        counters.TryGetValue(ruleId, out var p) && p.TryGetValue(key, out var c) ? c : 0;

    public int GetTotal(string ruleId) =>
        counters.TryGetValue(ruleId, out var p) ? p.Values.Sum() : 0;

    /// <summary>
    /// Alle Personen einer Regel, die höchsten Stände zuerst. Für die Übersicht im
    /// Fenster - bisher standen diese Zahlen nur in der gespeicherten Datei.
    /// </summary>
    public IReadOnlyList<(CounterKey Person, int Count)> GetAll(string ruleId)
    {
        if (!counters.TryGetValue(ruleId, out var perPerson))
            return [];

        return [.. perPerson
            .Select(e => (Person: e.Key, Count: e.Value))
            .OrderByDescending(e => e.Count)
            .ThenBy(e => e.Person.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>Wie viele verschiedene Personen bei dieser Regel gezählt wurden.</summary>
    public int PersonCount(string ruleId) =>
        counters.TryGetValue(ruleId, out var p) ? p.Count : 0;

    /// <summary>
    /// Setzt einen Stand von Hand. 0 oder weniger entfernt die Person ganz - ein
    /// Eintrag mit Null wäre in der Übersicht nur Rauschen.
    /// </summary>
    public void Set(string ruleId, CounterKey key, int value)
    {
        if (value <= 0)
        {
            Remove(ruleId, key);
            return;
        }

        if (!counters.TryGetValue(ruleId, out var perPerson))
            counters[ruleId] = perPerson = [];

        perPerson[key] = value;
    }

    /// <summary>Entfernt eine Person aus einer Regel.</summary>
    public void Remove(string ruleId, CounterKey key)
    {
        if (counters.TryGetValue(ruleId, out var perPerson))
            perPerson.Remove(key);
    }

    /// <summary>Setzt eine ganze Regel zurück.</summary>
    public void Reset(string ruleId) => counters.Remove(ruleId);

    /// <summary>
    /// Schreibt die Stände einer Regel unter eine neue Id um.
    ///
    /// Gebraucht beim Wandern älterer Konfigurationen: Dort lagen die Zähler unter dem
    /// Regelnamen, jetzt unter der Id. Eine vorhandene Zieltabelle bleibt unangetastet.
    /// </summary>
    public void Rekey(string fromRuleId, string toRuleId)
    {
        if (fromRuleId == toRuleId)
            return;

        if (!counters.Remove(fromRuleId, out var quelle))
            return;

        if (!counters.ContainsKey(toRuleId))
            counters[toRuleId] = quelle;
    }

    /// <summary>Beim Laden aus der Konfiguration.</summary>
    public void Load(Dictionary<string, Dictionary<CounterKey, int>> data)
    {
        counters.Clear();
        foreach (var (rule, perPerson) in data)
            counters[rule] = new Dictionary<CounterKey, int>(perPerson);
    }
}
