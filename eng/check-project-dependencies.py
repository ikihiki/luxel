#!/usr/bin/env python3
"""Validate ProjectReference cycles and the Controls/Terminal architecture boundary."""
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
projects = {p.resolve(): p for p in root.rglob("*.csproj") if not any(x in p.parts for x in ("bin", "obj"))}
graph = {p: [] for p in projects}
for absolute, relative in projects.items():
    try:
        xml = ET.parse(absolute)
    except ET.ParseError as error:
        print(f"Invalid project XML: {relative}: {error}", file=sys.stderr); sys.exit(1)
    for ref in xml.findall(".//ProjectReference"):
        include = ref.get("Include")
        if not include: continue
        target = (absolute.parent / include.replace("\\", "/")).resolve()
        if target in projects: graph[absolute].append(target)

def display(path: Path) -> str: return str(projects[path]).replace("\\", "/")

def find_path(start: Path, predicate):
    stack = [(start, [start])]
    while stack:
        node, path = stack.pop()
        for nxt in graph[node]:
            if nxt in path:
                cycle = path[path.index(nxt):] + [nxt]
                print("ProjectReference cycle:\n  " + "\n  -> ".join(map(display, cycle)), file=sys.stderr)
                sys.exit(1)
            if predicate(nxt): return path + [nxt]
            stack.append((nxt, path + [nxt]))
    return None

for project in graph: find_path(project, lambda _: False)
controls = [p for p in graph if p.name == "Luxel.Controls.csproj"]
terminals = {p for p in graph if p.stem == "Luxel.Terminal" or p.stem.startswith("Luxel.Terminal.")}
violations = []
for source in controls:
    path = find_path(source, lambda p: p in terminals)
    if path: violations.append(path)
for source in terminals:
    path = find_path(source, lambda p: p in controls)
    if path: violations.append(path)
if violations:
    for path in violations: print("Forbidden Controls/Terminal dependency:\n  " + "\n  -> ".join(map(display, path)), file=sys.stderr)
    sys.exit(1)
print(f"Project dependency graph OK ({len(graph)} projects).")
