using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Plugin.Services;
using EnCounter.Core;
using EnCounter.Ui;

namespace EnCounter;

public sealed partial class ConfigWindow
{
    // ----------------------------------------------------------------- rechts

    private enum Bereich { Trigger, Title, Look }

    private enum Art { Emotes, Chat }

    private static readonly Bereich[] Bereiche = [Bereich.Trigger, Bereich.Title, Bereich.Look];
    private static readonly Art[] Arten = [Art.Emotes, Art.Chat];
    private static readonly CountDirection[] Richtungen = [CountDirection.Received, CountDirection.Given, CountDirection.Both];

    /// <summary>Stand einer Regel vor dem Wechsel zwischen Emotes und Chat, damit sich der Wechsel zurücknehmen lässt.</summary>
    private sealed record ArtMerker(
        Rule Regel, string Hinweis, List<ChatChannel> Kanaele, List<string> TriggerTexte,
        bool EigenerName, List<uint> EmoteIds);

    private Bereich bereich;
    private ArtMerker? artMerker;

    private void DrawEditor(Rule regel)
    {
        DrawEditorKopf(regel);
        if (Gewaehlt != regel)
            return;

        // Der Merker gehört zu der Regel, an der gewechselt wurde.
        if (artMerker is not null && artMerker.Regel != regel)
            artMerker = null;

        ImGui.Spacing();
        DrawTitelVorschau(regel);
        ImGui.Spacing();

        Widgets.Segmented("bereich", Bereiche, b => b.ToString(), ref bereich, ImGui.GetContentRegionAvail().X);
        ImGui.Spacing();

        switch (bereich)
        {
            case Bereich.Trigger:
                DrawAusloeser(regel);
                break;
            case Bereich.Title:
                DrawVorlagen(regel);
                break;
            default:
                DrawFarben(regel);
                break;
        }
    }

    private static bool IstEmoteRegel(Rule regel) =>
        regel.Channels.Contains(ChatChannel.StandardEmote) ||
        regel.Channels.Contains(ChatChannel.CustomEmote);

    /// <summary>Ein Abschnitt: Überschrift mit optionaler Erklärung, darunter der Inhalt in einer Karte.</summary>
    private static void Abschnitt(string titel, string? hilfe, Action<float> inhalt)
    {
        Widgets.SectionHeader(titel, hilfe);
        Widgets.Card(titel, inhalt);
        ImGui.Spacing();
    }

    private static float KnopfBreite(string text) =>
        ImGui.CalcTextSize(text).X + ImGui.GetStyle().FramePadding.X * 2;

    // ------------------------------------------------------------- Vorschau

    private static void DrawTitelVorschau(Rule regel)
    {
        Widgets.Card("vorschau", breite =>
        {
            if (regel.Direction == CountDirection.Both)
            {
                ZeichneVorschauzeile(regel, "They emote you", false, breite);
                ImGui.Spacing();
                ZeichneVorschauzeile(regel, "You emote them", true, breite);
            }
            else
            {
                ZeichneVorschauzeile(regel, null, false, breite);
            }

            var beispiel = regel.ShowName
                ? $"Example: {TitleTemplate.ExampleName}, {TitleTemplate.ExamplePersonalCount}"
                : $"Example: {TitleTemplate.ExampleTotalCount} in total";
            ImGui.TextColored(Theme.Muted, beispiel);
        });
    }

    private static readonly Vector2[] GlowRichtungen =
    [
        new(-1, -1), new(0, -1), new(1, -1), new(-1, 0), new(1, 0), new(-1, 1), new(0, 1), new(1, 1),
    ];

