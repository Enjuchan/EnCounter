using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace EnCounter;

/// <summary>Ein Emote, so weit es für die Auswahlliste gebraucht wird.</summary>
public sealed record EmoteEntry(uint Id, string Name, string Command, uint Icon);

/// <summary>
/// Die Emotes aus den Spieldaten, damit sich in der Oberfläche Namen auswählen lassen
/// statt Nummern einzutippen.
///
/// Die Namen kommen in der Client-Sprache - bei deutschem Client also „Umarmen" statt
/// „Hug". Gespeichert wird trotzdem die Nummer, die ist sprachunabhängig.
/// </summary>
public static class EmoteCatalog
{
    private static IReadOnlyList<EmoteEntry>? zwischenspeicher;

    public static IReadOnlyList<EmoteEntry> All(IDataManager data)
    {
        if (zwischenspeicher is not null)
            return zwischenspeicher;

        var blatt = data.GetExcelSheet<Emote>();
        if (blatt is null)
            return zwischenspeicher = [];

        var liste = new List<EmoteEntry>();

        foreach (var emote in blatt)
        {
            var name = emote.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            // Ohne Textbefehl handelt es sich um interne Eintraege, die niemand
            // ausloesen kann - die gehoeren nicht in die Auswahl.
            var befehl = emote.TextCommand.ValueNullable?.Command.ExtractText() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(befehl))
                continue;

            liste.Add(new EmoteEntry(emote.RowId, name, befehl, emote.Icon));
        }

        liste.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCulture));
        return zwischenspeicher = liste;
    }

    /// <summary>Anzeigename zu einer Nummer, für die Liste der gewählten Emotes.</summary>
    public static string NameOf(IDataManager data, uint id)
    {
        foreach (var e in All(data))
            if (e.Id == id)
                return e.Name;

        return $"#{id}";
    }
}
