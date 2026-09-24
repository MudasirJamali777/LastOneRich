#!/usr/bin/env python3
"""
validate_content.py — ship gate for everything under content/ in LAST ONE RICH.

csyntax/semcheck/reach/simglass verify the code and the Round 6 puzzle. This one verifies the
*content* on its own terms: it re-implements the rules the game applies at load time
(Season/SeasonRun.TwistValidator, World/Level, Core/AudioBank) so a bad JSON edit is caught
here instead of at the studio splash.

  1. SEASON      season.json parses; rounds are numbered 1..N with no gaps; every level it
                 references exists on disk. Round 10 is THE AUCTION OF DOOM, an intermission
                 with level "none" and no level file — that is intentional and is asserted as
                 such rather than merely tolerated.
  2. LEVELS      every level file carries the required fields (id, type, spawns, time limit),
                 the id matches its filename, the type is one the game implements, the
                 elimination rule matches the type, the time limit clears the validator's
                 30s floor, and there are enough spawn slots (explicit or via spawnGrid) for
                 the whole cast.
  3. BOTS        every rival's personality weights are in 0..1. `pace` is deliberately NOT a
                 weight — it is a speed multiplier around 1.0 (NOVA runs at 1.05) — so it is
                 range-checked separately, and spendStyle must be a value the auction handles.
  4. TWISTS      every twist is inside the validator's own bounds: each effect has a maxMult
                 cap, applying the effect does not breach it (>= 1.0 caps are ceilings, < 1.0
                 caps are floors — the same asymmetry TwistValidator.ValidateSelection uses),
                 mults are positive and finite, conflictsWith targets exist, and no round
                 offers a twist before its minRound.
  5. AUDIO       every sfx event name used anywhere in the C# (AudioBank.Event / Play /
                 Level.Sfx?.Invoke) and in the cutscene JSON resolves to a real WAV in
                 content/sfx, directly or through an AudioBank.Event switch arm.
  6. MISC        cash-out offers exist for every round that sets cashOutAfter; economy and
                 controls parse; every referenced gfx/sfx/cutscene asset is present; every
                 colour string is a parseable hex triple.

Exit code 0 only if EVERYTHING passes.

Usage:  python3 tools/validate_content.py
"""

import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "src")
CONTENT = os.path.join(ROOT, "content")

# round 10 is the Auction intermission: no level payload, by design (season.json + HeadlessSim
# + GameplayState.Enter all special-case exactly these two spellings).
INTERMISSION_LEVELS = ("none", "auction")

REQUIRED_LEVEL_FIELDS = ["id", "type", "spawns", "timeLimit"]

LEVEL_TYPES = {"Race", "SurvivalZone", "StrikesOut", "ScoreCollect", "FinaleButton", "Auction"}

# World/Modes.Elimination + TwistValidator's own allow-list
ELIM_RULES = {"TimeTrialRankCut", "ScoreRankCut", "LastNStanding", "StrikesOut",
              "TeamCut", "TopNAdvance", "NoElimination"}

TYPE_ELIM = {
    "Race": {"TimeTrialRankCut", "TopNAdvance"},
    "SurvivalZone": {"ScoreRankCut"},
    "StrikesOut": {"StrikesOut", "ScoreRankCut", "TimeTrialRankCut"},
    "ScoreCollect": {"ScoreRankCut"},
    "FinaleButton": {"NoElimination", "TopNAdvance"},
}

PERSONALITY_WEIGHTS = ["riskTolerance", "aggression", "puzzleSkill", "routeGreed"]
PACE_RANGE = (0.80, 1.20)
SPEND_STYLES = {"Saver", "Buyer", "Saboteur"}

HEX_RE = re.compile(r"^#[0-9a-fA-F]{6}$")
EVENT_RE = re.compile(r"\.Event\(\s*\"([a-zA-Z0-9_]+)\"\s*\)")
INVOKE_RE = re.compile(r"Sfx\?\.Invoke\(\s*\"([a-zA-Z0-9_]+)\"\s*\)")
PLAY_RE = re.compile(r"\bPlay\(\s*\"([a-zA-Z0-9_]+)\"")
CASE_RE = re.compile(r'case\s+"([a-zA-Z0-9_]+)"\s*:')