    /// <param name="beschriftung">Bei Both die Richtung als Beschriftung darüber; sonst null.</param>
    private static void ZeichneVorschauzeile(Rule regel, string? beschriftung, bool outgoing, float breite)
    {
        if (beschriftung is not null)
            ImGui.TextColored(Theme.Muted, beschriftung);

        var text = TitleTemplate.Preview(regel, outgoing);
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();

        // 1 px Rand für den Glow, damit er nicht in die Nachbarzeile ragt.
        var start = pos + Vector2.One;

        if (text.Length == 0)
        {
            draw.AddText(start, Theme.U32(Theme.Muted), "(empty)");
        }
        else if (!regel.UseColours)
        {
            draw.AddText(start, Theme.U32(Theme.Text), text);
        }
        else
        {
            var farbe = new Vector4(regel.ColourR, regel.ColourG, regel.ColourB, 1f);
            var glow = new Vector4(regel.GlowR, regel.GlowG, regel.GlowB, 1f);
            var zweite = new Vector4(regel.Colour3R, regel.Colour3G, regel.Colour3B, 1f);

            // Je Zeichen einzeln, damit der Verlauf möglich ist; der Glow nutzt dieselben Stellen.
            var zeichen = new List<(string Text, float X)>();
            var x = 0f;
            foreach (var rune in text.EnumerateRunes())
            {
                var s = rune.ToString();
                zeichen.Add((s, x));
                x += ImGui.CalcTextSize(s).X;
            }

            var glowFarbe = Theme.U32(glow);
            foreach (var versatz in GlowRichtungen)
            {
                if (regel.UseGradient)
                    foreach (var (z, zx) in zeichen)
                        draw.AddText(start + versatz + new Vector2(zx, 0), glowFarbe, z);
                else
                    draw.AddText(start + versatz, glowFarbe, text);
            }

            if (regel.UseGradient)
            {
                for (var i = 0; i < zeichen.Count; i++)
                {
                    var t = zeichen.Count > 1 ? i / (float)(zeichen.Count - 1) : 0f;
                    draw.AddText(start + new Vector2(zeichen[i].X, 0), Theme.U32(Vector4.Lerp(farbe, zweite, t)), zeichen[i].Text);
                }
            }
            else
            {
                draw.AddText(start, Theme.U32(farbe), text);
            }
        }

        var zaehler = $"{text.Length} / {TitleTemplate.MaxTitleLength}";
        var zaehlerBreite = ImGui.CalcTextSize(zaehler).X;
        draw.AddText(new Vector2(pos.X + breite - zaehlerBreite, pos.Y + 1),
            Theme.U32(text.Length > TitleTemplate.MaxTitleLength ? Theme.Danger : Theme.Muted), zaehler);

        ImGui.Dummy(new Vector2(breite, ImGui.GetTextLineHeight() + 2));
    }

    // --------------------------------------------------------------- Trigger

    private void DrawAusloeser(Rule regel)
    {
        var istEmote = IstEmoteRegel(regel);
        DrawArtwahl(regel, istEmote);

        if (istEmote)
        {
            DrawRichtung(regel);
            DrawEmoteAuswahl(regel);
        }
        else
        {
            DrawKanaele(regel);
            DrawTrigger(regel);
        }

        DrawNurVon(regel);
        DrawBremse(regel);
    }

    /// <summary>
    /// Namensliste: Nur diese Personen lösen die Regel aus. Verglichen wird der
    /// Vorname — eingetippt werden darf trotzdem der volle Name.
    /// </summary>
    private void DrawNurVon(Rule regel)
    {
        // Nur die vollstaendige Form ist eindeutig: Vornamen wiederholen sich, und
        // Namen sind in FFXIV ohnehin nur pro Welt eindeutig.
        const string hilfe = "Only these people count - and this rule's text wins over a general one.\n" +
                             "Enter the full name and world, for example: Stella Unibell@Shiva.\n" +
                             "Names are only unique per world, so partial names never match.";

        Abschnitt("Only from", hilfe, breite =>
        {
            if (regel.OnlyFrom.Count == 0)
                ImGui.TextColored(Theme.Muted, "Anyone counts.");

            var entfernen = -1;
            var erste = true;
            for (var i = 0; i < regel.OnlyFrom.Count; i++)
            {
                var name = regel.OnlyFrom[i];
                var vollstaendig = RuleMatcher.IstVollstaendig(name);
                var beschriftung = vollstaendig ? TitleTemplate.FirstName(name) : name;
                var tooltip = vollstaendig ? name : "Incomplete - never matches. Use Firstname Lastname@World.";

                Widgets.Flow(ref erste, Widgets.TagChipWidth(beschriftung, false), 12f);
                if (Widgets.TagChip($"per{i}", beschriftung, vollstaendig ? null : Theme.Danger, 0, tooltip))
                    entfernen = i;
            }

            if (entfernen >= 0)
            {
                regel.OnlyFrom.RemoveAt(entfernen);
                save();
            }

            if (regel.OnlyFrom.Count > 0)
                ImGui.Spacing();

            var abstand = ImGui.GetStyle().ItemSpacing.X;
            var ziel = aktuellesZiel();
            var feld = Math.Max(Widgets.S(120), breite - KnopfBreite("Add") - KnopfBreite("Current target") - abstand * 2);

            ImGui.SetNextItemWidth(feld);
            ImGui.InputTextWithHint("##newperson", "Firstname Lastname@World", ref neuerName, 64);

            ImGui.SameLine();
            if (ImGui.Button("Add##person") && !string.IsNullOrWhiteSpace(neuerName))
            {
                regel.OnlyFrom.Add(neuerName.Trim());
                neuerName = string.Empty;
                save();
            }

            ImGui.SameLine();

            // Bequemer Weg zum richtigen Namen: Wer im Spiel steht, tippt sich nicht
            // vertippt - und die Welt kommt gleich mit.
            ImGui.BeginDisabled(ziel is null);
            var gewaehlt = ImGui.Button("Current target");
            ImGui.EndDisabled();

            if (ziel is null)
            {
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip("Target a player in game to use this.");
            }
            else
            {
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip($"Add {ziel}");

                if (gewaehlt && !regel.OnlyFrom.Contains(ziel, StringComparer.CurrentCultureIgnoreCase))
                {
                    regel.OnlyFrom.Add(ziel);
                    save();
                }
            }
        });
    }

