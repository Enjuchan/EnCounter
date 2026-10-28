using Dalamud.Interface;
using EnCounter.Ui;

namespace EnCounter;

/// <summary>Angaben für den About-Tab; Wirkung der Befehle steht auch als Hilfetext im Befehl selbst.</summary>
public static class EnCounterAbout
{
    public const string ToggleEffect = "Opens or closes the EnCounter window.";
    public const string DebugEffect = "Turns diagnostics on or off: logs how emotes are processed and shows the draw time.";

    public static AboutInfo Create(string version) => new(
        "EnCounter",
        version,
        "Counts emotes and chat messages directed at you and briefly displays the tally as your title.",
        [new("/encounter", ToggleEffect), new("/encounter debug", DebugEffect)],
        "AGPL-3.0",
        "EnCounter - Copyright (c) 2026 Enjuchan",
        [new("Open GitHub", FontAwesomeIcon.ExternalLinkAlt, "https://github.com/Enjuchan/EnCounter")]);
}
