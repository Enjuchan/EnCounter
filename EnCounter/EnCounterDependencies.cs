namespace EnCounter;

/// <summary>EnCounters Abhängigkeiten für den Dependencies-Tab.</summary>
public static class EnCounterDependencies
{
    public static readonly IReadOnlyList<DependencyGroup> Groups =
    [
        new(new("Required", "Erforderlich"), DependencyKind.Required,
        [
            // Honorific liegt in Dalamuds Haupt-Repository (am 2026-10-28 in der Installation geprüft: InstalledFromUrl OFFICIAL).
            new("Honorific", "Honorific", new("Shows the title above your name.", "Zeigt den Titel über deinem Namen an."), "https://github.com/Caraxi/Honorific",
                [Dependencies.MainRepository]),
        ]),
    ];
}
