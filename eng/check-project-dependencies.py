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

gallery_leaf = lambda stem: stem.startswith("Luxel.Gallery") or stem == "Luxel.Resources.Gallery"
forbid_closure("Luxel.UI", gallery_leaf, "UI/Gallery reverse")
forbid_closure("Luxel.UI.Generators", gallery_leaf, "UI generators/Gallery reverse")
forbid_closure("Luxel.Gallery", lambda stem: stem in {
    "Luxel.Gallery.Generators", "Luxel.Gallery.UI", "Luxel.Gallery.Native",
    "Luxel.Gallery.Stories", "Luxel.Gallery.Stories.CoreUi", "Luxel.Resources.Gallery"
}, "Gallery core reverse")

resource_root = (root / "src" / "Resource").resolve()
for source in graph:
    if resource_root not in source.parents or source.stem == "Luxel.Resources.Gallery": continue
    path = find_path(source, lambda p: gallery_leaf(p.stem))
    if path:
        print("Forbidden production Resource/Gallery reverse dependency:\n  " + "\n  -> ".join(map(display, path)), file=sys.stderr)
        sys.exit(1)

native_heavy = lambda stem: (
    stem in {"Luxel.Platform.Windows", "Luxel.Input.XInput", "Luxel.Audio.Windows", "Luxel.Audio.Silk",
             "Luxel.Graphics.Vulkan", "Luxel.Graphics.DirectX12", "Luxel.Graphics.TwoD.Skia",
             "Luxel.Typography.Icu"}
    or stem.startswith("Luxel.Terminal")
    or stem.startswith("Luxel.Scripting")
)
browser_forbidden = lambda stem: native_heavy(stem) or stem in {
    "Luxel.Gallery.Native", "Luxel.Gallery.Stories"
}
forbid_closure("Luxel.Resources", lambda stem: stem.endswith(".Browser"), "Resources core/browser reverse")
forbid_closure("Luxel.Framework.Game", native_heavy, "Game portable closure")
forbid_closure("Luxel.Framework.Game.Browser", lambda stem: browser_forbidden(stem) or stem in {
    "Luxel.Framework.UI", "Luxel.Framework.DevTools", "Luxel.Framework.Game.Native"
}, "Game browser closure")
forbid_closure("Luxel.Resources.Gallery", browser_forbidden, "Resource Gallery browser closure")
forbid_closure("Luxel.Gallery.Stories.CoreUi", browser_forbidden, "CoreUi browser closure")
forbid_closure("Luxel.Gallery.Browser", browser_forbidden, "browser Gallery closure")
forbid_closure("GalleryBrowser", browser_forbidden, "browser Gallery executable closure")
forbid_closure("GalleryE2E.Browser", browser_forbidden, "browser Gallery E2E closure")
# The playground host intentionally carries the transport-neutral scripting contracts and the
# browser-specific Roslyn implementation. Keep every other native-heavy dependency forbidden.
playground_browser_forbidden = lambda stem: (
    native_heavy(stem) and stem not in {"Luxel.Scripting", "Luxel.Scripting.Roslyn.Web"}
) or stem in {"Luxel.Gallery.Native", "Luxel.Gallery.Stories", "Luxel.Scripting.Framework"}
forbid_closure("LuxelPlaygroundBrowser", playground_browser_forbidden, "playground browser host closure")

print(f"Project dependency graph OK ({len(graph)} projects).")
