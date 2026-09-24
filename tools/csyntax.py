#!/usr/bin/env python3
"""
csyntax.py — static C# lexical / structural verifier for LAST ONE RICH.

This is NOT a compiler. There is no .NET SDK in the QA environment, so this tool does the
one class of check that can be done honestly without one: it lexes every .cs file in the
repository (correctly handling // and /* */ comments, "strings", @"verbatim strings",
$"interpolated strings" including their code holes, and 'char' literals) and then verifies
the structural facts a compiler would otherwise be the first to notice:

  1. every string / char literal terminates on its line (verbatim strings may span lines)
  2. every block comment terminates
  3. braces, parentheses and brackets balance, and never close below zero
  4. #if / #else / #elif / #endif preprocessor regions balance
  5. every file declares exactly one namespace, and it matches its folder
     (src/LastOneRich/World/*.cs -> LastOneRich.World, etc.)
  6. no file mixes a file-scoped namespace with a block-scoped one
  7. no tab characters (the repo is uniformly 4-space indented)
  8. files are UTF-8 decodable and end with a newline

Exit code 0 = clean, 1 = at least one failure.

Usage:  python3 tools/csyntax.py [--quiet]
"""

import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "src")

# folder -> expected namespace. Keys are paths relative to src/, using forward slashes.
NAMESPACE_MAP = {
    "LastOneRich": "LastOneRich",
    "LastOneRich/Core": "LastOneRich.Core",
    "LastOneRich/World": "LastOneRich.World",
    "LastOneRich/Season": "LastOneRich.Season",
    "LastOneRich/States": "LastOneRich.States",
    "LastOneRich/Cine": "LastOneRich.Cine",
    "HeadlessSim": "LastOneRich.HeadlessSim",
}


class Failure(Exception):
    pass


def cs_files():
    out = []
    for dirpath, dirnames, filenames in os.walk(SRC):
        dirnames[:] = [d for d in dirnames if d not in ("bin", "obj", ".vs")]
        for fn in sorted(filenames):
            if fn.endswith(".cs"):
                out.append(os.path.join(dirpath, fn))
    return sorted(out)


def strip_code(text, relpath, problems):
    """
    Return `text` with every comment, string, char literal and interpolation-hole-free
    string body replaced by spaces (newlines preserved so line numbers stay true).

    Code inside an interpolated string's { ... } hole IS kept, because it is real code and
    its brackets really do have to balance.
    """
    out = []
    i = 0
    n = len(text)
    line = 1
    # stack of open interpolated strings: each entry is (quote_depth_of_braces, is_verbatim)
    interp_stack = []

    def emit(ch):
        out.append("\n" if ch == "\n" else (ch if ch.isspace() else " "))

    while i < n:
        c = text[i]
        nxt = text[i + 1] if i + 1 < n else ""

        if c == "\n":
            line += 1
            out.append("\n")
            i += 1
            continue

        # ---- comments -------------------------------------------------------
        if c == "/" and nxt == "/":
            while i < n and text[i] != "\n":
                emit(text[i])
                i += 1
            continue

        if c == "/" and nxt == "*":
            start_line = line
            i += 2
            out.append("  ")
            closed = False
            while i < n:
                if text[i] == "\n":
                    line += 1
                    out.append("\n")
                    i += 1
                    continue
                if text[i] == "*" and i + 1 < n and text[i + 1] == "/":
                    out.append("  ")
                    i += 2
                    closed = True
                    break
                emit(text[i])
                i += 1
            if not closed:
                problems.append(f"{relpath}:{start_line}: unterminated block comment")
            continue

        # ---- char literal ---------------------------------------------------
        if c == "'":
            start_line = line
            i += 1
            out.append(" ")
            closed = False
            while i < n and text[i] != "\n":
                if text[i] == "\\":
                    out.append("  ")
                    i += 2
                    continue
                if text[i] == "'":
                    out.append(" ")
                    i += 1
                    closed = True
                    break
                emit(text[i])
                i += 1
            if not closed:
                problems.append(f"{relpath}:{start_line}: unterminated char literal")
            continue

        # ---- strings --------------------------------------------------------
        verbatim = False
        interpolated = False
        prefix_len = 0
        if c == '"':
            prefix_len = 1
        elif c == "@" and nxt == '"':
            verbatim, prefix_len = True, 2
        elif c == "$" and nxt == '"':
            interpolated, prefix_len = True, 2
        elif c == "$" and nxt == "@" and i + 2 < n and text[i + 2] == '"':
            verbatim = interpolated = True
            prefix_len = 3
        elif c == "@" and nxt == "$" and i + 2 < n and text[i + 2] == '"':
            verbatim = interpolated = True
            prefix_len = 3

        if prefix_len:
            start_line = line
            out.append(" " * prefix_len)
            i += prefix_len
            closed = False
            while i < n:
                ch = text[i]
                if ch == "\n":
                    line += 1
                    out.append("\n")
                    i += 1
                    if not verbatim:
                        break  # a non-verbatim string may not cross a line
                    continue
                if not verbatim and ch == "\\":
                    out.append("  ")
                    i += 2
                    continue
                if verbatim and ch == '"' and i + 1 < n and text[i + 1] == '"':
                    out.append("  ")
                    i += 2
                    continue
                if interpolated and ch == "{":
                    if i + 1 < n and text[i + 1] == "{":
                        out.append("  ")
                        i += 2
                        continue
                    # a code hole: keep the code, and let the scanner recurse over it
                    depth = 0
                    out.append(" ")
                    i += 1
                    hole = []
                    while i < n:
                        hc = text[i]
                        if hc == "\n":
                            line += 1
                        if hc == "{":
                            depth += 1
                        elif hc == "}":
                            if depth == 0:
                                break
                            depth -= 1
                        hole.append(hc)
                        i += 1
                    if i < n:
                        i += 1  # consume the closing }
                    # recursively sanitise the hole so nested strings don't confuse us,
                    # then re-emit it wrapped in parens so its brackets are still checked
                    sub = strip_code("".join(hole), relpath, problems)
                    out.append("(" + sub + ")")
                    continue
                if interpolated and ch == "}" and i + 1 < n and text[i + 1] == "}":
                    out.append("  ")
                    i += 2
                    continue
                if ch == '"':
                    out.append(" ")
                    i += 1
                    closed = True
                    break
                emit(ch)
                i += 1
            if not closed:
                problems.append(f"{relpath}:{start_line}: unterminated string literal")
            continue

        out.append(c)
        i += 1

    return "".join(out)


