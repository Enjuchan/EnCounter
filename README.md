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

**Counts are per person and permanent.** A rule can show either how often *this* person
did something, or the total across everyone.

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
