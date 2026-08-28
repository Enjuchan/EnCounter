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
/// **Leitgedanke der Aufteilung:** Eine Regel hört entweder auf Emotes oder auf Chat.
/// Das stand früher nur implizit in den Kanalhäkchen, und die Oberfläche wechselte
/// unerklärlich ihr Aussehen. Jetzt ist es die erste, bewusste Entscheidung; alles
/// Weitere richtet sich danach.
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

    /// <summary>Gelb: geht noch, aber nicht fuer jeden Namen.</summary>
    private const uint Hinweisfarbe = 0xFF66CCFFu;
    private const uint Graufarbe = 0xFF999999u;

    /// <summary>
    /// Die Kanäle einzeln, in drei Spalten nach Zusammengehörigkeit. Gruppenhäkchen
    /// gab es vorher - sie standen auf „aus", sobald ein Kanal der Gruppe fehlte, und
    /// verschwiegen damit genau das, was sie zeigen sollten.
    /// </summary>
    private static readonly (string Beschriftung, ChatChannel Kanal)[][] Kanalspalten =
    [
        [
            ("Say", ChatChannel.Say),
            ("Yell", ChatChannel.Yell),
            ("Shout", ChatChannel.Shout),
            ("Tell", ChatChannel.TellIncoming),
        ],
        [
            ("Party", ChatChannel.Party),
            ("Alliance", ChatChannel.Alliance),
            ("Cross-world party", ChatChannel.CrossParty),
            ("PvP team", ChatChannel.PvpTeam),
        ],
        [
            ("Free Company", ChatChannel.FreeCompany),
            ("Novice Network", ChatChannel.NoviceNetwork),
        ],
    ];

    private static readonly ChatChannel[] Linkshells =
    [
        ChatChannel.Ls1, ChatChannel.Ls2, ChatChannel.Ls3, ChatChannel.Ls4,
        ChatChannel.Ls5, ChatChannel.Ls6, ChatChannel.Ls7, ChatChannel.Ls8,
    ];

    private static readonly ChatChannel[] WeltLinkshells =
    [
        ChatChannel.Cwls1, ChatChannel.Cwls2, ChatChannel.Cwls3, ChatChannel.Cwls4,
        ChatChannel.Cwls5, ChatChannel.Cwls6, ChatChannel.Cwls7, ChatChannel.Cwls8,
    ];

    private static readonly ChatChannel[] Emotekanaele =
    [
        ChatChannel.StandardEmote, ChatChannel.CustomEmote,
    ];

    public bool Visible;

    private int ausgewaehlt;
    private string neuerTrigger = string.Empty;
    private string emoteSuche = string.Empty;

    public void Draw()
    {
        if (!Visible)
            return;

        ImGui.SetNextWindowSize(new(800, 580), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("EnCounter", ref Visible))
        {
            ImGui.End();
            return;
        }

        DrawLinkeSpalte();

        ImGui.SameLine();

        ImGui.BeginChild("editor", new(0, 0));

        if (Gewaehlt is { } regel)
            DrawEditor(regel);
        else
            ImGui.TextColored(Graufarbe, "No rule selected.");

        ImGui.EndChild();

        ImGui.End();
    }

    private Rule? Gewaehlt =>
        ausgewaehlt >= 0 && ausgewaehlt < config.Rules.Count ? config.Rules[ausgewaehlt] : null;

    // ------------------------------------------------------------------ links

    private void DrawLinkeSpalte()
    {
        ImGui.BeginChild("rules", new(215, 0), true);

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

            if (ImGui.Selectable($"{regel.Name}##sel{i}", ausgewaehlt == i))
                ausgewaehlt = i;

            // Zaehler und Art stehen klein darunter statt in Klammern dahinter - so
            // bleibt der Name lesbar, auch wenn er lang ist.
            var art = IstEmoteRegel(regel) ? "emotes" : "chat";
            ImGui.TextColored(Graufarbe, $"      {counters.GetTotal(regel.Id)} counted · {art}");
        }

        ImGui.Separator();

        if (ImGui.Button("Add", new(95, 0)))
        {
            var neu = new Rule { Name = $"Rule {config.Rules.Count + 1}" };
            neu.EnsureId();
            config.Rules.Add(neu);
            ausgewaehlt = config.Rules.Count - 1;
            save();
        }

        ImGui.SameLine();

        if (ImGui.Button("Duplicate", new(95, 0)) && Gewaehlt is { } vorlage)
        {
            config.Rules.Insert(ausgewaehlt + 1, Kopiere(vorlage));
            ausgewaehlt++;
            save();
        }

        // SetItemTooltip gibt es in diesen Bindings nicht - nur IsItemHovered.
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Same settings, own counter, starting at zero.");

        if (ImGui.Button("Delete", new(196, 0)) && Gewaehlt is { } zuLoeschen)
        {
            // Der Zaehler haengt an der Id und waere sonst ein Waisenkind in der Datei.
            counters.Reset(zuLoeschen.Id);
            config.Rules.RemoveAt(ausgewaehlt);
            ausgewaehlt = Math.Min(ausgewaehlt, config.Rules.Count - 1);
            save();
        }

        ImGui.EndChild();
    }

    /// <summary>
    /// Tiefe Kopie mit eigener Id. Die Listen müssen neu angelegt werden - sonst
    /// teilen sich Original und Kopie dieselben Kanäle, und ein Häkchen wirkt in
    /// beiden.
    /// </summary>
    private static Rule Kopiere(Rule v)
    {
        var kopie = new Rule
        {
            Name = $"{v.Name} (copy)",
            Enabled = v.Enabled,
            Channels = [.. v.Channels],
            TriggerTexts = [.. v.TriggerTexts],
            EmoteIds = [.. v.EmoteIds],
            RequiresOwnName = v.RequiresOwnName,
            Direction = v.Direction,
            CooldownSeconds = v.CooldownSeconds,
            ShowName = v.ShowName,
            PersonalTemplate = v.PersonalTemplate,
            GeneralTemplate = v.GeneralTemplate,
            DisplaySeconds = v.DisplaySeconds,
            UseColours = v.UseColours,
            ColourR = v.ColourR, ColourG = v.ColourG, ColourB = v.ColourB,
            GlowR = v.GlowR, GlowG = v.GlowG, GlowB = v.GlowB,
            UseGradient = v.UseGradient,
            Colour3R = v.Colour3R, Colour3G = v.Colour3G, Colour3B = v.Colour3B,
            GradientStyle = v.GradientStyle,
        };

        kopie.EnsureId();
        return kopie;
    }

    // ----------------------------------------------------------------- rechts

    private void DrawEditor(Rule regel)
    {
        var name = regel.Name;
        ImGui.SetNextItemWidth(300);
        if (ImGui.InputText("Name", ref name, 64))
        {
            regel.Name = name;
            save();
        }

        ImGui.Spacing();

        if (!ImGui.BeginTabBar("tabs"))
            return;

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

        if (ImGui.BeginTabItem("Counts"))
        {
            DrawZaehler(regel);
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private static bool IstEmoteRegel(Rule regel) =>
        regel.Channels.Contains(ChatChannel.StandardEmote) ||
        regel.Channels.Contains(ChatChannel.CustomEmote);

    // ------------------------------------------------------------- Listen to

    private void DrawAusloeser(Rule regel)
    {
        ImGui.Spacing();

        var istEmote = IstEmoteRegel(regel);
        DrawArtwahl(regel, istEmote);

        ImGui.Separator();

        if (istEmote)
        {
            DrawRichtung(regel);
            ImGui.Spacing();
            DrawEmoteAuswahl(regel);
        }
        else
        {
            DrawKanaele(regel);
            ImGui.Spacing();
            DrawTrigger(regel);
        }

        ImGui.Separator();
        DrawBremse(regel);
    }

    /// <summary>
    /// Emote- oder Chatregel. Die Wahl setzt die Kanäle passend - sie ist keine
    /// zusätzliche Angabe, sondern eine lesbare Sicht auf dieselbe Auswahl.
    /// </summary>
    private void DrawArtwahl(Rule regel, bool istEmote)
    {
        ImGui.SetNextItemWidth(220);

        if (ImGui.BeginCombo("Listens to", istEmote ? "Emotes" : "Chat messages"))
        {
            if (ImGui.Selectable("Chat messages", !istEmote) && istEmote)
            {
                foreach (var k in Emotekanaele)
                    regel.Channels.Remove(k);

                // Ohne einen Kanal hoert die Regel auf gar nichts. Say ist der
                // naheliegende Anfang.
                if (regel.Channels.Count == 0)
                    regel.Channels.Add(ChatChannel.Say);

                save();
            }

            if (ImGui.Selectable("Emotes", istEmote) && !istEmote)
            {
                regel.Channels.Clear();
                regel.Channels.AddRange(Emotekanaele);
                save();
            }

            ImGui.EndCombo();
        }

        ImGui.TextColored(Graufarbe, istEmote
            ? "Counts emotes like /hug or /pet, taken straight from the game."
            : "Counts chat messages containing one of your trigger words.");

        // Mischregeln aus aelteren Fassungen: Die Chatkanaele waeren hier unsichtbar,
        // wuerden aber weiter zaehlen.
        if (!istEmote)
            return;

        var uebrig = regel.Channels.Count(k => !Emotekanaele.Contains(k));
        if (uebrig == 0)
            return;

        ImGui.TextColored(Warnfarbe, $"This rule also still listens to {uebrig} chat channel(s).");

        if (!ImGui.Button("Remove them"))
            return;

        regel.Channels.RemoveAll(k => !Emotekanaele.Contains(k));
        save();
    }

    /// <summary>
    /// Die Kanäle einzeln, in Spalten. Eine einzelne lange Liste zwingt zum Scrollen
    /// und lässt Zusammengehöriges auseinanderfallen.
    /// </summary>
    private void DrawKanaele(Rule regel)
    {
        ImGui.TextUnformatted("Channels");

        if (ImGui.BeginTable("kanaele", Kanalspalten.Length))
        {
            for (var i = 0; i < Kanalspalten.Length; i++)
                ImGui.TableSetupColumn($"##sp{i}");

            // Zeilenweise fuellen: In jeder Zeile ein Eintrag je Spalte, damit die
            // Spalten nebeneinander stehen statt untereinander.
            var zeilen = Kanalspalten.Max(s => s.Length);

            for (var zeile = 0; zeile < zeilen; zeile++)
            {
                ImGui.TableNextRow();

                foreach (var spalte in Kanalspalten)
                {
                    ImGui.TableNextColumn();

                    if (zeile >= spalte.Length)
                        continue;

                    var (beschriftung, kanal) = spalte[zeile];
                    Kanalhaken(regel, beschriftung, kanal);
                }
            }

            ImGui.EndTable();
        }

        ImGui.Spacing();

        DrawLinkshellGruppe(regel, "Linkshells", Linkshells, "ls");
        DrawLinkshellGruppe(regel, "Cross-world linkshells", WeltLinkshells, "cwls");
    }

    /// <summary>
    /// Sechzehn Linkshells würden die Seite erschlagen. Zugeklappt steht nur, wie
    /// viele davon an sind - die Angabe, auf die es beim Überfliegen ankommt.
    /// </summary>
    private void DrawLinkshellGruppe(Rule regel, string titel, ChatChannel[] kanaele, string kuerzel)
    {
        var aktiv = kanaele.Count(regel.Channels.Contains);
        var beschriftung = aktiv > 0 ? $"{titel}  ({aktiv} on)###{kuerzel}" : $"{titel}###{kuerzel}";

        if (!ImGui.CollapsingHeader(beschriftung))
            return;

        if (!ImGui.BeginTable($"tbl{kuerzel}", 4))
            return;

        for (var i = 0; i < kanaele.Length; i++)
        {
            if (i % 4 == 0)
                ImGui.TableNextRow();

            ImGui.TableNextColumn();
            Kanalhaken(regel, $"{i + 1}##{kuerzel}{i}", kanaele[i]);
        }

        ImGui.EndTable();
    }

    private void Kanalhaken(Rule regel, string beschriftung, ChatChannel kanal)
    {
        var an = regel.Channels.Contains(kanal);

        if (!ImGui.Checkbox(beschriftung, ref an))
            return;

        if (an)
            regel.Channels.Add(kanal);
        else
            regel.Channels.Remove(kanal);

        save();
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
        ImGui.InputTextWithHint("##newtrigger", "add a word or phrase...", ref neuerTrigger, 128);
        ImGui.SameLine();

        if (ImGui.Button("Add##trigger") && !string.IsNullOrWhiteSpace(neuerTrigger))
        {
            regel.TriggerTexts.Add(neuerTrigger.Trim());
            neuerTrigger = string.Empty;
            save();
        }

        ImGui.Spacing();

        var eigenerName = regel.RequiresOwnName;
        if (ImGui.Checkbox("Message must contain my name", ref eigenerName))
        {
            regel.RequiresOwnName = eigenerName;
            save();
        }

        ImGui.TextColored(Graufarbe, "Ignored in Tell - those are addressed to you anyway.");
    }

    /// <summary>
    /// Empfangen, gegeben oder beides. Steht nur bei Emotes, weil es im Chat keinen
    /// verlässlichen Empfänger gibt - wer in Say etwas sagt, sagt es an alle.
    /// </summary>
    private void DrawRichtung(Rule regel)
    {
        ImGui.SetNextItemWidth(220);

        if (ImGui.BeginCombo("Direction", Richtungsname(regel.Direction)))
        {
            foreach (var richtung in new[] { CountDirection.Received, CountDirection.Given, CountDirection.Both })
            {
                if (!ImGui.Selectable(Richtungsname(richtung), regel.Direction == richtung))
                    continue;

                regel.Direction = richtung;
                save();
            }

            ImGui.EndCombo();
        }

        ImGui.TextColored(Graufarbe, "The counter is always kept per other person, never for yourself.");
    }

    private static string Richtungsname(CountDirection richtung) => richtung switch
    {
        CountDirection.Received => "They emote me",
        CountDirection.Given => "I emote them",
        _ => "Both directions",
    };

    private void DrawBremse(Rule regel)
    {
        var sekunden = regel.CooldownSeconds;
        ImGui.SetNextItemWidth(220);

        if (ImGui.SliderInt("Cooldown per person", ref sekunden, 0, 60, sekunden == 0 ? "off" : "%d s"))
        {
            regel.CooldownSeconds = sekunden;
            save();
        }

        ImGui.TextColored(Graufarbe, sekunden == 0
            ? "Every single event counts - a macro can run the number up."
            : $"The same person counts again after {sekunden}s at the earliest.");
    }

    /// <summary>
    /// Emotes über ihren Namen auswählen. Gespeichert wird die Nummer, weil die
    /// sprachunabhängig ist - angezeigt der Name in der Client-Sprache.
    /// </summary>
    private void DrawEmoteAuswahl(Rule regel)
    {
        ImGui.TextUnformatted("Emotes");

        ImGui.TextColored(Graufarbe, regel.Direction == CountDirection.Given
            ? "Empty: every emote you aim at someone counts."
            : "Empty: every emote aimed at you counts.");

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

    // ------------------------------------------------------------------ Text

    private void DrawVorlagen(Rule regel)
    {
        ImGui.Spacing();
        ImGui.SetNextItemWidth(280);

        // Der Unterschied ist nicht nur der Name: Mit Namen zeigt die Meldung den
        // Zaehler dieser Person, ohne Namen die Summe ueber alle.
        if (ImGui.BeginCombo("Shows", regel.ShowName ? "One person's count" : "The total across everyone"))
        {
            if (ImGui.Selectable("One person's count", regel.ShowName) && !regel.ShowName)
            {
                regel.ShowName = true;
                save();
            }

            if (ImGui.Selectable("The total across everyone", !regel.ShowName) && regel.ShowName)
            {
                regel.ShowName = false;
                save();
            }

            ImGui.EndCombo();
        }

        ImGui.TextColored(Graufarbe, regel.ShowName
            ? "How often THIS person did it."
            : "How often ANYONE did it.");

        ImGui.Separator();
        ImGui.TextColored(Graufarbe, "[Name] and [Count] are filled in.");

        // Bei ausgehenden Emotes ist [Name] nicht der Urheber, sondern das Ziel - eine
        // Vorlage wie "[Name] hugged me" stuende dann genau verkehrt herum.
        if (regel.Direction == CountDirection.Given && IstEmoteRegel(regel))
            ImGui.TextColored(Hinweisfarbe, "[Name] is the person you emoted, not the sender.");

        ImGui.Spacing();

        if (regel.ShowName)
        {
            DrawVorlage("With name", regel.PersonalTemplate, TitleTemplate.MaxPersonalFreeText,
                        v => regel.PersonalTemplate = v, mitNamen: true);

            ImGui.Spacing();
            ImGui.TextColored(Graufarbe, "Fallback if the name makes it too long:");
        }

        DrawVorlage("Without name", regel.GeneralTemplate, TitleTemplate.MaxTitleLength,
                    v => regel.GeneralTemplate = v, mitNamen: false);

        ImGui.Separator();

        var sekunden = regel.DisplaySeconds;
        ImGui.SetNextItemWidth(220);
        if (ImGui.SliderInt("Seconds shown", ref sekunden, 1, 60))
        {
            regel.DisplaySeconds = sekunden;
            save();
        }
    }

    /// <summary>
    /// Vorlage mit Zeichenzähler.
    ///
    /// Die feste Grenze rechnet mit dem längstmöglichen Vornamen. Sie zu überschreiten
    /// ist kein Fehler: Bei kürzeren Namen passt die Meldung weiterhin, bei längeren
    /// greift der Rückfall auf die namenlose Vorlage. Deshalb steht hier, **bis zu
    /// welcher Namenslänge** es reicht, statt einer pauschalen Warnung.
    /// </summary>
    private void DrawVorlage(string label, string wert, int grenze, Action<string> setzen,
                             bool mitNamen)
    {
        var text = wert;
        ImGui.SetNextItemWidth(320);
        if (ImGui.InputText(label, ref text, 128))
        {
            setzen(text);
            save();
        }

        var benutzt = TitleTemplate.FreeTextLength(text);

        if (!mitNamen)
        {
            // Ohne Namen ist die Grenze hart - hier gibt es keinen Rueckfall mehr.
            var zuLang = benutzt > grenze;
            ImGui.TextColored(zuLang ? Warnfarbe : Graufarbe, $"      {benutzt} / {grenze}");

            if (zuLang)
            {
                ImGui.SameLine();
                ImGui.TextColored(Warnfarbe, "too long, Honorific will not show it");
            }

            return;
        }

        var maxName = TitleTemplate.MaxNameLengthFor(text);

        if (maxName >= 15)
        {
            ImGui.TextColored(Graufarbe, $"      {benutzt} / {grenze}  fits any name");
        }
        else if (maxName > 0)
        {
            ImGui.TextColored(Hinweisfarbe,
                $"      {benutzt} / {grenze}  fits names up to {maxName} characters");
            ImGui.TextColored(Graufarbe, "      Longer names fall back to the template below.");
        }
        else
        {
            ImGui.TextColored(Warnfarbe,
                $"      {benutzt} / {grenze}  too long for any name");
            ImGui.TextColored(Warnfarbe, "      The template below will always be used instead.");
        }
    }

    // --------------------------------------------------------------- Colours

    private void DrawFarben(Rule regel)
    {
        ImGui.Spacing();

        var eigene = regel.UseColours;
        if (ImGui.Checkbox("Use own colours", ref eigene))
        {
            regel.UseColours = eigene;
            save();
        }

        ImGui.TextColored(Graufarbe, "Off: the game's default title styling is used.");

        if (!regel.UseColours)
            return;

        ImGui.Separator();

        var farbe = new Vector3(regel.ColourR, regel.ColourG, regel.ColourB);
        ImGui.SetNextItemWidth(220);
        if (ImGui.ColorEdit3("Text", ref farbe))
        {
            (regel.ColourR, regel.ColourG, regel.ColourB) = (farbe.X, farbe.Y, farbe.Z);
            save();
        }

        var glow = new Vector3(regel.GlowR, regel.GlowG, regel.GlowB);
        ImGui.SetNextItemWidth(220);
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
        ImGui.SetNextItemWidth(220);
        if (ImGui.ColorEdit3("Second colour", ref farbe3))
        {
            (regel.Colour3R, regel.Colour3G, regel.Colour3B) = (farbe3.X, farbe3.Y, farbe3.Z);
            save();
        }

        ImGui.SetNextItemWidth(220);

        if (!ImGui.BeginCombo("Style", Verlaufsname(regel.GradientStyle)))
            return;

        for (var stil = 0; stil <= 2; stil++)
        {
            if (!ImGui.Selectable(Verlaufsname(stil), regel.GradientStyle == stil))
                continue;

            regel.GradientStyle = stil;
            save();
        }

        ImGui.EndCombo();
    }

    /// <summary>Die Reihenfolge stammt aus Honorific: 0 Pulse, 1 Wave, 2 Static.</summary>
    private static string Verlaufsname(int stil) => stil switch
    {
        0 => "Pulse",
        1 => "Wave",
        _ => "Static",
    };

    // ---------------------------------------------------------------- Counts

    /// <summary>
    /// Wer wie oft. Nur zum Ansehen und Zurücksetzen - eine tippbare Zahl lädt dazu
    /// ein, den Stand zu erfinden, und dann steht über dem Kopf eine Behauptung
    /// statt einer Zählung.
    /// </summary>
    private void DrawZaehler(Rule regel)
    {
        ImGui.Spacing();

        var eintraege = counters.GetAll(regel.Id);

        ImGui.TextUnformatted($"{counters.GetTotal(regel.Id)} in total, {eintraege.Count} people");

        if (eintraege.Count > 0)
        {
            ImGui.SameLine();

            if (ImGui.Button("Reset all"))
            {
                counters.Reset(regel.Id);
                save();
                return;
            }
        }

        ImGui.Separator();

        if (eintraege.Count == 0)
        {
            ImGui.TextColored(Graufarbe, "Nothing counted yet.");
            return;
        }

        ImGui.BeginChild("zaehlerliste", new(0, 0), true);

        foreach (var (person, anzahl) in eintraege)
        {
            if (ImGui.Button($"x##del{person.Name}{person.WorldId}"))
            {
                counters.Remove(regel.Id, person);
                save();
                break;
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Reset this person to zero.");

            ImGui.SameLine();
            ImGui.TextUnformatted($"{anzahl,5}   {person.Name}");

            var welt = WorldCatalog.NameOf(data, person.WorldId);
            if (string.IsNullOrEmpty(welt))
                continue;

            ImGui.SameLine();
            ImGui.TextColored(Graufarbe, $"@ {welt}");
        }

        ImGui.EndChild();
    }
}
