#!/usr/bin/env python3
"""
simglass.py — Round 6 GLASS PATH puzzle verifier for LAST ONE RICH.

The glass course is the one piece of content that can be *silently unwinnable*: the safe pane
in each row is chosen at load time by a seeded shuffle, so a bad seed, a hand-authored row with
no safe pane, or rows spaced further apart than a contestant can jump would all build, load and
run — and then strand the entire field over a pit.

This tool rebuilds the course the way World/Level.BuildBreakTiles does, including a faithful
port of .NET's seeded System.Random (the Net5-compat subtractive generator that Random(int)
still uses on .NET 8), and then checks:

  1. STRUCTURE     every pane declares pos/size/row; rows are contiguous from 0
  2. CHOICE        every row offers at least 2 panes (a row of 1 is not a decision)
  3. SOLVABILITY   every row ends up with exactly one safe pane
                   (authored rows use their `safe` flags, with Level.cs's
                    "never ship a row nobody can cross" fallback applied)
  4. GEOMETRY      the course is a jumpable chain of supports. Panes inside a row must tile
                   into one continuous strip (so lateral movement along a row is free), and
                   every consecutive pair of supports — start platform, glass rows, the solid
                   mid-course ledges, the landing platform — must be separated by an EDGE-TO-EDGE
                   z gap the arcade jump can clear, with overlapping x ranges.
  5. TIMING        crackTime leaves a usable reaction window (also under the BRITTLE GLASS
                   twist), and reformTime is non-zero on a course whose opening stampede would
                   otherwise permanently destroy row 0 (the regression the Priority 6 notes describe)
  6. CROSSING SIM  a contestant with perfect knowledge crosses with 0 panes broken and inside
                   the level's time limit (so FLAWLESS GLASS is actually obtainable), and a
                   contestant with NO prior knowledge who merely remembers the panes that
                   already broke under them always eventually crosses.

Jump reach, walk speed, respawn penalty and checkpoint positions are all taken from
World/Phys.cs and the level JSON rather than guessed, so the numbers below mean something.

Exit code 0 = clean, 1 = at least one failure.

Usage:  python3 tools/simglass.py [--level level06] [--trials 400]
"""

import json
import os
import sys
from collections import defaultdict

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CONTENT = os.path.join(ROOT, "content")

INT_MAX = 2147483647

# --- constants mirrored from World/Phys.cs ---------------------------------------------------
MAX_SPEED = 10.0        # Phys.MaxSpeed
JUMP_VEL = 9.2          # Phys.JumpVel
GRAVITY = 25.0          # Phys.Gravity
AIR_TIME = 2.0 * JUMP_VEL / GRAVITY              # 0.736s hang time
JUMP_RANGE = MAX_SPEED * AIR_TIME                # 7.36u of flat ground cleared at full speed
SAFE_JUMP_GAP = JUMP_RANGE * 0.6                 # what a contestant clears *comfortably*
CRUISE_SPEED = MAX_SPEED * 0.85                  # realistic average, not a perfect-line sprint
TOP_SURFACE_MIN_Y = -1.5                         # ignore the cosmetic pit slab far below

MIN_CRACK_TIME = 0.25   # below this a fake pane drops faster than a player can react
MAX_CRACK_TIME = 1.50   # above this the puzzle stops punishing a wrong guess at all