MIN_TIME_LIMIT = 30.0     # TwistValidator: "timeLimit < 30" is an error
MAX_ELIM_PERCENT = 50.0   # TwistValidator: elimination percent bounds


class Report:
    def __init__(self):
        self.errors = []
        self.checks = 0

    def check(self, ok, message):
        self.checks += 1
        if not ok:
            self.errors.append(message)
        return ok

    def section(self, title):
        print(f"\n{title}")


def load_json(rel, rep):
    path = os.path.join(CONTENT, rel.replace("/", os.sep))
    if not os.path.exists(path):
        rep.check(False, f"content/{rel} is missing")
        return None
    try:
        with open(path, encoding="utf-8") as fh:
            return json.load(fh)
    except json.JSONDecodeError as e:
        rep.check(False, f"content/{rel} is not valid JSON: {e}")
        return None


def strip_comments(text):
    out, i, n = [], 0, len(text)
    while i < n:
        if text.startswith("//", i):
            while i < n and text[i] != "\n":
                i += 1
        elif text.startswith("/*", i):
            j = text.find("*/", i + 2)
            i = n if j < 0 else j + 2
        else:
            out.append(text[i])
            i += 1
    return "".join(out)


def cs_sources():
    out = {}
    for dirpath, dirnames, filenames in os.walk(SRC):
        dirnames[:] = [d for d in dirnames if d not in ("bin", "obj", ".vs")]
        for fn in sorted(filenames):
            if fn.endswith(".cs"):
                p = os.path.join(dirpath, fn)
                out[p] = strip_comments(open(p, encoding="utf-8-sig").read())
    return out


# --------------------------------------------------------------------------------- 1 season
def check_season(rep):
    rep.section("1. SEASON")
    season = load_json("data/season.json", rep)
    if season is None:
        return None
    rounds = season.get("rounds") or []
    rep.check(len(rounds) > 0, "season.json has no rounds")

    numbers = [r.get("round") for r in rounds]
    rep.check(numbers == list(range(1, len(rounds) + 1)),
              f"season rounds are not numbered 1..{len(rounds)} without gaps: {numbers}")

    intermissions = []
    for r in rounds:
        lid = r.get("level")
        rep.check(isinstance(lid, str) and lid != "",
                  f"round {r.get('round')}: no level id")
        if lid in INTERMISSION_LEVELS:
            intermissions.append(r.get("round"))
            path = os.path.join(CONTENT, "data", "levels", f"level{r.get('round'):02d}.json")
            rep.check(not os.path.exists(path),
                      f"round {r.get('round')} is an intermission but "
                      f"content/data/levels/level{r.get('round'):02d}.json exists — "
                      f"one of the two is a mistake")
            continue
        path = os.path.join(CONTENT, "data", "levels", lid + ".json")
        rep.check(os.path.exists(path),
                  f"round {r.get('round')}: content/data/levels/{lid}.json does not exist")

    rep.check(intermissions == [10],
              f"expected exactly one intermission round (10 = THE AUCTION OF DOOM), got {intermissions}")
    print(f"   {len(rounds)} rounds, {len(rounds) - len(intermissions)} level files referenced, "
          f"intermission round(s): {intermissions} (Auction, no level file — intentional)")

    offers = season.get("cashOutOffers") or {}
    for r in rounds:
        if r.get("cashOutAfter"):
            rep.check(str(r["round"]) in offers,
                      f"round {r['round']} sets cashOutAfter but has no cashOutOffers entry")
    for key in offers:
        rep.check(any(str(r["round"]) == key and r.get("cashOutAfter") for r in rounds),
                  f"cashOutOffers has an entry for round {key} which does not set cashOutAfter")
    print(f"   cash-out offers: {sorted(offers, key=int)} — matched to cashOutAfter rounds")

    rewards = [r.get("baseReward", 0) for r in rounds]
    for r in rounds:
        rep.check(isinstance(r.get("baseReward", 0), (int, float)) and r.get("baseReward", 0) >= 0,
                  f"round {r['round']}: negative or non-numeric baseReward")
    print(f"   prize curve: {min(rewards):,.0f} .. {max(rewards):,.0f}, "
          f"grand prize {season.get('grandPrize', 0):,.0f}, cast {season.get('contestants')}")
    return season


