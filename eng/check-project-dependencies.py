#!/usr/bin/env python3
"""Validate the typed Luxel project graph and architecture boundaries."""
from __future__ import annotations

import argparse
from pathlib import Path
import sys

from project_graph import (
    cycles, find_path, load_baseline, load_graph, native_heavy, reference_key,
    relative, validate_metadata,
)


def validate(root: Path, solution: str, baseline_path: Path) -> list[str]:
    projects, errors = load_graph(root, solution)
    baseline = load_baseline(baseline_path)
    errors.extend(validate_metadata(root, projects))

    for cycle in cycles(projects):
        errors.append("ProjectReference cycle: " + " -> ".join(relative(root, p) for p in cycle))

    legacy = set(baseline.get("legacy_production_gallery_references", []))
    seen_legacy: set[str] = set()
    cross_category = set(baseline.get("legacy_cross_category_native_base_references", []))
    seen_cross: set[str] = set()
    for project in projects.values():
        for reference in project.references:
            target = projects.get(reference.target)
            if target is None:
                continue
            key = reference_key(root, reference)
            if project.role == "Production" and target.is_gallery:
                if key in legacy:
                    seen_legacy.add(key)
                else:
                    errors.append(f"Production project references Gallery: {key}")
            if (
                project.role == "GalleryCategory" and project.tier == "Native"
                and target.role == "GalleryCategory" and target.tier == "Base"
                and project.category != target.category
            ):
                if key in cross_category:
                    seen_cross.add(key)
                else:
                    errors.append(f"Native Gallery references another category's base Gallery: {key}")

    stale = sorted(legacy - seen_legacy)
    if stale:
        errors.append("Stale legacy production/Gallery baseline entries:\n  " + "\n  ".join(stale))
    stale_cross = sorted(cross_category - seen_cross)
    if stale_cross:
        errors.append("Stale cross-category Gallery baseline entries:\n  " + "\n  ".join(stale_cross))

    categories = {p.category for p in projects.values() if p.role == "GalleryCategory"}
    required = set(baseline.get("required_gallery_categories", []))
    missing_allowed = set(baseline.get("missing_gallery_categories", []))
    unexpected_missing = required - categories - missing_allowed
    if unexpected_missing:
        errors.append("Required Gallery categories are missing: " + ", ".join(sorted(unexpected_missing)))
    stale_missing = missing_allowed & categories
    if stale_missing:
        errors.append("Stale missing Gallery category baseline entries: " + ", ".join(sorted(stale_missing)))

    native_categories = {p.category for p in projects.values() if p.role == "GalleryCategory" and p.platform == "Native"}
    forbidden_native = native_categories - {"Platform"}
    if forbidden_native:
        errors.append("Only Platform is approved for Native Gallery projects: " + ", ".join(sorted(forbidden_native)))

    for project in projects.values():
        if project.platform != "Browser":
            continue
        path = find_path(projects, project.path, native_heavy)
        if path:
            errors.append(
                "Browser project reaches a Native dependency: "
                + " -> ".join(relative(root, p) for p in path)
            )

    # Preserve the pre-graph-checker boundaries while expressing them over compile/runtime edges.
    by_stem = {p.path.stem: p for p in projects.values()}
    def forbid_closure(source_stem: str, predicate, label: str) -> None:
        source = by_stem.get(source_stem)
        if source is None:
            return
        path = find_path(projects, source.path, predicate)
        if path:
            errors.append(
                f"Forbidden {label} dependency: "
                + " -> ".join(relative(root, p) for p in path)
            )

    gallery_project = lambda p: p.is_gallery
    forbid_closure("Luxel.UI", gallery_project, "UI/Gallery reverse")
    forbid_closure("Luxel.UI.Generators", gallery_project, "UI generators/Gallery reverse")
    forbid_closure("Luxel.Gallery", lambda p: p.path.stem in {
        "Luxel.Gallery.Generators", "Luxel.Gallery.UI", "Luxel.Gallery.Native",
        "Luxel.Gallery.Stories", "Luxel.Resources.Gallery",
    }, "Gallery core reverse")
    forbid_closure("Luxel.Resources", lambda p: p.path.stem.endswith(".Browser"), "Resources core/browser reverse")
    forbid_closure("Luxel.Framework.Game", native_heavy, "Game portable closure")
    forbid_closure("Luxel.Framework.Game.Browser", lambda p: native_heavy(p) or p.path.stem in {
        "Luxel.Framework.UI", "Luxel.Framework.DevTools", "Luxel.Framework.Game.Native",
    }, "Game browser closure")
    playground_allowed_native = {"Luxel.Scripting", "Luxel.Scripting.Roslyn.Web"}
    forbid_closure("LuxelPlaygroundBrowser", lambda p: (
        native_heavy(p) and p.path.stem not in playground_allowed_native
    ) or p.path.stem in {
        "Luxel.Gallery.Native", "Luxel.Gallery.Stories", "Luxel.Scripting.Framework",
    }, "playground browser host closure")

    resource_root = (root / "src" / "Resource").resolve()
    for project in projects.values():
        if resource_root not in project.path.parents or project.path.stem == "Luxel.Resources.Gallery":
            continue
        path = find_path(projects, project.path, gallery_project)
        if path:
            errors.append(
                "Forbidden production Resource/Gallery reverse dependency: "
                + " -> ".join(relative(root, p) for p in path)
            )

    controls = by_stem.get("Luxel.Controls")
    terminals = [p for p in projects.values() if p.path.stem == "Luxel.Terminal" or p.path.stem.startswith("Luxel.Terminal.")]
    boundary_pairs = ([(controls, p) for p in terminals] + [(p, controls) for p in terminals]) if controls else []
    for source, target in boundary_pairs:
        if target is None:
            continue
        path = find_path(projects, source.path, lambda p, target=target: p.path == target.path)
        if path:
            errors.append("Forbidden Controls/Terminal dependency: " + " -> ".join(relative(root, p) for p in path))

    return errors


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--solution", default="Luxel.slnx")
    parser.add_argument("--baseline", type=Path)
    args = parser.parse_args()
    root = args.root.resolve()
    baseline = args.baseline or root / "eng" / "project-architecture-baseline.json"
    errors = validate(root, args.solution, baseline)
    if errors:
        print("\n\n".join(errors), file=sys.stderr)
        return 1
    projects, _ = load_graph(root, args.solution)
    print(f"Project dependency graph OK ({len(projects)} projects).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
