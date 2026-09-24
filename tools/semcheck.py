#!/usr/bin/env python3
"""
semcheck.py — cross-layer semantic verifier for LAST ONE RICH.

Where csyntax.py asks "is this lexically well-formed C#?", semcheck.py asks the questions a
compiler cannot: do the *strings* that wire this data-driven game together actually point at
something real, and do the architectural layer rules still hold?

Checks:

  A. TYPES
     A1 no duplicate fully-qualified type name. "Fully qualified" includes the *nesting*
        chain, because nested helper types deliberately repeat short names across the repo
        (PauseMenu.Result vs SettingsScreen.Result, ResultsState.Row vs SettingsScreen.Row,
        Particles.P vs WorldParticles.P) and those are perfectly legal, distinct types.

  B. CONTENT WIRING
     B1 every Json.Load<T>("path") names a type declared in the repo
     B2 every Json.Load<T>("path") path exists under content/
     B3 every Level.Load("levelXX") / "data/levels/{...}" level id exists

  C. AUDIO WIRING
     C1 every AudioBank.Event(...)/Play(...)/Sfx?.Invoke(...) literal resolves to a WAV
        in content/sfx (directly, or through an AudioBank.Event switch arm)
     C2 every AudioBank.Event switch arm plays clips that exist

  D. INPUT WIRING
     D1 every Input.*Action("Name") names a binding property on ControlsDTO

  E. ACHIEVEMENTS
     E1 every Achievements.Unlock/Has("id") literal is declared in Achievements.All

  F. TWISTS
     F1 every Mult("target","param") call site has a matching twists.json maxMult key
     F2 every twists.json effect target.param is consumed by a Mult() call site

  G. LAYER ISOLATION (the HeadlessSim contract)
     The headless harness links the game assembly and replays whole seasons with no GPU,
     no window and no player. That only stays true while the simulation layer it consumes
     (World/, Season/, plus HeadlessSim itself) never reaches for a presentation or
     player-session service. Forbidden there: Xna Graphics, Input, SaveSystem, the
     achievement toast layer, and GameServices.
     G2 additionally forbids Rng (the shared deterministic stream) inside MenuCast, which
        runs on the main menu *between* seasons and would otherwise silently shift the draws
        the next simulated season consumes.

Exit code 0 = clean, 1 = at least one failure.

Usage:  python3 tools/semcheck.py
"""

import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "src")
CONTENT = os.path.join(ROOT, "content")

TYPE_RE = re.compile(
    r"^\s*(?:public|internal|private|protected|sealed|static|abstract|partial|readonly|ref|\s)*"
    r"\b(class|struct|interface|enum|record)\s+([A-Za-z_]\w*)"
)
NS_RE = re.compile(r"^\s*namespace\s+([A-Za-z_][\w.]*)\s*[;{]")

LOAD_RE = re.compile(r"Json\.Load<\s*([A-Za-z_]\w*)\s*>\s*\(\s*\"([^\"]+)\"\s*\)")
LEVEL_LOAD_RE = re.compile(r"Level\.Load\(\s*\"([^\"]+)\"")
EVENT_RE = re.compile(r"\.Event\(\s*\"([a-zA-Z0-9_]+)\"\s*\)")
INVOKE_RE = re.compile(r"Sfx\?\.Invoke\(\s*\"([a-zA-Z0-9_]+)\"\s*\)")
PLAY_RE = re.compile(r"\bPlay\(\s*\"([a-zA-Z0-9_]+)\"")
ACTION_RE = re.compile(r"\b(?:Pressed|Down|Released)?Action\(\s*\"([A-Za-z_]\w*)\"\s*\)")
ACH_RE = re.compile(r"Achievements\.(?:Unlock|Has|Find)\(\s*\"([a-z_0-9]+)\"\s*\)")
MULT_RE = re.compile(r"\bMult\(\s*\"([a-zA-Z]+)\"\s*,\s*\"([a-zA-Z]+)\"")
ACH_DEF_RE = re.compile(r'new\(\s*"([a-z_0-9]+)"\s*,')
CTRL_PROP_RE = re.compile(r"public\s+string\[\]\s+([A-Za-z_]\w*)\s*\{\s*get;")
CASE_RE = re.compile(r'case\s+"([a-zA-Z0-9_]+)"\s*:')