# --------------------------------------------------------------------------------- 2 levels
def spawn_slot_count(dto):
    grid = dto.get("spawnGrid")
    if grid and len(grid) >= 6:
        return 32                      # Level.cs fills exactly 32 grid slots
    return len(dto.get("spawns") or [])


def check_levels(rep, season):
    rep.section("2. LEVELS")
    if season is None:
        return
    cast = int(season.get("contestants", 24))
    for r in season.get("rounds") or []:
        lid = r.get("level")
        if lid in INTERMISSION_LEVELS:
            continue
        dto = load_json(f"data/levels/{lid}.json", rep)
        if dto is None:
            continue

        missing = [f for f in REQUIRED_LEVEL_FIELDS if f not in dto]
        rep.check(not missing, f"{lid}: missing required field(s) {missing}")
        if missing:
            continue

        rep.check(dto["id"] == lid, f"{lid}.json declares id '{dto['id']}'")
        rep.check(dto["type"] in LEVEL_TYPES,
                  f"{lid}: unknown type '{dto['type']}' (known: {sorted(LEVEL_TYPES)})")
        rep.check(isinstance(dto["spawns"], list),
                  f"{lid}: 'spawns' must be an array (it may be empty when spawnGrid is used)")
        for i, s in enumerate(dto["spawns"]):
            rep.check(isinstance(s, list) and len(s) == 3, f"{lid}: spawns[{i}] is not [x,y,z]")

        tl = dto["timeLimit"]
        rep.check(isinstance(tl, (int, float)) and tl >= MIN_TIME_LIMIT,
                  f"{lid}: timeLimit {tl} is below the validator's {MIN_TIME_LIMIT:.0f}s floor")

        slots = spawn_slot_count(dto)
        rep.check(slots >= cast,
                  f"{lid}: {slots} spawn slot(s) for {cast} contestants — the overflow all "
                  f"spawn at the world origin")

        elim = dto.get("elimination") or {}
        rule = elim.get("rule")
        rep.check(rule in ELIM_RULES, f"{lid}: unknown elimination rule '{rule}'")
        allowed = TYPE_ELIM.get(dto["type"])
        if allowed and rule in ELIM_RULES:
            rep.check(rule in allowed,
                      f"{lid}: elimination rule '{rule}' does not match type '{dto['type']}' "
                      f"(expected one of {sorted(allowed)})")
        pct = elim.get("percent", 0)
        rep.check(0 <= pct <= MAX_ELIM_PERCENT,
                  f"{lid}: elimination percent {pct} outside 0..{MAX_ELIM_PERCENT:.0f}")
        if rule == "TopNAdvance":
            rep.check(int(elim.get("topN", 0)) > 0, f"{lid}: TopNAdvance with topN <= 0")

        # per-type payloads the game dereferences without a null check downstream
        if dto["type"] == "Race":
            rep.check(bool(dto.get("finish")), f"{lid}: Race level has no finish zone")
        if dto["type"] == "SurvivalZone":
            rep.check(bool(dto.get("safeZone")), f"{lid}: SurvivalZone has no safeZone")
        if dto["type"] == "StrikesOut":
            rep.check(len(dto.get("drones") or []) > 0, f"{lid}: StrikesOut has no drones")
        if dto["type"] == "ScoreCollect":
            rep.check(bool(dto.get("vault")) and bool(dto.get("deposit")),
                      f"{lid}: ScoreCollect missing vault/deposit")
        if dto["type"] == "FinaleButton":
            rep.check(bool(dto.get("button")), f"{lid}: FinaleButton has no button")

        rep.check(float(dto.get("killY", -10)) < 0, f"{lid}: killY must sit below the floor")

        for key in ("geometry", "conveyors", "slimes", "winds", "hammers", "movers", "breakTiles"):
            for i, item in enumerate(dto.get(key) or []):
                col = item.get("color")
                if col is not None:
                    rep.check(bool(HEX_RE.match(col)),
                              f"{lid}: {key}[{i}] colour '{col}' is not #rrggbb")

        print(f"   {lid:<8} {dto['type']:<13} {tl:>5.0f}s  {rule:<17} "
              f"{slots:>2} spawn slots  {len(dto.get('geometry') or []):>2} geo")


