namespace EnCounter.Core;

/// <summary>
/// Ein empfangenes Ereignis, bereits von allem Spielspezifischen befreit.
///
/// <paramref name="TargetedAtMe"/> stammt aus Dalamuds TargetKind und ist damit
/// unabhängig vom Meldungstext - der Client kann auf jeder Sprache laufen.
/// </summary>
public sealed record IncomingEvent(
    string SenderName,
    uint SenderWorldId,
    ChatChannel Channel,
    bool TargetedAtMe,
    bool FromSelf,
    string Text,
    uint EmoteId = 0);
