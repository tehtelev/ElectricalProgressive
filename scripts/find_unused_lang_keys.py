# -*- coding: utf-8 -*-
"""
Find localization keys defined in source ru.json files of the
ElectricalProgressive mod that are NOT referenced anywhere else.

v3 (fixed interpolated-string parsing):
- Fast matching via pyahocorasick (or one combined regex fallback).
- Only scans .cs (string literals) and .json (string values) — no comments,
  no .md/.txt/.csproj noise, no JSON keys.
- Correct handling of @", $", @$", $@", raw triple-quoted strings, AND
  strings nested inside interpolation holes of $"...{ ... }...".
- `game:Foo` matched by suffix `Foo` unless --strict-game.
- Per-file error handling, sorted output, CLI options.
"""
import argparse
import json
import os
import re
import sys
import time
from collections import defaultdict

try:
    import ahocorasick
    _HAVE_AC = True
except ImportError:
    _HAVE_AC = False

ROOT = r"C:\Users\Tehtelev\source\repos\ElectricalProgressive"

PROJECTS = [
    ("ElectricalProgressive-Basics",    "electricalprogressivebasics"),
    ("ElectricalProgressive-Core",      "electricalprogressivecore"),
    ("ElectricalProgressive-Equipment", "electricalprogressiveequipment"),
    ("ElectricalProgressive-Industry",  "electricalprogressiveindustry"),
    ("ElectricalProgressive-QOL",       "electricalprogressiveqol"),
    ("ElectricalProgressive-Transport", "electricalprogressivetransport"),
    ("ElectricalProgressive-Storage",   "electricalprogressivestorage"),
]

EXCLUDE_DIRS = {"bin", "obj", "Releases", ".git", ".vs", "packages", "Tools"}

# JSON fields where a lang key is likely to appear as the value.
JSON_LANG_FIELDS = {
    "displayname", "name", "title", "description", "desc", "label",
    "text", "tooltip", "tutorial", "message", "msg",
}


# ---------- boundaries (same semantics as v1) ----------

def _is_prev_ok(c):
    # (?<![\w\-]) : previous char must NOT be word-char or '-'
    return not (c.isalnum() or c == '_' or c == '-')

def _is_next_ok(c):
    # (?![\w\-:/]) : next char must NOT be word-char, '-', ':', '/'
    return not (c.isalnum() or c in '_-:/')


# ---------- lang-file helpers ----------

def strip_comments(text):
    out = []
    i, n = 0, len(text)
    in_str = False
    while i < n:
        c = text[i]
        if in_str:
            out.append(c)
            if c == '\\' and i + 1 < n:
                out.append(text[i + 1]); i += 2; continue
            if c == '"':
                in_str = False
            i += 1; continue
        if c == '"':
            in_str = True; out.append(c); i += 1; continue
        if c == '/' and i + 1 < n:
            if text[i + 1] == '/':
                while i < n and text[i] != '\n':
                    i += 1
                continue
            if text[i + 1] == '*':
                i += 2
                while i + 1 < n and not (text[i] == '*' and text[i + 1] == '/'):
                    i += 1
                i = min(i + 2, n)
                continue
        out.append(c); i += 1
    return ''.join(out)


def remove_trailing_commas(text):
    out = []
    i, n = 0, len(text)
    in_str = False
    while i < n:
        c = text[i]
        if in_str:
            out.append(c)
            if c == '\\' and i + 1 < n:
                out.append(text[i + 1]); i += 2; continue
            if c == '"':
                in_str = False
            i += 1; continue
        if c == '"':
            in_str = True; out.append(c); i += 1; continue
        if c == ',':
            j = i + 1
            while j < n and text[j] in ' \t\r\n':
                j += 1
            if j < n and text[j] in ']}':
                i += 1; continue
        out.append(c); i += 1
    return ''.join(out)


# ---------- C# string-literal extraction (skips comments, handles $"...{...}...") ----------

def extract_cs_string_literals(text):
    """Single-pass extractor. Handles // and /* */ comments, char literals,
    regular/verbatim/interpolated/raw strings, and strings nested inside
    interpolation holes of $"...{ ... }...". Returns string contents
    (holes in interpolated strings are replaced by '{...}')."""
    out = []
    _scan_code(text, 0, len(text), out)
    return out


