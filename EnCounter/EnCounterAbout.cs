using Dalamud.Interface;
using EnCounter.Ui;

namespace EnCounter;

/// <summary>Angaben für den About-Tab; Wirkung der Befehle steht auch als Hilfetext im Befehl selbst.</summary>
public static class EnCounterAbout
{
    public static readonly LocText ToggleEffect = new("Opens or closes the EnCounter window.", "Öffnet oder schließt das EnCounter-Fenster.");
    public static readonly LocText DebugEffect = new("Turns diagnostics on or off: logs how emotes are processed and shows the draw time.",
        "Schaltet die Diagnose ein oder aus: protokolliert, wie Emotes verarbeitet werden, und zeigt die Zeichenzeit.");

    public static AboutInfo Create(string version) => new(
        "EnCounter",
        version,
        new("Counts emotes and chat messages directed at you and briefly displays the tally as your title.",
            "Zählt Emotes und Chat-Nachrichten, die an dich gerichtet sind, und zeigt den Stand kurz als deinen Titel an."),
        [new("/encounter", ToggleEffect), new("/encounter debug", DebugEffect)],
        "AGPL-3.0",
        "EnCounter - Copyright (c) 2026 Enjuchan",
        [new(new("Open GitHub", "GitHub öffnen"), FontAwesomeIcon.ExternalLinkAlt, "https://github.com/Enjuchan/EnCounter")]);
}
