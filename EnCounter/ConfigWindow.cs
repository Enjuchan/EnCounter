using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using EnCounter.Core;

namespace EnCounter;

/// <summary>
/// Regeln verwalten.
///
/// Zweispaltig: links die Regeln, rechts der Editor der ausgewählten. Beides
/// gleichzeitig sichtbar, damit man beim Vergleichen nicht springen muss.
///
/// Die Bindings heißen `Dalamud.Bindings.ImGui`, nicht ImGuiNET - Beispiele aus dem
/// Netz passen meist nicht.
/// </summary>
public sealed class ConfigWindow(
    Configuration config,
    CounterStore counters,
    IDataManager data,
    Action save)
{
    private const uint Warnfarbe = 0xFF3333FFu;
    private const uint Graufarbe = 0xFF999999u;

    /// <summary>
    /// Kanäle in Gruppen, wie man sie im Kopf hat - statt siebzehn Häkchen in einer
    /// Reihe.
    /// </summary>
    private static readonly (string Name, ChatChannel[] Kanaele)[] Gruppen =
    [
        ("Public",  [ChatChannel.Say, ChatChannel.Yell, ChatChannel.Shout]),
        ("Private", [ChatChannel.TellIncoming, ChatChannel.Party, ChatChannel.CrossParty, ChatChannel.FreeCompany]),
        ("Linkshells", [ChatChannel.Ls1, ChatChannel.Ls2, ChatChannel.Ls3, ChatChannel.Ls4,
                        ChatChannel.Ls5, ChatChannel.Ls6, ChatChannel.Ls7, ChatChannel.Ls8]),
        ("Emotes",  [ChatChannel.StandardEmote, ChatChannel.CustomEmote]),
    ];

    public bool Visible;

    private int ausgewaehlt;
    private string neuerTrigger = string.Empty;
    private string emoteSuche = string.Empty;

    public void Draw()
    {
        if (!Visible)
            return;

        ImGui.SetNextWindowSize(new(760, 560), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("EnCounter", ref Visible))
        {
            ImGui.End();
            return;
        }

        DrawLinkeSpalte();

        ImGui.SameLine();

        ImGui.BeginChild("editor", new(0, 0));

        if (ausgewaehlt >= 0 && ausgewaehlt < config.Rules.Count)
            DrawEditor(config.Rules[ausgewaehlt]);
        else
            ImGui.TextColored(Graufarbe, "No rule selected.");

        ImGui.EndChild();

        ImGui.End();
    }

    // ------------------------------------------------------------------ links

    private void DrawLinkeSpalte()
    {
        ImGui.BeginChild("rules", new(210, 0), true);

        ImGui.TextUnformatted("Rules");
        ImGui.Separator();

        for (var i = 0; i < config.Rules.Count; i++)
        {
            var regel = config.Rules[i];

            var an = regel.Enabled;
            if (ImGui.Checkbox($"##on{i}", ref an))
            {
                regel.Enabled = an;
                save();
            }

            ImGui.SameLine();

            var beschriftung = $"{regel.Name}##sel{i}";
            if (ImGui.Selectable(beschriftung, ausgewaehlt == i))
                ausgewaehlt = i;

            // Der Zaehler steht klein darunter statt in Klammern dahinter - so bleibt
            // der Name lesbar, auch wenn er lang ist.
            ImGui.TextColored(Graufarbe, $"      {counters.GetTotal(regel.Name)} counted");
        }

        ImGui.Separator();

        if (ImGui.Button("Add", new(90, 0)))
        {
            config.Rules.Add(new Rule { Name = $"Rule {config.Rules.Count + 1}" });
            ausgewaehlt = config.Rules.Count - 1;
            save();
        }

        ImGui.SameLine();

        if (ImGui.Button("Delete", new(90, 0)) && ausgewaehlt >= 0 && ausgewaehlt < config.Rules.Count)
        {
            config.Rules.RemoveAt(ausgewaehlt);
            ausgewaehlt = Math.Min(ausgewaehlt, config.Rules.Count - 1);
            save();
        }

        ImGui.EndChild();
    }

    // ----------------------------------------------------------------- rechts

    private void DrawEditor(Rule regel)
    {
        var name = regel.Name;
        if (ImGui.InputText("Name", ref name, 64))
        {
            regel.Name = name;
            save();
        }

        if (ImGui.BeginTabBar("tabs"))
        {
            if (ImGui.BeginTabItem("Listen to"))
            {
                DrawAusloeser(regel);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Text"))
            {
                DrawVorlagen(regel);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Colours"))
            {
                DrawFarben(regel);
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawAusloeser(Rule regel)
    {
        ImGui.TextUnformatted("Channels");
        ImGui.TextColored(Graufarbe, "Where to listen.");

        foreach (var (gruppe, kanaele) in Gruppen)
        {
            var alle = kanaele.All(k => regel.Channels.Contains(k));
            var teils = !alle && kanaele.Any(k => regel.Channels.Contains(k));

            var an = alle;
            if (ImGui.Checkbox(gruppe, ref an))
            {
                foreach (var k in kanaele)
                {
                    regel.Channels.Remove(k);
                    if (an) regel.Channels.Add(k);
                }

                save();
            }

            if (teils)
            {
                ImGui.SameLine();
                ImGui.TextColored(Graufarbe, "(partly)");
            }
        }

        ImGui.Separator();

        var hoertAufEmotes = regel.Channels.Contains(ChatChannel.StandardEmote)
                          || regel.Channels.Contains(ChatChannel.CustomEmote);

        if (hoertAufEmotes)
            DrawEmoteAuswahl(regel);
        else
            DrawTrigger(regel);
    }

    private void DrawTrigger(Rule regel)
    {
        ImGui.TextUnformatted("Trigger words");
        ImGui.TextColored(Graufarbe, "Any of these must appear. Empty: every message counts.");

        for (var i = 0; i < regel.TriggerTexts.Count; i++)
        {
            if (ImGui.Button($"x##trg{i}"))
            {
                regel.TriggerTexts.RemoveAt(i);
                save();
                return;
            }

            ImGui.SameLine();
            ImGui.TextUnformatted(regel.TriggerTexts[i]);
        }

        ImGui.SetNextItemWidth(220);
        ImGui.InputText("##newtrigger", ref neuerTrigger, 128);
        ImGui.SameLine();

        if (ImGui.Button("Add##trigger") && !string.IsNullOrWhiteSpace(neuerTrigger))
        {
            regel.TriggerTexts.Add(neuerTrigger.Trim());
            neuerTrigger = string.Empty;
            save();
        }

        ImGui.Separator();

        var eigenerName = regel.RequiresOwnName;
        if (ImGui.Checkbox("Message must contain my name", ref eigenerName))
        {
            regel.RequiresOwnName = eigenerName;
            save();
        }

        ImGui.TextColored(Graufarbe, "Ignored in Tell - those are addressed to you anyway.");
    }

    /// <summary>
    /// Emotes über ihren Namen auswählen. Gespeichert wird die Nummer, weil die
    /// sprachunabhängig ist - angezeigt der Name in der Client-Sprache.
    /// </summary>
    private void DrawEmoteAuswahl(Rule regel)
    {
        ImGui.TextUnformatted("Emotes");
        ImGui.TextColored(Graufarbe, "Empty: every emote aimed at you counts.");

        foreach (var id in regel.EmoteIds.ToList())
        {
            if (ImGui.Button($"x##emo{id}"))
            {
                regel.EmoteIds.Remove(id);
                save();
                return;
            }

            ImGui.SameLine();
            ImGui.TextUnformatted(EmoteCatalog.NameOf(data, id));
        }

        ImGui.Separator();
        ImGui.SetNextItemWidth(220);
        ImGui.InputTextWithHint("##emotesuche", "search emote...", ref emoteSuche, 64);

        if (string.IsNullOrWhiteSpace(emoteSuche))
            return;

        ImGui.BeginChild("emoteliste", new(0, 180), true);

        var treffer = 0;
        foreach (var emote in EmoteCatalog.All(data))
        {
            if (regel.EmoteIds.Contains(emote.Id))
                continue;

            if (emote.Name.Contains(emoteSuche, StringComparison.CurrentCultureIgnoreCase) is false &&
                emote.Command.Contains(emoteSuche, StringComparison.CurrentCultureIgnoreCase) is false)
                continue;

            if (++treffer > 40)
            {
                ImGui.TextColored(Graufarbe, "...more matches, refine the search.");
                break;
            }

            if (!ImGui.Selectable($"{emote.Name}  {emote.Command}##pick{emote.Id}"))
                continue;

            regel.EmoteIds.Add(emote.Id);
            emoteSuche = string.Empty;
            save();
            break;
        }

        if (treffer == 0)
            ImGui.TextColored(Graufarbe, "nothing found");

        ImGui.EndChild();
    }

    private void DrawVorlagen(Rule regel)
    {
        ImGui.TextColored(Graufarbe, "[Name] and [Anzahl] are filled in.");
        ImGui.Spacing();

        // Der Unterschied ist nicht nur der Name: Mit Namen zeigt die Meldung den
        // Zaehler dieser Person, ohne Namen die Summe ueber alle.
        var mitNamen = regel.ShowName;

        if (ImGui.RadioButton("Name the person", ref mitNamen, true))
        {
            regel.ShowName = true;
            save();
        }

        ImGui.SameLine();

        if (ImGui.RadioButton("Show the total", ref mitNamen, false))
        {
            regel.ShowName = false;
            save();
        }

        ImGui.TextColored(Graufarbe, regel.ShowName
            ? "Shows how often THIS person did it."
            : "Shows how often ANYONE did it.");

        ImGui.Spacing();

        if (regel.ShowName)
        {
            DrawVorlage("With name", regel.PersonalTemplate, TitleTemplate.MaxPersonalFreeText,
                        v => regel.PersonalTemplate = v);

            ImGui.Spacing();
            ImGui.TextColored(Graufarbe, "Fallback if the name makes it too long:");
        }

        DrawVorlage("Without name", regel.GeneralTemplate, TitleTemplate.MaxTitleLength,
                    v => regel.GeneralTemplate = v);

        ImGui.Separator();

        var sekunden = regel.DisplaySeconds;
        ImGui.SetNextItemWidth(240);
        if (ImGui.SliderInt("Seconds shown", ref sekunden, 1, 60))
        {
            regel.DisplaySeconds = sekunden;
            save();
        }
    }

    private void DrawVorlage(string label, string wert, int grenze, Action<string> setzen)
    {
        var text = wert;
        ImGui.SetNextItemWidth(320);
        if (ImGui.InputText(label, ref text, 128))
        {
            setzen(text);
            save();
        }

        var benutzt = TitleTemplate.FreeTextLength(text);
        var zuLang = benutzt > grenze;

        ImGui.TextColored(zuLang ? Warnfarbe : Graufarbe, $"      {benutzt} / {grenze}");

        if (!zuLang)
            return;

        ImGui.SameLine();
        ImGui.TextColored(Warnfarbe, "too long, Honorific will not show it");
    }

    private void DrawFarben(Rule regel)
    {
        var eigene = regel.UseColours;
        if (ImGui.Checkbox("Use own colours", ref eigene))
        {
            regel.UseColours = eigene;
            save();
        }

        ImGui.TextColored(Graufarbe, "Off: Honorific's own styling is kept.");

        if (!regel.UseColours)
            return;

        ImGui.Separator();

        var farbe = new Vector3(regel.ColourR, regel.ColourG, regel.ColourB);
        if (ImGui.ColorEdit3("Text", ref farbe))
        {
            (regel.ColourR, regel.ColourG, regel.ColourB) = (farbe.X, farbe.Y, farbe.Z);
            save();
        }

        var glow = new Vector3(regel.GlowR, regel.GlowG, regel.GlowB);
        if (ImGui.ColorEdit3("Glow", ref glow))
        {
            (regel.GlowR, regel.GlowG, regel.GlowB) = (glow.X, glow.Y, glow.Z);
            save();
        }

        ImGui.Separator();

        var verlauf = regel.UseGradient;
        if (ImGui.Checkbox("Gradient", ref verlauf))
        {
            regel.UseGradient = verlauf;
            save();
        }

        if (!regel.UseGradient)
            return;

        var farbe3 = new Vector3(regel.Colour3R, regel.Colour3G, regel.Colour3B);
        if (ImGui.ColorEdit3("Second colour", ref farbe3))
        {
            (regel.Colour3R, regel.Colour3G, regel.Colour3B) = (farbe3.X, farbe3.Y, farbe3.Z);
            save();
        }

        var stil = regel.GradientStyle;
        if (ImGui.RadioButton("Pulse", ref stil, 0)) { regel.GradientStyle = stil; save(); }
        ImGui.SameLine();
        if (ImGui.RadioButton("Wave", ref stil, 1)) { regel.GradientStyle = stil; save(); }
        ImGui.SameLine();
        if (ImGui.RadioButton("Static", ref stil, 2)) { regel.GradientStyle = stil; save(); }
    }
}
