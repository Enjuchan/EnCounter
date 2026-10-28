namespace EnCounter;

/// <summary>EnCounters Abhängigkeiten für den Dependencies-Tab.</summary>
public static class EnCounterDependencies
{
    public static readonly IReadOnlyList<DependencyGroup> Groups =
    [
        new("Required", DependencyKind.Required,
        [
            // Honorific liegt in Dalamuds Haupt-Repository (am 2026-10-28 in der Installation geprüft: InstalledFromUrl OFFICIAL).
            new("Honorific", "Honorific", "Shows the title above your name.", "https://github.com/Caraxi/Honorific",
                [Dependencies.MainRepository]),
        ]),
    ];
}
