# EnCounter

Counts what other players do to you — emotes and chat messages — and briefly shows the
tally as your title, above your head.

## Why

FFXIV has no way to know that someone hugged you fifty times. EnCounter keeps track and
puts the number where people can see it: in your title.

```
Enju petted you 5x
got love 47x
```

Every rule is yours to define. Which emotes count, which words in chat count, which
channels are watched, what the message says, how long it stays, and in which colours.

A rule listens either to emotes or to chat — you pick which at the top of its settings,
and everything below follows from that. `[Name]` and `[Count]` in a template are filled
in when the title is built, and each rule decides whether its title sits above or below
your name.

## Requirements

**Honorific is required.** EnCounter does not draw anything itself — it hands the title
to Honorific, which already knows how to render it and, more importantly, how to send it
to other players through sync plugins. Without Honorific nothing appears, and EnCounter
will say so on startup instead of staying silent.

## How it works

**Emotes** are read from the game's packet handling, not from the chat log. That matters:
whether an emote produces a chat message is decided by the *sender's* settings. Someone
who turned those off would be invisible to a chat-based plugin. The packet always
arrives — it is what makes the animation play in the first place.

This also makes the detection independent of client language. Nothing is parsed from
message text.

**Chat messages** are matched against trigger words you define, optionally requiring
your own name to appear. In tells that requirement is ignored, since those are addressed
to you anyway.

**Counts belong to the event, not to the rule.** A dote from someone is stored under
that emote, whichever rule picks it up. Add a second rule later — say, one just for a
particular person — and it already knows how often they doted you, instead of starting
at zero. Two rules covering the same thing therefore agree, and a single event is only
ever counted once.

Deleting a rule leaves its counts alone. They were never its property.

The **Counts** tab has three parts: totals per event with both directions side by side,
then everyone who has emoted you, then everyone you have emoted. Each person opens up
to show which emotes make up their number. Resetting there affects every rule that
counts the same thing — which is the point, and why it says so.

**Counts are per person and permanent.** A rule can show either how often *this* person
did something, or the total across everyone.

**Only from** limits a rule to named people. Type the full name if you like; the first
name is what gets compared.

**Both directions.** An emote rule counts what others aim at you, what you aim at
others, or both. The counter always belongs to the *other* person — a tally on your own
name would say nothing.

**A cooldown per person** keeps a macro from running the number up. Three seconds by
default; it locks per rule *and* per person, so one busy emoter cannot block everyone
else. Set it to zero to count every single event.

## Coming from PatMe

*Import...* in the rule list takes counts from **PatMe** and **PatMe Honorific** into
the shared list. It creates no rules — what you make of those numbers is yours to
decide. Nothing changes until you confirm, and importing twice replaces the entry
rather than adding it up again.

Neither plugin records *who* did it, so imported numbers arrive as a single entry
rather than pretending to a breakdown that never existed. You choose whether to take
the character you are playing or all of them summed — the former by default, since
EnCounter counts for whoever is logged in.

The two sources overlap: PatMe counts by emote *name* and has no notion of direction,
while PatMe Honorific counts by emote number and direction but only since it was
switched on. Where they disagree, **the higher number wins**, and the dialog shows what
each source said. Adding them would double every emote both plugins saw; the file has
no timestamps to do better. The lower guess invents nothing.

## Title length

Honorific limits titles to 32 characters and refuses to display anything longer —
it does not truncate. Templates with a name therefore have **13 characters** of free
text: 32 minus the longest possible first name (15) minus a count up to 9999.

The settings window counts along and warns before you cross the line. If a name turns
out too long at runtime, the message falls back to the nameless template rather than
being cut off.

## Privacy

Counts are stored locally, per player name and home world, and never leave your machine.
Be aware that this means a lasting record of who did what to you accumulates in the
plugin's configuration file.

## Settings

`/encounter` or the cog in Dalamud's plugin list.

## Building

```
dotnet build
dotnet test
```

`EnCounter.Core` holds the logic and has no Dalamud reference — that is what makes rule
matching, counters and templates testable without the game running.

## Licence

AGPL-3.0
