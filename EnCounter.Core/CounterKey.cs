namespace EnCounter.Core;

/// <summary>
/// Wer gezählt wird. Die Welt gehört dazu, sonst liefen gleichnamige Spieler
/// verschiedener Server in denselben Topf.
/// </summary>
public readonly record struct CounterKey(string Name, uint WorldId);