def _scan_code(text, i, end, out):
    """Scan a code region [i, end), collecting string literal contents into out."""
    n = min(end, len(text))
    while i < n:
        c = text[i]
        if c == '/':
            if i + 1 < n and text[i + 1] == '/':
                i += 2
                while i < n and text[i] != '\n':
                    i += 1
                continue
            if i + 1 < n and text[i + 1] == '*':
                i += 2
                while i + 1 < n and not (text[i] == '*' and text[i + 1] == '/'):
                    i += 1
                i = min(i + 2, n)
                continue
            i += 1
            continue
        if c == "'":
            i = _skip_char_literal(text, i, n)
            continue
        if c in '"@$':
            j = _try_string(text, i, n, out)
            if j is not None:
                i = j
                continue
            i += 1
            continue
        i += 1
    return i


def _skip_char_literal(text, i, n):
    """Skip a C# char literal starting at opening '. Returns position after '."""
    i += 1
    while i < n:
        if text[i] == '\\' and i + 1 < n:
            i += 2
            continue
        if text[i] == "'":
            return i + 1
        if text[i] == '\n':
            return i
        i += 1
    return i


def _try_string(text, i, n, out):
    """Attempt to parse a C# string literal at i. Returns new position after
    the closing quote, or None if this is not a string (e.g. identifier '@class')."""
    verbatim = False
    interpolated = False
    j = i
    prefix_count = 0
    while j < n and text[j] in '@$' and prefix_count < 2:
        if text[j] == '@':
            verbatim = True
        else:
            interpolated = True
        j += 1
        prefix_count += 1
    if j >= n or text[j] != '"':
        return None
    # raw string """...""" (verbatim and raw are mutually exclusive)
    if not verbatim:
        q = j
        while q < n and text[q] == '"':
            q += 1
        if q - j >= 3:
            return _scan_raw_string(text, q, n, out, q - j)
    return _scan_string_body(text, j + 1, n, out, verbatim, interpolated)


def _scan_string_body(text, i, n, out, verbatim, interpolated):
    """Scan body of a "..." / @"..." string. i is position after opening quote."""
    buf = []
    while i < n:
        c = text[i]
        if interpolated and c == '{':
            if i + 1 < n and text[i + 1] == '{':       # escaped literal '{'
                buf.append('{')
                i += 2
                continue
            i = _scan_hole(text, i + 1, n, out)         # recursion into hole
            buf.append('{...}')
            continue
        if interpolated and c == '}':
            if i + 1 < n and text[i + 1] == '}':       # escaped literal '}'
                buf.append('}')
                i += 2
                continue
            i += 1
            continue
        if verbatim:
            if c == '"':
                if i + 1 < n and text[i + 1] == '"':   # escaped '"'
                    buf.append('"')
                    i += 2
                    continue
                out.append(''.join(buf))
                return i + 1
            buf.append(c)
            i += 1
        else:
            if c == '\\' and i + 1 < n:
                buf.append(text[i + 1])
                i += 2
                continue
            if c == '"':
                out.append(''.join(buf))
                return i + 1
            if c == '\n':                                # unterminated — bail out
                out.append(''.join(buf))
                return i
            buf.append(c)
            i += 1
    out.append(''.join(buf))
    return i


def _scan_hole(text, i, n, out):
    """Scan an interpolation hole expression starting right after '{'.
    Recursively extracts string literals from the expression. Returns
    position after the matching '}'."""
    depth = 0
    while i < n:
        c = text[i]
        if c == '{':
            depth += 1
            i += 1
            continue
        if c == '}':
            if depth == 0:
                return i + 1
            depth -= 1
            i += 1
            continue
        if c == '/':
            if i + 1 < n and text[i + 1] == '/':
                i += 2
                while i < n and text[i] != '\n':
                    i += 1
                continue
            if i + 1 < n and text[i + 1] == '*':
                i += 2
                while i + 1 < n and not (text[i] == '*' and text[i + 1] == '/'):
                    i += 1
                i = min(i + 2, n)
                continue
            i += 1
            continue
        if c == "'":
            i = _skip_char_literal(text, i, n)
            continue
        if c in '"@$':
            j = _try_string(text, i, n, out)
            if j is not None:
                i = j
                continue
            i += 1
            continue
        i += 1
    return i


def _scan_raw_string(text, i, n, out, num_quotes):
    """Scan a C# 11 raw string (already positioned after the opening quotes).
    Raw strings are collected whole; interpolated raw strings with nested
    Lang.Get are handled implicitly by substring matching later."""
    end_marker = '"' * num_quotes
    idx = text.find(end_marker, i)
    if idx < 0:
        out.append(text[i:])
        return n
    out.append(text[i:idx])
    return idx + num_quotes


