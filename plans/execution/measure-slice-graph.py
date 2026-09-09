"""Measure the live cross-slice dependency graph of the Basil solution.

A slice is a feature of the product, not a directory. It owns whatever files
carry its name, wherever they live: `Basil.Server/Features/Multiplayer` today,
`Basil.Domain/Multiplayer` and `Basil.Hosts.Bancho/Multiplayer` once the
migration has split the projects. An edge A -> B exists when a file owned by
slice A names slice B's namespace in code.

Ownership by name rather than by directory is the whole point. The migration
moves roughly ninety-six files out of `Features/`, and a measurement scoped to
`Features/` would report those edges as gone the moment the files relocate --
turning task C5, which is supposed to stop the migration if the coupling did not
actually fall, into a gate that passes for free. An edge disappears here only
when the dependency does.

Two counts are reported, and they answer different questions:

* `features-only` reproduces the 2026-09-08 assessment exactly: files under
  `Basil.Server/Features`, references written `Basil.Server.Features.<Slice>`.
  It exists so the log has one number measured the same way from start to
  finish, and it necessarily falls as files leave that directory.
* `solution-wide` counts a reference to a slice's namespace in any project.
  This is the number C5 gates on, because it does not fall for free.

`Basil.Protocol` is excluded from the solution-wide reference pattern.
`Basil.Protocol.Irc` and `Basil.Protocol.Multiplayer` are wire-format libraries
that happen to share a name with a slice; they reference no feature and every
transport is entitled to depend on them. Counting them would have reported eight
edges that do not exist.

Comments are stripped before matching. A slice named only from an XML comment is
a documentation-only edge, reported separately, because it compiles away and the
assessment counted it separately too.

Run from the repository root:

    python plans/execution/measure-slice-graph.py
"""

import os
import re
import sys
from collections import defaultdict

SRC = "src"
FEATURES = os.path.join("src", "Basil.Server", "Features")
BLOCK_COMMENT = re.compile(r"/\*.*?\*/", re.S)
LINE_COMMENT = re.compile(r"//[^\n]*")


def strip_comments(text):
    """Remove comments so a slice named only in prose does not count as code."""
    return LINE_COMMENT.sub("", BLOCK_COMMENT.sub("", text))


def slice_names():
    """The slices, taken from the directories under Features/."""
    return sorted(d for d in os.listdir(FEATURES) if os.path.isdir(os.path.join(FEATURES, d)))


def owner_of(path, names):
    """The slice a file belongs to, from the first slice-named directory in its path."""
    for part in path.split(os.sep):
        if part in names:
            return part
    return None


def source_files(root):
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d not in ("bin", "obj")]
        for name in filenames:
            if name.endswith(".cs"):
                yield os.path.join(dirpath, name)


FEATURES_ONLY = r"\bBasil\.Server\.Features\.(%s)\b"
SOLUTION_WIDE = r"\bBasil\.(?!Protocol\b)(?:[A-Za-z0-9_]+\.)*(%s)\b"


def measure(root, names, pattern):
    reference = re.compile(pattern % "|".join(names))
    edges = defaultdict(set)  # (a, b) -> files naming b in code
    prose = defaultdict(set)  # (a, b) -> files naming b only in a comment
    for path in source_files(root):
        owner = owner_of(path, names)
        if owner is None:
            continue
        with open(path, encoding="utf-8-sig") as handle:
            text = handle.read()
        code = set(reference.findall(strip_comments(text)))
        for target in set(reference.findall(text)):
            if target == owner:
                continue
            (edges if target in code else prose)[(owner, target)].add(path)
    return edges, prose


def strongly_connected(nodes, adjacency):
    index, low, stack, on_stack, components, counter = {}, {}, [], set(), [], [0]

    def walk(v):
        index[v] = low[v] = counter[0]
        counter[0] += 1
        stack.append(v)
        on_stack.add(v)
        for w in adjacency[v]:
            if w not in index:
                walk(w)
                low[v] = min(low[v], low[w])
            elif w in on_stack:
                low[v] = min(low[v], index[w])
        if low[v] == index[v]:
            component = []
            while True:
                w = stack.pop()
                on_stack.discard(w)
                component.append(w)
                if w == v:
                    break
            components.append(sorted(component))

    for node in nodes:
        if node not in index:
            walk(node)
    return components


def report(label, root, names, pattern, detail):
    edges, prose = measure(root, names, pattern)
    adjacency = defaultdict(set)
    for (a, b) in edges:
        adjacency[a].add(b)

    live = sorted(edges)
    mutual = sorted({tuple(sorted(p)) for p in live if (p[1], p[0]) in edges})
    cycles = [c for c in strongly_connected(names, adjacency) if len(c) > 1]
    free = sorted(n for n in names if not adjacency[n])
    documentation_only = sorted(k for k in prose if k not in edges)

    print("== %s (%s)" % (label, root))
    print("live edges: %d" % len(live))
    print("mutual pairs: %d" % len(mutual))
    print("documentation-only edges: %d" % len(documentation_only))
    print("slices with no outgoing edge: %d (%s)" % (len(free), ", ".join(free) or "none"))
    for cycle in sorted(cycles, key=len, reverse=True):
        print("cycle of %d: %s" % (len(cycle), ", ".join(cycle)))
    if detail:
        print()
        for (a, b) in live:
            print("%s -> %s  (%d files)" % (a, b, len(edges[(a, b)])))
        for (a, b) in documentation_only:
            print("documentation only: %s -> %s" % (a, b))
    print()


def main():
    names = slice_names()
    print("slices: %d (%s)" % (len(names), ", ".join(names)))
    print()
    report("features-only, comparable to the assessment", FEATURES, names, FEATURES_ONLY, detail=False)
    report("solution-wide, the number C5 gates on", SRC, names, SOLUTION_WIDE, detail=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
