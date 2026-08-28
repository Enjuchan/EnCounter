using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace EnCounter;

/// <summary>
/// Weltnamen zu den gespeicherten Nummern.
///
/// Der Zähler unterscheidet gleichnamige Spieler über die Heimatwelt. In der Übersicht
/// nützt die Nummer niemandem - „Enju @ Shiva" schon.
/// </summary>
public static class WorldCatalog
{
    private static readonly Dictionary<uint, string> zwischenspeicher = [];

    /// <summary>Leer, wenn die Welt unbekannt ist - dann steht nur der Name da.</summary>
    public static string NameOf(IDataManager data, uint id)
    {
        if (id == 0)
            return string.Empty;

        if (zwischenspeicher.TryGetValue(id, out var bekannt))
            return bekannt;

        var welt = data.GetExcelSheet<World>()?.GetRowOrDefault(id);
        var name = welt?.Name.ExtractText() ?? string.Empty;

        return zwischenspeicher[id] = name;
    }
}
