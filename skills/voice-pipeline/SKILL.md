---
name: voice-pipeline
description: End-to-end voice authoring for a MENACE character — transcribe source clips to JP+EN, build the SoundBank, clone conversation templates against a vanilla speaker, normalise loudness to vanilla. Use when adding voice barks to a new character or troubleshooting why a clone doesn't fire / sounds wrong.
---

# Voice pipeline

## What this covers

How a character's voice barks get from raw audio rips to triggering in-game. Five tools + one runtime pattern:

1. `scripts/voice/transcribe.py` — OpenAI ASR + MT → `assets/additions/audio/<char>/.trans.csv`
2. `scripts/voice/normalize_audio.py` — LUFS-normalise raw clips to match vanilla MENACE bark loudness
3. `templates/<char>/voice/soundbank.kdl` — SoundBank clone that registers the WAVs as a runtime bank
4. `templates/<char>/voice/{arrivals,clicks,enemy,misc,movement,objectives,responses}.kdl` — ConversationTemplate clones that fire those bank entries on in-game events
5. `scripts/voice/serve.py` — local web utility to browse + play each character's clips and read their transcripts

## Pipeline at a glance

```
[ rip dir of *.wav ] ──cp+rename──▶ [ assets/additions/audio/<char>/*.wav ]
                                              │
                                              ├─normalize_audio.py──▶ same files, LUFS-aligned
                                              │
                                              ├─transcribe.py──▶ <char>/.trans.csv
                                              │
                                              └─referenced by templates/<char>/voice/soundbank.kdl
                                                          │
                                                          ▼
                                          templates/<char>/voice/*.kdl
                                          ConversationTemplate clones
                                              │
                                              ▼
                                          mise compile + deploy
                                              │
                                              ▼
                                            in-game
```

## 1. Drop the WAVs

Source the character's voice clips (typically a community rip — GFL2 voice files at `~/dev/github.com/beanpuppy/gfl2-voice/JP/VO_<Character>_JP/` was our reference). Copy + rename to strip the source-prefix junk:

```
VO_Cheyenne_JP_VO_Cheyenne_Single_Login_001.wav → Single_Login_001.wav
```

Three source dirs typically: main VO + ServR_ (server-room) + Bedroom_. Merge into one flat dir at `assets/additions/audio/<char>/`. The cleaned filenames double as bank item names (see SoundBank section).

## 2. Normalise to vanilla loudness

Source rips are usually ~10–12 dB louder than vanilla MENACE barks. Run:

```bash
uv run --script scripts/voice/normalize_audio.py \
  --reference assets/additions/audio/voymastina \
  --target assets/additions/audio/<char>
```

Voymastina's audio is already normalised to vanilla (the original mod's reference). Median LUFS ≈ −28. `--dry-run` to preview deltas without writing.

## 3. Transcribe + translate

`transcribe.py` uses OpenAI's `gpt-4o-transcribe` for JP ASR and `gpt-5` (or override via `--translate-model`) for JP→EN translation. Output is `<source>/.trans.csv` with columns `filename, transcript, english, note`.

```bash
uv run --script scripts/voice/transcribe.py assets/additions/audio/<char>
```

Reads `OPENAI_API_KEY` from `.env` at the repo root via `python-dotenv`. The .env file is `.gitignore`-protected.

Cost is pennies for 60-65 clips.

## 4. SoundBank clone

`templates/<char>/voice/soundbank.kdl`:

```kdl
clone "SoundBank" from="tactical_barks_carda_va_full_mid" id="tactical_barks_<character>_va" {
    clear "sounds"
    clear "busIndices"

    append "sounds" {
        set "name" "Single_Login_001"
        append "variations" {
            set "clip" asset="<character>/Single_Login_001"
        }
    }
    append "busIndices" 0

    append "sounds" {
        set "name" "Single_Obtain_SSR_001"
        append "variations" {
            set "clip" asset="<character>/Single_Obtain_SSR_001"
        }
    }
    append "busIndices" 0

    ...
}
```

**Filename-as-name convention** — each Sound's `name` field equals the WAV filename (without `.wav`). Bank itemId references in conversation/squad-leader templates use this same name. The asset path (e.g. `<character>/Single_Login_001`) is the Jiangyu-derived addition asset name (strips `assets/additions/audio/`).

**Exception**: weapon SoundBanks need multiple variations per Sound (so the engine picks randomly for shot-to-shot variety). For those, the Sound name is a semantic shorthand (e.g. `rf_shot`) and the variations are the per-shot files. See [`weapon-pipeline`](../weapon-pipeline/SKILL.md). Voice banks are one-Sound-per-clip and use filename-as-name.

Each `append "sounds"` must be followed by an `append "busIndices" 0` (zero = the default voice bus). Sound and bus arrays are parallel — if they desync the bank loader fails. There's an auto-extension that may save you, but don't rely on it.

Mod-bank IDs are FNV-1a hashed from the clone-ID string at load time, then the loader binds them. Conversation/skill references use the string `"tactical_barks_<character>_va"`; the loader does the hash routing.

Cloning from a Speaker-specific bank (e.g. `tactical_barks_carda_va_full_mid`) inherits the bus/falloff defaults the speaker uses. If you can find a parent bank that matches your character's archetype, prefer that over `weapons_soundbank` or other class banks.

