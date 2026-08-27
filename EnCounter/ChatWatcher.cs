using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using EnCounter.Core;

namespace EnCounter;

/// <summary>
/// Hört den Chat ab und übersetzt Nachrichten in spielunabhängige Ereignisse.
///
/// Emotes kommen über denselben Kanal wie Text, nur mit eigenem Typ - es braucht also
/// keine zweite Erkennung.
///
/// Entscheidend: Ob eine Nachricht an einen gerichtet war, steht in TargetKind und
/// **nicht** im Meldungstext. Damit funktioniert die Erkennung auf jeder Client-Sprache.
/// </summary>
public sealed class ChatWatcher : IDisposable
{
    private readonly IChatGui chat;

    public event Action<IncomingEvent>? OnEvent;

    public ChatWatcher(IChatGui chat)
    {
        this.chat = chat;
        chat.ChatMessage += Handle;
    }

    /// <summary>Schreibt jede erkannte Nachricht ins Log. Nur zur Fehlersuche.</summary>
    public bool Diagnostics { get; set; }

    /// <summary>
    /// Die unteren sieben Bit tragen den Kanal, darüber stehen Flags des Spiels.
    /// Ohne Maskierung schlägt jeder Vergleich mit XivChatType fehl - Emotes kamen
    /// deshalb nie an.
    /// </summary>
    private const int ChannelMask = 0x7F;

    private void Handle(IHandleableChatMessage message)
    {
        var roh = (int)message.LogKind;
        var kanal = roh & ChannelMask;
        var (name, world) = Absender(message);

        if (Diagnostics)
            Plugin.Log.Information(
                $"[EnCounter] roh={roh} kanal={kanal} ({message.LogKind}) " +
                $"src={message.SourceKind} tgt={message.TargetKind} " +
                $"sender='{message.Sender.TextValue}' name='{name}' world={world} " +
                $"text='{message.Message.TextValue}'");

        if (!Enum.IsDefined(typeof(ChatChannel), kanal))
            return;

        if (string.IsNullOrEmpty(name))
            return;

        OnEvent?.Invoke(new IncomingEvent(
            SenderName: name,
            SenderWorldId: world,
            Channel: (ChatChannel)kanal,
            TargetedAtMe: message.TargetKind == XivChatRelationKind.LocalPlayer,
            FromSelf: message.SourceKind == XivChatRelationKind.LocalPlayer,
            Text: message.Message.TextValue));
    }

    /// <summary>
    /// Name und Heimatwelt des Absenders.
    ///
    /// Bei normalem Chat steht der Spieler im Sender-Feld. **Bei Emotes nicht:** Dort
    /// ist das Feld oft leer, und der Urheber steckt als Payload im Nachrichtentext
    /// ("Enju umarmt Stella"). Deshalb werden beide Felder durchsucht.
    /// </summary>
    private static (string Name, uint World) Absender(IChatMessage message)
    {
        if (AusPayloads(message.Sender) is { } ausSender)
            return ausSender;

        if (AusPayloads(message.Message) is { } ausNachricht)
            return ausNachricht;

        // Kein Payload gefunden - bleibt der reine Text des Absenderfelds.
        var text = message.Sender.TextValue;
        return string.IsNullOrWhiteSpace(text) ? (string.Empty, 0u) : (Vorname(text), 0u);
    }

    private static (string Name, uint World)? AusPayloads(Dalamud.Game.Text.SeStringHandling.SeString se)
    {
        foreach (var payload in se.Payloads)
            if (payload is PlayerPayload spieler)
                return (Vorname(spieler.PlayerName), spieler.World.RowId);

        return null;
    }

    private static string Vorname(string voll)
    {
        var leer = voll.IndexOf(' ');
        return leer > 0 ? voll[..leer] : voll;
    }

    public void Dispose() => chat.ChatMessage -= Handle;
}