    /// <summary>
    /// Emote- oder Chatregel. Die Wahl setzt die Kanäle passend - sie ist keine
    /// zusätzliche Angabe, sondern eine lesbare Sicht auf dieselbe Auswahl.
    /// </summary>
    private void DrawArtwahl(Rule regel, bool istEmote)
    {
        Abschnitt("Listens to", null, breite =>
        {
            var art = istEmote ? Art.Emotes : Art.Chat;
            if (Widgets.Segmented("art", Arten, a => a == Art.Emotes ? "Emotes" : "Chat", ref art, breite))
                WechsleArt(regel, istEmote, art);

            ImGui.TextColored(Theme.Muted, istEmote
                ? "Counts emotes like /hug or /pet, taken straight from the game."
                : "Counts chat messages containing one of your trigger words.");

            if (artMerker is not null && artMerker.Regel == regel)
            {
                ImGui.TextUnformatted(artMerker.Hinweis);
                ImGui.SameLine();
                if (ImGui.SmallButton("Undo"))
                {
                    regel.Channels.Clear();
                    regel.Channels.AddRange(artMerker.Kanaele);
                    regel.TriggerTexts.Clear();
                    regel.TriggerTexts.AddRange(artMerker.TriggerTexte);
                    regel.RequiresOwnName = artMerker.EigenerName;
                    regel.EmoteIds.Clear();
                    regel.EmoteIds.AddRange(artMerker.EmoteIds);
                    artMerker = null;
                    save();
                    return;
                }
            }

            // Mischregeln aus aelteren Fassungen: Die Chatkanaele waeren hier unsichtbar,
            // wuerden aber weiter zaehlen.
            if (!istEmote)
                return;

            var uebrig = regel.Channels.Count(k => !Emotekanaele.Contains(k));

            if (uebrig > 0)
            {
                ImGui.TextColored(Theme.Danger, $"This rule also still listens to {uebrig} chat channel(s).");
                ImGui.SameLine();

                if (ImGui.SmallButton("Remove them"))
                {
                    regel.Channels.RemoveAll(k => !Emotekanaele.Contains(k));
                    save();
                }
            }

            // Reste einer Chatregel, die auf Emotes umgestellt wurde. Sie wirken nicht
            // mehr - ein Emote hat keinen Text -, stehen aber weiter in der Datei und
            // verwirren beim naechsten Blick hinein.
            var textreste = regel.TriggerTexts.Count > 0 || regel.RequiresOwnName;
            if (!textreste)
                return;

            ImGui.TextColored(Theme.Warning, "Trigger words or the name check are left over and ignored for emotes.");
            ImGui.SameLine();

            if (!ImGui.SmallButton("Clear them"))
                return;

            regel.TriggerTexts.Clear();
            regel.RequiresOwnName = false;
            save();
        });
    }

    private void WechsleArt(Rule regel, bool istEmote, Art neu)
    {
        if (istEmote == (neu == Art.Emotes))
            return;

        artMerker = new ArtMerker(regel, neu == Art.Chat ? "Switched to Chat." : "Switched to Emotes.",
            [.. regel.Channels], [.. regel.TriggerTexts], regel.RequiresOwnName, [.. regel.EmoteIds]);

        if (neu == Art.Chat)
        {
            foreach (var k in Emotekanaele)
                regel.Channels.Remove(k);

            // Ohne einen Kanal hoert die Regel auf gar nichts. Say ist der
            // naheliegende Anfang.
            if (regel.Channels.Count == 0)
                regel.Channels.Add(ChatChannel.Say);
        }
        else
        {
            regel.Channels.Clear();
            regel.Channels.AddRange(Emotekanaele);
        }

        save();
    }

