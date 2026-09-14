#!/usr/bin/env python3
"""
FryPDF Plugin Ecosystem Validator
Validates marketplace catalog, plugin manifests, strict versioned distribution packages (.fryplugin),
and example solution runner architectures.
"""

import hashlib
import json
import os
import sys
import zipfile
from pathlib import Path

ROOT_DIR = Path(__file__).resolve().parent.parent
PLUGINS_DIR = ROOT_DIR / "plugins"
EXAMPLES_DIR = ROOT_DIR / "examples"
CATALOG_PATH = PLUGINS_DIR / "catalog.json"

REQUIRED_CATALOG_FIELDS = [
    "id",
    "name",
    "version",
    "publisher",
    "category",
    "description",
    "downloadUrl",
    "dependencies",
    "versions",
]

REQUIRED_VERSION_FIELDS = [
    "version",
    "downloadUrl",
    "sha256",
    "formattedSize",
]

REQUIRED_MANIFEST_FIELDS = [
    "id",
    "name",
    "version",
    "description",
    "entryPoint",
]


def log_pass(msg: str):
    print(f"  \033[32m✔\033[0m {msg}")


def log_warn(msg: str):
    print(f"  \033[33m▲\033[0m {msg}")


def log_fail(msg: str):
    print(f"  \033[31m✖\033[0m {msg}")


def compute_sha256(file_path: Path) -> str:
    h = hashlib.sha256()
    with open(file_path, "rb") as f:
        while chunk := f.read(65536):
            h.update(chunk)
    return h.hexdigest()


def validate_catalog() -> tuple[list[dict], int]:
    errors = 0
    print("\n[1/3] Validating plugins/catalog.json and strict versioned distribution...")

    if not CATALOG_PATH.exists():
        log_fail(f"Catalog file not found at: {CATALOG_PATH}")
        return [], 1

    try:
        with open(CATALOG_PATH, "r", encoding="utf-8") as f:
            catalog = json.load(f)
    except Exception as ex:
        log_fail(f"Malformed JSON in catalog: {ex}")
        return [], 1

    if not isinstance(catalog, list):
        log_fail("Catalog root must be a JSON array of plugin objects.")
        return [], 1

    log_pass(f"Catalog parsed successfully ({len(catalog)} entries).")

    seen_ids = set()
    for idx, item in enumerate(catalog):
        name = item.get("name", f"Item #{idx}")
        item_id = item.get("id")

        if not item_id:
            log_fail(f"Item #{idx} ({name}) is missing 'id'.")
            errors += 1
            continue

        if item_id in seen_ids:
            log_fail(f"Duplicate plugin id '{item_id}' detected.")
            errors += 1
        seen_ids.add(item_id)

        # Check required fields
        for field in REQUIRED_CATALOG_FIELDS:
            if field not in item or item[field] is None:
                log_fail(f"[{item_id}] Missing required catalog field '{field}'.")
                errors += 1

        # Check corresponding plugin distribution folder in plugins/
        plugin_folder = PLUGINS_DIR / item_id
        if not plugin_folder.is_dir():
            log_fail(f"[{item_id}] Missing distribution folder: plugins/{item_id}")
            errors += 1
            continue

        # Strict versioning rule: No loose unversioned .fryplugin or plugin.json at plugin root
        loose_packages = list(plugin_folder.glob("*.fryplugin"))
        if loose_packages:
            log_fail(f"[{item_id}] Found loose unversioned package(s) at plugin root: {[p.name for p in loose_packages]}. Must reside strictly in v<version>/.")
            errors += 1

        loose_manifest = plugin_folder / "plugin.json"
        if loose_manifest.exists():
            log_fail(f"[{item_id}] Found loose unversioned plugin.json at plugin root. Must reside strictly in v<version>/.")
            errors += 1

        # Validate versions array
        versions_list = item.get("versions")
        if not versions_list or not isinstance(versions_list, list):
            log_fail(f"[{item_id}] Missing or non-list 'versions' in catalog.")
            errors += 1
            continue

        # Check latest version alignment
        latest_version_entry = versions_list[0]
        if item.get("version") != latest_version_entry.get("version"):
            log_fail(f"[{item_id}] Catalog top-level version '{item.get('version')}' does not match latest versions[0] '{latest_version_entry.get('version')}'.")
            errors += 1

        if item.get("downloadUrl") != latest_version_entry.get("downloadUrl"):
            log_fail(f"[{item_id}] Catalog top-level downloadUrl does not match latest versions[0] downloadUrl.")
            errors += 1

        # Check every version in versions list
        for v_info in versions_list:
            for vf in REQUIRED_VERSION_FIELDS:
                if vf not in v_info:
                    log_fail(f"[{item_id}] Version entry missing field '{vf}'.")
                    errors += 1

            v_num = v_info.get("version", "").lstrip("vV")
            v_folder = plugin_folder / f"v{v_num}"
            if not v_folder.is_dir():
                log_fail(f"[{item_id}] Missing version folder: plugins/{item_id}/v{v_num}")
                errors += 1
                continue

            # Check version manifest
            v_manifest_path = v_folder / "plugin.json"
            if not v_manifest_path.exists():
                log_fail(f"[{item_id}] Missing version manifest: plugins/{item_id}/v{v_num}/plugin.json")
                errors += 1
            else:
                try:
                    with open(v_manifest_path, "r", encoding="utf-8") as mf:
                        v_manifest = json.load(mf)
                    if v_manifest.get("id") != item_id:
                        log_fail(f"[{item_id} v{v_num}] Manifest id '{v_manifest.get('id')}' != catalog id '{item_id}'.")
                        errors += 1
                    if v_manifest.get("version", "").lstrip("vV") != v_num:
                        log_fail(f"[{item_id} v{v_num}] Manifest version '{v_manifest.get('version')}' != '{v_num}'.")
                        errors += 1
                except Exception as ex:
                    log_fail(f"[{item_id} v{v_num}] Malformed plugin.json: {ex}")
                    errors += 1

            # Check .fryplugin archive in version folder
            download_url = v_info.get("downloadUrl", "")
            pkg_name = os.path.basename(download_url)
            pkg_path = v_folder / pkg_name

            if not pkg_path.exists():
                log_fail(f"[{item_id} v{v_num}] Missing package archive: {pkg_path.relative_to(ROOT_DIR)}")
                errors += 1
                continue

            # Validate SHA256
            actual_sha256 = compute_sha256(pkg_path)
            expected_sha256 = v_info.get("sha256", "")
            if actual_sha256 != expected_sha256:
                log_fail(f"[{item_id} v{v_num}] SHA256 mismatch! Expected {expected_sha256[:12]}..., got {actual_sha256[:12]}...")
                errors += 1

            # Validate zip contents
            try:
                with zipfile.ZipFile(pkg_path, "r") as zf:
                    namelist = zf.namelist()
                    if "plugin.json" not in namelist:
                        log_fail(f"[{item_id} v{v_num}] Archive does not contain 'plugin.json'.")
                        errors += 1
                    dlls = [n for n in namelist if n.endswith(".dll")]
                    if not dlls:
                        log_fail(f"[{item_id} v{v_num}] Archive contains no .dll assemblies.")
                        errors += 1
                    else:
                        log_pass(f"[{item_id}] Version {v_num} valid: {pkg_path.name} ({len(namelist)} files, {pkg_path.stat().st_size // 1024} KB, SHA256 verified)")
            except Exception as ex:
                log_fail(f"[{item_id} v{v_num}] Corrupt .fryplugin zip archive: {ex}")
                errors += 1

    return catalog, errors


