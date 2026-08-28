namespace EnCounter.Core;

/// <summary>
/// Verhindert, dass ein Makro den Zähler hochtreibt.
///
/// Ohne Bremse zählt jedes einzelne Paket: Wer zehnmal hintereinander `/pet` auslöst,
/// erzeugt zehn Treffer, zehn Titelwechsel und zehn Schreibvorgänge auf die Platte.
///
/// Gesperrt wird je Regel **und** Person - dass A gerade gezählt hat, darf B nicht
/// blockieren. Der Zustand ist reine Laufzeit und wird nicht gespeichert; nach einem
/// Neustart darf jeder sofort wieder zählen.
/// </summary>
public sealed class CooldownGate
{
    private readonly Dictionary<(string RuleId, CounterKey Person), DateTime> letzte = [];

    /// <summary>
    /// Darf jetzt gezählt werden? Ein Ja merkt sich den Zeitpunkt sofort.
    ///
    /// <paramref name="seconds"/> 0 oder kleiner schaltet die Bremse ab.
    /// </summary>
    public bool Allow(string ruleId, CounterKey person, int seconds, DateTime now)
    {
        if (seconds <= 0)
            return true;

        var schluessel = (ruleId, person);

        if (letzte.TryGetValue(schluessel, out var zuletzt) &&
            now - zuletzt < TimeSpan.FromSeconds(seconds))
            return false;

        letzte[schluessel] = now;
        return true;
    }

    /// <summary>Alles vergessen - etwa wenn eine Regel gelöscht wurde.</summary>
    public void Clear() => letzte.Clear();
}
