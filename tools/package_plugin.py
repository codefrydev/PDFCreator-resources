#!/usr/bin/env python3
"""
FryPDF Plugin Packaging Automation Tool
Builds plugins in Release mode, produces .fryplugin archives,
stages them strictly into versioned subdirectories plugins/<id>/v<version>/,
removes any legacy unversioned root files, computes SHA256 checksums,
and updates catalog.json multi-version metadata.
"""

import argparse
import datetime
import hashlib
import json
import os
import subprocess
import sys
from pathlib import Path

ROOT_DIR = Path(__file__).resolve().parent.parent
EXAMPLES_DIR = ROOT_DIR / "examples"
PLUGINS_DIR = ROOT_DIR / "plugins"
CATALOG_PATH = PLUGINS_DIR / "catalog.json"
REPO_RAW_BASE = "https://raw.githubusercontent.com/codefrydev/PDFCreator-resources/refs/heads/main"


def format_size(num_bytes: int) -> str:
    if num_bytes < 1024 * 1024:
        return f"{max(1, round(num_bytes / 1024))} KB"
    return f"{num_bytes / (1024 * 1024):.1f} MB"


def compute_sha256(file_path: Path) -> str:
    h = hashlib.sha256()
    with open(file_path, "rb") as f:
        while chunk := f.read(65536):
            h.update(chunk)
    return h.hexdigest()


def parse_semver(ver: str) -> tuple:
    parts = []
    clean = ver.strip().lstrip("vV")
    for p in clean.split("."):
        try:
            parts.append(int(p))
        except ValueError:
            parts.append(0)
    while len(parts) < 3:
        parts.append(0)
    return tuple(parts)


