namespace EnCounter;

/// <summary>
/// Zeigt eine Meldung als Titel und gibt ihn nach Ablauf wieder frei.
///
/// Neueste Meldung gewinnt: Kommt während einer laufenden Anzeige etwas Neues, ersetzt
/// es sie und die Dauer beginnt von vorn. Keine Warteschlange - gezählt wird ohnehin
/// unabhängig davon, was gerade zu sehen ist.
/// </summary>
public sealed class TitlePresenter(HonorificBridge honorific)
{
    private DateTime endsAt = DateTime.MinValue;

    public bool IsShowing => DateTime.UtcNow < endsAt;

    public void Show(string text, EnCounter.Core.Rule regel)
    {
        honorific.SetTitle(text, regel);
        endsAt = DateTime.UtcNow.AddSeconds(Math.Max(1, regel.DisplaySeconds));
    }

    /// <summary>
    /// Gehört in den Frame-Puls. Prüft nur, ob die Zeit abgelaufen ist - der Titel
    /// selbst muss nicht wiederholt gesetzt werden, weil Honorific IPC-Titel hält,
    /// bis sie gelöscht werden.
    /// </summary>
    public void Tick()
    {
        if (endsAt == DateTime.MinValue || IsShowing)
            return;

        honorific.ClearTitle();
        endsAt = DateTime.MinValue;
    }

    public void Clear()
    {
        if (endsAt == DateTime.MinValue)
            return;

        honorific.ClearTitle();
        endsAt = DateTime.MinValue;
    }
}
