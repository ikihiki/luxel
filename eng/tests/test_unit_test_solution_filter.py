#!/usr/bin/env python3
"""Tests for the CI unit-test solution-filter check."""
from __future__ import annotations

import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ENG = Path(__file__).resolve().parents[1]
CHECK = ENG / "check-unit-test-solution-filter.py"


class FilterFixture:
    def __init__(self, projects: list[str], registered: list[str] | None = None):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        for project in projects:
            path = self.root / project
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("<Project Sdk=\"Microsoft.NET.Sdk\" />\n", encoding="utf-8")
        solution = "<Solution>\n" + "".join(
            f'  <Project Path="{project}" />\n' for project in projects
        ) + "</Solution>\n"
        (self.root / "Luxel.slnx").write_text(solution, encoding="utf-8")
        payload = {
            "solution": {
                "path": "Luxel.slnx",
                "projects": projects if registered is None else registered,
            }
        }
        (self.root / "Luxel.UnitTests.slnf").write_text(
            json.dumps(payload), encoding="utf-8"
        )

    def run(self) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            [sys.executable, str(CHECK), "--root", str(self.root)],
            text=True,
            capture_output=True,
            check=False,
        )

    def close(self) -> None:
        self.temp.cleanup()


class UnitTestSolutionFilterTests(unittest.TestCase):
    def fixture(
        self, projects: list[str], registered: list[str] | None = None
    ) -> FilterFixture:
        fixture = FilterFixture(projects, registered)
        self.addCleanup(fixture.close)
        return fixture

    def test_accepts_exact_ci_unit_test_set(self) -> None:
        unit = "tests/UI/Luxel.UI.Tests/Luxel.UI.Tests.csproj"
        e2e = "tests/Gallery/Luxel.E2e.Tests/Luxel.E2e.Tests.csproj"
        present = "tests/Graphics/Luxel.WebGPU.Present.Tests/Luxel.WebGPU.Present.Tests.csproj"
        fixture = self.fixture([unit, e2e, present], [unit])
        result = fixture.run()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("1 projects", result.stdout)

    def test_rejects_missing_unit_test_project(self) -> None:
        project = "tests/Editor/Luxel.Editor.Tests/Luxel.Editor.Tests.csproj"
        fixture = self.fixture([project], [])
        result = fixture.run()
        self.assertNotEqual(0, result.returncode)
        self.assertIn("missing from Luxel.UnitTests.slnf", result.stderr)

    def test_rejects_excluded_or_unknown_project(self) -> None:
        e2e = "tests/Gallery/Luxel.E2e.Tests/Luxel.E2e.Tests.csproj"
        fixture = self.fixture([e2e], [e2e])
        result = fixture.run()
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Unexpected project", result.stderr)

    def test_rejects_project_missing_from_solution(self) -> None:
        project = "tests/UI/Luxel.UI.Tests/Luxel.UI.Tests.csproj"
        fixture = self.fixture([project])
        (fixture.root / "Luxel.slnx").write_text("<Solution />\n", encoding="utf-8")
        result = fixture.run()
        self.assertNotEqual(0, result.returncode)
        self.assertIn("missing from Luxel.slnx", result.stderr)

    def test_rejects_duplicate_registration(self) -> None:
        project = "tests/UI/Luxel.UI.Tests/Luxel.UI.Tests.csproj"
        fixture = self.fixture([project], [project, project])
        result = fixture.run()
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Duplicate project", result.stderr)


if __name__ == "__main__":
    unittest.main()
