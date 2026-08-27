using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Network;

namespace EnCounter;

/// <summary>
/// Fängt Emotes direkt an der Paketverarbeitung ab.
///
/// **Warum nicht über den Chat:** Ob ein Emote eine Chatmeldung erzeugt, entscheidet
/// der *Absender* in seinen eigenen Einstellungen. Wer sie abgeschaltet hat, wäre für
/// uns unsichtbar. Das Paket dagegen kommt immer - es ist die Grundlage dafür, dass die
/// Animation überhaupt abgespielt wird.
///
/// Gelesen wird nur; das Paket wird unverändert weitergereicht.
/// </summary>
public sealed unsafe class EmoteWatcher : IDisposable
{
    /// <summary>
    /// Signatur aus FFXIVClientStructs ausgelesen, nicht geraten: Die Funktion hat
    /// **kein** this - der erste Parameter ist bereits die EntityId. Ein zusaetzlicher
    /// Zeiger vorne verschiebt alle Werte um eine Stelle, und dann liest man die
    /// Kategorie als Entitaet.
    /// </summary>
    private delegate void ActorControlDelegate(
        uint entityId, uint category,
        uint arg1, uint arg2, uint arg3, uint arg4,
        uint arg5, uint arg6, uint arg7, uint arg8,
        ulong targetId, byte isRecorded);

    private readonly Hook<ActorControlDelegate> hook;

    /// <summary>Urheber (EntityId), Ziel (EntityId), Emote-Nummer.</summary>
    public event Action<uint, uint, uint>? OnEmote;

    /// <summary>
    /// Schreibt jedes Paket ins Log, das den eigenen Charakter betrifft. Dient dazu,
    /// die Kategorienummer für Emotes zu ermitteln - sie steht in keiner Aufzählung.
    /// </summary>
    public bool Diagnostics { get; set; }

    /// <summary>
    /// Kategorie, unter der Emotes laufen. Am 28.08.2026 im Spiel gemessen: Ein
    /// /hug, /stroke und /dote erzeugten jeweils ein Paket mit dieser Kategorie, die
    /// Emote-Nummer stand in arg1 (112, 105, 146 - passend zur Emote-Tabelle).
    ///
    /// Steht in keiner Aufzaehlung von FFXIVClientStructs; nach einem grossen Patch
    /// gehoert sie nachgemessen.
    /// </summary>
    public const uint EmoteCategory = 290;

    public EmoteWatcher(IGameInteropProvider interop)
    {
        hook = interop.HookFromAddress<ActorControlDelegate>(
            (nint)PacketDispatcher.Addresses.HandleActorControlPacket.Value, Detour);

        hook.Enable();
    }

    /// <summary>
    /// Die untere Haelfte einer GameObjectId ist die EntityId. Ein direkter Vergleich
    /// mit PlayerState.EntityId schlaegt sonst fehl, weil oben noch Typbits stehen.
    /// </summary>
    private static uint AlsEntityId(ulong gameObjectId) => (uint)(gameObjectId & 0xFFFFFFFF);

    private void Detour(
        uint entityId, uint category,
        uint arg1, uint arg2, uint arg3, uint arg4,
        uint arg5, uint arg6, uint arg7, uint arg8,
        ulong targetId, byte isRecorded)
    {
        try
        {
            var eigene = Plugin.PlayerState.IsLoaded ? Plugin.PlayerState.EntityId : 0u;
            var ziel = AlsEntityId(targetId);
            var betrifftUns = eigene != 0 && (entityId == eigene || ziel == eigene);

            // Zuerst wird ueberhaupt gemessen: Die ersten Pakete gehen ungefiltert ins
            // Log, damit erkennbar ist, ob der Hook feuert und wie die Werte aussehen.
            if (Diagnostics && category == EmoteCategory && betrifftUns)
            {
                Plugin.Log.Information(
                    $"[EnCounter/ActorControl] cat={category} ent={entityId} " +
                    $"tgtRoh={targetId} tgt={ziel} " +
                    $"args=[{arg1},{arg2},{arg3},{arg4},{arg5},{arg6}] " +
                    $"eigene={eigene} {(betrifftUns ? "BETRIFFT UNS" : "")}");
            }

            if (category == EmoteCategory)
                OnEmote?.Invoke(entityId, ziel, arg1);
        }
        catch (Exception e)
        {
            Plugin.Log.Error(e, "Fehler beim Auswerten eines ActorControl-Pakets");
        }

        // Immer weiterreichen - wir lesen nur mit.
        hook.Original(entityId, category,
                      arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8,
                      targetId, isRecorded);
    }

    public void Dispose()
    {
        hook.Disable();
        hook.Dispose();
    }
}