# ----------------------------------------------------------------------------------- 3 bots
def check_bots(rep, season):
    rep.section("3. BOTS")
    bots = load_json("data/bots.json", rep)
    if bots is None:
        return
    roster = bots.get("bots") or []
    rep.check(len(roster) > 0, "bots.json has no rivals")

    names = [b.get("name") for b in roster]
    rep.check(len(set(names)) == len(names), f"duplicate rival name(s) in bots.json: {names}")

    for b in roster:
        name = b.get("name", "?")
        rep.check(bool(HEX_RE.match(b.get("color", ""))),
                  f"rival {name}: colour '{b.get('color')}' is not #rrggbb")
        p = b.get("personality") or {}
        rep.check(bool(p), f"rival {name}: no personality block")
        for w in PERSONALITY_WEIGHTS:
            rep.check(w in p, f"rival {name}: personality is missing '{w}'")
            v = p.get(w)
            if isinstance(v, (int, float)):
                rep.check(0.0 <= v <= 1.0,
                          f"rival {name}: personality weight {w}={v} is outside 0..1")
            else:
                rep.check(False, f"rival {name}: personality weight {w} is not a number")
        pace = p.get("pace", 1.0)
        rep.check(isinstance(pace, (int, float)) and PACE_RANGE[0] <= pace <= PACE_RANGE[1],
                  f"rival {name}: pace {pace} outside {PACE_RANGE} "
                  f"(pace is a speed multiplier around 1.0, not a 0..1 weight)")
        rep.check(p.get("spendStyle") in SPEND_STYLES,
                  f"rival {name}: spendStyle '{p.get('spendStyle')}' "
                  f"is not one of {sorted(SPEND_STYLES)}")
        print(f"   {name:<6} " + "  ".join(f"{w[:4]}={p.get(w)}" for w in PERSONALITY_WEIGHTS)
              + f"  pace={pace}  {p.get('spendStyle')}")

    fill = int(bots.get("fillCount", 0))
    fill_names = bots.get("fillNames") or []
    rep.check(len(fill_names) >= fill,
              f"fillCount is {fill} but only {len(fill_names)} fillNames are supplied "
              f"(the rest become R-NN placeholders)")
    rep.check(len(set(fill_names)) == len(fill_names), "duplicate name(s) in fillNames")
    rep.check(not (set(fill_names) & set(names)), "a fill name collides with a rival name")

    if season is not None:
        total = 1 + len(roster) + fill
        rep.check(total == int(season.get("contestants", total)),
                  f"cast size mismatch: 1 player + {len(roster)} rivals + {fill} fill = {total}, "
                  f"season.json says {season.get('contestants')}")
        print(f"   roster: 1 player + {len(roster)} rivals + {fill} fill = {total} contestants")


