namespace EnCounter.Core;

/// <summary>
/// Die Chatkanäle, auf die eine Regel hören kann.
///
/// Die Zahlenwerte entsprechen Dalamuds XivChatType. Ein eigener Typ ist trotzdem
/// nötig, weil Core keine Dalamud-Referenz haben darf - sonst wäre die Logik nicht
/// ohne laufendes Spiel testbar.
/// </summary>
public enum ChatChannel
{
    Say = 10,
    Shout = 11,
    TellIncoming = 13,
    Party = 14,
    Ls1 = 16, Ls2 = 17, Ls3 = 18, Ls4 = 19,
    Ls5 = 20, Ls6 = 21, Ls7 = 22, Ls8 = 23,
    FreeCompany = 24,
    CustomEmote = 28,
    StandardEmote = 29,
    Yell = 30,
    CrossParty = 32,
}
