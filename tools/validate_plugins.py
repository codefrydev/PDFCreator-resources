#!/usr/bin/env python3
"""
FryPDF Plugin Ecosystem Validator
Validates marketplace catalog, plugin manifests, distribution packages (.fryplugin),
and example solution runner architectures.
"""

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


def validate_catalog() -> tuple[list[dict], int]:
    errors = 0
    print("\n[1/3] Validating plugins/catalog.json...")

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
                log_fail(f"[{item_id}] Missing required field '{field}'.")
                errors += 1

        # Check corresponding plugin folder in plugins/
        plugin_folder = PLUGINS_DIR / item_id
        if not plugin_folder.is_dir():
            log_fail(f"[{item_id}] Missing distribution folder: plugins/{item_id}")
            errors += 1
            continue

        # Check plugin.json in distribution folder
        manifest_path = plugin_folder / "plugin.json"
        if not manifest_path.exists():
            log_fail(f"[{item_id}] Missing manifest: plugins/{item_id}/plugin.json")
            errors += 1
        else:
            try:
                with open(manifest_path, "r", encoding="utf-8") as mf:
                    manifest = json.load(mf)
                if manifest.get("id") != item_id:
                    log_fail(f"[{item_id}] Manifest id '{manifest.get('id')}' does not match catalog id '{item_id}'.")
                    errors += 1
                if manifest.get("version") != item.get("version"):
                    log_warn(f"[{item_id}] Version mismatch: catalog is '{item.get('version')}', manifest is '{manifest.get('version')}'.")
            except Exception as ex:
                log_fail(f"[{item_id}] Malformed plugin.json: {ex}")
                errors += 1

        # Check .fryplugin archive
        download_url = item.get("downloadUrl", "")
        pkg_name = os.path.basename(download_url)
        pkg_path = plugin_folder / pkg_name

        if not pkg_path.exists():
            # Try to find any .fryplugin in folder
            fry_files = list(plugin_folder.glob("*.fryplugin"))
            if fry_files:
                log_warn(f"[{item_id}] Expected {pkg_name}, found {fry_files[0].name}")
                pkg_path = fry_files[0]
            else:
                log_fail(f"[{item_id}] Missing .fryplugin archive in plugins/{item_id}")
                errors += 1
                continue

        # Validate .fryplugin zip contents
        try:
            with zipfile.ZipFile(pkg_path, "r") as zf:
                namelist = zf.namelist()
                if "plugin.json" not in namelist:
                    log_fail(f"[{item_id}] .fryplugin archive does not contain 'plugin.json'.")
                    errors += 1
                dlls = [n for n in namelist if n.endswith(".dll")]
                if not dlls:
                    log_fail(f"[{item_id}] .fryplugin archive contains no .dll assemblies.")
                    errors += 1
                else:
                    log_pass(f"[{item_id}] Package valid: {pkg_path.name} ({len(namelist)} files, {pkg_path.stat().st_size // 1024} KB)")
        except Exception as ex:
            log_fail(f"[{item_id}] Corrupt .fryplugin zip archive: {ex}")
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
        print("  - Catalog and .fryplugin packages: OK")
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