    /// <summary>
    /// Die Kanäle einzeln als Chips, nach Zusammengehörigkeit gruppiert. Eine einzelne
    /// lange Liste zwingt zum Scrollen und lässt Zusammengehöriges auseinanderfallen.
    /// </summary>
    private void DrawKanaele(Rule regel)
    {
        Abschnitt("Channels", null, _ =>
        {
            DrawKanalgruppe(regel, "General", Kanalspalten[0]);
            DrawKanalgruppe(regel, "Group", Kanalspalten[1].Concat(Kanalspalten[2]));
            DrawKanalgruppe(regel, "Linkshells", Linkshells.Select((k, i) => ($"{i + 1}", k)));
            DrawKanalgruppe(regel, "Cross-world linkshells", WeltLinkshells.Select((k, i) => ($"{i + 1}", k)));
        });
    }

    private void DrawKanalgruppe(Rule regel, string titel, IEnumerable<(string Beschriftung, ChatChannel Kanal)> kanaele)
    {
        ImGui.TextColored(Theme.Muted, titel);

        var erste = true;
        foreach (var (beschriftung, kanal) in kanaele)
        {
            Widgets.Flow(ref erste, Widgets.ChipWidth(beschriftung), 12f);
            Kanalchip(regel, beschriftung, kanal);
        }

        ImGui.Spacing();
    }

    private void Kanalchip(Rule regel, string beschriftung, ChatChannel kanal)
    {
        var an = regel.Channels.Contains(kanal);

        if (!Widgets.Chip(kanal.ToString(), beschriftung, an))
            return;

        if (an)
            regel.Channels.Remove(kanal);
        else
            regel.Channels.Add(kanal);

        save();
    }

    private void DrawTrigger(Rule regel)
    {
        Abschnitt("Trigger words", "Any of these must appear in the message. Empty: every message counts.", breite =>
        {
            if (regel.TriggerTexts.Count == 0)
                ImGui.TextColored(Theme.Muted, "Empty: every message counts.");

            var entfernen = -1;
            var erste = true;
            for (var i = 0; i < regel.TriggerTexts.Count; i++)
            {
                var wort = regel.TriggerTexts[i];
                Widgets.Flow(ref erste, Widgets.TagChipWidth(wort, false), 12f);
                if (Widgets.TagChip($"trg{i}", wort))
                    entfernen = i;
            }

            if (entfernen >= 0)
            {
                regel.TriggerTexts.RemoveAt(entfernen);
                save();
            }

            if (regel.TriggerTexts.Count > 0)
                ImGui.Spacing();

            ImGui.SetNextItemWidth(Math.Max(Widgets.S(120), breite - KnopfBreite("Add") - ImGui.GetStyle().ItemSpacing.X));
            var enter = ImGui.InputTextWithHint("##newtrigger", "Add a word or phrase…", ref neuerTrigger, 128,
                ImGuiInputTextFlags.EnterReturnsTrue);
            ImGui.SameLine();
            var knopf = ImGui.Button("Add##trigger");

            if ((enter || knopf) && !string.IsNullOrWhiteSpace(neuerTrigger))
            {
                regel.TriggerTexts.Add(neuerTrigger.Trim());
                neuerTrigger = string.Empty;
                save();
            }

            // Enter nimmt dem Feld den Fokus; für mehrere Wörter hintereinander soll er bleiben.
            if (enter)
                ImGui.SetKeyboardFocusHere(-1);

            ImGui.Spacing();

            var eigenerName = regel.RequiresOwnName;
            if (Widgets.ToggleRow("Message must contain my name", ref eigenerName, breite,
                    "Ignored in Tell - those are addressed to you anyway."))
            {
                regel.RequiresOwnName = eigenerName;
                save();
            }
        });
    }

    /// <summary>
    /// Empfangen, gegeben oder beides. Steht nur bei Emotes, weil es im Chat keinen
    /// verlässlichen Empfänger gibt - wer in Say etwas sagt, sagt es an alle.
    /// </summary>
    private void DrawRichtung(Rule regel)
    {
        Abschnitt("Direction", null, breite =>
        {
            var richtung = regel.Direction;
            if (Widgets.Segmented("richtung", Richtungen, Richtungsname, ref richtung, breite))
            {
                regel.Direction = richtung;
                save();
            }

            ImGui.TextColored(Theme.Muted, "The counter is always kept per other person, never for yourself.");
        });
    }

    private static string Richtungsname(CountDirection richtung) => richtung switch
    {
        CountDirection.Received => "They emote me",
        CountDirection.Given => "I emote them",
        _ => "Both",
    };

