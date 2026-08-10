#!/usr/bin/env python3
"""Shared MSBuild project graph model for Luxel architecture checks."""
from __future__ import annotations

from dataclasses import dataclass, field
import json
from pathlib import Path
import xml.etree.ElementTree as ET

ROLE_VALUES = {
    "Production", "GalleryInfrastructure", "GalleryCategory", "GalleryHost",
    "Test", "Sample", "Tool", "Analyzer",
}
CATEGORY_VALUES = {
    "External", "Shared", "Platform", "Resources", "Audio", "UI", "Graphics", "Input",
    "Framework", "Animation", "Particles", "Scripting", "Editor", "DevTools", "GamesSamples", "GalleryDocs", "CoreUi",
}
PLATFORM_VALUES = {"Portable", "Browser", "Native"}
TIER_VALUES = {"Product", "Foundation", "Base", "Native", "Extension", "Host"}
REFERENCE_KINDS = {"Compile", "Analyzer"}

@dataclass(frozen=True)
class Reference:
    source: Path
    target: Path
    kind: str

@dataclass
class Project:
    path: Path
    role: str
    category: str
    subsystem: str
    platform: str
    tier: str
    is_packable: bool
    assembly_name: str
    registration_identity: str
    explicit_metadata: set[str] = field(default_factory=set)
    references: list[Reference] = field(default_factory=list)
    in_solution: bool = False

    @property
    def is_gallery(self) -> bool:
        return self.role.startswith("Gallery")


def _text(root: ET.Element, name: str) -> str | None:
    values = [node.text.strip() for node in root.findall(f".//{name}") if node.text and node.text.strip()]
    return values[-1] if values else None


def _bool(value: str | None, default: bool) -> bool:
    if value is None:
        return default
    return value.lower() == "true"


def _default_role(relative: Path) -> str:
    if relative.parts and relative.parts[0] == "tests":
        return "Test"
    if relative.parts and relative.parts[0] in {"samples", "gallery"}:
        return "Sample"
    if relative.parts and relative.parts[0] == "eng":
        return "Tool"
    return "Production"


def _solution_projects(root: Path, solution: Path) -> tuple[set[Path], list[str]]:
    errors: list[str] = []
    members: set[Path] = set()
    if not solution.exists():
        return members, [f"Solution file not found: {solution.relative_to(root)}"]
    try:
        xml = ET.parse(solution).getroot()
    except ET.ParseError as error:
        return members, [f"Invalid solution XML: {solution.relative_to(root)}: {error}"]
    for item in xml.findall(".//Project"):
        include = item.get("Path")
        if not include:
            continue
        target = (root / include.replace("\\", "/")).resolve()
        if target in members:
            errors.append(f"Duplicate solution project: {include}")
        members.add(target)
        if not target.exists():
            errors.append(f"Missing solution project: {include}")
    return members, errors


def load_baseline(path: Path | None) -> dict:
    if path is None or not path.exists():
        return {}
    with path.open(encoding="utf-8") as stream:
        return json.load(stream)


def load_graph(root: Path, solution_name: str = "Luxel.slnx") -> tuple[dict[Path, Project], list[str]]:
    root = root.resolve()
    solution_members, errors = _solution_projects(root, root / solution_name)
    paths = sorted(
        p.resolve() for p in root.rglob("*.csproj")
        if not any(part in {"bin", "obj", ".git"} for part in p.parts)
    )
    projects: dict[Path, Project] = {}
    xml_roots: dict[Path, ET.Element] = {}
    metadata_names = {
        "LuxelProjectRole", "LuxelGalleryCategory", "LuxelSubsystem",
        "LuxelPlatform", "LuxelArchitectureTier", "IsPackable",
        "AssemblyName", "LuxelGalleryRegistrationIdentity",
    }
    for path in paths:
        relative = path.relative_to(root)
        try:
            xml = ET.parse(path).getroot()
        except ET.ParseError as error:
            errors.append(f"Invalid project XML: {relative}: {error}")
            continue
        xml_roots[path] = xml
        values = {name: _text(xml, name) for name in metadata_names}
        explicit = {name for name, value in values.items() if value is not None}
        role = values["LuxelProjectRole"] or _default_role(relative)
        category = values["LuxelGalleryCategory"] or "External"
        subsystem = values["LuxelSubsystem"] or path.stem.removeprefix("Luxel.").split(".")[0]
        platform = values["LuxelPlatform"] or "Portable"
        tier = values["LuxelArchitectureTier"] or "Product"
        assembly = values["AssemblyName"] or path.stem
        identity = values["LuxelGalleryRegistrationIdentity"] or ""
        projects[path] = Project(
            path=path, role=role, category=category, subsystem=subsystem,
            platform=platform, tier=tier,
            is_packable=_bool(values["IsPackable"], role == "Production"),
            assembly_name=assembly, registration_identity=identity,
            explicit_metadata=explicit, in_solution=path in solution_members,
        )

    for path, project in projects.items():
        xml = xml_roots[path]
        for item in xml.findall(".//ProjectReference"):
            include = item.get("Include")
            if not include:
                errors.append(f"ProjectReference without Include: {path.relative_to(root)}")
                continue
            target = (path.parent / include.replace("\\", "/")).resolve()
            output_type = (item.get("OutputItemType") or _text(item, "OutputItemType") or "").lower()
            reference_output = (item.get("ReferenceOutputAssembly") or _text(item, "ReferenceOutputAssembly") or "true").lower()
            kind = "Analyzer" if output_type == "analyzer" or reference_output == "false" else "Compile"
            project.references.append(Reference(path, target, kind))
            if target not in projects:
                errors.append(
                    f"Missing {kind} ProjectReference: {path.relative_to(root)} -> "
                    f"{target.relative_to(root) if target.is_relative_to(root) else target}"
                )
    return projects, errors


