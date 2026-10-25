# Poke Library

`pokes.json` is the user-maintainable game and cheat library used by the Poke
Manager. The application validates the complete file before showing any entry.

Each game contains a stable `id`, display `title`, optional filename `aliases`,
supported `models`, exact media `sha256` values, and one or more named `pokes`.
Filename aliases aid discovery only; an exact checksum is the trusted automatic
match.

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