    private void DrawBremse(Rule regel)
    {
        Abschnitt("Spam guard", null, _ =>
        {
            var sekunden = regel.CooldownSeconds;
            ImGui.SetNextItemWidth(Widgets.S(220));

            if (ImGui.SliderInt("Cooldown per person", ref sekunden, 0, 60, sekunden == 0 ? "off" : "%d s"))
            {
                regel.CooldownSeconds = sekunden;
                save();
            }

            ImGui.TextColored(Theme.Muted, sekunden == 0
                ? "Every single event counts - a macro can run the number up."
                : $"The same person counts again after {sekunden}s at the earliest.");
        });
    }

    /// <summary>
    /// Emotes über ihren Namen auswählen. Gespeichert wird die Nummer, weil die
    /// sprachunabhängig ist - angezeigt der Name in der Client-Sprache.
    /// </summary>
    private void DrawEmoteAuswahl(Rule regel)
    {
        Abschnitt("Emotes", null, breite =>
        {
            if (regel.EmoteIds.Count == 0)
            {
                Widgets.Pill("All emotes", Theme.Border, Theme.Text);
                ImGui.TextColored(Theme.Muted, regel.Direction == CountDirection.Given
                    ? "Empty: every emote you aim at someone counts."
                    : "Empty: every emote aimed at you counts.");
            }

            var katalog = EmoteCatalog.All(data);
            uint entfernen = 0;
            var erste = true;
            foreach (var id in regel.EmoteIds)
            {
                var eintrag = katalog.FirstOrDefault(e => e.Id == id);
                var name = eintrag?.Name ?? $"#{id}";

                Widgets.Flow(ref erste, Widgets.TagChipWidth(name, true), 12f);
                if (Widgets.TagChip($"emo{id}", name, null, eintrag?.Icon ?? 0))
                    entfernen = id;
            }

            if (entfernen != 0)
            {
                regel.EmoteIds.Remove(entfernen);
                save();
            }

            if (regel.EmoteIds.Count > 0)
                ImGui.Spacing();

            ImGui.SetNextItemWidth(breite);
            ImGui.InputTextWithHint("##emotesuche", "Add emote…", ref emoteSuche, 64);

            if (string.IsNullOrWhiteSpace(emoteSuche))
                return;

            ImGui.BeginChild("emoteliste", new Vector2(0, Widgets.S(180)), true);

            var treffer = 0;
            var zeile = ImGui.GetFrameHeight();
            foreach (var emote in katalog)
            {
                if (regel.EmoteIds.Contains(emote.Id))
                    continue;

                if (emote.Name.Contains(emoteSuche, StringComparison.CurrentCultureIgnoreCase) is false &&
                    emote.Command.Contains(emoteSuche, StringComparison.CurrentCultureIgnoreCase) is false)
                    continue;

                if (++treffer > 40)
                {
                    ImGui.TextColored(Theme.Muted, "...more matches, refine the search.");
                    break;
                }

                var pos = ImGui.GetCursorScreenPos();
                var gewaehlt = ImGui.Selectable($"##pick{emote.Id}", false, ImGuiSelectableFlags.None, new Vector2(0, zeile));

                var draw = ImGui.GetWindowDrawList();
                var icon = zeile - 4;
                Widgets.GameIconAt(pos + new Vector2(2, 2), emote.Icon, icon);
                var textY = pos.Y + (zeile - ImGui.GetTextLineHeight()) / 2;
                var x = pos.X + icon + 10;
                draw.AddText(new Vector2(x, textY), Theme.U32(Theme.Text), emote.Name);
                draw.AddText(new Vector2(x + ImGui.CalcTextSize(emote.Name).X + 10, textY), Theme.U32(Theme.Muted), emote.Command);

                if (!gewaehlt)
                    continue;

                regel.EmoteIds.Add(emote.Id);
                emoteSuche = string.Empty;
                save();
                break;
            }

            if (treffer == 0)
                ImGui.TextColored(Theme.Muted, "Nothing found.");

            ImGui.EndChild();
        });
    }

    // ----------------------------------------------------------------- Title

    private enum Zeige { Person, Gesamt }

    private enum Lage { Ueber, Unter }

    private static readonly Zeige[] ZeigeWerte = [Zeige.Person, Zeige.Gesamt];
    private static readonly Lage[] LageWerte = [Lage.Ueber, Lage.Unter];

    private readonly record struct Vorlagenfeld(string Id, Func<string> Lesen, Action<string> Setzen);

    /// <summary>Die Vorlagenfelder dieses und des letzten Bilds; die Platzhalter-Knöpfe stehen vor den Feldern und kennen nur das letzte.</summary>
    private List<Vorlagenfeld> vorlagenfelder = [];
    private List<Vorlagenfeld> vorlagenfelderVorher = [];
    private string fokusFeld = string.Empty;