# ---------- JSON string-value extraction ----------

def extract_json_string_values(obj, lang_only):
    out = []
    def walk(x, parent_key):
        if isinstance(x, str):
            if (not lang_only) or (parent_key and parent_key.lower() in JSON_LANG_FIELDS):
                out.append(x)
        elif isinstance(x, list):
            for v in x:
                walk(v, parent_key)
        elif isinstance(x, dict):
            for k, v in x.items():
                walk(v, k)
    walk(obj, None)
    return out


# ---------- lang keys ----------

def load_keys(root, proj, langid):
    path = os.path.join(root, proj, "assets", langid, "lang", "ru.json")
    with open(path, encoding="utf-8-sig") as f:
        raw = f.read()
    clean = remove_trailing_commas(strip_comments(raw))
    data = json.loads(clean)
    if not isinstance(data, dict):
        raise ValueError(f"{path}: root is not a JSON object")
    return list(data.keys())


def collect_keys(root):
    keys_mods = {}
    counts = {}
    for proj, langid in PROJECTS:
        try:
            keys = load_keys(root, proj, langid)
        except FileNotFoundError:
            print(f"  WARNING: missing lang file for {proj}", file=sys.stderr)
            keys = []
        except Exception as e:
            print(f"  ERROR parsing lang for {proj}: {e}", file=sys.stderr)
            keys = []
        counts[proj] = len(keys)
        for k in keys:
            keys_mods.setdefault(k, [])
            if proj not in keys_mods[k]:
                keys_mods[k].append(proj)
    return keys_mods, counts


# ---------- source scanning ----------

def iter_source_files(root):
    for proj, _ in PROJECTS:
        base = os.path.join(root, proj)
        if not os.path.isdir(base):
            continue
        for dirpath, dirnames, filenames in os.walk(base):
            dirnames[:] = [d for d in dirnames
                           if d not in EXCLUDE_DIRS and not d.startswith('.')]
            rel = os.path.relpath(dirpath, base)
            parts = [p.lower() for p in rel.split(os.sep) if p]
            if 'lang' in parts:
                continue
            for fn in filenames:
                ext = os.path.splitext(fn)[1].lower()
                if ext in ('.cs', '.json'):
                    yield os.path.join(dirpath, fn), ext


def extract_strings_from_file(path, ext, json_all_values):
    try:
        with open(path, encoding='utf-8-sig') as f:
            text = f.read()
    except (UnicodeDecodeError, OSError) as e:
        print(f"  skip {path}: {e}", file=sys.stderr)
        return []
    if ext == '.cs':
        return extract_cs_string_literals(text)
    if ext == '.json':
        try:
            data = json.loads(remove_trailing_commas(strip_comments(text)))
        except (json.JSONDecodeError, ValueError):
            return []
        return extract_json_string_values(data, lang_only=not json_all_values)
    return []


def collect_search_strings(root, json_all_values):
    chunks = []
    files = 0
    for path, ext in iter_source_files(root):
        files += 1
        strings = extract_strings_from_file(path, ext, json_all_values)
        if strings:
            chunks.extend(strings)
    return '\0'.join(chunks), files


# ---------- matching ----------

def key_to_regex_body(key):
    return '.*'.join(re.escape(p) for p in key.split('*'))


def build_search_terms(keys, strict_game):
    literal_terms = set()
    term_to_keys = defaultdict(list)
    pattern_keys = []
    for k in keys:
        if '*' in k:
            pattern_keys.append(k)
            continue
        literal_terms.add(k)
        term_to_keys[k].append(k)
        if not strict_game and k.startswith('game:'):
            suffix = k[len('game:'):]
            if suffix:
                literal_terms.add(suffix)
                term_to_keys[suffix].append(k)
    return literal_terms, term_to_keys, pattern_keys


