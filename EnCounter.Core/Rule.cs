namespace EnCounter.Core;

/// <summary>
/// Eine Regel: worauf gehört wird und wie das Ergebnis angezeigt wird.
///
/// Bewusst eine Klasse und kein Record - die Oberfläche bearbeitet Regeln direkt.
/// </summary>
public sealed class Rule
{
    public string Name { get; set; } = "Neue Regel";
    public bool Enabled { get; set; } = true;

    /// <summary>Kanäle, auf die gehört wird. Leer heißt: keiner.</summary>
    public List<ChatChannel> Channels { get; set; } = [];

    /// <summary>
    /// Wörter oder Sätze, von denen eines vorkommen muss. Leer heißt: der Text ist
    /// egal - sinnvoll bei Emotes, wo schon das Ziel die Zuordnung liefert.
    /// </summary>
    public List<string> TriggerTexts { get; set; } = [];

    /// <summary>
    /// Ob zusätzlich der eigene Vorname im Text stehen muss. Im Tell unnötig, im Say
    /// dagegen sinnvoll - sonst zählt jedes Gespräch in der Nähe mit.
    /// </summary>
    public bool RequiresOwnName { get; set; }

    /// <summary>
    /// Auf welche Emotes gehört wird, als Emote-Nummer aus den Spieldaten. Leer heißt:
    /// alle. Wird nur bei Emote-Kanälen ausgewertet.
    /// </summary>
    public List<uint> EmoteIds { get; set; } = [];

    /// <summary>
    /// Ob die Meldung den Namen des Ausloesers nennt.
    ///
    /// Mit Namen zeigt sie den Zaehler **dieser Person**, ohne Namen die **Summe ueber
    /// alle**. Das ist der eigentliche Unterschied - nicht nur, ob ein Name dasteht.
    /// </summary>
    public bool ShowName { get; set; } = true;

    /// <summary>Mit Namen. Zeigt den Zaehler der ausloesenden Person.</summary>
    public string PersonalTemplate { get; set; } = "[Name] petted you [Anzahl]x";

    /// <summary>Ohne Namen. Zeigt die Summe ueber alle Personen.</summary>
    public string GeneralTemplate { get; set; } = "petted [Anzahl]x";

    public int DisplaySeconds { get; set; } = 10;

    // --- Farben, wie Honorific sie kennt ---------------------------------------
    // Als einzelne Fliesskommawerte statt Vector3, damit Core ohne System.Numerics
    // auskommt und die Speicherung schlicht bleibt.

    /// <summary>Ob eigene Farben gesetzt werden. Sonst bleibt es bei Honorifics Vorgabe.</summary>
    public bool UseColours { get; set; }

    /// <summary>Textfarbe, 0 bis 1 je Kanal.</summary>
    public float ColourR { get; set; } = 1f;
    public float ColourG { get; set; } = 1f;
    public float ColourB { get; set; } = 1f;

    /// <summary>Leuchtrand.</summary>
    public float GlowR { get; set; } = 0.98f;
    public float GlowG { get; set; }
    public float GlowB { get; set; } = 1f;

    /// <summary>Zweitfarbe für den Verlauf. Nur wirksam, wenn <see cref="UseGradient"/> gesetzt ist.</summary>
    public bool UseGradient { get; set; }
    public float Colour3R { get; set; }
    public float Colour3G { get; set; } = 1f;
    public float Colour3B { get; set; } = 0.93f;

    /// <summary>0 Pulse, 1 Wave, 2 Static - wie in Honorific.</summary>
    public int GradientStyle { get; set; } = 1;
}
