#!/usr/bin/env python3
"""Fixture tests for the project graph and Gallery ownership checks."""
from __future__ import annotations

import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ENG = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ENG))
from project_graph import load_graph  # noqa: E402


def project(metadata: str = "", references: str = "") -> str:
    return f"""<Project Sdk=\"Microsoft.NET.Sdk\">
  <PropertyGroup>{metadata}</PropertyGroup>
  <ItemGroup>{references}</ItemGroup>
</Project>
"""


GALLERY_METADATA = """
    <LuxelProjectRole>GalleryCategory</LuxelProjectRole>
    <LuxelGalleryCategory>Resources</LuxelGalleryCategory>
    <LuxelSubsystem>Resources</LuxelSubsystem>
    <LuxelPlatform>Browser</LuxelPlatform>
    <LuxelArchitectureTier>Base</LuxelArchitectureTier>
    <IsPackable>false</IsPackable>
    <LuxelGalleryRegistrationIdentity>Resources.Base</LuxelGalleryRegistrationIdentity>
"""


TEST_METADATA = """
    <LuxelProjectRole>Test</LuxelProjectRole>
    <LuxelGalleryCategory>Audio</LuxelGalleryCategory>
    <LuxelSubsystem>Audio</LuxelSubsystem>
    <LuxelPlatform>Portable</LuxelPlatform>
    <LuxelArchitectureTier>Extension</LuxelArchitectureTier>
"""


class RepoFixture:
    def __init__(self, files: dict[str, str], baseline: dict | None = None):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        for name, content in files.items():
            path = self.root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(content, encoding="utf-8")
        projects = sorted(name for name in files if name.endswith(".csproj"))
        solution = "<Solution>\n" + "".join(f'  <Project Path="{name}" />\n' for name in projects) + "</Solution>\n"
        (self.root / "Luxel.slnx").write_text(solution, encoding="utf-8")
        baseline_path = self.root / "eng" / "project-architecture-baseline.json"
        baseline_path.parent.mkdir(parents=True, exist_ok=True)
        baseline_path.write_text(json.dumps(baseline or {}), encoding="utf-8")

    def run(self, script: str) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            [sys.executable, str(ENG / script), "--root", str(self.root)],
            text=True, capture_output=True, check=False,
        )

    def close(self) -> None:
        self.temp.cleanup()


