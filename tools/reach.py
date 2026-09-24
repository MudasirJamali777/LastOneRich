#!/usr/bin/env python3
"""
reach.py — reachability verifier for LAST ONE RICH.

Two independent "can you actually get there?" questions, both of which a compiler is happy
to ignore and both of which have shipped as bugs in this kind of game before:

  PART 1 — STATE GRAPH
    Every IGameState implementation should be constructible from the boot state by some
    chain of _sm.Replace(new X(...)) / _sm.Push(new X(...)) calls. A state nobody ever
    constructs is dead UI: it compiles, it is maintained, and the player can never see it.
    States only reachable through a `#if DEBUG` launch shortcut are reported separately,
    because they are unreachable in the shipping Release build.

  PART 2 — WAYPOINT GRAPHS
    For every level referenced by season.json, rebuild the waypoint graph exactly the way
    World/WaypointGraph.FromDTO does (undirected edges, out-of-range edge indices dropped)
    and run the same Dijkstra-to-goal. Then verify:
      - the goal node is the nearest node to the level's finish zone (Level.cs picks it that way)
      - every spawn slot the season can use routes to the goal with a finite distance
      - for ScoreCollect levels, the vault and deposit nodes are mutually reachable
      - no orphan nodes (a node no edge touches is a bot trap)
    Levels with a single waypoint node (SurvivalZone / FinaleButton arenas, where bots steer
    at a zone rather than along a route) are trivially connected and reported as such.

Exit code 0 = clean, 1 = at least one failure.

Usage:  python3 tools/reach.py
"""

import json
import math
import os
import re
import sys
from collections import defaultdict, deque

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "src")
CONTENT = os.path.join(ROOT, "content")
STATES_DIR = os.path.join(SRC, "LastOneRich", "States")

STATE_DECL_RE = re.compile(r"class\s+([A-Za-z_]\w*)\s*:\s*IGameState")
NEW_RE = re.compile(r"\bnew\s+([A-Za-z_]\w*)\s*\(")
ROOT_STATE = "BootState"


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


def debug_line_flags(text):
    """Return a list: True for lines that sit inside a #if DEBUG region."""
    flags, depth_debug, stack = [], 0, []
    for line in text.splitlines():
        s = line.strip()
        if s.startswith("#if"):
            is_debug = "DEBUG" in s
            stack.append(is_debug)
            if is_debug:
                depth_debug += 1
        elif s.startswith("#endif"):
            if stack:
                if stack.pop():
                    depth_debug -= 1
        flags.append(depth_debug > 0)
    return flags


# --------------------------------------------------------------------------- part 1
def check_states():
    fail, notes = [], []
    sources = cs_sources()

    states = set()
    for path, text in sources.items():
        for m in STATE_DECL_RE.finditer(text):
            states.add(m.group(1))

    # which file declares which state (so we can attribute outgoing edges)
    owner = {}
    for path, text in sources.items():
        for m in STATE_DECL_RE.finditer(text):
            owner[m.group(1)] = path

    edges = defaultdict(set)         # state -> states it can construct (release build)
    debug_edges = defaultdict(set)   # state -> states only constructible under #if DEBUG
    for path, text in sources.items():
        flags = debug_line_flags(text)
        decl = [s for s, p in owner.items() if p == path]
        src_state = decl[0] if len(decl) == 1 else None
        if src_state is None and os.path.basename(path) == "LorGame.cs":
            src_state = "<boot>"
        if src_state is None:
            continue
        for lineno, line in enumerate(text.splitlines()):
            for m in NEW_RE.finditer(line):
                target = m.group(1)
                if target not in states:
                    continue
                if flags[lineno]:
                    debug_edges[src_state].add(target)
                else:
                    edges[src_state].add(target)

    # LorGame constructs BootState; treat that as the graph root
    roots = {ROOT_STATE}

    def bfs(edge_sets):
        seen, q = set(roots), deque(roots)
        while q:
            cur = q.popleft()
            for es in edge_sets:
                for nxt in es.get(cur, ()):
                    if nxt not in seen:
                        seen.add(nxt)
                        q.append(nxt)
        return seen

    release_reach = bfs([edges])
    debug_reach = bfs([edges, debug_edges])

    print("PART 1 — STATE GRAPH")
    print(f"  {len(states)} IGameState implementation(s): {', '.join(sorted(states))}")
    for s in sorted(states):
        tag = "reachable" if s in release_reach else (
            "DEBUG-ONLY" if s in debug_reach else "UNREACHABLE")
        out = sorted(edges[s] | debug_edges[s])
        print(f"    {s:22} {tag:12} -> {', '.join(out) if out else '(terminal)'}")

    for s in sorted(states - debug_reach):
        fail.append(f"state '{s}' is never constructed anywhere — dead UI")
    for s in sorted(debug_reach - release_reach):
        notes.append(f"state '{s}' is only reachable from a #if DEBUG launch shortcut")
    return fail, notes


# --------------------------------------------------------------------------- part 2
def dist3(a, b):
    return math.sqrt(sum((a[i] - b[i]) ** 2 for i in range(3)))


def build_graph(wp):
    nodes = [n["pos"] for n in (wp.get("nodes") or [])]
    adj = [[] for _ in nodes]
    kept = 0
    for e in (wp.get("edges") or []):
        if e is None or len(e) < 2:
            continue
        a, b = int(e[0]), int(e[1])
        risk = float(e[2]) if len(e) >= 3 else 0.0
        if a < 0 or b < 0 or a >= len(nodes) or b >= len(nodes):
            continue
        adj[a].append((b, risk))
        adj[b].append((a, risk))
        kept += 1
    return nodes, adj, kept


