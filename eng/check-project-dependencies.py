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

by_stem = {p.stem: p for p in graph}
def forbid_closure(source_stem: str, forbidden, label: str):
    source = by_stem.get(source_stem)
    if source is None: return
    path = find_path(source, lambda p: forbidden(p.stem))
    if path:
        print(f"Forbidden {label} dependency:\n  " + "\n  -> ".join(map(display, path)), file=sys.stderr)
        sys.exit(1)

forbid_closure("Luxel.UI", lambda stem: stem.startswith("Luxel.Gallery"), "UI/Gallery reverse")
forbid_closure("Luxel.UI.Generators", lambda stem: stem.startswith("Luxel.Gallery"), "UI generators/Gallery reverse")
forbid_closure("Luxel.Gallery", lambda stem: stem in {
    "Luxel.Gallery.Generators", "Luxel.Gallery.UI", "Luxel.Gallery.Host", "Luxel.Gallery.Site",
    "Luxel.Gallery.Stories", "Luxel.Gallery.Stories.CoreUi"
}, "Gallery core reverse")
forbid_closure("Luxel.Gallery.Site", lambda stem: stem == "Luxel.Gallery.Host", "Site/Host")

native_heavy = lambda stem: (
    stem in {"Luxel.Platform.Windows", "Luxel.Input.XInput", "Luxel.Audio", "Luxel.Audio.Windows",
             "Luxel.Graphics.Vulkan", "Luxel.Graphics.DirectX12", "Luxel.Graphics.TwoD.Skia",
             "Luxel.Typography.Icu"}
    or stem.startswith("Luxel.Terminal")
    or stem.startswith("Luxel.Scripting")
)
browser_forbidden = lambda stem: native_heavy(stem) or stem in {
    "Luxel.Gallery.Host", "Luxel.Gallery.Site", "Luxel.Gallery.Stories"
}
forbid_closure("Luxel.Gallery.Stories.CoreUi", browser_forbidden, "CoreUi browser closure")
forbid_closure("LuxelWebGpuBrowser", browser_forbidden, "browser host closure")
forbid_closure("Luxel.Gallery.RuntimeManifest", browser_forbidden, "runtime manifest closure")

print(f"Project dependency graph OK ({len(graph)} projects).")