class ArchitectureFixtureTests(unittest.TestCase):
    def fixture(self, files: dict[str, str], baseline: dict | None = None) -> RepoFixture:
        fixture = RepoFixture(files, baseline)
        self.addCleanup(fixture.close)
        return fixture

    def test_valid_fixture_and_analyzer_reference_classification(self) -> None:
        fixture = self.fixture({
            "src/App/App.csproj": project(references='''
    <ProjectReference Include="../../tools/Generator/Generator.csproj"
                      OutputItemType="Analyzer" ReferenceOutputAssembly="false" />'''),
            "src/ResourceGallery/ResourceGallery.csproj": project(GALLERY_METADATA),
            "tools/Generator/Generator.csproj": project(),
        })
        result = fixture.run("check-project-dependencies.py")
        self.assertEqual(0, result.returncode, result.stderr)
        graph, errors = load_graph(fixture.root)
        self.assertFalse(errors)
        app = graph[(fixture.root / "src/App/App.csproj").resolve()]
        self.assertEqual("Analyzer", app.references[0].kind)

    def test_rejects_project_reference_custom_build_flavor(self) -> None:
        fixture = self.fixture({
            "src/App/App.csproj": project(references='''
    <ProjectReference Include="../Library/Library.csproj"
                      AdditionalProperties="LuxelBrowserWasm=true" />'''),
            "src/Library/Library.csproj": project(),
        })
        result = fixture.run("check-project-dependencies.py")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("unsafe custom build flavor", result.stderr)
        self.assertIn("LuxelBrowserWasm=true", result.stderr)

    def test_accepts_nested_managed_test_with_explicit_metadata(self) -> None:
        fixture = self.fixture({
            "tests/Audio/Luxel.Audio.Tests/Luxel.Audio.Tests.csproj": project(TEST_METADATA),
        })
        result = fixture.run("check-project-dependencies.py")
        self.assertEqual(0, result.returncode, result.stderr)

    def test_rejects_flat_or_unlisted_managed_test_project(self) -> None:
        fixture = self.fixture({
            "tests/Luxel.Audio.Tests/Luxel.Audio.Tests.csproj": project(TEST_METADATA),
        })
        (fixture.root / "Luxel.slnx").write_text("<Solution />\n", encoding="utf-8")
        result = fixture.run("check-project-dependencies.py")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("tests/<Category>/<Project>/<Project>.csproj", result.stderr)
        self.assertIn("Managed test project missing from solution", result.stderr)

    def test_rejects_unbaselined_production_gallery_reference(self) -> None:
        fixture = self.fixture({
            "src/App/App.csproj": project(references='<ProjectReference Include="../ResourceGallery/ResourceGallery.csproj" />'),
            "src/ResourceGallery/ResourceGallery.csproj": project(GALLERY_METADATA),
        })
        result = fixture.run("check-project-dependencies.py")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Production project references Gallery", result.stderr)

    def test_ratcheted_baseline_allows_only_named_legacy_edge(self) -> None:
        edge = "src/App/App.csproj -> src/ResourceGallery/ResourceGallery.csproj [Compile]"
        fixture = self.fixture({
            "src/App/App.csproj": project(references='<ProjectReference Include="../ResourceGallery/ResourceGallery.csproj" />'),
            "src/ResourceGallery/ResourceGallery.csproj": project(GALLERY_METADATA),
        }, {"legacy_production_gallery_references": [edge]})
        result = fixture.run("check-project-dependencies.py")
        self.assertEqual(0, result.returncode, result.stderr)

    def test_rejects_cross_category_native_to_base_reference(self) -> None:
        native = GALLERY_METADATA.replace("Resources", "Platform").replace("Browser", "Native").replace("Base", "Native")
        fixture = self.fixture({
            "src/Native/Native.csproj": project(native, '<ProjectReference Include="../ResourceGallery/ResourceGallery.csproj" />'),
            "src/ResourceGallery/ResourceGallery.csproj": project(GALLERY_METADATA),
        })
        result = fixture.run("check-project-dependencies.py")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("another category's base Gallery", result.stderr)

    def test_rejects_missing_reference_and_duplicate_identities(self) -> None:
        second = GALLERY_METADATA.replace("Resources.Base", "Resources.Base")
        fixture = self.fixture({
            "src/One/One.csproj": project(GALLERY_METADATA + "<AssemblyName>Duplicate</AssemblyName>"),
            "src/Two/Two.csproj": project(second + "<AssemblyName>Duplicate</AssemblyName>", '<ProjectReference Include="../Missing/Missing.csproj" />'),
        })
        result = fixture.run("check-project-dependencies.py")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Missing Compile ProjectReference", result.stderr)
        self.assertIn("Duplicate assembly name", result.stderr)
        self.assertIn("Duplicate Gallery registration identity", result.stderr)

    def test_rejects_browser_to_native_closure(self) -> None:
        browser = GALLERY_METADATA.replace("Resources.Base", "Resources.Browser")
        native = GALLERY_METADATA.replace("Resources", "Platform").replace("Browser", "Native").replace("Base", "Native")
        fixture = self.fixture({
            "src/Browser/Browser.csproj": project(browser, '<ProjectReference Include="../Native/Native.csproj" />'),
            "src/Native/Native.csproj": project(native),
        })
        result = fixture.run("check-project-dependencies.py")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Browser project reaches a Native dependency", result.stderr)

    def test_requires_exactly_one_browser_base_project_per_category(self) -> None:
        duplicate = GALLERY_METADATA.replace("Resources.Base", "Resources.Other")
        fixture = self.fixture({
            "src/One/One.csproj": project(GALLERY_METADATA),
            "src/Two/Two.csproj": project(duplicate),
        }, {"required_gallery_categories": ["Resources"]})
        result = fixture.run("check-project-dependencies.py")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("exactly one Browser/Base project; found 2", result.stderr)

    def test_requires_approved_native_project_to_reference_its_base_once(self) -> None:
        native = GALLERY_METADATA.replace("Resources", "Platform").replace("Browser", "Native").replace("Base", "Native")
        platform_base = GALLERY_METADATA.replace("Resources", "Platform")
        fixture = self.fixture({
            "src/PlatformBase/PlatformBase.csproj": project(platform_base),
            "src/PlatformNative/PlatformNative.csproj": project(native),
        }, {
            "required_gallery_categories": ["Platform"],
            "approved_native_gallery_categories": ["Platform"],
        })
        result = fixture.run("check-project-dependencies.py")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("must reference exactly one same-category Browser/Base Gallery; found 0", result.stderr)

    def test_rejects_unapproved_native_gallery_category(self) -> None:
        native = GALLERY_METADATA.replace("Browser", "Native").replace("Base", "Native")
        fixture = self.fixture({
            "src/ResourcesBase/ResourceBase.csproj": project(GALLERY_METADATA),
            "src/ResourcesNative/ResourceNative.csproj": project(
                native, '<ProjectReference Include="../ResourceBase/ResourceBase.csproj" />'),
        }, {"required_gallery_categories": ["Resources"]})
        result = fixture.run("check-project-dependencies.py")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("has an unapproved Native project", result.stderr)

    def test_rejects_duplicate_native_gallery_projects(self) -> None:
        native = GALLERY_METADATA.replace("Resources", "Platform").replace("Browser", "Native").replace("Base", "Native")
        second_native = native.replace("Platform.Native", "Platform.Native.Other")
        platform_base = GALLERY_METADATA.replace("Resources", "Platform")
        fixture = self.fixture({
            "src/PlatformBase/PlatformBase.csproj": project(platform_base),
            "src/NativeOne/NativeOne.csproj": project(
                native, '<ProjectReference Include="../PlatformBase/PlatformBase.csproj" />'),
            "src/NativeTwo/NativeTwo.csproj": project(
                second_native, '<ProjectReference Include="../PlatformBase/PlatformBase.csproj" />'),
        }, {
            "required_gallery_categories": ["Platform"],
            "approved_native_gallery_categories": ["Platform"],
        })
        result = fixture.run("check-project-dependencies.py")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("may have at most one Native project; found 2", result.stderr)

    def test_rejects_base_to_base_gallery_category_reference(self) -> None:
        audio = GALLERY_METADATA.replace("Resources", "Audio")
        fixture = self.fixture({
            "src/ResourcesBase/ResourceBase.csproj": project(
                GALLERY_METADATA, '<ProjectReference Include="../AudioBase/AudioBase.csproj" />'),
            "src/AudioBase/AudioBase.csproj": project(audio),
        }, {"required_gallery_categories": ["Resources", "Audio"]})
        result = fixture.run("check-project-dependencies.py")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Forbidden Gallery category reference", result.stderr)

    def test_accepts_one_approved_native_extension_over_its_base(self) -> None:
        native = GALLERY_METADATA.replace("Resources", "Platform").replace("Browser", "Native").replace("Base", "Native")
        platform_base = GALLERY_METADATA.replace("Resources", "Platform")
        fixture = self.fixture({
            "src/PlatformBase/PlatformBase.csproj": project(platform_base),
            "src/PlatformNative/PlatformNative.csproj": project(
                native, '<ProjectReference Include="../PlatformBase/PlatformBase.csproj" />'),
        }, {
            "required_gallery_categories": ["Platform"],
            "approved_native_gallery_categories": ["Platform"],
        })
        result = fixture.run("check-project-dependencies.py")
        self.assertEqual(0, result.returncode, result.stderr)

    def test_rejects_production_story_ownership_and_mathematics_registrar(self) -> None:
        fixture = self.fixture({
            "src/App/App.csproj": project(),
            "src/App/Stories.cs": '[Story("Bad/Owner")] static object Build() => new();',
            "src/Shared/Luxel.Mathematics/Luxel.Mathematics.csproj": project(),
            "src/Shared/Luxel.Mathematics/Register.cs": "StoryRegistry.Register(value);",
        })
        result = fixture.run("check-gallery-ownership.py")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Production project owns Gallery stories/registrar", result.stderr)
        self.assertIn("Luxel.Mathematics must not contain stories", result.stderr)


if __name__ == "__main__":
    unittest.main()
