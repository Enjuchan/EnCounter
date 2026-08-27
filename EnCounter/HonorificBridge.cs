using System.Text.Json;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace EnCounter;

/// <summary>
/// Setzt den Titel über Honorific.
///
/// Honorific ist zwingend, nicht optional: LightlessSync überträgt Titel über dessen
/// IPC. Ein eigener Nameplate-Hook würde den Titel nur lokal setzen - andere Spieler
/// sähen nichts, und genau darin liegt der Zweck des Plugins.
///
/// Per IPC gesetzte Titel haben in Honorific Vorrang vor der eigenen Auswahl. Einmal
/// setzen genügt also; ein Wechsler wie SomethingNeedDoing kommt nicht dazwischen.
/// </summary>
public sealed class HonorificBridge(IDalamudPluginInterface plugin)
{
    private const int LocalPlayerIndex = 0;

    private readonly ICallGateSubscriber<(uint, uint)> apiVersion =
        plugin.GetIpcSubscriber<(uint, uint)>("Honorific.ApiVersion");

    private readonly ICallGateSubscriber<int, string, object> setTitle =
        plugin.GetIpcSubscriber<int, string, object>("Honorific.SetCharacterTitle");

    private readonly ICallGateSubscriber<int, object> clearTitle =
        plugin.GetIpcSubscriber<int, object>("Honorific.ClearCharacterTitle");

    public string? LastError { get; private set; }

    /// <summary>Ob Honorific erreichbar ist. Wird bei jedem Aufruf neu geprüft.</summary>
    public bool IsAvailable
    {
        get
        {
            try
            {
                var (major, _) = apiVersion.InvokeFunc();
                if (major >= 3) return true;

                LastError = $"Honorific-API {major} ist zu alt, benoetigt wird 3 oder neuer";
                return false;
            }
            catch (Exception e)
            {
                LastError = "Honorific nicht erreichbar: " + e.GetType().Name;
                return false;
            }
        }
    }

    /// <summary>
    /// Setzt den Titel. Farben werden nur mitgeschickt, wenn die Regel welche vorgibt.
    ///
    /// **Ohne Farben greift die Standarddarstellung des Spiels**, nicht Honorifics
    /// eigene Gestaltung: Der IPC-Titel ersetzt dessen Auswahl vollstaendig, also
    /// kommen auch dessen Farben nicht zum Zug. Im Spiel beobachtet.
    ///
    /// Der Aufbau entspricht Honorifics TitleData: Farben als Vector3 mit X, Y, Z
    /// im Bereich 0 bis 1, null für "nicht gesetzt".
    /// </summary>
    public void SetTitle(string text, EnCounter.Core.Rule? regel = null)
    {
        try
        {
            object? farbe = null, glow = null, farbe3 = null;
            int? verlaufSet = null, verlaufStil = null;

            if (regel is { UseColours: true })
            {
                farbe = new { X = regel.ColourR, Y = regel.ColourG, Z = regel.ColourB };
                glow = new { X = regel.GlowR, Y = regel.GlowG, Z = regel.GlowB };

                if (regel.UseGradient)
                {
                    farbe3 = new { X = regel.Colour3R, Y = regel.Colour3G, Z = regel.Colour3B };
                    verlaufSet = -1;                    // -1 heisst: eigene Farben
                    verlaufStil = regel.GradientStyle;
                }
            }

            var daten = new
            {
                Title = text,
                IsPrefix = true,
                IsOriginal = false,
                Color = farbe,
                Glow = glow,
                Color3 = farbe3,
                GradientColourSet = verlaufSet,
                GradientAnimationStyle = verlaufStil,
            };

            setTitle.InvokeAction(LocalPlayerIndex, JsonSerializer.Serialize(daten));
            LastError = null;
        }
        catch (Exception e)
        {
            LastError = e.GetType().Name + ": " + e.Message;
        }
    }

    public void ClearTitle()
    {
        try
        {
            clearTitle.InvokeAction(LocalPlayerIndex);
            LastError = null;
        }
        catch (Exception e)
        {
            LastError = e.GetType().Name + ": " + e.Message;
        }
    }
}
