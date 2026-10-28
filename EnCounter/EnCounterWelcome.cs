using EnCounter.Ui;

namespace EnCounter;

/// <summary>Inhalte des Willkommensfensters: Zweck und erste Schritte. Statt Schaltern stehen unten der Import und der Weg zu den Regeln.</summary>
public static class EnCounterWelcome
{
    /// <param name="drawSettings">Zeichnet den Import aus PatMe und den Knopf zu den Regeln.</param>
    public static WelcomeContent Create(string version, Action<float> drawSettings) => new(
        "EnCounter",
        version,
        new("EnCounter counts emotes and chat messages directed at you and briefly shows the tally as your title. What is counted is up to your rules: each rule listens for emotes or for words in chosen chat channels.",
            "EnCounter zählt Emotes und Chat-Nachrichten, die an dich gerichtet sind, und zeigt den Stand kurz als deinen Titel an. Was gezählt wird, bestimmen deine Regeln: Jede Regel hört auf Emotes oder auf Wörter in gewählten Chat-Kanälen."),
        [
            new("Make sure Honorific is installed and loaded. The list below shows its status and installs it for you.",
                "Stell sicher, dass Honorific installiert und geladen ist. Die Liste unten zeigt den Status und installiert es für dich."),
            new("If you counted with PatMe before, read its counts below and import them. No rules are created by that.",
                "Wenn du bisher mit PatMe gezählt hast, lies unten dessen Zähler ein und importiere sie. Dabei werden keine Regeln angelegt."),
            new("Open the rules. A few starter rules for hugs, dotes, pets and love are already there; change them or add your own, then let someone emote you.",
                "Öffne die Regeln. Einige Start-Regeln für Umarmen, Verehren, Streicheln und Liebe sind schon da; ändere sie oder lege eigene an und lass dich dann von jemandem emoten."),
        ],
        drawSettings);
}