    private void DrawVorlagen(Rule regel)
    {
        vorlagenfelderVorher = vorlagenfelder;
        vorlagenfelder = [];

        // Der Unterschied ist nicht nur der Name: Mit Namen zeigt die Meldung den
        // Zaehler dieser Person, ohne Namen die Summe ueber alle.
        Abschnitt("Shows", null, breite =>
        {
            var zeige = regel.ShowName ? Zeige.Person : Zeige.Gesamt;
            if (Widgets.Segmented("zeige", ZeigeWerte, z => z == Zeige.Person ? "One person's count" : "Total across everyone",
                    ref zeige, breite))
            {
                regel.ShowName = zeige == Zeige.Person;
                save();
            }

            ImGui.TextColored(Theme.Muted, regel.ShowName
                ? "How often THIS person did it."
                : "How often ANYONE did it.");
        });

        var istEmote = IstEmoteRegel(regel);
        var beideRichtungen = istEmote && regel.Direction == CountDirection.Both;

        const string hilfe = "[Name] and [Count] are filled in.\n" +
                             "With a name, the title shows this person's count; if the name makes it too long, " +
                             "the template without a name is used instead.";

        Abschnitt("Templates", hilfe, breite =>
        {
            ImGui.TextColored(Theme.Muted, "Insert");
            foreach (var platzhalter in new[] { "[Name]", "[Count]" })
            {
                ImGui.SameLine();
                if (ImGui.SmallButton(platzhalter))
                    FuegePlatzhalterEin(platzhalter);
            }

            // Bei ausgehenden Emotes ist [Name] nicht der Urheber, sondern das Ziel - eine
            // Vorlage wie "[Name] hugged me" stuende dann genau verkehrt herum.
            if (istEmote && regel.Direction == CountDirection.Given)
                ImGui.TextColored(Theme.Warning, "[Name] is the person you emoted, not the sender.");

            ImGui.Spacing();

            if (beideRichtungen)
                ImGui.TextUnformatted("When they emote you");

            DrawVorlagenpaar(regel,
                () => regel.PersonalTemplate, v => regel.PersonalTemplate = v,
                () => regel.GeneralTemplate, v => regel.GeneralTemplate = v,
                kuerzel: string.Empty, ersatz: null, breite);

            // Bei "beides" faellt sonst eine der beiden Meldungen verkehrt herum aus.
            // Leer bleiben duerfen die Felder trotzdem - dann gilt die Vorlage von oben.
            if (!beideRichtungen)
                return;

            ImGui.Spacing();
            ImGui.TextUnformatted("When you emote them");
            ImGui.TextColored(Theme.Muted, "Leave empty to use the templates above.");

            DrawVorlagenpaar(regel,
                () => regel.GivenPersonalTemplate, v => regel.GivenPersonalTemplate = v,
                () => regel.GivenGeneralTemplate, v => regel.GivenGeneralTemplate = v,
                kuerzel: "given", ersatz: regel, breite);
        });

        Abschnitt("Display", null, breite =>
        {
            var sekunden = regel.DisplaySeconds;
            ImGui.SetNextItemWidth(Widgets.S(220));
            if (ImGui.SliderInt("Seconds shown", ref sekunden, 1, 60))
            {
                regel.DisplaySeconds = sekunden;
                save();
            }

            ImGui.Spacing();

            // Ueber oder unter dem Namen - Honorific kann beides.
            var lage = regel.IsPrefix ? Lage.Ueber : Lage.Unter;
            if (Widgets.Segmented("lage", LageWerte, l => l == Lage.Ueber ? "Above the name" : "Below the name", ref lage, breite))
            {
                regel.IsPrefix = lage == Lage.Ueber;
                save();
            }
        });
    }

    private void FuegePlatzhalterEin(string platzhalter)
    {
        var ziel = vorlagenfelderVorher.FirstOrDefault(f => f.Id == fokusFeld);
        if (ziel.Id is null && vorlagenfelderVorher.Count > 0)
            ziel = vorlagenfelderVorher[0];

        if (ziel.Id is null)
            return;

        var neu = ziel.Lesen() + platzhalter;
        ziel.Setzen(neu.Length > 128 ? neu[..128] : neu);
        save();
    }