def relative(root: Path, path: Path) -> str:
    return path.relative_to(root.resolve()).as_posix()


def reference_key(root: Path, reference: Reference) -> str:
    return f"{relative(root, reference.source)} -> {relative(root, reference.target)} [{reference.kind}]"


def native_heavy(project: Project) -> bool:
    stem = project.path.stem
    return project.platform == "Native" or (
        stem in {
            "Luxel.Platform.Windows", "Luxel.Input.XInput", "Luxel.Audio.Windows",
            "Luxel.Audio.Silk", "Luxel.Graphics.Vulkan", "Luxel.Graphics.DirectX12",
            "Luxel.Graphics.TwoD.Skia", "Luxel.Typography.Icu",
        }
        or stem in {"Luxel.Terminal.Linux", "Luxel.Terminal.Windows"}
    )


def find_path(projects: dict[Path, Project], start: Path, predicate, compile_only: bool = True) -> list[Path] | None:
    stack = [(start, [start])]
    while stack:
        node, path = stack.pop()
        for reference in projects[node].references:
            if compile_only and reference.kind != "Compile":
                continue
            target = reference.target
            if target not in projects:
                continue
            if target in path:
                continue
            next_path = path + [target]
            if predicate(projects[target]):
                return next_path
            stack.append((target, next_path))
    return None


def cycles(projects: dict[Path, Project]) -> list[list[Path]]:
    found: list[list[Path]] = []
    state: dict[Path, int] = {}
    stack: list[Path] = []
    def visit(node: Path) -> None:
        state[node] = 1
        stack.append(node)
        for reference in projects[node].references:
            if reference.kind != "Compile" or reference.target not in projects:
                continue
            target = reference.target
            if state.get(target) == 1:
                found.append(stack[stack.index(target):] + [target])
            elif state.get(target, 0) == 0:
                visit(target)
        stack.pop()
        state[node] = 2
    for node in projects:
        if state.get(node, 0) == 0:
            visit(node)
    return found


def validate_metadata(root: Path, projects: dict[Path, Project]) -> list[str]:
    errors: list[str] = []
    identities: dict[str, Path] = {}
    assemblies: dict[str, Path] = {}
    required_gallery = {
        "LuxelProjectRole", "LuxelGalleryCategory", "LuxelSubsystem",
        "LuxelPlatform", "LuxelArchitectureTier", "IsPackable",
        "LuxelGalleryRegistrationIdentity",
    }
    for path, project in projects.items():
        rel = relative(root, path)
        if project.role not in ROLE_VALUES:
            errors.append(f"Invalid LuxelProjectRole '{project.role}': {rel}")
        if project.category not in CATEGORY_VALUES:
            errors.append(f"Invalid LuxelGalleryCategory '{project.category}': {rel}")
        if project.platform not in PLATFORM_VALUES:
            errors.append(f"Invalid LuxelPlatform '{project.platform}': {rel}")
        if project.tier not in TIER_VALUES:
            errors.append(f"Invalid LuxelArchitectureTier '{project.tier}': {rel}")
        if project.path.parts and "src" in project.path.parts and not project.in_solution:
            errors.append(f"Source project missing from solution: {rel}")
        previous = assemblies.get(project.assembly_name)
        if previous is not None:
            errors.append(f"Duplicate assembly name '{project.assembly_name}': {relative(root, previous)}, {rel}")
        else:
            assemblies[project.assembly_name] = path
        if project.is_gallery:
            missing = sorted(required_gallery - project.explicit_metadata)
            if missing:
                errors.append(f"Gallery project lacks explicit metadata ({', '.join(missing)}): {rel}")
            if project.is_packable:
                errors.append(f"Gallery project must be non-packable: {rel}")
            if not project.registration_identity:
                errors.append(f"Gallery project lacks registration identity: {rel}")
            elif project.registration_identity in identities:
                errors.append(
                    f"Duplicate Gallery registration identity '{project.registration_identity}': "
                    f"{relative(root, identities[project.registration_identity])}, {rel}"
                )
            else:
                identities[project.registration_identity] = path
        elif project.role == "Production" and project.category != "External":
            errors.append(f"Production project must use category External: {rel}")
    return errors
