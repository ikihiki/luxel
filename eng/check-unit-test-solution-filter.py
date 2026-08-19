#!/usr/bin/env python3
"""Validate that Luxel.UnitTests.slnf exactly tracks CI unit-test projects."""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

FILTER_NAME = "Luxel.UnitTests.slnf"
SOLUTION_NAME = "Luxel.slnx"


def normalized(path: str) -> str:
    return Path(path.replace("\\", "/")).as_posix()


def is_ci_unit_test(path: Path) -> bool:
    value = path.as_posix()
    return (
        path.name.endswith(".Tests.csproj")
        and "E2E" not in value
        and "E2e" not in value
        and ".Present.Tests" not in value
    )


def load_filter(path: Path) -> tuple[str, list[str]]:
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
        solution = payload["solution"]
        solution_path = normalized(solution["path"])
        projects = [normalized(project) for project in solution["projects"]]
    except (OSError, json.JSONDecodeError, KeyError, TypeError) as error:
        raise ValueError(f"Invalid {FILTER_NAME}: {error}") from error
    return solution_path, projects


def solution_projects(path: Path) -> set[str]:
    try:
        root = ET.parse(path).getroot()
    except (OSError, ET.ParseError) as error:
        raise ValueError(f"Invalid {SOLUTION_NAME}: {error}") from error
    return {
        normalized(element.attrib["Path"])
        for element in root.iter("Project")
        if "Path" in element.attrib
    }


def validate(root: Path) -> list[str]:
    errors: list[str] = []
    filter_path = root / FILTER_NAME
    solution_path = root / SOLUTION_NAME

    if not filter_path.is_file():
        return [f"Missing unit-test solution filter: {FILTER_NAME}"]
    if not solution_path.is_file():
        return [f"Missing solution: {SOLUTION_NAME}"]

    try:
        referenced_solution, registered = load_filter(filter_path)
        in_solution = solution_projects(solution_path)
    except ValueError as error:
        return [str(error)]

    if referenced_solution != SOLUTION_NAME:
        errors.append(
            f"{FILTER_NAME} must reference {SOLUTION_NAME}; found {referenced_solution!r}"
        )

    duplicates = sorted({project for project in registered if registered.count(project) > 1})
    for project in duplicates:
        errors.append(f"Duplicate project in {FILTER_NAME}: {project}")

    expected = {
        path.relative_to(root).as_posix()
        for path in (root / "tests").rglob("*.Tests.csproj")
        if is_ci_unit_test(path.relative_to(root))
    }
    actual = set(registered)

    for project in sorted(expected - actual):
        errors.append(f"CI unit-test project missing from {FILTER_NAME}: {project}")
    for project in sorted(actual - expected):
        errors.append(f"Unexpected project in {FILTER_NAME}: {project}")
    for project in sorted(actual):
        if not (root / project).is_file():
            errors.append(f"Project listed in {FILTER_NAME} does not exist: {project}")
        if project not in in_solution:
            errors.append(f"Project listed in {FILTER_NAME} is missing from {SOLUTION_NAME}: {project}")

    return errors


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    root = args.root.resolve()
    errors = validate(root)
    if errors:
        for error in errors:
            print(error, file=sys.stderr)
        return 1
    count = len(load_filter(root / FILTER_NAME)[1])
    print(f"Unit-test solution filter check passed: {count} projects")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
