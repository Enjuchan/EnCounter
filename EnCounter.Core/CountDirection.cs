namespace EnCounter.Core;

/// <summary>
/// Welche Richtung eine Regel zählt.
///
/// Der Hook auf die Paketverarbeitung sieht beide Seiten: Emotes, die andere auf einen
/// richten, und die eigenen auf andere. Gezählt wird in beiden Fällen die **andere**
/// Person - bei <see cref="Given"/> also die, die man angeemotet hat.
///
/// Wirkt nur bei Emotes. Im Chat gibt es keinen verlässlichen Empfänger: Wer in Say
/// etwas sagt, sagt es an alle, und ein Zähler bräuchte eine Person.
/// </summary>
public enum CountDirection
{
    /// <summary>Was andere mit einem machen.</summary>
    Received = 0,

    /// <summary>Was man selbst mit anderen macht.</summary>
    Given = 1,

    /// <summary>Beides, in einem Zähler.</summary>
    Both = 2,
}
