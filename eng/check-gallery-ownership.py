#!/usr/bin/env python3
"""Validate Gallery source ownership and story registration identities."""
from __future__ import annotations

import argparse
from pathlib import Path
import re
import sys

from project_graph import load_graph, relative

REGISTRAR_MARKER = re.compile(r"\b(?:StoryRegistry\s*\.\s*Register|StoryRegistration_[A-Za-z0-9_]+)\b")
OWNERSHIP_MARKER = re.compile(r"\[\s*(?:Story|ComponentStory)(?:Attribute)?\s*\(")


def project_sources(project_path: Path) -> list[Path]:
    return sorted(
        path for path in project_path.parent.rglob("*.cs")
        if not any(part in {"bin", "obj"} for part in path.parts)
    )


def validate(root: Path, solution: str, baseline_path: Path) -> list[str]:
    projects, errors = load_graph(root, solution)

    for project in projects.values():
        source_files = project_sources(project.path)
        rel_project = relative(root, project.path)
        owns_gallery_source = False
        for source in source_files:
            text = source.read_text(encoding="utf-8-sig", errors="replace")
            if OWNERSHIP_MARKER.search(text) or REGISTRAR_MARKER.search(text):
                owns_gallery_source = True

        if project.role == "Production" and owns_gallery_source:
            errors.append(f"Production project owns Gallery stories/registrar: {rel_project}")
        if project.path.stem == "Luxel.Mathematics":
            if project.role != "Production" or project.category != "External":
                errors.append("Luxel.Mathematics must remain category-external Production metadata")
            if owns_gallery_source:
                errors.append("Luxel.Mathematics must not contain stories or a Gallery registrar")

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
    print("Gallery ownership OK.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