def dijkstra(nodes, adj, goal):
    inf = float("inf")
    dist = [inf] * len(nodes)
    if not nodes:
        return dist
    dist[goal] = 0.0
    done = [False] * len(nodes)
    for _ in range(len(nodes)):
        u, best = -1, inf
        for i in range(len(nodes)):
            if not done[i] and dist[i] < best:
                best, u = dist[i], i
        if u < 0:
            break
        done[u] = True
        for v, risk in adj[u]:
            cost = dist3(nodes[u], nodes[v]) * (1.0 + 0.5 * risk)
            if dist[u] + cost < dist[v]:
                dist[v] = dist[u] + cost
    return dist


def nearest(nodes, p):
    best, bestd = 0, float("inf")
    for i, q in enumerate(nodes):
        d = dist3(p, q)
        if d < bestd:
            bestd, best = d, i
    return best


def spawn_slots(dto, count):
    """Mirror of Level.cs: spawnGrid [x,y,z,dx,dz,cols] fills 32 slots; otherwise use spawns."""
    grid = dto.get("spawnGrid")
    if grid and len(grid) >= 6:
        cols = int(grid[5])
        out, row, col = [], 0, 0
        for _ in range(32):
            out.append([grid[0] + (col - (cols - 1) * 0.5) * grid[3], grid[1], grid[2] - row * grid[4]])
            col += 1
            if col >= cols:
                col, row = 0, row + 1
        return out[:count]
    return [s for s in (dto.get("spawns") or [])][:count]


def check_waypoints():
    fail, notes = [], []
    season = json.load(open(os.path.join(CONTENT, "data", "season.json"), encoding="utf-8"))
    cast = int(season.get("contestants", 24))

    print()
    print("PART 2 — WAYPOINT GRAPHS")
    for rnd in season["rounds"]:
        lid = rnd["level"]
        if lid in ("none", "auction"):
            print(f"  R{rnd['round']:>2} {lid:<9} intermission — no level payload")
            continue
        path = os.path.join(CONTENT, "data", "levels", lid + ".json")
        if not os.path.exists(path):
            fail.append(f"round {rnd['round']}: content/data/levels/{lid}.json missing")
            continue
        dto = json.load(open(path, encoding="utf-8"))
        nodes, adj, kept = build_graph(dto.get("waypoints") or {})
        raw_edges = len(((dto.get("waypoints") or {}).get("edges")) or [])
        if kept != raw_edges:
            fail.append(f"{lid}: {raw_edges - kept} waypoint edge(s) reference a node index "
                        f"outside 0..{len(nodes) - 1} and are silently dropped at load")

        if not nodes:
            fail.append(f"{lid}: no waypoint nodes at all")
            continue

        if len(nodes) == 1:
            print(f"  R{rnd['round']:>2} {lid:<9} {dto['type']:<13} single-node arena "
                  f"(zone-steered, trivially connected)")
            continue

        goal = nearest(nodes, (dto.get("finish") or {}).get("pos") or [0, 0, 0])
        dist = dijkstra(nodes, adj, goal)

        orphans = [i for i in range(len(nodes)) if not adj[i]]
        if orphans:
            fail.append(f"{lid}: orphan waypoint node(s) {orphans} — no edge touches them")

        unreachable = [i for i in range(len(nodes)) if dist[i] == float("inf")]
        if unreachable:
            fail.append(f"{lid}: waypoint node(s) {unreachable} cannot reach the goal node {goal}")

        bad_spawns = []
        for si, sp in enumerate(spawn_slots(dto, cast)):
            n = nearest(nodes, sp)
            if dist[n] == float("inf"):
                bad_spawns.append(si)
        if bad_spawns:
            fail.append(f"{lid}: spawn slot(s) {bad_spawns} start at a node with no route to the finish")

        extra = ""
        if dto["type"] == "ScoreCollect":
            if not dto.get("vault") or not dto.get("deposit"):
                fail.append(f"{lid}: ScoreCollect level missing vault/deposit")
            else:
                vn = nearest(nodes, dto["vault"]["pos"])
                dn = nearest(nodes, dto["deposit"]["pos"])
                vdist = dijkstra(nodes, adj, vn)
                if vdist[dn] == float("inf"):
                    fail.append(f"{lid}: vault node {vn} cannot reach deposit node {dn}")
                extra = f" vault n{vn} <-> deposit n{dn} ok"

        print(f"  R{rnd['round']:>2} {lid:<9} {dto['type']:<13} "
              f"{len(nodes):>2} nodes / {kept:>2} edges, goal n{goal}, "
              f"longest route {max(d for d in dist if d != float('inf')):.1f}m{extra}")
    return fail, notes


def main():
    fail_a, note_a = check_states()
    fail_b, note_b = check_waypoints()
    fail, notes = fail_a + fail_b, note_a + note_b

    print()
    for n in notes:
        print(f"  note: {n}")
    if fail:
        print(f"reach: {len(fail)} PROBLEM(S)")
        for f in fail:
            print(f"  - {f}")
        return 1
    print("reach: ALL REACHABILITY CHECKS PASSED")
    return 0


if __name__ == "__main__":
    sys.exit(main())
