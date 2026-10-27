using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EnCounter.Ui;

/// <summary>
/// Hintergrund-Struktur und Kopfband für Fenster der En-Plugins. Wird direkt nach ImGui.Begin gezeichnet,
/// damit alles Weitere darüber liegt; Karten mit eigenem Hintergrund decken es ab.
/// </summary>
public static class Decor
{
    private const float DotSpacing = 11f;
    private const float DotSize = 1.5f;
    private const float DotAlphaTop = 0.55f;
    private const float GlowAlphaTop = 0.11f;

    /// <summary>Anteil der Fensterhöhe, nach dem Schimmer und Punktraster ganz ausgeblendet sind.</summary>
    private const float FadeEnd = 0.7f;

    /// <summary>Rosa Schimmer und Punktraster, oben deutlich, nach unten hin ausgeblendet.</summary>
    public static void Background()
    {
        var draw = ImGui.GetWindowDrawList();
        var min = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var fadeBottom = min.Y + size.Y * FadeEnd;

        var glowTop = Theme.U32(Theme.Hex(0xE8679F, GlowAlphaTop));
        var glowBottom = Theme.U32(Theme.Hex(0xE8679F, 0f));
        draw.AddRectFilledMultiColor(min, new Vector2(min.X + size.X, fadeBottom), glowTop, glowTop, glowBottom, glowBottom);

        // Auf ganze Pixel gelegt: Bei Bruchteil-Positionen verteilt ImGui so kleine Punkte auf mehrere Pixel,
        // und sie verblassen bis zur Unsichtbarkeit.
        var originX = MathF.Floor(min.X);
        var originY = MathF.Floor(min.Y);

        for (var y = originY + DotSpacing; y < fadeBottom; y += DotSpacing)
        {
            var t = (y - min.Y) / (fadeBottom - min.Y);
            var alpha = DotAlphaTop * (1 - t) * (1 - t);
            var color = Theme.U32(Theme.Hex(0xF08CBC, alpha));

            for (var x = originX + DotSpacing; x < min.X + size.X; x += DotSpacing)
                draw.AddRectFilled(new Vector2(x, y), new Vector2(x + DotSize, y + DotSize), color);
        }
    }

    /// <summary>Hinterlegtes Band hinter der Kopfzeile, von Rand zu Rand, mit Trennlinie darunter.</summary>
    /// <param name="height">Höhe des Inhalts der Kopfzeile ab der aktuellen Cursor-Position.</param>
    public static void HeaderBand(float height)
    {
        var draw = ImGui.GetWindowDrawList();
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        var padding = ImGui.GetStyle().WindowPadding;
        var top = ImGui.GetCursorScreenPos().Y - padding.Y;
        var bottom = ImGui.GetCursorScreenPos().Y + height + padding.Y * 0.7f;

        // Halbtransparent, damit Schimmer und Punktraster auch hinter der Kopfzeile durchscheinen.
        draw.AddRectFilled(new Vector2(windowPos.X + 1, top), new Vector2(windowPos.X + windowSize.X - 1, bottom), Theme.U32(Theme.Hex(0x1A1319, 0.6f)));
        draw.AddLine(new Vector2(windowPos.X + 1, bottom), new Vector2(windowPos.X + windowSize.X - 1, bottom), Theme.U32(Theme.CardBorder), 1f);
    }
}