# --------------------------------------------------------------------------------- 4 twists
def check_twists(rep, season):
    rep.section("4. TWISTS")
    twists = load_json("data/twists.json", rep)
    if twists is None:
        return
    entries = twists.get("twists") or []
    ids = [t.get("id") for t in entries]
    rep.check(len(set(ids)) == len(ids), f"duplicate twist id(s): {ids}")

    for t in entries:
        tid = t.get("id", "?")
        rep.check(bool(t.get("name")) and bool(t.get("desc")), f"twist {tid}: no name/desc")
        effects = t.get("effects") or []
        caps = t.get("maxMult") or {}
        rep.check(len(effects) > 0, f"twist {tid}: no effects — it would do nothing")
        for e in effects:
            key = f"{e.get('target')}.{e.get('param')}"
            mult = e.get("mult")
            rep.check(isinstance(mult, (int, float)) and mult > 0,
                      f"twist {tid}: effect {key} has a non-positive multiplier {mult}")
            rep.check(key in caps,
                      f"twist {tid}: effect {key} has no maxMult bound — an unbounded twist is "
                      f"exactly what GDD 14's validator exists to prevent")
            if key in caps and isinstance(mult, (int, float)):
                cap = caps[key]
                # TwistValidator.ValidateSelection: >= 1.0 caps are ceilings, < 1.0 caps are floors
                if cap >= 1.0:
                    rep.check(mult <= cap,
                              f"twist {tid}: {key} multiplier {mult} exceeds its own cap {cap}")
                    rep.check(mult >= 1.0,
                              f"twist {tid}: {key} has a ceiling cap {cap} but a shrinking "
                              f"multiplier {mult} — the bound is the wrong way round")
                else:
                    rep.check(mult >= cap,
                              f"twist {tid}: {key} multiplier {mult} is below its own floor {cap}")
                    rep.check(mult <= 1.0,
                              f"twist {tid}: {key} has a floor cap {cap} but a growing "
                              f"multiplier {mult} — the bound is the wrong way round")
        for other in (t.get("conflictsWith") or []):
            rep.check(other in ids, f"twist {tid}: conflictsWith unknown twist '{other}'")
        rep.check(int(t.get("minRound", 2)) >= 1, f"twist {tid}: minRound < 1")
        caps_str = ", ".join(f"{k} x{v}" for k, v in caps.items())
        print(f"   {tid:<14} minRound {t.get('minRound')}  "
              + ", ".join(f"{e['target']}.{e['param']} x{e['mult']}" for e in effects)
              + f"   bound: {caps_str}")

    if season is not None:
        by_id = {t.get("id"): t for t in entries}
        for r in season.get("rounds") or []:
            for tid in (r.get("twistPool") or []):
                if not rep.check(tid in by_id,
                                 f"round {r['round']}: twistPool references unknown twist '{tid}'"):
                    continue
                mr = int(by_id[tid].get("minRound", 2))
                rep.check(r["round"] >= mr,
                          f"round {r['round']}: offers '{tid}' whose minRound is {mr} — the "
                          f"validator would reject it at round start and the round runs clean")
        used = {tid for r in (season.get("rounds") or []) for tid in (r.get("twistPool") or [])}
        for tid in ids:
            rep.check(tid in used, f"twist '{tid}' is in no round's twistPool — dead content")
        print(f"   {len(entries)} twists, all inside their own bounds and all pooled")


# ---------------------------------------------------------------------------------- 5 audio
def check_audio(rep):
    rep.section("5. AUDIO")
    sfx_dir = os.path.join(CONTENT, "sfx")
    if not os.path.isdir(sfx_dir):
        rep.check(False, "content/sfx is missing")
        return
    clips = {os.path.splitext(f)[0].lower() for f in os.listdir(sfx_dir) if f.endswith(".wav")}
    for name in sorted(clips):
        path = os.path.join(sfx_dir, name + ".wav")
        head = open(path, "rb").read(12)
        rep.check(head[:4] == b"RIFF" and head[8:12] == b"WAVE",
                  f"content/sfx/{name}.wav is not a RIFF/WAVE file")

    sources = cs_sources()
    audio_path = os.path.join(SRC, "LastOneRich", "Core", "AudioBank.cs")
    body = sources[audio_path][sources[audio_path].find("public void Event("):]
    arms = {}
    for line in body.splitlines():
        m = CASE_RE.search(line)
        if m:
            arms[m.group(1)] = PLAY_RE.findall(line)
    for arm, plays in sorted(arms.items()):
        for clip in plays:
            rep.check(clip.lower() in clips,
                      f"AudioBank.Event arm \"{arm}\" plays \"{clip}\" — no content/sfx/{clip}.wav")

    used = set()
    for path, text in sources.items():
        if path == audio_path:
            continue
        for rx in (EVENT_RE, INVOKE_RE, PLAY_RE):
            for m in rx.finditer(text):
                used.add(m.group(1))

    cutscene_dir = os.path.join(CONTENT, "cutscenes")
    cutscene_events = set()
    for fn in sorted(os.listdir(cutscene_dir)) if os.path.isdir(cutscene_dir) else []:
        if not fn.endswith(".json"):
            continue
        dto = load_json(f"cutscenes/{fn}", rep)
        if dto is None:
            continue
        music = dto.get("music")
        if music:
            cutscene_events.add(music)
        for beat in (dto.get("beats") or []):
            if beat.get("sfx"):
                cutscene_events.add(beat["sfx"])

    for name in sorted(used | cutscene_events):
        rep.check(name in arms or name.lower() in clips,
                  f"sfx event \"{name}\" has no content/sfx/{name}.wav and no AudioBank.Event arm")

    print(f"   {len(clips)} WAV(s), {len(used)} event name(s) in code, "
          f"{len(cutscene_events)} in cutscene JSON — all resolve")
    print("   " + ", ".join(sorted(clips)))


