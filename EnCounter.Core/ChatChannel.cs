namespace EnCounter.Core;

/// <summary>
/// Die Chatkanäle, auf die eine Regel hören kann.
///
/// Die Zahlenwerte entsprechen Dalamuds XivChatType. Ein eigener Typ ist trotzdem
/// nötig, weil Core keine Dalamud-Referenz haben darf - sonst wäre die Logik nicht
/// ohne laufendes Spiel testbar.
///
/// Alle Werte liegen unter 128 und überstehen damit die Maskierung mit 0x7F, die der
/// ChatWatcher vornimmt.
/// </summary>
public enum ChatChannel
{
    Say = 10,
    Shout = 11,
    TellIncoming = 13,
    Party = 14,
    Alliance = 15,

    Ls1 = 16, Ls2 = 17, Ls3 = 18, Ls4 = 19,
    Ls5 = 20, Ls6 = 21, Ls7 = 22, Ls8 = 23,

    FreeCompany = 24,
    NoviceNetwork = 27,
    CustomEmote = 28,
    StandardEmote = 29,
    Yell = 30,
    CrossParty = 32,
    PvpTeam = 36,

    /// <summary>
    /// Achtung: Die Nummern der weltübergreifenden Linkshells laufen **nicht**
    /// fortlaufend. Die erste steht bei 37, die übrigen sieben erst bei 101 bis 107.
    /// Aus Dalamuds XivChatType ausgelesen, nicht fortgeschrieben.
    /// </summary>
    Cwls1 = 37,
    Cwls2 = 101, Cwls3 = 102, Cwls4 = 103, Cwls5 = 104,
    Cwls6 = 105, Cwls7 = 106, Cwls8 = 107,
}