    /// <summary>
    /// Die zwei Vorlagen einer Richtung. <paramref name="ersatz"/> ist gesetzt, wenn
    /// leere Felder auf die Vorlagen der Gegenrichtung zurückfallen - dann steht
    /// darunter, was stattdessen erscheint.
    /// </summary>
    private void DrawVorlagenpaar(Rule regel,
                                  Func<string> mitNamenLesen, Action<string> mitNamenSetzen,
                                  Func<string> ohneNamenLesen, Action<string> ohneNamenSetzen,
                                  string kuerzel, Rule? ersatz, float breite)
    {
        if (regel.ShowName)
        {
            DrawVorlage("With name", $"mit{kuerzel}", mitNamenLesen, TitleTemplate.MaxPersonalFreeText,
                        mitNamenSetzen, mitNamen: true, ersatz: ersatz?.PersonalTemplate, breite);

            ImGui.Spacing();
        }

        DrawVorlage(regel.ShowName ? "Without name (fallback if the name makes it too long)" : "Without name",
                    $"ohne{kuerzel}", ohneNamenLesen, TitleTemplate.MaxTitleLength,
                    ohneNamenSetzen, mitNamen: false, ersatz: ersatz?.GeneralTemplate, breite);
    }

    /// <summary>
    /// Vorlage mit Zeichenzähler.
    ///
    /// Die feste Grenze rechnet mit dem längstmöglichen Vornamen. Sie zu überschreiten
    /// ist kein Fehler: Bei kürzeren Namen passt die Meldung weiterhin, bei längeren
    /// greift der Rückfall auf die namenlose Vorlage. Deshalb steht hier, **bis zu
    /// welcher Namenslänge** es reicht, statt einer pauschalen Warnung.
    /// </summary>
    private void DrawVorlage(string label, string id, Func<string> lesen, int grenze, Action<string> setzen,
                             bool mitNamen, string? ersatz, float breite)
    {
        vorlagenfelder.Add(new Vorlagenfeld(id, lesen, setzen));

        ImGui.TextColored(Theme.Muted, label);

        var text = lesen();
        ImGui.SetNextItemWidth(breite);
        if (ImGui.InputText($"##vorlage{id}", ref text, 128))
        {
            setzen(text);
            save();
        }

        if (ImGui.IsItemActive())
            fokusFeld = id;

        // Leeres Feld mit Rueckfall: Der Zeichenzaehler waere hier nur verwirrend,
        // gezeigt wird stattdessen, was tatsaechlich erscheint.
        if (ersatz is not null && string.IsNullOrWhiteSpace(text))
        {
            ImGui.TextColored(Theme.Muted, $"uses: {ersatz}");
            return;
        }

        // Steuerzeichen sind im Eingabefeld unsichtbar, lassen Honorific aber den
        // ganzen Titel verwerfen. Angezeigt wird trotzdem etwas - die gereinigte
        // Fassung -, aber wer sie loswird, hat auch die Vorlage wieder im Griff.
        if (TitleTemplate.HasControlCharacters(text))
        {
            ImGui.TextColored(Theme.Danger, "Contains invisible control characters.");
            ImGui.SameLine();

            if (ImGui.SmallButton($"Clean up##{id}"))
            {
                setzen(TitleTemplate.Clean(text));
                save();
            }
        }

        var benutzt = TitleTemplate.FreeTextLength(text);

        if (!mitNamen)
        {
            // Ohne Namen ist die Grenze hart - hier gibt es keinen Rueckfall mehr.
            var zuLang = benutzt > grenze;
            ImGui.TextColored(zuLang ? Theme.Danger : Theme.Muted,
                zuLang ? $"{benutzt} / {grenze}  too long, Honorific will not show it" : $"{benutzt} / {grenze}");
            return;
        }

        var maxName = TitleTemplate.MaxNameLengthFor(text);

        if (maxName >= 15)
        {
            ImGui.TextColored(Theme.Muted, $"{benutzt} / {grenze}  fits any name");
        }
        else if (maxName > 0)
        {
            ImGui.TextColored(Theme.Warning,
                $"{benutzt} / {grenze}  fits names up to {maxName} characters; longer names use the template below");
        }
        else
        {
            ImGui.TextColored(Theme.Danger,
                $"{benutzt} / {grenze}  too long for any name; the template below is always used");
        }
    }

    // ------------------------------------------------------------------ Look

    private enum Stil { Pulse, Wave, Static }

    private static readonly Stil[] Stile = [Stil.Pulse, Stil.Wave, Stil.Static];

    /// <summary>Text- und Glow-Farbe, so gewählt, dass sie auf hellem wie dunklem Grund lesbar bleiben.</summary>
    private static readonly (string Name, Vector3 Text, Vector3 Glow)[] Farbvorlagen =
    [
        ("Rose", new(1.00f, 0.55f, 0.75f), new(0.85f, 0.10f, 0.45f)),
        ("Sakura", new(1.00f, 0.85f, 0.90f), new(0.95f, 0.45f, 0.65f)),
        ("Gold", new(1.00f, 0.85f, 0.35f), new(0.75f, 0.45f, 0.00f)),
        ("Sky", new(0.60f, 0.85f, 1.00f), new(0.15f, 0.45f, 0.85f)),
        ("Mint", new(0.65f, 1.00f, 0.80f), new(0.10f, 0.60f, 0.40f)),
        ("Lavender", new(0.80f, 0.70f, 1.00f), new(0.45f, 0.30f, 0.85f)),
    ];

