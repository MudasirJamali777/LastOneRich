# LAST ONE RICH! — Season 1

![gameplay](docs/shot_gameplay.png)

**Version 1.0.0 · release** — built with **C# / .NET 8 + MonoGame DesktopGL**.

---

## Contents

1. [The game](#1-the-game)
2. [Controls](#2-controls)
3. [How to run](#3-how-to-run)
4. [How to build a release](#4-how-to-build-a-release)
5. [The content folder](#5-the-content-folder)
6. [Achievements](#6-achievements)
7. [QA tooling](#7-qa-tooling)
8. [Credits](#8-credits)

---

## 1) The game

**LAST ONE RICH!** is a single-player third-person elimination game show. You are contestant
number 24 in the Volt Dome, a televised mega-challenge where twenty-four people walk in, one
walks out a millionaire, and the host is enjoying himself far too much.

A season is **twelve rounds across six game modes**. Every round, the field runs, dodges,
collects or survives; at the results ceremony the bottom slice of the leaderboard is
eliminated on camera. Between rounds you face the part that actually hurts: the money.

* **Earn.** Every round pays a base reward plus placement, top-half and speed bonuses.
* **Bank or Risk.** Bank your winnings and they are safe forever. Risk them and the pot
  **doubles** if you survive the next round — and vanishes completely if you do not.
* **Cash out.** After rounds 2, 4, 6 and 8 the host offers you real money to walk away now.
  Take $500,000 and go home, or chase the **$1,000,000** grand prize.
* **Twists.** Between rounds, twists are drawn from a per-round pool and validated before
  they run: faster conveyors, harder wind, turbo hammers, brittle glass, a shorter clock.
* **The Auction of Doom** (round 10) is an intermission — spend your banked cash on a strike
  shield, an extra life, a sabotage token, a golden ticket or a twist preview.
* **The Button** (round 12) is the finale: three contestants, one king-of-the-hill pad,
  last one standing is the last one rich.

### The twelve rounds

| # | round | mode | elimination |
|---|---|---|---|
| 1 | WELCOME RUN | Race (obstacle course) | TimeTrialRankCut 20% |
| 2 | SHRINKING SPOTLIGHT | SurvivalZone (the spotlight shrinks) | ScoreRankCut 25% |
| 3 | DRONE DODGE | StrikesOut (3 scans and you are out) | StrikesOut |
| 4 | PRIZE SHOP MAZE | Race through a conveyor maze | TimeTrialRankCut 8% |
| 5 | BLOCK BOOM TOWER | ScoreCollect (vault → deposit) | ScoreRankCut 8% |
| 6 | GLASS PATH MEMORY | Race over breakable glass panes | TimeTrialRankCut 8% |
| 7 | TRIVIA GATES | Race through trivia door walls | TimeTrialRankCut 8% |
| 8 | THE HEIST | ScoreCollect under swinging hammers | ScoreRankCut 8% |
| 9 | FREEZING ROOM | SurvivalZone on ice | ScoreRankCut 25% |
| 10 | THE AUCTION OF DOOM | intermission — spend your cash | — |
| 11 | MEGA GAUNTLET | Race remixing rounds 1–9 | TopNAdvance (top 3) |
| 12 | THE BUTTON | FinaleButton (king of the hill) | last one standing wins |

Round 10 is the only round with no level file: it is an intermission, and `season.json`
records its level as `"none"` on purpose.

### How it is built

Everything is data-driven and code-first. All geometry is procedural primitives with a
three-light rig and baked static meshes; all text uses a generated bitmap font; all audio is
generated WAVs. There is **no MonoGame Content Pipeline (.mgcb)**, no external editor and no
native tooling — JSON is read with `System.Text.Json`, PNGs via `Texture2D.FromStream`, WAVs
via `SoundEffect.FromStream`. Simulation runs at a **fixed 60 Hz** with render interpolation
between physics steps.

```
src/
  LastOneRich/        the game
    Core/             services: Input, AudioBank, SaveSystem, Achievements, Rng, Json,
                      BitmapFont, Camera3D, GeometryRenderer, Particles, BuildInfo, LorGame
    World/            GPU-free simulation: Level, CollisionWorld, Phys, PlayerController,
                      BotController, Hazards, BreakTiles, Modes, RaceTracker, WaypointGraph
    Season/           SeasonRun (cast, wallet, rounds), TwistValidator, MenuCast
    States/           Boot → Welcome → MainMenu → Intro → Gameplay → Results → BankRisk /
                      CashOut → TwistReveal → Auction → SeasonEnd, plus PauseMenu,
                      SettingsScreen, CreditsState, ContentErrorState
    Cine/             CutscenePlayer (tokenised JSON timelines)
  HeadlessSim/        GPU-free harness: replays all 12 rounds with the real physics/AI/rules
content/              all game data and assets (see section 5)
tools/                asset generators and the QA verifier suite (see section 7)
docs/                 screenshots
```

The **World layer references no XNA Graphics types at all**, which is what makes `HeadlessSim`
possible: it links the same assembly and simulates entire seasons with no GPU and no window,
so "the bots can actually finish this level" is a testable fact rather than a hope.

---

## 2) Controls

The camera is a **mouse-look chase camera**, and movement is **camera-relative**: your keys
produce screen-space intent which is converted to world directions using the live camera
basis, so **A is always screen-left and D is always screen-right at every camera angle**.

### Playing a round

| action | input | gamepad |
|---|---|---|
| **Look / aim the camera** | **move the mouse** | right stick |
| **Move** | **W A S D** (or arrow keys) | left stick |
| **Jump** | **Space** | A |
| **Dive** (burst dash, has a cooldown) | **Shift** (left or right) | X |
| **Pause** | **Esc** | Start |
| Debug overlay (debug builds) | F3 | — |

The mouse cursor is captured while a round is being played and released the moment you pause,
finish or alt-tab away. Mouse **sensitivity**, **invert Y** and **camera distance** are all in
Settings ▸ Controls; `cameraHeight`, `pitchMinDeg` and `pitchMaxDeg` can be hand-edited in
`content/data/controls.json`.

### Menus and ceremonies

| action | input | gamepad |
|---|---|---|
| Move the highlight | ↑ ↓ or move the mouse over a row | d-pad / left stick |
| Confirm / skip | Enter, Space, E, or left-click the highlighted row | A |
| Back / close | Esc | B |
| Binary choice (BANK vs RISK, cash-out) | ← → or 1 / 2, then Enter | d-pad / bumpers |

### Remapping

Every binding lives in `content/data/controls.json`:

```json
{
  "moveLeft":  ["A", "Left"],  "moveRight": ["D", "Right"],
  "moveForward": ["W", "Up"],  "moveBack":  ["S", "Down"],
  "jump": ["Space"],           "dive":      ["LeftShift", "RightShift"],
  "confirm": ["Enter", "Space", "E"],
  "pause": ["Escape"],         "debugOverlay": ["F3"]
}
```

Edit, save, restart. Unknown key names are ignored; delete the file to get the defaults back.

---

## 3) How to run

### Prerequisites

* [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (8.0.400 or newer)
* Visual Studio 2022 with the *.NET desktop development* workload — or just the `dotnet` CLI

### Visual Studio

1. Open `LastOneRich.sln`
2. Set **LastOneRich** as the startup project
3. Press **F5**

### Command line

```bash
dotnet run --project src/LastOneRich/LastOneRich.csproj -c Release
```

### Settings

One settings screen, reachable from the main menu and from the pause menu, with four tabs:

| tab | rows |
|---|---|
| GRAPHICS | resolution (6 modes), fullscreen, VSync, FOV 50–100°, fog |
| AUDIO | master / music / SFX volume, 0–100 |
| CONTROLS | mouse sensitivity, invert Y, camera distance |
| GAMEPLAY | difficulty — CASUAL / STANDARD / HARDCORE (affects rivals and hazards only) |

Everything is stored in `saves/settings.json`, which overlays `content/data/controls.json`
field by field at startup. Changes apply live except resolution and fullscreen, which queue
behind an *APPLY & RESTART* prompt.

If a launch applies a saved display mode and never reaches its first rendered frame, the game
detects the stale boot probe and boots **once** with the display overrides ignored, so a bad
resolution can never lock you out. Your settings file is left intact and retried next launch.

### Save data

`saves/save.json` (next to the executable) holds career money, seasons played, championships
and unlocked achievements. It is versioned, written atomically, and keeps the previous good
write as `save.json.bak` as an automatic fallback. `saves/game.log` is a best-effort runtime
log, and `saves/crash.txt` is written if the game dies unexpectedly.

### Developer launch flags

| flag | effect |
|---|---|
| `--goto=menu` \| `intro` \| `game` | skip ahead (debug builds) |
| `--round=N` | with `--goto=game`, start at season round N (1–12) |
| `--overlay` | start with the F3 debug overlay visible |
| `--shot=path.png --shot-frame=N` | save a screenshot at frame N and exit |
| `--safe` | neither read nor write `saves/settings.json` for this run |

### Headless validation harness

```bash
dotnet run --project src/HeadlessSim/HeadlessSim.csproj -c Release
```

Simulates the full 12-round season with the same physics, AI and scoring code as the game —
no GPU, no window. It prints a per-round verdict, the roster shrinking 24 → 3 and the
champion, and exits 0 only when every check passes. `--debug` adds a 5-second position trace.

---

## 4) How to build a release

Self-contained Windows x64 build (no .NET runtime needed on the target machine):

```bash
dotnet publish src/LastOneRich/LastOneRich.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64
```

The output lands in `publish/win-x64/` as `LastOneRich.exe` plus the `content/` folder, which
is copied next to the executable by the csproj. Ship the whole folder.

Before you publish, run the QA gate (see section 7) — the content validator is the cheap way
to find out that a level file went missing before a player does.

The published build reads `content/version.json`:

```json
{ "version": "1.0.0", "build": "release" }
```

Bump `version` for every build you hand to anyone. The game loads it at boot and shows it in
small grey text in the bottom-right corner of the main menu, which turns "it crashed" into
"v1.0.0 release crashed". If the file is missing or malformed the game boots exactly as
normal, logs the reason to `saves/game.log` and simply draws no stamp.

---

## 5) The content folder

Everything under `content/` is loose, hot-editable data — no rebuild step, no pipeline. It is
copied beside the executable on build and publish.

```
content/
  version.json            build stamp: { "version", "build" } — shown in the menu corner
  data/
    season.json           the 12 rounds: level id, base reward, twist pool, cash-out flags,
                          cash-out offer amounts, grand prize, cast size
    levels/
      level01..09,11,12   one JSON per playable round (round 10 is the Auction, no file):
                          id, name, type, timeLimit, parTime, killY, respawnPenalty,
                          spawns / spawnGrid, checkpoints, finish, elimination rule,
                          geometry, conveyors, slimes, winds, hammers, movers, drones,
                          iceZones, safeZone, vault, deposit, button, breakTiles, waypoints
    bots.json             the six named rivals (colour + personality weights: riskTolerance,
                          aggression, puzzleSkill, routeGreed, pace, spendStyle) plus the
                          fill-bot count and name pool
    twists.json           every twist: effects (target.param × multiplier), the maxMult bound
                          the validator enforces, minRound and conflicts
    economy.json          risk multiplier, placement and speed bonuses, auction item catalogue
    controls.json         default keybinds and mouse/camera tuning
  cutscenes/
    intro_template.json   tokenised cutscene timeline (camera beats, dialogue, orbit, sfx)
  gfx/
    font.png / font.json  the generated bitmap font and its glyph metrics
    pixel.png             1×1 white texture for all UI rectangles
    particle.png          particle sprite
  sfx/                    16 generated WAVs: music_loop, blip, cash, cheer, go, move, jump,
                          splash, hammer_hit, glass_crack / _break / _land / _reform,
                          stinger_elim, stinger_twist, stinger_win
```

Edit any of it and relaunch. `tools/validate_content.py` checks the whole folder against the
rules the game applies at load time; `tools/make_gfx.py`, `make_font.py` and `make_audio.py`
regenerate the placeholder assets.

---

## 6) Achievements

Ten Season-1 achievements, stored in `saves/save.json` and displayed in the main menu's
trophy case (earned = gold, locked = `???`). They are evaluated at the results ceremony and
at the season's decision points, never polled per frame; unlocking one persists immediately
and raises a golden toast over whatever is on screen.

| id | name | how to earn it |
|---|---|---|
| `first_steps` | FIRST STEPS | Survive your first round of the season. |
| `podium` | PODIUM FINISH | Place in the top 3 of any round. |
| `round_win` | CENTER STAGE | Win a round outright — 1st of the whole field. |
| `first_bank` | SAFE HANDS | Choose BANK at the Bank vs Risk decision. |
| `risk_taker` | DOUBLE OR NOTHING | Risk the pot and survive to collect the multiplier. |
| `money_bags` | HEIST MASTER | Deposit $100,000 or more in a single heist round. |
| `drone_ghost` | GHOST PROTOCOL | Clear DRONE DODGE without taking a single strike. |
| `glass_perfect` | FLAWLESS GLASS | Cross GLASS PATH MEMORY without breaking one tile. |
| `cash_out` | KNOW WHEN TO FOLD 'EM | Take the cash-out offer and walk away. |
| `champion` | LAST ONE RICH | Win the $1,000,000 grand prize. |

---

## 7) QA tooling

`tools/` holds the asset generators and a Python verifier suite that needs no .NET SDK. Run
all five before tagging a build; each exits 0 only when it is happy.

```bash
python3 tools/csyntax.py           # C# lexical + structural verification
python3 tools/semcheck.py          # cross-layer semantic + layer-isolation checks
python3 tools/reach.py             # state-graph and waypoint-graph reachability
python3 tools/simglass.py          # Round 6 glass course solvability simulation
python3 tools/validate_content.py  # the content/ gate — season, levels, bots, twists, audio
```

| tool | what it proves |
|---|---|
| `csyntax.py` | Every `.cs` file lexes: comments, verbatim and interpolated strings (including their code holes) and char literals all terminate, brackets balance, `#if` regions balance, one namespace per file matching its folder. |
| `semcheck.py` | The strings that wire the game together resolve: `Json.Load<T>` types and paths, level ids, sfx event names, `Action("...")` binding names, achievement ids, twist channels. Also enforces the HeadlessSim contract — `World/`, `Season/` and `HeadlessSim` touch no Graphics, Input, SaveSystem, Achievements or GameServices, and `MenuCast` never draws from the shared `Rng`. |
| `reach.py` | Every game state is constructible from `BootState`, and every level's waypoint graph routes every spawn slot to its goal with no orphan nodes. |
| `simglass.py` | Round 6 has exactly one safe pane per row, a jumpable support chain measured against `Phys.cs`, sane crack/reform timings (including under the BRITTLE GLASS twist), and is both flawless-capable and always crossable. |
| `validate_content.py` | The whole `content/` tree: season round numbering and level files, required level fields, rival personality weights in 0..1, twist multipliers inside their own bounds, every sfx event backed by a real WAV, cash-out offers matched to their rounds. |

`tools/make_gfx.py`, `tools/make_font.py` and `tools/make_audio.py` regenerate the placeholder
textures, bitmap font and WAVs.

---

## 8) Credits

**LAST ONE RICH!** — a Volt Dome mega-challenge.

| | |
|---|---|
| **Developer** | Volt Dome Studios |
| **Engine** | MonoGame DesktopGL · .NET 8 · C# |
| **Design, code, content** | Volt Dome Studios |
| **Graphics** | Every shape on screen is procedural geometry — no imported 3D models. |
| **Audio** | Every sound effect and music bed is generated — no licensed audio. |
| **Fonts** | Bitmap font generated by `tools/make_font.py`. |

Original fictional branding. Any resemblance to a real televised mega-challenge is
affectionate energy, not affiliation.

**Thanks for playing.**