class NetRandom:
    """Faithful port of .NET's seeded System.Random (Net5CompatSeedImpl subtractive generator).

    Used so the safe-pane layout this tool reports is byte-identical to the one the game builds
    from the same glassSeed. The pass/fail invariants below never depend on it — they hold for
    *any* RNG — so a future .NET change can only make the printed layout stale, never the verdict.
    """

    def __init__(self, seed):
        self._seed_array = [0] * 56
        subtraction = INT_MAX if seed == -2147483648 else abs(seed)
        mj = 161803398 - subtraction
        self._seed_array[55] = mj
        mk = 1
        ii = 0
        for i in range(1, 55):
            ii += 21
            if ii >= 55:
                ii -= 55
            self._seed_array[ii] = mk
            mk = mj - mk
            if mk < 0:
                mk += INT_MAX
            mj = self._seed_array[ii]
        for _ in range(1, 5):
            for i in range(1, 56):
                n = i + 30
                if n >= 55:
                    n -= 55
                self._seed_array[i] -= self._seed_array[1 + n]
                if self._seed_array[i] < 0:
                    self._seed_array[i] += INT_MAX
        self._inext = 0
        self._inextp = 21

    def _sample(self):
        i, p = self._inext + 1, self._inextp + 1
        if i >= 56:
            i = 1
        if p >= 56:
            p = 1
        val = self._seed_array[i] - self._seed_array[p]
        if val == INT_MAX:
            val -= 1
        if val < 0:
            val += INT_MAX
        self._seed_array[i] = val
        self._inext, self._inextp = i, p
        return val

    def next(self, max_exclusive):
        return int(self._sample() * (1.0 / INT_MAX) * max_exclusive)

    def next_double(self):
        return self._sample() * (1.0 / INT_MAX)


def build_safe_flags(tiles, seed):
    """Mirror of World/Level.BuildBreakTiles."""
    rows = defaultdict(list)
    for i, t in enumerate(tiles):
        rows[t.get("row", 0)].append(i)

    safe = [False] * len(tiles)
    rng = NetRandom(seed)
    for row in sorted(rows):
        idx = rows[row]
        authored = any(tiles[i].get("safe") is not None for i in idx)
        if authored:
            for i in idx:
                safe[i] = bool(tiles[i].get("safe") or False)
            if not any(safe[i] for i in idx):
                safe[idx[0]] = True        # Level.cs's "never ship an uncrossable row" fallback
        else:
            safe[idx[rng.next(len(idx))]] = True
    return rows, safe


def dist_xz(a, b):
    return ((a[0] - b[0]) ** 2 + (a[2] - b[2]) ** 2) ** 0.5