def check_brackets(code, relpath, problems):
    pairs = {")": "(", "]": "[", "}": "{"}
    stack = []
    line = 1
    for ch in code:
        if ch == "\n":
            line += 1
        elif ch in "([{":
            stack.append((ch, line))
        elif ch in ")]}":
            if not stack:
                problems.append(f"{relpath}:{line}: stray closing '{ch}'")
                return
            open_ch, open_line = stack.pop()
            if open_ch != pairs[ch]:
                problems.append(
                    f"{relpath}:{line}: '{ch}' closes '{open_ch}' opened at line {open_line}"
                )
                return
    for open_ch, open_line in stack:
        problems.append(f"{relpath}:{open_line}: unclosed '{open_ch}'")


def check_preprocessor(code, relpath, problems):
    depth = 0
    for lineno, raw in enumerate(code.splitlines(), 1):
        s = raw.strip()
        if s.startswith("#if"):
            depth += 1
        elif s.startswith("#endif"):
            depth -= 1
            if depth < 0:
                problems.append(f"{relpath}:{lineno}: #endif without #if")
                return
        elif (s.startswith("#else") or s.startswith("#elif")) and depth == 0:
            problems.append(f"{relpath}:{lineno}: {s.split()[0]} outside any #if")
            return
    if depth != 0:
        problems.append(f"{relpath}: {depth} unclosed #if region(s)")


def check_namespace(code, path, relpath, problems):
    file_scoped = []
    block_scoped = []
    for lineno, raw in enumerate(code.splitlines(), 1):
        s = raw.strip()
        if not s.startswith("namespace "):
            continue
        rest = s[len("namespace "):].strip()
        if rest.endswith(";"):
            file_scoped.append((rest[:-1].strip(), lineno))
        else:
            block_scoped.append((rest.rstrip("{").strip(), lineno))

    names = file_scoped + block_scoped
    if not names:
        problems.append(f"{relpath}: no namespace declaration")
        return
    if len(names) > 1:
        problems.append(f"{relpath}: {len(names)} namespace declarations (expected 1)")
        return
    if file_scoped and block_scoped:
        problems.append(f"{relpath}: mixes file-scoped and block-scoped namespaces")
        return

    declared = names[0][0]
    folder = os.path.relpath(os.path.dirname(path), SRC).replace(os.sep, "/")
    expected = NAMESPACE_MAP.get(folder)
    if expected is None:
        problems.append(f"{relpath}: folder '{folder}' has no namespace convention entry")
    elif declared != expected:
        problems.append(
            f"{relpath}:{names[0][1]}: namespace '{declared}' != expected '{expected}'"
        )


def main():
    quiet = "--quiet" in sys.argv
    problems = []
    files = cs_files()
    if not files:
        print("csyntax: FAIL — no .cs files found under src/")
        return 1

    for path in files:
        relpath = os.path.relpath(path, ROOT).replace(os.sep, "/")
        raw = open(path, "rb").read()
        try:
            text = raw.decode("utf-8-sig")
        except UnicodeDecodeError as e:
            problems.append(f"{relpath}: not valid UTF-8 ({e})")
            continue
        if text and not text.endswith("\n"):
            problems.append(f"{relpath}: file does not end with a newline")
        if "\t" in text:
            bad = [str(i) for i, l in enumerate(text.splitlines(), 1) if "\t" in l]
            problems.append(f"{relpath}: tab character(s) on line(s) {','.join(bad[:8])}")

        code = strip_code(text, relpath, problems)
        check_brackets(code, relpath, problems)
        check_preprocessor(text, relpath, problems)
        check_namespace(code, path, relpath, problems)

        if not quiet:
            print(f"  ok  {relpath}" if not any(p.startswith(relpath) for p in problems)
                  else f"  !!  {relpath}")

    print()
    if problems:
        print(f"csyntax: {len(problems)} PROBLEM(S) in {len(files)} file(s)")
        for p in problems:
            print(f"  - {p}")
        return 1
    print(f"csyntax: {len(files)} file(s) lexed — brackets, literals, regions, "
          f"namespaces ALL OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
