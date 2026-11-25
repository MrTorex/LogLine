#!/usr/bin/env python3
"""Zero-dependency Unity UPM package validator for LogLine CI and Release workflows."""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

SEMVER_RE = re.compile(
    r"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)"
    r"(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)"
    r"(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?"
    r"(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$"
)
UPM_NAME_RE = re.compile(r"^[a-z0-9][a-z0-9._-]{0,213}$")
GUID_RE = re.compile(r"^guid:\s*([0-9a-fA-F]{32})\s*$", re.MULTILINE)

REQUIRED_PACKAGE_FIELDS = ("name", "version", "displayName", "description", "unity", "license")
REQUIRED_ROOT_FILES = ("package.json", "README.md", "LICENSE.md", "CHANGELOG.md")
FORBIDDEN_ROOT_ENTRIES = ("Samples.meta", "Samples~.meta", "Documentation.meta", "Documentation~.meta")
OS_JUNK_NAMES = {".DS_Store", "Thumbs.db", "desktop.ini"}


def validate_package_json(root: Path, errors: list[str]) -> dict:
    pkg_path = root / "package.json"
    if not pkg_path.is_file():
        errors.append("Missing required file: package.json")
        return {}

    try:
        data = json.loads(pkg_path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        errors.append(f"Invalid JSON in package.json: {exc}")
        return {}

    for field in REQUIRED_PACKAGE_FIELDS:
        val = data.get(field)
        if not isinstance(val, str) or not val.strip():
            errors.append(f"package.json: missing or empty required field '{field}'")

    name = data.get("name", "")
    if isinstance(name, str) and name and not UPM_NAME_RE.match(name):
        errors.append(f"package.json: invalid UPM package name '{name}'")

    version = data.get("version", "")
    if isinstance(version, str) and version and not SEMVER_RE.match(version):
        errors.append(f"package.json: version '{version}' is not valid SemVer 2.0.0")

    icon = data.get("icon")
    if icon is not None:
        if not isinstance(icon, str) or not (root / icon).is_file():
            errors.append(f"package.json: icon path '{icon}' does not exist")

    samples = data.get("samples", [])
    if not isinstance(samples, list):
        errors.append("package.json: 'samples' must be an array")
    else:
        for idx, sample in enumerate(samples):
            if not isinstance(sample, dict):
                errors.append(f"package.json: samples[{idx}] must be an object")
                continue
            sample_path = sample.get("path", "")
            display_name = sample.get("displayName", "")
            if not display_name:
                errors.append(f"package.json: samples[{idx}] missing 'displayName'")
            if not sample_path or not (root / sample_path).is_dir():
                errors.append(f"package.json: samples[{idx}].path '{sample_path}' does not exist")

    return data


def validate_asmdefs(root: Path, errors: list[str]) -> None:
    asmdef_names: dict[str, Path] = {}
    for asmdef in sorted(root.rglob("*.asmdef")):
        if any(part.startswith(".") for part in asmdef.relative_to(root).parts):
            continue
        try:
            data = json.loads(asmdef.read_text(encoding="utf-8"))
        except json.JSONDecodeError as exc:
            errors.append(f"Invalid JSON in {asmdef.relative_to(root)}: {exc}")
            continue

        asm_name = data.get("name")
        if not isinstance(asm_name, str) or not asm_name.strip():
            errors.append(f"{asmdef.relative_to(root)}: missing 'name' field")
            continue

        if asm_name in asmdef_names:
            errors.append(
                f"Duplicate asmdef name '{asm_name}': "
                f"{asmdef_names[asm_name].relative_to(root)} and {asmdef.relative_to(root)}"
            )
        else:
            asmdef_names[asm_name] = asmdef


def check_meta_tree(
    directory: Path,
    root: Path,
    guids: dict[str, Path],
    errors: list[str],
    skip_top_level: set[str] | None = None,
) -> None:
    for entry in sorted(directory.iterdir()):
        name = entry.name
        rel = entry.relative_to(root)

        if name in OS_JUNK_NAMES:
            errors.append(f"OS junk file should not be committed: {rel}")
            continue

        if name.startswith("."):
            continue

        if skip_top_level and name in skip_top_level:
            continue

        if name.endswith(".meta"):
            target = entry.with_name(name[:-5])
            if not target.exists():
                errors.append(f"Orphan .meta file without matching asset/folder: {rel}")
            record_guid(entry, root, guids, errors)
            continue

        meta_path = entry.with_name(name + ".meta")
        if not meta_path.is_file():
            errors.append(f"Missing .meta file for: {rel}")

        if entry.is_dir():
            check_meta_tree(entry, root, guids, errors)


def record_guid(meta_file: Path, root: Path, guids: dict[str, Path], errors: list[str]) -> None:
    rel = meta_file.relative_to(root)
    try:
        content = meta_file.read_text(encoding="utf-8")
    except OSError as exc:
        errors.append(f"Unable to read {rel}: {exc}")
        return

    match = GUID_RE.search(content)
    if not match:
        errors.append(f"Missing or invalid GUID in {rel}")
        return

    guid = match.group(1).lower()
    if guid in guids:
        errors.append(f"Duplicate GUID '{guid}' in {guids[guid].relative_to(root)} and {rel}")
    else:
        guids[guid] = meta_file


def validate_meta_and_hidden_dirs(root: Path, errors: list[str]) -> None:
    for forbidden in FORBIDDEN_ROOT_ENTRIES:
        if (root / forbidden).exists():
            errors.append(f"Forbidden root meta file present: {forbidden}")

    guids: dict[str, Path] = {}

    # 1. Standard Unity-imported package root (excluding ~ hidden folders)
    skip_root = {
        entry.name
        for entry in root.iterdir()
        if entry.name.endswith("~")
    }
    check_meta_tree(root, root, guids, errors, skip_top_level=skip_root)

    # 2. Documentation~ must NOT contain .meta files
    docs_dir = root / "Documentation~"
    if docs_dir.is_dir():
        for meta in sorted(docs_dir.rglob("*.meta")):
            errors.append(f"Unexpected .meta file inside Documentation~: {meta.relative_to(root)}")

    # 3. Samples~/<SampleName> internal files MUST preserve .meta files for intact scene/script GUIDs
    samples_dir = root / "Samples~"
    if samples_dir.is_dir():
        for sample_entry in sorted(samples_dir.iterdir()):
            if sample_entry.name.startswith("."):
                continue
            if sample_entry.is_dir():
                check_meta_tree(sample_entry, root, guids, errors)
            elif sample_entry.name.endswith(".meta"):
                # Optional <SampleName>.meta left from Samples -> Samples~ rename is allowed if target exists
                target = sample_entry.with_name(sample_entry.name[:-5])
                if not target.is_dir():
                    errors.append(f"Orphan sample meta file: {sample_entry.relative_to(root)}")
                else:
                    record_guid(sample_entry, root, guids, errors)


def extract_changelog_section(changelog_path: Path, version: str) -> str | None:
    if not changelog_path.is_file():
        return None

    lines = changelog_path.read_text(encoding="utf-8").splitlines()
    header_re = re.compile(rf"^##\s+\[?v?{re.escape(version)}\]?(?:\s|$)")

    collecting = False
    collected: list[str] = []

    for line in lines:
        if not collecting:
            if header_re.match(line):
                collecting = True
        else:
            if line.startswith("## "):
                break
            collected.append(line)

    body = "\n".join(collected).strip()
    return body if body else None


def validate_release_tag(
    root: Path,
    pkg_data: dict,
    tag: str,
    write_notes: Path | None,
    errors: list[str],
) -> None:
    if not tag.startswith("v"):
        errors.append(f"Release tag '{tag}' must start with 'v' (e.g. v1.0.0)")
        return

    tag_version = tag[1:]
    if not SEMVER_RE.match(tag_version):
        errors.append(f"Release tag '{tag}' does not contain a valid SemVer 2.0.0 version")
        return

    pkg_version = pkg_data.get("version")
    if pkg_version != tag_version:
        errors.append(
            f"Version mismatch: git tag is '{tag}' ({tag_version}) "
            f"but package.json version is '{pkg_version}'"
        )

    notes = extract_changelog_section(root / "CHANGELOG.md", tag_version)
    if not notes:
        errors.append(
            f"CHANGELOG.md is missing a non-empty '## [{tag_version}]' section for release '{tag}'"
        )
    elif write_notes is not None:
        write_notes.write_text(notes + "\n", encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate LogLine UPM package integrity.")
    parser.add_argument("--root", type=Path, default=Path("."), help="Repository root directory")
    parser.add_argument("--tag", type=str, default=None, help="Git release tag (e.g. v1.0.0)")
    parser.add_argument(
        "--write-notes",
        type=Path,
        default=None,
        help="Output path to write extracted CHANGELOG.md release notes",
    )
    args = parser.parse_args()

    root = args.root.resolve()
    errors: list[str] = []

    for req_file in REQUIRED_ROOT_FILES:
        if not (root / req_file).is_file():
            errors.append(f"Missing required root file: {req_file}")

    pkg_data = validate_package_json(root, errors)
    validate_asmdefs(root, errors)
    validate_meta_and_hidden_dirs(root, errors)

    if args.tag:
        validate_release_tag(root, pkg_data, args.tag, args.write_notes, errors)

    if errors:
        print(f"Package validation FAILED with {len(errors)} error(s):", file=sys.stderr)
        for err in errors:
            print(f"  - {err}", file=sys.stderr)
        return 1

    print(
        f"Package validation PASSED: {pkg_data.get('name')}@{pkg_data.get('version')}"
        + (f" (tag {args.tag})" if args.tag else "")
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
