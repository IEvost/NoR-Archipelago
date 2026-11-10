# Translation review notes (v0.1, LLM draft)

All 3,803 unique Japanese strings are covered except the 11 logic-key literals (must stay Japanese,
`data/translation/code_unsafe.txt`) and 2 credits supporter lists (left untranslated on purpose:
romanizing real people's handles would be guesswork). Everything is an LLM draft pending human review.

## Decided by the user (2026-11-10)
- **Uncensored**: censored words in the source are written out in English (pussy, dick, penis). The censored
  age "〇〇歳" stays "age __" (uncensoring would invent a number).
- **Degraid** confirmed.
- **Names as close to the original as possible**: romanize, never translate meanings (Hinagiku, not "Daisy";
  鬼神丸 / 喜人丸 both "Kijinmaru").

## Decisions needing a human
- Weapon prefix 激質な "Fierce", 無銘 湾れ刃 "Unsigned Notare Blade", 終代 "Suzune the Last".
- Hinagiku kept as a name (the community translation used "Daisy").
- Credits romanizations (composer/voice names in scene part A, line 113) are guesses — check official spellings.

## Check in-game
- Concatenated fragments (assumed word order): `" acquired."`, `" used."`, `" times"` ("1 times"),
  `"'s memory has been engraved"`, `" will be used to ... Proceed?"`, `" required"`.
- Padded stat/label alignment (ASCII spaces replacing full-width ones): status screen, rescue/area rank labels.
- Symbols kept from the source: → ↑ ◇ × ♪ (should exist in the game font).
- `<size=80%>` tags added to 5 bad-status labels in `20_code.tsv` so longer English fits.
- Developer/test strings were translated literally (some look unused).

## Per-file uncertainty lists
See the translator reports summarized in the project log (PLAN.md section 14); each listed hashes worth checking.
