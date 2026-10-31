# Poke Library

`pokes.json` is the user-maintainable game and cheat library used by the Poke
Manager. The application validates the complete file before showing any entry.

Each game contains a stable `id`, display `title`, optional filename `aliases`,
supported `models`, exact media `sha256` values, and one or more named `pokes`.
Filename aliases aid discovery only; an exact checksum is the trusted automatic
match. If neither is found, the manager leaves `Choose a game...` selected and
does not expose an unrelated game's pokes until the user chooses it explicitly.

Mapped writes use CPU addresses from `4000H` to `FFFFH`:

```json
{ "space": "mapped", "address": "0x8000", "value": "0x00" }
```

Physical writes use a 128K RAM bank and an offset from `0000H` to `3FFFH`:

```json
{ "space": "ramBank", "bank": 7, "offset": "0x0123", "value": "0x2A" }
```

Numbers may be decimal or hexadecimal using `0x`, `$`, or a trailing `H`.
Every poke must contain at least one write, and a poke cannot write the same
target twice.

The shipped catalogue contains 39 exact-image game definitions and 143 named
pokes. The Ocean 128K RoboCop infinite-lives entry has been verified during
gameplay. The other entries are published
[World of Spectrum Hackers Anonymous](https://worldofspectrum.org/hackersanon/)
pokes whose target layouts were checked against the exact local snapshot
checksums recorded in `pokes.json`; their individual gameplay results should be
confirmed progressively. Filename-only matches remain suggestions and are never
treated as trusted automatic matches.

When extending the catalogue, record the exact media checksum, confirm the
target bytes belong to the expected release, and then verify the resulting game
behavior. Do not assign a poke to another tape or snapshot merely because its
filename names the same game.
