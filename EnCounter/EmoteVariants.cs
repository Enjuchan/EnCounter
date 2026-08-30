using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace EnCounter;

/// <summary>
/// Führt Emote-Varianten auf ihre Hauptnummer zurück.
///
/// **Ein Emote kann je nach Ziel unter verschiedenen Nummern ankommen.** Fotografieren
/// ist 288, bei einem hochgewachsenen Gegenüber aber 289 und bei einem kleinen 290 —
/// das Spiel wählt die Animation passend zur Größe. Wer nur 288 in seiner Regel stehen
/// hat, zählt bei manchen Personen und bei anderen nicht, ohne dass ein Unterschied zu
/// sehen wäre.
///
/// Am 30.08.2026 genau so gemeldet: Zwei Spielerinnen wurden nie gezählt, alle anderen
/// schon. Die Diagnose zeigte 289 statt 288.
///
/// **Erkannt werden sie am Animationspfad.** Die Varianten tragen den Pfad der
/// Hauptfassung als Präfix: `sp69` → `sp69_high`, `sp69_low`. Dasselbe Muster bei
/// Kussregen (`sp04` → `sp04_no_target`), Wasserschlacht und Verwandlungsmagie.
///
/// Über Namen oder Symbole ginge es nicht: Die Varianten haben beides nicht — 289 und
/// 290 sind namenlose Zeilen. Posen und Ornamente tragen zwar auch keinen Namen, teilen
/// aber keinen Pfad mit einem Emote und bleiben deshalb unberührt.
/// </summary>
public static class EmoteVariants
{
    private static Dictionary<uint, uint>? abbildung;

    /// <summary>
    /// Die Hauptnummer zu einer Emote-Nummer. Ist sie selbst schon die Hauptfassung
    /// oder unbekannt, kommt sie unverändert zurück.
    /// </summary>
    public static uint Normalise(IDataManager data, uint emoteId)
    {
        abbildung ??= Baue(data);

        return abbildung.GetValueOrDefault(emoteId, emoteId);
    }

    /// <summary>Wie viele Varianten gefunden wurden - für die Diagnose.</summary>
    public static int Count(IDataManager data)
    {
        abbildung ??= Baue(data);

        return abbildung.Count;
    }

    private static Dictionary<uint, uint> Baue(IDataManager data)
    {
        var karte = new Dictionary<uint, uint>();
        var blatt = data.GetExcelSheet<Emote>();

        if (blatt is null)
            return karte;

        // Die Hauptfassungen: alles mit Textbefehl und Animation.
        var haupt = new List<(uint Id, string Key)>();

        foreach (var e in blatt)
        {
            var befehl = e.TextCommand.ValueNullable?.Command.ExtractText() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(befehl))
                continue;

            var key = ErsterKey(e);
            if (key.Length > 0)
                haupt.Add((e.RowId, key));
        }

        foreach (var e in blatt)
        {
            var befehl = e.TextCommand.ValueNullable?.Command.ExtractText() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(befehl))
                continue;

            var key = ErsterKey(e);
            if (key.Length == 0)
                continue;

            // Der laengste passende Stamm gewinnt - sonst schluckte ein kurzer Pfad
            // die Varianten eines laengeren.
            uint besteId = 0;
            var besteLaenge = 0;

            foreach (var (id, hauptKey) in haupt)
            {
                if (key.Length <= hauptKey.Length)
                    continue;

                if (!key.StartsWith(hauptKey, StringComparison.Ordinal))
                    continue;

                if (hauptKey.Length <= besteLaenge)
                    continue;

                besteId = id;
                besteLaenge = hauptKey.Length;
            }

            if (besteId != 0)
                karte[e.RowId] = besteId;
        }

        return karte;
    }

    private static string ErsterKey(Emote e)
    {
        foreach (var timeline in e.ActionTimeline)
        {
            var key = timeline.ValueNullable?.Key.ExtractText();

            if (!string.IsNullOrWhiteSpace(key))
                return key;
        }

        return string.Empty;
    }
}