    private void DrawFarben(Rule regel)
    {
        Abschnitt("Colours", null, breite =>
        {
            var eigene = regel.UseColours;
            if (Widgets.ToggleRow("Use own colours", ref eigene, breite, "Off: the game's default title styling is used."))
            {
                regel.UseColours = eigene;
                save();
            }

            if (!regel.UseColours)
                return;

            ImGui.Spacing();

            var farbe = new Vector3(regel.ColourR, regel.ColourG, regel.ColourB);
            ImGui.SetNextItemWidth(Widgets.S(220));
            if (ImGui.ColorEdit3("Text", ref farbe))
            {
                (regel.ColourR, regel.ColourG, regel.ColourB) = (farbe.X, farbe.Y, farbe.Z);
                save();
            }

            var glow = new Vector3(regel.GlowR, regel.GlowG, regel.GlowB);
            ImGui.SetNextItemWidth(Widgets.S(220));
            if (ImGui.ColorEdit3("Glow", ref glow))
            {
                (regel.GlowR, regel.GlowG, regel.GlowB) = (glow.X, glow.Y, glow.Z);
                save();
            }

            ImGui.Spacing();

            var verlauf = regel.UseGradient;
            if (Widgets.ToggleRow("Gradient", ref verlauf, breite))
            {
                regel.UseGradient = verlauf;
                save();
            }

            if (!regel.UseGradient)
                return;

            ImGui.Spacing();

            var farbe3 = new Vector3(regel.Colour3R, regel.Colour3G, regel.Colour3B);
            ImGui.SetNextItemWidth(Widgets.S(220));
            if (ImGui.ColorEdit3("Second colour", ref farbe3))
            {
                (regel.Colour3R, regel.Colour3G, regel.Colour3B) = (farbe3.X, farbe3.Y, farbe3.Z);
                save();
            }

            ImGui.Spacing();

            var stil = (Stil)Math.Clamp(regel.GradientStyle, 0, 2);
            if (Widgets.Segmented("stil", Stile, s => Verlaufsname((int)s), ref stil, breite))
            {
                regel.GradientStyle = (int)stil;
                save();
            }
        });

        Abschnitt("Presets", null, _ =>
        {
            var durchmesser = Widgets.S(28);
            var erste = true;
            foreach (var (name, text, glow) in Farbvorlagen)
            {
                Widgets.Flow(ref erste, durchmesser, 12f);
                if (Farbfeld(name, text, glow, durchmesser))
                {
                    (regel.ColourR, regel.ColourG, regel.ColourB) = (text.X, text.Y, text.Z);
                    (regel.GlowR, regel.GlowG, regel.GlowB) = (glow.X, glow.Y, glow.Z);
                    regel.UseColours = true;
                    save();
                }
            }

            ImGui.TextColored(Theme.Muted, "Sets text and glow colour; the gradient stays as it is.");
        });
    }

    /// <summary>Rundes Feld in der Textfarbe mit einem Ring in der Glow-Farbe.</summary>
    private static bool Farbfeld(string name, Vector3 text, Vector3 glow, float durchmesser)
    {
        var pos = ImGui.GetCursorScreenPos();
        var geklickt = ImGui.InvisibleButton($"##farbe{name}", new Vector2(durchmesser, durchmesser));
        var hovered = ImGui.IsItemHovered();
        var mitte = pos + new Vector2(durchmesser / 2);
        var radius = durchmesser / 2 - 1;
        var draw = ImGui.GetWindowDrawList();

        draw.AddCircleFilled(mitte, radius, Theme.U32(new Vector4(text, 1f)));
        draw.AddCircle(mitte, radius - 1.5f, Theme.U32(new Vector4(glow, 1f)), 0, 3f);
        if (hovered)
            draw.AddCircle(mitte, radius + 1.5f, Theme.U32(Theme.AccentLight), 0, 1f);

        if (hovered)
            ImGui.SetTooltip(name);

        return geklickt;
    }

    /// <summary>Die Reihenfolge stammt aus Honorific: 0 Pulse, 1 Wave, 2 Static.</summary>
    private static string Verlaufsname(int stil) => stil switch
    {
        0 => "Pulse",
        1 => "Wave",
        _ => "Static",
    };
}