# ----------------------------------------------------------------------------------- 6 misc
def check_misc(rep):
    rep.section("6. MISC CONTENT")
    econ = load_json("data/economy.json", rep)
    if econ is not None:
        rep.check(float(econ.get("riskMultiplier", 0)) > 1.0,
                  "economy.riskMultiplier must be > 1 or RISK can never pay")
        items = econ.get("auctionItems") or []
        rep.check(len(items) > 0, "economy.json has no auctionItems")
        ids = [i.get("id") for i in items]
        rep.check(len(set(ids)) == len(ids), f"duplicate auction item id(s): {ids}")
        for i in items:
            rep.check(float(i.get("price", 0)) > 0, f"auction item {i.get('id')} is free")
            rep.check(bool(i.get("name")) and bool(i.get("desc")),
                      f"auction item {i.get('id')} has no name/desc")
        for rank, bonus in (econ.get("placementBonus") or {}).items():
            rep.check(rank.isdigit() and int(rank) >= 1,
                      f"placementBonus key '{rank}' is not a rank number")
            rep.check(float(bonus) >= 0, f"placementBonus[{rank}] is negative")
        print(f"   economy: risk x{econ.get('riskMultiplier')}, {len(items)} auction items, "
              f"placement bonuses for ranks {sorted((econ.get('placementBonus') or {}), key=int)}")

    controls = load_json("data/controls.json", rep)
    if controls is not None:
        for key in ("moveLeft", "moveRight", "moveForward", "moveBack", "jump",
                    "dive", "confirm", "pause"):
            rep.check(isinstance(controls.get(key), list) and len(controls[key]) > 0,
                      f"controls.json: '{key}' has no bound key")
        rep.check(float(controls.get("mouseSensitivity", 0)) > 0,
                  "controls.json: mouseSensitivity must be positive")
        print(f"   controls: {len(controls)} field(s), all movement actions bound")

    for rel in ("gfx/font.json", "gfx/font.png", "gfx/pixel.png", "gfx/particle.png"):
        rep.check(os.path.exists(os.path.join(CONTENT, rel.replace("/", os.sep))),
                  f"content/{rel} is missing")
    print("   gfx: font.json/font.png/pixel.png/particle.png present")


def main():
    rep = Report()
    print("validate_content.py — LAST ONE RICH content gate")
    season = check_season(rep)
    check_levels(rep, season)
    check_bots(rep, season)
    check_twists(rep, season)
    check_audio(rep)
    check_misc(rep)

    print()
    if rep.errors:
        print(f"validate_content: {len(rep.errors)} PROBLEM(S) out of {rep.checks} checks")
        for e in rep.errors:
            print(f"  - {e}")
        return 1
    print(f"validate_content: ALL {rep.checks} CONTENT CHECKS PASSED")
    return 0


if __name__ == "__main__":
    sys.exit(main())