## 5. ConversationTemplate clones

The 7-file split that voymastina follows (arrivals/clicks/enemy/misc/movement/objectives/responses) is just organisational — the engine doesn't care which file a clone lives in.

Each clone shape:

```kdl
clone "ConversationTemplate" from="<Parent_Namespace>/<event_name>" id="<Character>/<event_name>" {
    set "Roles" index=<speaker_role_index> {
        set "m_SerializedRequirements" index=2 composite="HasOneTag" {
            set "Tags" "<character>"
        }
    }
    set "Nodes" {
        append "m_SerializedNodes" composite="VARIATION" {
            append "Variations" {
                append "m_SerializedNodes" composite="SAY" {
                    set "Sound" {
                        set "bankId" "tactical_barks_<character>_va"
                        set "itemId" "<filename_from_bank>"
                    }
                    set "RoleGuid" "<role_name_in_parent>"
                    set "Text" "<english_from_csv>"
                }
            }
            ...more variations...
        }
        append "m_SerializedNodes" composite="EMPTY" {}
    }
}
```

**Parent namespace** — JeanSy templates live at `JeanSy/<event>` (sy's barks). Carda's at `Carda_Early/<event>` (her early-game progression bank). Pick the parent that matches your character's source archetype. Voymastina (cloned from sy) uses JeanSy parents; cheyanne (cloned from carda) uses Carda_Early parents.

**Speaker role index** — the index of the role in the parent template that gets the character's tag-override. Find it via:

```bash
dotnet ../jiangyu/src/Jiangyu.Cli/bin/Debug/net10.0/jiangyu.dll templates inspect \
  --type ConversationTemplate --name <event_name>
```

The Roles array is small (1–3 entries). The speaker is the role tagged in your character clones (named after the source speaker, e.g. `JeanSy`, `Carda`, `SL`).

**RoleGuid per template** — must match the role NAME at the same index in the parent template, NOT a fixed value. This varies per-template:

- arrival_carda Roles: `[Carda]` → speaker RoleGuid = `Carda`
- idle_bark_combat Roles: `[SL]` → speaker RoleGuid = `SL`
- enemy_fleeing Roles: `[Fleeing, SL]` → speaker (index 1) RoleGuid = `SL`
- response_taking_fire_anyone Roles: `[Damaged, Attacker, SL]` → speaker (index 2) RoleGuid = `SL`

A `RoleGuid` that doesn't match any role in the parent fails template validation at compile. The error message lists the parent's known role names — exactly what you need.

## What inherits, what you override

A ConversationTemplate clone deep-copies the parent. Anything you don't `set` keeps the parent's:

- `Condition`, `Triggers`, `TriggerTag`, `EventSettings`, `Stage`
- `PlayChance`, `Priority`, `Repeatable`, `Repetitions`
- All Roles except the one you tag-override

The `set "Nodes" { ... }` block fully replaces the parent's Nodes — your `append "m_SerializedNodes"` builds a fresh container, the parent's variations are discarded. That's intentional: the character's lines are different from the parent's lines.

`Repetitions: 0` (recurring filler, e.g. `taking_fire`) vs `Repetitions: 1` (one-shot flavour barks like `taking_fire1`/`11`/`111`/`1111`) is inherited. You don't need to set it. The numbered suffix templates are intended to play once per mission for variety; the base recurs.

## Browse + play locally

`scripts/voice/serve.py` runs a tiny http.server at `localhost:8765` that walks `assets/additions/audio/`, finds every dir with a `.trans.csv`, and serves a table view: filename / JP / EN / per-row play button. Useful for:

- Hand-correcting `english` cells in the CSV when the model garbled a translation
- Picking which `itemId` to wire into a specific conversation event (you can hear the line first)
- Spot-checking loudness vs voymastina's reference (eyes on the audio bar)

```bash
python3 scripts/voice/serve.py
```

Stdlib-only (no extra deps); auto-opens `http://127.0.0.1:8765/`.

## Auto-converter for cheyanne-style derivatives

`/tmp/gen_cheyanne_voice_v2.py` (kept out of the repo intentionally — it's a one-shot bootstrap) takes voymastina's KDLs and rewrites them for a new character cloned from a different speaker. Substitutions:

- `JeanSy/<event>` → `<Carda_Early_or_X>/<carda_event>` via a survey-built map
- `Voymastina/X` → `<Cheyanne>/<carda_event>`
- `voymastina` tag → `<character>` tag
- `tactical_barks_voymastina_va` → `tactical_barks_<character>_va`
- `RoleGuid "JeanSy"` → role name at the matching index in the carda parent
- `Text` body → looked up by itemId from the new character's `.trans.csv`

Used twice: once for cheyanne's initial conversion (Carda_Early), once after the OpenAI re-transcribe to refresh translations.

When adding a third+ character, rename the script and update the survey-map paths.

## Cross-references

- [`character-authoring`](../character-authoring/SKILL.md) for the parent-character-pick decision and the squad-leader spine.
- [`weapon-pipeline`](../weapon-pipeline/SKILL.md) for the variations-per-Sound convention used by weapon banks (different from voice banks).
- [`../../AGENTS.md`](../../AGENTS.md) for the SoundBank-construction gotchas (`fixedVolume=1`, `fixedPitch=1`, `dopplerLevel=1` defaults; bus-index auto-extend behaviour).