FORBIDDEN = [
    (re.compile(r"\bMicrosoft\.Xna\.Framework\.Graphics\b"), "Xna Graphics"),
    (re.compile(r"\busing\s+Microsoft\.Xna\.Framework\.Graphics\s*;"), "Xna Graphics"),
    (re.compile(r"\bInput\s*\."), "Input"),
    (re.compile(r"\bSaveSystem\s*\."), "SaveSystem"),
    (re.compile(r"\bAchievements\s*\."), "Achievements/ToastLayer"),
    (re.compile(r"\bGameServices\s*\."), "GameServices"),
]

ISOLATED_DIRS = [
    os.path.join(SRC, "LastOneRich", "World"),
    os.path.join(SRC, "LastOneRich", "Season"),
    os.path.join(SRC, "HeadlessSim"),
]


def cs_files():
    out = []
    for dirpath, dirnames, filenames in os.walk(SRC):
        dirnames[:] = [d for d in dirnames if d not in ("bin", "obj", ".vs")]
        for fn in sorted(filenames):
            if fn.endswith(".cs"):
                out.append(os.path.join(dirpath, fn))
    return sorted(out)


def strip_comments(text):
    """Cheap comment stripper — good enough for symbol harvesting (csyntax.py owns rigour)."""
    out = []
    i, n = 0, len(text)
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


def strip_literals(text):
    """Blank out string / char literal bodies (newlines kept) so braces inside them never
    shift the nesting depth. Comments are already gone by the time this runs."""
    out = []
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        verbatim = False
        plen = 0
        if c == '"':
            plen = 1
        elif c in "@$" and i + 1 < n and text[i + 1] == '"':
            verbatim = c == "@"
            plen = 2
        elif c in "@$" and i + 2 < n and text[i + 1] in "@$" and text[i + 2] == '"':
            verbatim = True
            plen = 3
        if plen:
            out.append(" " * plen)
            i += plen
            while i < n:
                ch = text[i]
                if ch == "\n":
                    out.append("\n")
                    i += 1
                    if not verbatim:
                        break
                    continue
                if not verbatim and ch == "\\":
                    out.append("  ")
                    i += 2
                    continue
                if verbatim and ch == '"' and i + 1 < n and text[i + 1] == '"':
                    out.append("  ")
                    i += 2
                    continue
                if ch == '"':
                    out.append(" ")
                    i += 1
                    break
                out.append(" ")
                i += 1
            continue
        if c == "'":
            out.append(" ")
            i += 1
            while i < n and text[i] != "\n":
                if text[i] == "\\":
                    out.append("  ")
                    i += 2
                    continue
                if text[i] == "'":
                    out.append(" ")
                    i += 1
                    break
                out.append(" ")
                i += 1
            continue
        out.append(c)
        i += 1
    return "".join(out)


def rel(path):
    return os.path.relpath(path, ROOT).replace(os.sep, "/")


