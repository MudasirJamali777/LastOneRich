# LAST ONE RICH! — Vertical Slice

A "viral mega-challenge show" competition game (original fictional branding, MrBeast-style *energy*),
built with **C# / .NET 8 + MonoGame DesktopGL** — Visual Studio only, code-first, data-driven.

This repository is the **Section 19 vertical slice** of the GDD, proven end-to-end:

> one obstacle-race level → waypoint bots that finish it → results ceremony + elimination cut →
> Bank vs Risk wallet decision → JSON intro cutscene → twist reveal → (repeatable rounds)

Everything visual is primitives + a generated bitmap font; everything audible is generated WAVs.
**No Content Pipeline (.mgcb), no external editor, no native tools** — build and run.

![gameplay](docs/shot_gameplay.png)

---

## 1) Quick start

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (8.0.400+)
- Visual Studio 2022 (workload: *.NET desktop development*) — or just the `dotnet` CLI

### Run (Visual Studio)
1. Open `LastOneRich.sln`
2. Set **LastOneRich** as the startup project
3. F5

### Run (CLI)
```bash
dotnet run --project src/LastOneRich -c Release
```

### Headless validation harness (CI / balance testing)
Simulates real races with the *same physics + AI code* the game uses (no GPU, no window):
```bash
dotnet run --project src/HeadlessSim -c Release
```
Current result: **7/7 bots finish in every twist scenario — ALL CHECKS PASSED ✔**
It also runs the season/twist validator and fails (exit code ≠ 0) on any regression.

| flag | effect |
|---|---|
| `--debug` | 5s position trace + fall/respawn log + final standings |
| `--debug --fine --who=NOVA --until=40.2` | 0.5s per-actor frame trace (AI debugging) |

### Dev launch flags (game)
| flag | effect |
|---|---|
| `--goto=menu` | skip the studio splash |
| `--goto=intro` | jump straight into the intro cutscene |
| `--goto=game` | jump straight into Round 1 gameplay |
| `--shot=path.png --shot-frame=N` | save a screenshot at frame N, then exit |

---

## 2) Controls (GDD §4)

| action | keyboard | gamepad |
|---|---|---|
| Move | WASD / arrows | left stick |
| Jump | Space | A |
| Dive (burst dash, cooldown) | Shift | X |
| Confirm / skip | Enter / Space / E | A |
| Menu choice | ← → or 1 / 2 | d-pad / bumpers |
| Pause | Esc | Start |

---

## 3) What's in the slice

- **Level 1 "WELCOME RUN"** — pure JSON (`content/data/levels/level01.json`): start pad,
  twin conveyor belts, two rotating hammers, a hammer gauntlet, stepping stones through a
  slime slow-pool, two moving-platform ferries over a void, a pulsing headwind + crosswind
  stretch, and ramped finish plaza with prize podium and confetti crowd.
- **Player controller** — arcade run/jump/air-control/dive with coyote time, jump buffering,
  step-up ledges, moving-platform carry, forgiving respawns at checkpoints.
- **Waypoint bots (GDD §10)** — Dijkstra over a waypoint graph, personality-weighted route
  risk, honest jump-ballistics timing, platform waits/centering, stumbles, comedic bumps,
  stuck recovery. 6 rivals: NOVA, JAX, MIRA, TANK, LUXE, PIXEL (`content/data/bots.json`).
- **Elimination system (GDD §11.2)** — `TimeTrialRankCut` (bottom 20%) as pure data + code.
- **Results ceremony** — dramatic rank reveal, ELIMINATED stamps + stinger, payout breakdown.
- **Economy (GDD §6)** — BankedCash (safe) vs RiskedCash (prize pot), payout formula
  (base + placement + top-half + speed bonus), **Bank vs Risk ×2** decision, **cash-out
  offer** ($60k walk-away ending), $1,000,000 champion ending, elimination ending. `save.json` persists career stats.
- **Twists (GDD §14)** — data-driven modifiers with a **validator** (bounds, conflicts,
  round gates). The validator's caps double as difficulty floors for slowdown effects.
- **JSON cutscene player (GDD §9)** — timed beats: camera moves (lerped, smoothstepped),
  speaker subtitles, prize overlays, audio stingers, confetti events.
- **State machine (GDD §18.1)** — Boot → Menu → Intro → Gameplay → Results → BankRisk →
  CashOut → TwistReveal → Gameplay → … → SeasonEnd → Menu.
- **Update order (GDD §18.3)** — input → player → AI → platforms → hazard effects →
  collision integration → scoring/elimination → camera → UI.

---

## 4) Project layout