def find_used(keys, big_text, strict_game, use_ac):
    literal_terms, term_to_keys, pattern_keys = build_search_terms(keys, strict_game)
    used = set()
    n = len(big_text)

    if literal_terms:
        if use_ac and _HAVE_AC:
            A = ahocorasick.Automaton()
            for t in literal_terms:
                A.add_word(t, t)
            A.make_automaton()
            for end, t in A.iter(big_text):
                start = end - len(t) + 1
                if start > 0 and not _is_prev_ok(big_text[start - 1]):
                    continue
                if end + 1 < n and not _is_next_ok(big_text[end + 1]):
                    continue
                for k in term_to_keys[t]:
                    used.add(k)
        else:
            sorted_terms = sorted(literal_terms, key=len, reverse=True)
            pat = re.compile('|'.join(re.escape(t) for t in sorted_terms))
            for m in pat.finditer(big_text):
                t = m.group(0)
                start = m.start()
                end = m.end() - 1
                if start > 0 and not _is_prev_ok(big_text[start - 1]):
                    continue
                if end + 1 < n and not _is_next_ok(big_text[end + 1]):
                    continue
                for k in term_to_keys[t]:
                    used.add(k)

    # wildcard keys — individually (they're few)
    for k in pattern_keys:
        terms = [k]
        if not strict_game and k.startswith('game:'):
            terms.append(k[len('game:'):])
        for t in terms:
            rx = re.compile(r'(?<![\w\-])' + key_to_regex_body(t) + r'(?![\w\-:/])')
            if rx.search(big_text):
                used.add(k)
                break

    return used


# ---------- main ----------

def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--root', default=ROOT,
                    help=f'mod root directory (default: {ROOT})')
    ap.add_argument('--strict-game', action='store_true',
                    help="require literal 'game:Foo' in source (don't match bare 'Foo')")
    ap.add_argument('--json-all-values', action='store_true',
                    help='scan all .json string values (noisier; default: only likely lang fields)')
    ap.add_argument('--no-ac', action='store_true',
                    help='force combined-regex path even if pyahocorasick is installed')
    ap.add_argument('--output', default=None,
                    help='path to write unused keys report (default: <root>/unused_lang_keys.txt)')
    args = ap.parse_args()

    root = args.root
    if not os.path.isdir(root):
        print(f"ERROR: root not found: {root}", file=sys.stderr)
        return 2

    t0 = time.time()

    print("Loading lang keys...")
    keys_mods, counts = collect_keys(root)
    all_keys = list(keys_mods.keys())
    for proj, _ in PROJECTS:
        print(f"  {proj:32s} {counts.get(proj, 0)}")
    print(f"  unique keys: {len(all_keys)}")

    use_ac = _HAVE_AC and not args.no_ac
    print(f"Matching method: {'Aho-Corasick' if use_ac else 'combined regex'}"
          f"{' (install pyahocorasick for speed)' if not use_ac else ''}")

    t1 = time.time()
    print("Scanning source files...")
    big_text, nfiles = collect_search_strings(root, args.json_all_values)
    t2 = time.time()
    print(f"  files scanned: {nfiles}")
    print(f"  extracted text size: {len(big_text):,} chars  ({t2 - t1:.2f}s)")

    print("Matching keys...")
    used = find_used(all_keys, big_text, args.strict_game, use_ac)
    t3 = time.time()
    print(f"  matched in {t3 - t2:.2f}s")

    unused = [k for k in all_keys if k not in used]
    by_mod = {proj: [] for proj, _ in PROJECTS}
    for k in unused:
        for m in keys_mods[k]:
            by_mod[m].append(k)
    for proj in by_mod:
        by_mod[proj].sort()

    print(f"\nUSED: {len(used)}    UNUSED: {len(unused)}    "
          f"(total time {t3 - t0:.2f}s)")
    print("=" * 70)
    for proj, _ in PROJECTS:
        lst = by_mod[proj]
        print(f"\n### {proj}  ({len(lst)} unused)")
        for k in lst:
            print(f"  {k}{'  <pattern>' if '*' in k else ''}")

    out_path = args.output or os.path.join(root, "unused_lang_keys.txt")
    try:
        with open(out_path, "w", encoding="utf-8") as f:
            f.write("UNUSED localization keys — not found in any .cs string literal "
                    "or .json string value of the mod source.\n")
            f.write("=" * 90 + "\n\n")
            for proj, _ in PROJECTS:
                lst = by_mod[proj]
                if not lst:
                    continue
                f.write(f"## {proj} ({len(lst)})\n")
                for k in lst:
                    f.write(k + "\n")
                f.write("\n")
        print(f"\nSaved to {out_path}")
    except OSError as e:
        print(f"WARNING: could not save report: {e}", file=sys.stderr)

    return 0


if __name__ == '__main__':
    sys.exit(main())