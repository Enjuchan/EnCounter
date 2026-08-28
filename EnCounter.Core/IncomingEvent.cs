namespace EnCounter.Core;

/// <summary>
/// Ein Ereignis, bereits von allem Spielspezifischen befreit.
///
/// <paramref name="TargetedAtMe"/> stammt aus Dalamuds TargetKind und ist damit
/// unabhängig vom Meldungstext - der Client kann auf jeder Sprache laufen.
///
/// <paramref name="SenderName"/> ist immer die **andere** Person: bei empfangenen
/// Ereignissen der Urheber, bei ausgehenden das Ziel. Gezählt wird die andere Seite,
/// nie man selbst - ein Zähler auf den eigenen Namen wäre sinnlos.
/// </summary>
public sealed record IncomingEvent(
    string SenderName,
    uint SenderWorldId,
    ChatChannel Channel,
    bool TargetedAtMe,
    bool FromSelf,
    string Text,
    uint EmoteId = 0,

    /// <summary>Ob man es selbst getan hat, statt es zu empfangen. Nur bei Emotes.</summary>
    bool Outgoing = false);