def main():
    fail = []
    note = []
    files = cs_files()
    sources = {p: strip_comments(open(p, encoding="utf-8-sig").read()) for p in files}

    # ---------------------------------------------------------------- A types
    types = {}                       # fully-qualified name -> [files]
    all_type_names = set()
    for path, text in sources.items():
        ns = ""
        depth = 0
        stack = []                   # [(type name, brace depth it opened at)]
        pending = None               # a type header whose '{' is on a later line
        for line in strip_literals(text).splitlines():
            m = NS_RE.match(line)
            if m:
                ns = m.group(1)
            else:
                m = TYPE_RE.match(line)
                if m:
                    pending = m.group(2)
            for ch in line:
                if ch == "{":
                    if pending is not None:
                        stack.append((pending, depth))
                        fq = ".".join([ns] + [s[0] for s in stack])
                        all_type_names.add(pending)
                        types.setdefault(fq, []).append(rel(path))
                        pending = None
                    depth += 1
                elif ch == "}":
                    depth -= 1
                    while stack and stack[-1][1] >= depth:
                        stack.pop()
            if pending is not None and line.rstrip().endswith(";"):
                pending = None        # record/enum one-liner or forward form
    for fq, where in sorted(types.items()):
        if len(where) > 1:
            fail.append(f"A1 duplicate type {fq} declared in {', '.join(where)}")
    print(f"A1 types ................ {len(types)} declared, no duplicates"
          if not fail else "A1 types ................ PROBLEM")

    # ------------------------------------------------- B content wiring
    load_sites = []
    for path, text in sources.items():
        for m in LOAD_RE.finditer(text):
            load_sites.append((rel(path), m.group(1), m.group(2)))
    for where, typ, relpath in load_sites:
        if typ not in all_type_names:
            fail.append(f"B1 {where}: Json.Load<{typ}> — no such type in the repo")
        if not os.path.exists(os.path.join(CONTENT, relpath.replace("/", os.sep))):
            fail.append(f"B2 {where}: Json.Load(\"{relpath}\") — content/{relpath} missing")
    print(f"B1/B2 Json.Load sites ... {len(load_sites)} checked")

    level_ids = set()
    for path, text in sources.items():
        for m in LEVEL_LOAD_RE.finditer(text):
            level_ids.add((rel(path), m.group(1)))
    for where, lid in sorted(level_ids):
        if lid in ("none", "auction"):
            continue
        p = os.path.join(CONTENT, "data", "levels", lid + ".json")
        if not os.path.exists(p):
            fail.append(f"B3 {where}: Level.Load(\"{lid}\") — content/data/levels/{lid}.json missing")
    print(f"B3 Level.Load sites ..... {len(level_ids)} checked")

    # ------------------------------------------------------- C audio wiring
    sfx_dir = os.path.join(CONTENT, "sfx")
    clips = {os.path.splitext(f)[0].lower() for f in os.listdir(sfx_dir) if f.endswith(".wav")}

    audio_path = os.path.join(SRC, "LastOneRich", "Core", "AudioBank.cs")
    audio_src = sources[audio_path]
    event_body = audio_src[audio_src.find("public void Event("):]
    arms = {}
    for line in event_body.splitlines():
        m = CASE_RE.search(line)
        if m:
            arms[m.group(1)] = PLAY_RE.findall(line)
    for arm, plays in sorted(arms.items()):
        for clip in plays:
            if clip.lower() not in clips:
                fail.append(f"C2 AudioBank.Event case \"{arm}\" plays \"{clip}\" — no content/sfx/{clip}.wav")

    event_names = set()
    for path, text in sources.items():
        if path == audio_path:
            continue
        for m in EVENT_RE.finditer(text):
            event_names.add((rel(path), m.group(1)))
        for m in INVOKE_RE.finditer(text):
            event_names.add((rel(path), m.group(1)))
        for m in PLAY_RE.finditer(text):
            event_names.add((rel(path), m.group(1)))
    for where, name in sorted(event_names):
        if name in arms:
            continue                       # resolved by an explicit Event switch arm
        if name.lower() not in clips:
            fail.append(f"C1 {where}: sfx event \"{name}\" has no content/sfx/{name}.wav "
                        f"and no AudioBank.Event arm")
    print(f"C1/C2 sfx events ........ {len(event_names)} call site(s), "
          f"{len(arms)} switch arm(s), {len(clips)} clip(s)")

    # ------------------------------------------------------- D input wiring
    controls_src = sources[os.path.join(SRC, "LastOneRich", "Core", "Controls.cs")]
    bindings = set(CTRL_PROP_RE.findall(controls_src))
    used_actions = set()
    for path, text in sources.items():
        for m in ACTION_RE.finditer(text):
            used_actions.add((rel(path), m.group(1)))
    for where, action in sorted(used_actions):
        if action not in bindings:
            fail.append(f"D1 {where}: Action(\"{action}\") — ControlsDTO has no such binding")
    print(f"D1 input actions ........ {len(used_actions)} call site(s) over "
          f"{len(bindings)} binding(s)")

    # ------------------------------------------------------ E achievements
    ach_src = sources[os.path.join(SRC, "LastOneRich", "Core", "Achievements.cs")]
    declared_ach = set(ACH_DEF_RE.findall(ach_src))
    used_ach = set()
    for path, text in sources.items():
        for m in ACH_RE.finditer(text):
            used_ach.add((rel(path), m.group(1)))
    for where, aid in sorted(used_ach):
        if aid not in declared_ach:
            fail.append(f"E1 {where}: achievement id \"{aid}\" is not in Achievements.All")
    print(f"E1 achievements ......... {len(declared_ach)} declared, "
          f"{len(used_ach)} literal use(s)")

    # ------------------------------------------------------------ F twists
    twists = json.load(open(os.path.join(CONTENT, "data", "twists.json"), encoding="utf-8"))
    twist_keys = set()
    effect_keys = set()
    for t in twists["twists"]:
        for k in (t.get("maxMult") or {}):
            twist_keys.add(k)
        for e in (t.get("effects") or []):
            effect_keys.add(f"{e['target']}.{e['param']}")
    code_keys = set()
    for path, text in sources.items():
        for m in MULT_RE.finditer(text):
            code_keys.add(f"{m.group(1)}.{m.group(2)}")
    for k in sorted(effect_keys - code_keys):
        fail.append(f"F2 twists.json effect '{k}' is never consumed by a Mult() call site")
    for k in sorted(code_keys - twist_keys - effect_keys):
        note.append("F1 Mult(" + k + ") has no twist in twists.json "
                    "(harmless: the multiplier just stays 1.0)")
    print(f"F1/F2 twist channels .... {len(code_keys)} in code, "
          f"{len(effect_keys)} in twists.json")

    # -------------------------------------------------- G layer isolation
    iso_files = 0
    for d in ISOLATED_DIRS:
        for dirpath, dirnames, filenames in os.walk(d):
            dirnames[:] = [x for x in dirnames if x not in ("bin", "obj")]
            for fn in sorted(filenames):
                if not fn.endswith(".cs"):
                    continue
                path = os.path.join(dirpath, fn)
                iso_files += 1
                for lineno, line in enumerate(sources[path].splitlines(), 1):
                    for pat, label in FORBIDDEN:
                        if pat.search(line):
                            fail.append(
                                f"G1 {rel(path)}:{lineno}: simulation layer touches {label} "
                                f"— breaks the HeadlessSim contract")
    menucast = os.path.join(SRC, "LastOneRich", "Season", "MenuCast.cs")
    if os.path.exists(menucast):
        for lineno, line in enumerate(sources[menucast].splitlines(), 1):
            if re.search(r"\bRng\s*\.", line):
                fail.append(f"G2 {rel(menucast)}:{lineno}: MenuCast draws from the shared Rng "
                            f"— would desynchronise the next simulated season")
    print(f"G1/G2 layer isolation ... {iso_files} simulation file(s) scanned")

    # ------------------------------------------------------------- report
    print()
    for n in note:
        print(f"  note: {n}")
    if fail:
        print(f"semcheck: {len(fail)} PROBLEM(S)")
        for f in fail:
            print(f"  - {f}")
        return 1
    print("semcheck: ALL SEMANTIC CHECKS PASSED")
    return 0


if __name__ == "__main__":
    sys.exit(main())
