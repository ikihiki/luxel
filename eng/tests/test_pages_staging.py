#!/usr/bin/env python3
"""Fixture and workflow-contract tests for GitHub Pages staging."""
from __future__ import annotations

from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
STAGE_SCRIPT = ROOT / "eng" / "stage-pages.sh"
CHOOSER = ROOT / "eng" / "pages" / "index.html"
WORKFLOWS = (
    ROOT / ".github" / "workflows" / "deploy-pages.yml",
    ROOT / ".github" / "workflows" / "preview-pages.yml",
)


class PagesFixture:
    def __init__(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.chooser = self.root / "index.html"
        self.output = self.root / "output"
        self.apps = {
            name: self.root / name
            for name in ("gallery", "editor", "demo")
        }
        self.write_chooser()
        for name, directory in self.apps.items():
            self.write_app(directory, marker=f"{name}.txt")
        demo = self.apps["demo"]
        (demo / "demo").mkdir()
        (demo / "demo" / "luxel-demo.project.json").write_text(
            '{"startScene":"Scenes/Main.scene"}\n', encoding="utf-8")
        (demo / "main.js").write_text(
            'import { installLuxelEditorApi } from "./editor-api.js";\n',
            encoding="utf-8",
        )

    def write_chooser(self, links: tuple[str, ...] = ("gallery/", "editor/", "demo/")) -> None:
        anchors = "".join(f'<a href="{link}">{link}</a>' for link in links)
        self.chooser.write_text(f"<!doctype html><nav>{anchors}</nav>\n", encoding="utf-8")

    @staticmethod
    def write_app(directory: Path, base_href: str | None = "./", marker: str = "asset.txt") -> None:
        directory.mkdir(parents=True, exist_ok=True)
        base = "" if base_href is None else f'<base href="{base_href}">'
        (directory / "index.html").write_text(
            f'<!doctype html><head>{base}</head><body></body>\n', encoding="utf-8")
        framework = directory / "_framework"
        framework.mkdir()
        (framework / "blazor.webassembly.js").write_text("// startup\n", encoding="utf-8")
        (directory / marker).write_text(marker + "\n", encoding="utf-8")

    def run(self, output: Path | str | None = None) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            [
                str(STAGE_SCRIPT),
                str(self.chooser),
                str(self.apps["gallery"]),
                str(self.apps["editor"]),
                str(self.apps["demo"]),
                str(self.output if output is None else output),
            ],
            cwd=self.root,
            text=True,
            capture_output=True,
            check=False,
        )

    def close(self) -> None:
        self.temp.cleanup()


class PagesStagingTests(unittest.TestCase):
    def fixture(self) -> PagesFixture:
        fixture = PagesFixture()
        self.addCleanup(fixture.close)
        return fixture

    def test_stages_three_apps_without_a_root_framework(self) -> None:
        fixture = self.fixture()

        result = fixture.run()

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertTrue((fixture.output / ".nojekyll").is_file())
        self.assertEqual(fixture.chooser.read_text(encoding="utf-8"),
                         (fixture.output / "index.html").read_text(encoding="utf-8"))
        for name in ("gallery", "editor", "demo"):
            self.assertTrue((fixture.output / name / "index.html").is_file())
            self.assertTrue((fixture.output / name / "_framework" / "blazor.webassembly.js").is_file())
        self.assertTrue((fixture.output / "demo" / "demo" / "luxel-demo.project.json").is_file())
        self.assertTrue((fixture.output / "demo" / "main.js").is_file())
        self.assertFalse((fixture.output / "_framework").exists())
        self.assertIn("Browser Editor demo", result.stdout)
        self.assertIn("/demo/", result.stdout)

    def test_rejects_chooser_without_demo_link(self) -> None:
        fixture = self.fixture()
        fixture.write_chooser(("gallery/", "editor/"))

        result = fixture.run()

        self.assertNotEqual(0, result.returncode)
        self.assertIn("must link to demo/", result.stderr)
        self.assertFalse(fixture.output.exists())

    def test_rejects_missing_relative_base(self) -> None:
        fixture = self.fixture()
        self._replace_app(fixture, "demo", None)

        result = fixture.run()

        self.assertNotEqual(0, result.returncode)
        self.assertIn('relative <base href="./">', result.stderr)
        self.assertFalse(fixture.output.exists())

    def test_rejects_absolute_base(self) -> None:
        fixture = self.fixture()
        self._replace_app(fixture, "editor", "/editor/")

        result = fixture.run()

        self.assertNotEqual(0, result.returncode)
        self.assertIn('relative <base href="./">', result.stderr)
        self.assertFalse(fixture.output.exists())

    def test_rejects_unsafe_output_directory(self) -> None:
        for unsafe_output in (".", "..", "/"):
            with self.subTest(output=unsafe_output):
                fixture = self.fixture()

                result = fixture.run(unsafe_output)

                self.assertNotEqual(0, result.returncode)
                self.assertIn("unsafe output directory", result.stderr)
                self.assertTrue(fixture.chooser.is_file())

    def test_rejects_output_overlapping_an_app(self) -> None:
        fixture = self.fixture()
        unsafe_output = fixture.apps["demo"] / "staged"

        result = fixture.run(unsafe_output)

        self.assertNotEqual(0, result.returncode)
        self.assertIn("must not overlap an input wwwroot", result.stderr)
        self.assertTrue((fixture.apps["demo"] / "index.html").is_file())

    @staticmethod
    def _replace_app(fixture: PagesFixture, name: str, base_href: str | None) -> None:
        directory = fixture.apps[name]
        for child in sorted(directory.rglob("*"), reverse=True):
            if child.is_file() or child.is_symlink():
                child.unlink()
            elif child.is_dir():
                child.rmdir()
        directory.rmdir()
        fixture.write_app(directory, base_href=base_href, marker=f"{name}.txt")


class PagesContractTests(unittest.TestCase):
    def test_repository_chooser_links_all_three_relative_routes(self) -> None:
        chooser = CHOOSER.read_text(encoding="utf-8")
        for route in ("gallery/", "editor/", "demo/"):
            self.assertIn(f'href="{route}"', chooser)
        self.assertIn("Browser Editor Demo (Fixed Project)", chooser)
        self.assertIn('aria-label="Luxel applications"', chooser)

    def test_deploy_and_preview_workflows_publish_and_smoke_demo(self) -> None:
        for workflow in WORKFLOWS:
            with self.subTest(workflow=workflow.name):
                text = workflow.read_text(encoding="utf-8")
                self.assertIn("dotnet restore samples/LuxelEditorBrowser/LuxelEditorBrowser.csproj", text)
                self.assertIn(
                    "dotnet publish samples/LuxelEditorBrowser/LuxelEditorBrowser.csproj "
                    "--no-restore --configuration Release -p:BlazorFingerprintBlazorJs=false",
                    text,
                )
                self.assertIn(
                    "samples/LuxelEditorBrowser/bin/Release/net10.0/publish/wwwroot",
                    text,
                )
                self.assertIn('for route in ("", "gallery/", "editor/", "demo/"):', text)
                self.assertIn('"demo/luxel-demo.project.json"', text)
                self.assertIn('"main.js"', text)
                self.assertIn('"_framework/blazor.webassembly"', text)
                self.assertIn('"editor-api.js"', text)


if __name__ == "__main__":
    unittest.main()
