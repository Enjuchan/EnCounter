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

    /// <summary>
    /// Führt einen Stand, der noch unter dem bloßen Vornamen liegt, mit dem vollen
    /// Namen zusammen.
    ///
    /// Bis 0.3.1.0 wurde nur der Vorname gespeichert. Ohne diese Zusammenführung
    /// stünde dieselbe Person ab jetzt zweimal in der Liste - einmal mit altem Stand,
    /// einmal von vorn.
    ///
    /// Dass dabei zwei echte Namensvettern verschmelzen könnten, ist keine
    /// Verschlechterung: Unter dem alten Vornamen lagen sie ohnehin schon gemeinsam.
    /// </summary>
    public void MergeShortName(string ruleId, CounterKey person)
    {
        var leer = person.Name.IndexOf(' ');
        if (leer <= 0)
            return;

        if (!counters.TryGetValue(ruleId, out var perPerson))
            return;

        var kurz = new CounterKey(person.Name[..leer], person.WorldId);

        if (!perPerson.Remove(kurz, out var alt))
            return;

        perPerson[person] = perPerson.GetValueOrDefault(person) + alt;
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

    // --- Sichten über mehrere Schlüssel ----------------------------------------
    //
    // Eine Regel steht selten für genau einen Schlüssel: „Hug" umfasst zwei Emotes,
    // eine Regel ohne Emote-Auswahl gleich alle. Deshalb rechnen die folgenden
    // Methoden über eine ganze Auswahl.

    /// <summary>Welche gespeicherten Schlüssel zu dieser Auswahl gehören.</summary>
    public IEnumerable<string> Resolve(CounterScope.Selection auswahl)
    {
        if (auswahl.Prefix is null)
            return auswahl.Keys.Where(counters.ContainsKey);

        return counters.Keys.Where(k => PasstZumPraefix(k, auswahl));
    }

    /// <summary>
    /// Beim Präfix muss die Richtung mitgeprüft werden: `e:` fängt sonst auch alles
    /// ein, was unter `e:146:g` liegt, und Gegebenes zählte als Empfangenes.
    /// </summary>
    private static bool PasstZumPraefix(string schluessel, CounterScope.Selection auswahl)
    {
        if (!schluessel.StartsWith(auswahl.Prefix!, StringComparison.Ordinal))
            return false;

        var istGegeben = schluessel.EndsWith(CounterScope.GivenSuffix, StringComparison.Ordinal);
        var willGegeben = auswahl.Suffix == CounterScope.GivenSuffix;

        return istGegeben == willGegeben;
    }

    public int GetTotal(CounterScope.Selection auswahl) =>
        Resolve(auswahl).Sum(GetTotal);

    public int GetPersonal(CounterScope.Selection auswahl, CounterKey key) =>
        Resolve(auswahl).Sum(k => GetPersonal(k, key));

    /// <summary>Alle Personen einer Auswahl, zusammengefasst, die höchsten zuerst.</summary>
    public IReadOnlyList<(CounterKey Person, int Count)> GetAll(CounterScope.Selection auswahl)
    {
        var zusammen = new Dictionary<CounterKey, int>();

        foreach (var schluessel in Resolve(auswahl))
            foreach (var (person, anzahl) in counters[schluessel])
                zusammen[person] = zusammen.GetValueOrDefault(person) + anzahl;

        return [.. zusammen
            .Select(e => (Person: e.Key, Count: e.Value))
            .OrderByDescending(e => e.Count)
            .ThenBy(e => e.Person.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    public void Reset(CounterScope.Selection auswahl)
    {
        foreach (var schluessel in Resolve(auswahl).ToList())
            counters.Remove(schluessel);
    }

    public void Remove(CounterScope.Selection auswahl, CounterKey key)
    {
        foreach (var schluessel in Resolve(auswahl).ToList())
            counters[schluessel].Remove(key);
    }

    /// <summary>Beim Laden aus der Konfiguration.</summary>
    public void Load(Dictionary<string, Dictionary<CounterKey, int>> data)
    {
        counters.Clear();
        foreach (var (rule, perPerson) in data)
            counters[rule] = new Dictionary<CounterKey, int>(perPerson);
    }
}