```
last-one-rich/
├── LastOneRich.sln
├── build.sh                        # sandbox/CLI build helper
├── tools/                          # asset generators (Python + Pillow)
│   ├── make_font.py                #   bitmap font atlas + metrics  -> content/gfx/font.*
│   ├── make_gfx.py                 #   particle / white pixel       -> content/gfx/*.png
│   └── make_audio.py               #   all SFX + music loop (WAV)   -> content/sfx/*.wav
├── content/                        # all data-driven content (copied to output, no .mgcb)
│   ├── data/
│   │   ├── season.json             # rounds, twist pools, cash-out offers, grand prize
│   │   ├── economy.json            # payout formula params
│   │   ├── bots.json               # rival names, colors, personality weights
│   │   ├── twists.json             # modifiers + validator caps
│   │   └── levels/level01.json     # geometry, hazards, spawns, waypoints, elimination rule
│   ├── cutscenes/intro.json        # timeline beats (camera/subtitle/overlay/audio/event)
│   ├── gfx/  sfx/                  # generated at build-time by tools/
└── src/
    ├── LastOneRich/
    │   ├── Core/                   # game loop, state machine, input, camera, renderer,
    │   │                           # bitmap font, audio, particles, save, JSON loader
    │   ├── World/                  # DTOs (JSON schema), physics, collision, hazards,
    │   │                           # waypoint AI, bot/player controllers, race rules, Level
    │   ├── Season/                 # SeasonRun, Wallet, payout formula, TwistValidator
    │   ├── Cine/                   # cutscene player
    │   └── States/                 # Boot, MainMenu, IntroCutscene, Gameplay, Results,
    │                               # BankRisk, CashOut, TwistReveal, SeasonEnd + HUD
    └── HeadlessSim/                # GPU-free race simulator + content validator (CI gate)
```

### Design notes (why it's built this way)
- **World layer is GPU-free.** `Actor`, `CollisionWorld`, hazards, `BotController`,
  `RaceTracker` reference no Xna Graphics types — the headless sim reuses them 1:1,
  so "bots can finish the level" is a *testable fact*, not a hope.
- **No Content Pipeline.** JSON is read with `System.Text.Json`; PNGs via
  `Texture2D.FromStream`; WAVs via `SoundEffect.FromStream`. All assets are regenerated
  placeholders — replace `content/gfx/*` and `content/sfx/*` with real art/audio later
  without touching code.
- **Collision** is custom AABB with axis-separated resolve *using the min-penetration axis*
  (this exact bug class was found and fixed via the headless harness), step-up ledges,
  ramp height-fields, platform carry.
- **All JSON is hot-editable** — change a hammer speed or slime zone and re-run; no rebuild
  needed (content is copied at build; run `dotnet build` once after edits).

---

## 5) GDD coverage map

| GDD section | where |
|---|---|
| §3.1 Story campaign (slice: 3 rounds) | `content/data/season.json` |
| §4 feel/controller | `src/LastOneRich/World/PlayerController.cs`, `Phys.cs` |
| §5 core loop | `States/*` chain |
| §6 economy/prizes/cash-out | `Season/SeasonRun.cs` (Wallet, payout), `States/BankRisk*`, `CashOut*`, `SeasonEnd*` |
| §7 characters | `content/data/bots.json` + host/co-host/announcer lines in states & cutscene |
| §8 Level 1 (+ hooks for 2–12) | `content/data/levels/level01.json` |
| §9 cutscene timelines | `content/cutscenes/intro.json`, `Cine/CutscenePlayer.cs` |
| §10 waypoint bots + personalities | `World/WaypointGraph.cs`, `World/BotController.cs` |
| §11 hazards & elimination | `World/Hazards.cs` (MovingPlatform, RotatorHammer, ConveyorZone, WindCannon, SlimeZone=IceZone-family), `World/RaceTracker.cs` |
| §12 broadcast HUD | `States/GameplayState.cs` (HUD), `ResultsState` |
| §13 data-driven content | `World/DTOs.cs`, `Core/Json.cs`, `content/data/**` |
| §14 AI-created content guardrails | `Season/TwistValidator.cs` + HeadlessSim |
| §15 audio | `Core/AudioBank.cs`, `content/sfx/*` (EDM loop, stingers, crowd) |
| §17 save/progression | `Core/SaveSystem.cs` → `%APPDATA%/LastOneRich/save.json` |
| §18 architecture | `Core/StateMachine.cs`, `Core/LorGame.cs` (update order) |

---

## 6) Roadmap from here (suggested order)

1. **Scale content, not code**: author levels 2–12 as JSON from templates; the Level loader
   already supports all needed primitives. Add `DroneScanner` + `LaserGate` + `BreakTile`
   hazard classes (they fit the existing `Hazards.cs` pattern).
2. **More elimination rules**: `ScoreRankCut`, `LastNStanding`, `StrikesOut`, `TeamCut`
   (slots already exist in `EliminationDTO.Rule`).
3. **Trivia/minigame states** for Level 7 (`questions_trivia.json` per GDD).
4. **Shop/auction intermissions** (Level 4/10) using `SpendStyle` personalities.
5. **Full 12-round arc**: extend `season.json`, add per-level cutscene JSONs, grand-finale
   "The Button" state.
6. **Replace placeholder assets**: font/UI theme, arena materials, contestant models —
   swap via Content Pipeline or keep runtime loading.

---

## 7) Known slice limitations

- Music is a 4-bar placeholder loop; host VO is text-only.
- Party Playlist & Practice modes (GDD §3.2/3.3) not started.
- Menu navigation is keyboard/gamepad only (no mouse hit-testing).
- Window resize keeps a fixed 1280×720 virtual canvas (letterboxed scaling).
