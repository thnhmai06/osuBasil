"""Measure the live cross-slice dependency graph of Basil.Server.

An edge A -> B exists when any file whose namespace belongs to slice A names a
type from slice B in code, either through a using directive or a fully qualified
reference. Slices are the immediate directories under Features/.

Comments are stripped before matching. A slice named only from an XML comment is
a documentation-only edge, reported separately, because it compiles away and the
2026-09-08 assessment counted it separately too. Comparing against that
assessment's numbers requires the same definition of an edge, not a similar one.

Run from the repository root:

    python plans/execution/measure-slice-graph.py
"""

import os
import re
import sys
from collections import defaultdict

ROOT = os.path.join("src", "Basil.Server", "Features")
REFERENCE = re.compile(r"\bBasil\.Server\.Features\.([A-Za-z0-9_]+)")
BLOCK_COMMENT = re.compile(r"/\*.*?\*/", re.S)
LINE_COMMENT = re.compile(r"//[^\n]*")


def strip_comments(text):
    """Remove comments so a slice named only in prose does not count as code."""
    return LINE_COMMENT.sub("", BLOCK_COMMENT.sub("", text))


def slices():
    return sorted(d for d in os.listdir(ROOT) if os.path.isdir(os.path.join(ROOT, d)))


def measure():
    edges = defaultdict(set)  # (a, b) -> files naming b in code
    prose = defaultdict(set)  # (a, b) -> files naming b only in a comment
    for owner in slices():
        for dirpath, _, filenames in os.walk(os.path.join(ROOT, owner)):
            for name in filenames:
                if not name.endswith(".cs"):
                    continue
                path = os.path.join(dirpath, name)
                with open(path, encoding="utf-8-sig") as handle:
                    text = handle.read()
                code = set(REFERENCE.findall(strip_comments(text)))
                for target in set(REFERENCE.findall(text)):
                    if target == owner:
                        continue
                    if target in code:
                        edges[(owner, target)].add(path)
                    else:
                        prose[(owner, target)].add(path)
    return edges, prose


def strongly_connected(nodes, adjacency):
    index = {}
    low = {}
    stack = []
    on_stack = set()
    components = []
    counter = [0]

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


def main():
    names = slices()
    edges, prose = measure()
    adjacency = defaultdict(set)
    for (a, b) in edges:
        if b in names:
            adjacency[a].add(b)

    live = sorted(k for k in edges if k[1] in names)
    mutual = sorted({tuple(sorted(p)) for p in live if (p[1], p[0]) in edges})
    cycles = [c for c in strongly_connected(names, adjacency) if len(c) > 1]
    free = sorted(n for n in names if not adjacency[n])
    documentation_only = sorted(k for k in prose if k[1] in names and k not in edges)

    print("slices: %d (%s)" % (len(names), ", ".join(names)))
    print("live edges: %d" % len(live))
    print("mutual pairs: %d" % len(mutual))
    print("documentation-only edges: %d" % len(documentation_only))
    print("slices with no outgoing edge: %d (%s)" % (len(free), ", ".join(free) or "none"))
    for cycle in sorted(cycles, key=len, reverse=True):
        print("cycle of %d: %s" % (len(cycle), ", ".join(cycle)))
    print()
    for (a, b) in live:
        print("%s -> %s  (%d files)" % (a, b, len(edges[(a, b)])))
    for (a, b) in documentation_only:
        print("documentation only: %s -> %s" % (a, b))
    return 0


if __name__ == "__main__":
    sys.exit(main())