def package_single_plugin(plugin_dir: Path, target_version: str | None = None) -> bool:
    print(f"\nPackaging plugin from: {plugin_dir.name}...")

    csproj_files = [f for f in plugin_dir.glob("*.csproj") if not f.name.endswith(".Runner.csproj")]
    if not csproj_files:
        print(f"  No main .csproj found in {plugin_dir}")
        return False
    csproj = csproj_files[0]

    manifest_path = plugin_dir / "plugin.json"
    if not manifest_path.exists():
        print(f"  No plugin.json found in {plugin_dir}")
        return False

    with open(manifest_path, "r", encoding="utf-8") as f:
        manifest = json.load(f)

    plugin_id = manifest.get("id")
    if not plugin_id:
        print(f"  Missing 'id' in {manifest_path}")
        return False

    version = target_version or manifest.get("version", "1.0.0")
    version = version.strip().lstrip("vV")
    manifest["version"] = version

    # 1. Build in Release mode
    print(f"  Compiling {csproj.name} (Configuration=Release)...")
    cmd = ["dotnet", "build", str(csproj), "-c", "Release"]
    res = subprocess.run(cmd, cwd=str(ROOT_DIR), capture_output=True, text=True)
    if res.returncode != 0:
        print(f"  Build failed:\n{res.stderr or res.stdout}")
        return False

    # 2. Locate .fryplugin output
    bin_release = plugin_dir / "bin" / "Release" / "net10.0"
    fry_packages = list(bin_release.glob("*.fryplugin"))
    if not fry_packages:
        print(f"  No .fryplugin found in {bin_release}")
        return False

    package_file = fry_packages[0]
    pkg_size = package_file.stat().st_size
    formatted_sz = format_size(pkg_size)
    sha256_hex = compute_sha256(package_file)

    # 3. Stage strictly to versioned subfolder plugins/<id>/v<version>/
    root_plugin_dir = PLUGINS_DIR / plugin_id
    root_plugin_dir.mkdir(parents=True, exist_ok=True)

    version_plugin_dir = root_plugin_dir / f"v{version}"
    version_plugin_dir.mkdir(parents=True, exist_ok=True)

    pkg_bytes = package_file.read_bytes()
    manifest_str = json.dumps(manifest, indent=2) + "\n"

    # Stage strictly to versioned folder
    (version_plugin_dir / package_file.name).write_bytes(pkg_bytes)
    (version_plugin_dir / "plugin.json").write_text(manifest_str, encoding="utf-8")

    # Purge any loose root packages or loose manifest (pure versioned layout)
    for loose_fry in root_plugin_dir.glob("*.fryplugin"):
        try:
            loose_fry.unlink()
            print(f"  Removed loose root package: {loose_fry.name}")
        except Exception:
            pass

    loose_manifest = root_plugin_dir / "plugin.json"
    if loose_manifest.exists():
        try:
            loose_manifest.unlink()
            print(f"  Removed loose root manifest: plugins/{plugin_id}/plugin.json")
        except Exception:
            pass

    # Copy README if present (root and version folder)
    readme_src = plugin_dir / "README.md"
    if readme_src.exists():
        readme_bytes = readme_src.read_bytes()
        (root_plugin_dir / "README.md").write_bytes(readme_bytes)
        (version_plugin_dir / "README.md").write_bytes(readme_bytes)

    print(f"  Staged version: plugins/{plugin_id}/v{version}/{package_file.name} ({formatted_sz})")
    print(f"  SHA256: {sha256_hex[:16]}...")

    # 4. Update catalog.json with multi-version metadata
    if CATALOG_PATH.exists():
        try:
            with open(CATALOG_PATH, "r", encoding="utf-8") as f:
                catalog = json.load(f)

            today_str = datetime.date.today().isoformat()
            version_download_url = f"{REPO_RAW_BASE}/plugins/{plugin_id}/v{version}/{package_file.name}"

            new_version_entry = {
                "version": version,
                "releaseDate": today_str,
                "downloadUrl": version_download_url,
                "sha256": sha256_hex,
                "formattedSize": formatted_sz,
                "minHostVersion": manifest.get("minHostVersion", "1.0.0"),
                "targetFramework": manifest.get("targetFramework", "net10.0"),
                "releaseNotes": manifest.get("releaseNotes", f"Release v{version} of {manifest.get('name', plugin_id)}.")
            }

            updated = False
            for item in catalog:
                if item.get("id") == plugin_id:
                    versions_list = item.setdefault("versions", [])

                    # Find if version already exists
                    found_idx = -1
                    for idx, v_item in enumerate(versions_list):
                        if v_item.get("version", "").lstrip("vV") == version:
                            found_idx = idx
                            break

                    if found_idx >= 0:
                        versions_list[found_idx] = new_version_entry
                    else:
                        versions_list.append(new_version_entry)

                    # Sort versions by semver descending
                    versions_list.sort(key=lambda v: parse_semver(v.get("version", "0.0.0")), reverse=True)

                    # Update top-level latest version
                    latest = versions_list[0]
                    item["version"] = latest["version"]
                    item["formattedSize"] = latest["formattedSize"]
                    item["downloadUrl"] = latest["downloadUrl"]
                    item["sha256"] = latest["sha256"]
                    updated = True
                    break

            if updated:
                with open(CATALOG_PATH, "w", encoding="utf-8") as f:
                    json.dump(catalog, f, indent=2)
                    f.write("\n")
                print(f"  Updated multi-version catalog in plugins/catalog.json (v{version})")
        except Exception as ex:
            print(f"  Note: Failed to update catalog.json: {ex}")

    return True


def main():
    parser = argparse.ArgumentParser(description="Package FryPDF plugins strictly into versioned .fryplugin archives.")
    parser.add_argument("plugin", nargs="?", help="Plugin folder name (e.g. SnakePlugin) or '--all'")
    parser.add_argument("--all", action="store_true", help="Package all plugins in examples/")
    parser.add_argument("--version", type=str, default=None, help="Target release version (e.g. 1.1.0)")
    args = parser.parse_args()

    print("=" * 60)
    print("  FryPDF Plugin Packaging Tool (Strict Versioned)")
    print("=" * 60)

    if args.all or args.plugin == "all" or not args.plugin:
        example_dirs = sorted([d for d in EXAMPLES_DIR.iterdir() if d.is_dir() and (d / "plugin.json").exists()])
        success = True
        for pdir in example_dirs:
            if not package_single_plugin(pdir, args.version):
                success = False
        if not success:
            sys.exit(1)
    else:
        target = EXAMPLES_DIR / args.plugin
        if not target.is_dir():
            matches = [d for d in EXAMPLES_DIR.iterdir() if args.plugin.lower() in d.name.lower()]
            if matches:
                target = matches[0]
            else:
                print(f"Error: Plugin directory '{args.plugin}' not found in {EXAMPLES_DIR}")
                sys.exit(1)
        if not package_single_plugin(target, args.version):
            sys.exit(1)

    print("\n" + "=" * 60)
    print("Packaging complete!")
    print("=" * 60)


if __name__ == "__main__":
    main()
