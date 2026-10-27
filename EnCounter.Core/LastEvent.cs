namespace EnCounter.Core;

/// <summary>
/// Das zuletzt gezählte Ereignis, für die Statuszeile der Oberfläche.
///
/// <paramref name="Person"/> ist schon zur Anzeige aufbereitet (`Name@Welt`),
/// <paramref name="Count"/> der Stand dieser Person nach dem Zählen unter dem Zähler
/// der Regel, die den Titel stellt. <paramref name="Time"/> ist UTC.
/// </summary>
public sealed record LastEvent(
    string Person,
    string EventName,
    int Count,
    string RuleName,
    DateTime Time);