def validate_example_plugins() -> int:
    errors = 0
    print("\n[2/3] Validating example plugin projects in examples/...")

    if not EXAMPLES_DIR.exists():
        log_fail("examples/ directory not found.")
        return 1

    example_dirs = [d for d in EXAMPLES_DIR.iterdir() if d.is_dir() and not d.name.startswith(".")]

    for plugin_dir in sorted(example_dirs, key=lambda d: d.name):
        pname = plugin_dir.name
        manifest_path = plugin_dir / "plugin.json"
        slnx_files = list(plugin_dir.glob("*.slnx"))
        csproj_files = [f for f in plugin_dir.glob("*.csproj") if not f.name.endswith(".Runner.csproj")]
        runner_dir = plugin_dir / "Runner"

        # Check manifest
        if not manifest_path.exists():
            log_fail(f"[{pname}] Missing plugin.json manifest.")
            errors += 1
        else:
            try:
                with open(manifest_path, "r", encoding="utf-8") as mf:
                    m = json.load(mf)
                for req in REQUIRED_MANIFEST_FIELDS:
                    if req not in m:
                        log_fail(f"[{pname}] Manifest missing field '{req}'.")
                        errors += 1
            except Exception as ex:
                log_fail(f"[{pname}] Malformed plugin.json: {ex}")
                errors += 1

        # Check primary project
        if not csproj_files:
            log_fail(f"[{pname}] Missing primary .csproj project file.")
            errors += 1

        # Check solution
        if not slnx_files:
            log_fail(f"[{pname}] Missing modern XML solution file (*.slnx).")
            errors += 1

        # Check Runner
        if not runner_dir.is_dir():
            log_fail(f"[{pname}] Missing standalone 'Runner/' test directory.")
            errors += 1
        else:
            runner_projs = list(runner_dir.glob("*.Runner.csproj"))
            if not runner_projs:
                log_fail(f"[{pname}] Missing *.Runner.csproj in Runner/")
                errors += 1
            else:
                log_pass(f"[{pname}] Solution & Runner architecture complete: {slnx_files[0].name if slnx_files else 'slnx'} + Runner/{runner_projs[0].name}")

    return errors


def validate_repository_layout() -> int:
    errors = 0
    print("\n[3/3] Validating repository structure & mandates...")

    agents_rules = ROOT_DIR / ".agents" / "rules"
    if not agents_rules.exists():
        log_fail(".agents/rules directory missing.")
        errors += 1
    else:
        rules = list(agents_rules.glob("*.md"))
        log_pass(f"Agent rules directory contains {len(rules)} mandate files.")

    fonts_dir = ROOT_DIR / "fonts"
    if not fonts_dir.exists():
        log_warn("fonts/ directory not found.")
    else:
        font_count = len(list(fonts_dir.glob("*.ttf"))) + len(list(fonts_dir.glob("*.otf")))
        log_pass(f"Fonts directory verified ({font_count} font files).")

    return errors


def main():
    print("=" * 60)
    print("  FryPDF Plugin Ecosystem & Repository Integrity Validator")
    print("=" * 60)

    _, cat_errors = validate_catalog()
    ex_errors = validate_example_plugins()
    layout_errors = validate_repository_layout()

    total_errors = cat_errors + ex_errors + layout_errors

    print("\n" + "=" * 60)
    if total_errors == 0:
        print("\033[32m✔ ALL VALIDATION CHECKS PASSED (0 errors)\033[0m")
        print("  - Strict versioned catalog and .fryplugin packages: OK")
        print("  - Example solutions and standalone runners: OK")
        print("  - Repository structure and rule definitions: OK")
        print("=" * 60)
        sys.exit(0)
    else:
        print(f"\033[31m✖ VALIDATION FAILED WITH {total_errors} ERROR(S)\033[0m")
        print("=" * 60)
        sys.exit(1)


if __name__ == "__main__":
    main()