def main():
    args = sys.argv[1:]
    level_id = "level06"
    trials = 400
    if "--level" in args:
        level_id = args[args.index("--level") + 1]
    if "--trials" in args:
        trials = int(args[args.index("--trials") + 1])

    path = os.path.join(CONTENT, "data", "levels", level_id + ".json")
    if not os.path.exists(path):
        print(f"simglass: FAIL — content/data/levels/{level_id}.json not found")
        return 1
    dto = json.load(open(path, encoding="utf-8"))
    tiles = dto.get("breakTiles") or []
    if not tiles:
        print(f"simglass: FAIL — {level_id} declares no breakTiles")
        return 1

    fail, notes = [], []
    seed = int(dto.get("glassSeed", 6061))

    # ---------------------------------------------------------------- 1 structure
    for i, t in enumerate(tiles):
        if not t.get("pos") or len(t["pos"]) != 3:
            fail.append(f"pane {i}: missing/!=3 pos")
        if t.get("size") and len(t["size"]) != 3:
            fail.append(f"pane {i}: size must have 3 components")
        if "row" not in t:
            fail.append(f"pane {i}: no row — it would silently join row 0")

    rows, safe = build_safe_flags(tiles, seed)
    row_keys = sorted(rows)
    if row_keys != list(range(len(row_keys))):
        fail.append(f"rows are not contiguous from 0: {row_keys}")

    print(f"simglass: {level_id} — {len(tiles)} panes over {len(rows)} rows, glassSeed {seed}")

    # ------------------------------------------------------- 2/3 choice + solvability
    for row in row_keys:
        idx = rows[row]
        if len(idx) < 2:
            fail.append(f"row {row}: only {len(idx)} pane(s) — no choice to make")
        n_safe = sum(1 for i in idx if safe[i])
        if n_safe != 1:
            fail.append(f"row {row}: {n_safe} safe panes (exactly 1 required)")

    safe_path = [tiles[next(i for i in rows[r] if safe[i])] for r in row_keys]
    lane = {}
    for r in row_keys:
        xs = sorted(tiles[i]["pos"][0] for i in rows[r])
        s = tiles[next(i for i in rows[r] if safe[i])]["pos"][0]
        lane[r] = xs.index(s)
    print("  safe lane per row: " + " ".join(f"{r}:{lane[r]}" for r in row_keys))

    # ---------------------------------------------------------------- 4 geometry
    # 4a: the panes of one row must tile into a single continuous strip, otherwise a contestant
    #     who guesses the wrong lane cannot simply walk sideways to the safe one.
    supports = []      # (label, zmin, zmax, xmin, xmax)
    for row in row_keys:
        panes = sorted((tiles[i] for i in rows[row]), key=lambda t: t["pos"][0])
        spans = []
        for t in panes:
            sz = t.get("size") or [3.2, 0.5, 3.6]
            spans.append((t["pos"][0] - sz[0] * 0.5, t["pos"][0] + sz[0] * 0.5))
        for (a0, a1), (b0, b1) in zip(spans, spans[1:]):
            if b0 > a1 + 0.01:
                fail.append(f"row {row}: panes leave a {b0 - a1:.2f}u lateral hole at x={a1:.2f} "
                            f"— a wrong lane cannot be walked out of")
        sz = (panes[0].get("size") or [3.2, 0.5, 3.6])
        z = panes[0]["pos"][2]
        supports.append((f"row{row}", z - sz[2] * 0.5, z + sz[2] * 0.5,
                         spans[0][0], spans[-1][1]))

    # 4b: solid static geometry counts as a support too (start platform, mid-course ledges,
    #     the landing platform in front of the finish line).
    for g in (dto.get("geometry") or []):
        if g.get("collide") is False:
            continue
        pos, size = g["pos"], g["size"]
        if pos[1] + size[1] * 0.5 < TOP_SURFACE_MIN_Y:
            continue
        supports.append((g.get("kind", "box"), pos[2] - size[2] * 0.5, pos[2] + size[2] * 0.5,
                         pos[0] - size[0] * 0.5, pos[0] + size[0] * 0.5))

    supports.sort(key=lambda s: s[1])
    worst_gap = 0.0
    for a, b in zip(supports, supports[1:]):
        gap = b[1] - a[2]
        if gap <= 0:
            continue                            # overlapping / touching supports
        worst_gap = max(worst_gap, gap)
        if gap > SAFE_JUMP_GAP:
            fail.append(f"gap of {gap:.2f}u between {a[0]} (ends z={a[2]:.2f}) and {b[0]} "
                        f"(starts z={b[1]:.2f}) exceeds the {SAFE_JUMP_GAP:.2f}u comfortable jump")
        if min(a[4], b[4]) - max(a[3], b[3]) <= 0:
            fail.append(f"{a[0]} and {b[0]} do not overlap in x — no landing lane")
    print(f"  support chain: {len(supports)} platforms/rows, widest gap {worst_gap:.2f}u "
          f"(jump clears {JUMP_RANGE:.2f}u, comfortable limit {SAFE_JUMP_GAP:.2f}u)")

    finish = (dto.get("finish") or {}).get("pos")
    if finish:
        last = supports[-1]
        if not (last[1] - 0.01 <= finish[2] <= last[2] + 0.01):
            fail.append(f"the finish line at z={finish[2]} does not sit on the final support "
                        f"{last[0]} (z {last[1]:.2f}..{last[2]:.2f})")
        else:
            print(f"  finish z={finish[2]} sits on {last[0]} (z {last[1]:.2f}..{last[2]:.2f})")

    # ---------------------------------------------------------------- 5 timing
    for i, t in enumerate(tiles):
        ct = float(t.get("crackTime", 0.45))
        if ct < MIN_CRACK_TIME:
            fail.append(f"pane {i}: crackTime {ct}s is below the {MIN_CRACK_TIME}s reaction floor")
        if ct > MAX_CRACK_TIME:
            fail.append(f"pane {i}: crackTime {ct}s is above the {MAX_CRACK_TIME}s ceiling — "
                        f"a wrong guess stops costing anything")
    reforms = {float(t.get("reformTime", 0) or 0) for t in tiles}
    if reforms == {0.0}:
        fail.append("no pane reforms (reformTime 0 everywhere): the opening stampede permanently "
                    "destroys row 0 and the course becomes unfinishable for anyone who respawns")
    print(f"  crackTime {min(float(t.get('crackTime', 0.45)) for t in tiles)}"
          f"–{max(float(t.get('crackTime', 0.45)) for t in tiles)}s, "
          f"reformTime {sorted(reforms)}")

    # ------------------------------------------------- 5b twist-worst-case crack time
    twists = json.load(open(os.path.join(CONTENT, "data", "twists.json"), encoding="utf-8"))
    glass_mult = 1.0
    for t in twists["twists"]:
        for e in (t.get("effects") or []):
            if e["target"] == "glass" and e["param"] == "crackTime":
                glass_mult = min(glass_mult, float(e["mult"]))
    worst = min(float(t.get("crackTime", 0.45)) for t in tiles) * glass_mult
    if worst < MIN_CRACK_TIME:
        fail.append(f"with the BRITTLE GLASS twist (x{glass_mult}) crackTime drops to {worst:.3f}s, "
                    f"below the {MIN_CRACK_TIME}s reaction floor")
    print(f"  worst case with BRITTLE GLASS (x{glass_mult}): {worst:.3f}s")

    # ---------------------------------------------------------------- 6 crossing sim
    time_limit = float(dto.get("timeLimit", 120))
    penalty = float(dto.get("respawnPenalty", 3))
    checkpoints = sorted(c[2] for c in (dto.get("checkpoints") or [[0, 0, 0]]))
    course_end = supports[-1][2]
    course_len = course_end - supports[0][1]

    # perfect knowledge: walk the safe lane, never break a pane
    perfect_time = course_len / CRUISE_SPEED
    print(f"  perfect-knowledge crossing: 0 panes broken, ~{perfect_time:.1f}s over "
          f"{course_len:.1f}u (limit {time_limit:.0f}s) -> FLAWLESS GLASS obtainable")
    if perfect_time > time_limit:
        fail.append(f"even a flawless run needs ~{perfect_time:.1f}s but timeLimit is "
                    f"{time_limit:.0f}s — nobody can finish this round")

    # no prior knowledge, but the contestant remembers the panes that broke under them
    # (and a fall costs respawnPenalty plus the walk back from the last checkpoint reached).
    rng = NetRandom(seed ^ 0x5EED)
    finished = 0
    tot_breaks = tot_time = 0.0
    for _ in range(trials):
        known_bad = {r: set() for r in row_keys}
        row, breaks, t, cp = 0, 0, 0.0, checkpoints[0]
        guard = 0
        while row < len(row_keys) and guard < 500:
            guard += 1
            z = tiles[rows[row_keys[row]][0]]["pos"][2]
            for c in checkpoints:
                if c <= z:
                    cp = c
            options = [i for i in rows[row_keys[row]] if i not in known_bad[row_keys[row]]]
            pick = options[rng.next(len(options))]
            t += 5.5 / CRUISE_SPEED
            if safe[pick]:
                row += 1
            else:
                known_bad[row_keys[row]].add(pick)
                breaks += 1
                t += penalty + abs(z - cp) / CRUISE_SPEED
        if row >= len(row_keys):
            finished += 1
        tot_breaks += breaks
        tot_time += t
    rate = finished / trials
    print(f"  blind-but-remembering sim: {trials} runs, {rate * 100:.0f}% cross, "
          f"{tot_breaks / trials:.1f} panes broken and ~{tot_time / trials:.0f}s per run")
    if rate < 1.0:
        fail.append(f"a remembering contestant fails to cross in {(1 - rate) * 100:.0f}% of runs "
                    f"— the course is not solvable by elimination alone")
    if tot_time / trials > time_limit:
        notes.append(f"a first-time blind crossing averages ~{tot_time / trials:.0f}s against a "
                     f"{time_limit:.0f}s limit — intended: Round 6 is a rank cut (bottom "
                     f"{dto['elimination']['percent']}%), not a finish-or-die round, and bots "
                     f"share proven-safe panes so the field converges on the route")

    print()
    for n in notes:
        print(f"  note: {n}")
    if fail:
        print(f"simglass: {len(fail)} PROBLEM(S)")
        for f in fail:
            print(f"  - {f}")
        return 1
    print("simglass: GLASS COURSE VALID — solvable, flawless-capable, survivable")
    return 0


if __name__ == "__main__":
    sys.exit(main())
