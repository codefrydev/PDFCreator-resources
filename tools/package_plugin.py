#!/usr/bin/env python3
"""
FryPDF Plugin Packaging Automation Tool
Builds plugins in Release mode, produces .fryplugin archives,
stages them into plugins/<id>/, and updates catalog.json metadata.
"""

import argparse
import json
import os
import subprocess
import sys
from pathlib import Path

ROOT_DIR = Path(__file__).resolve().parent.parent
EXAMPLES_DIR = ROOT_DIR / "examples"
PLUGINS_DIR = ROOT_DIR / "plugins"
CATALOG_PATH = PLUGINS_DIR / "catalog.json"


def format_size(num_bytes: int) -> str:
    if num_bytes < 1024 * 1024:
        return f"{max(1, round(num_bytes / 1024))} KB"
    return f"{num_bytes / (1024 * 1024):.1f} MB"


def package_single_plugin(plugin_dir: Path) -> bool:
    print(f"\n📦 Packaging plugin from: {plugin_dir.name}...")

    csproj_files = [f for f in plugin_dir.glob("*.csproj") if not f.name.endswith(".Runner.csproj")]
    if not csproj_files:
        print(f"  ❌ No main .csproj found in {plugin_dir}")
        return False
    csproj = csproj_files[0]

    manifest_path = plugin_dir / "plugin.json"
    if not manifest_path.exists():
        print(f"  ❌ No plugin.json found in {plugin_dir}")
        return False

    with open(manifest_path, "r", encoding="utf-8") as f:
        manifest = json.load(f)

    plugin_id = manifest.get("id")
    if not plugin_id:
        print(f"  ❌ Missing 'id' in {manifest_path}")
        return False

    # 1. Build in Release mode
    print(f"  Compiling {csproj.name} (Configuration=Release)...")
    cmd = ["dotnet", "build", str(csproj), "-c", "Release"]
    res = subprocess.run(cmd, cwd=str(ROOT_DIR), capture_output=True, text=True)
    if res.returncode != 0:
        print(f"  ❌ Build failed:\n{res.stderr or res.stdout}")
        return False

    # 2. Locate .fryplugin output
    bin_release = plugin_dir / "bin" / "Release" / "net10.0"
    fry_packages = list(bin_release.glob("*.fryplugin"))
    if not fry_packages:
        print(f"  ❌ No .fryplugin found in {bin_release}")
        return False

    package_file = fry_packages[0]
    pkg_size = package_file.stat().st_size
    formatted_sz = format_size(pkg_size)

    # 3. Stage to plugins/<id>/
    dest_dir = PLUGINS_DIR / plugin_id
    dest_dir.mkdir(parents=True, exist_ok=True)

    dest_package = dest_dir / package_file.name
    dest_manifest = dest_dir / "plugin.json"

    with open(package_file, "rb") as src, open(dest_package, "wb") as dst:
        dst.write(src.read())

    with open(manifest_path, "r", encoding="utf-8") as src, open(dest_manifest, "w", encoding="utf-8") as dst:
        dst.write(src.read())

    # Copy README if present
    readme_src = plugin_dir / "README.md"
    if readme_src.exists():
        dest_readme = dest_dir / "README.md"
        with open(readme_src, "r", encoding="utf-8") as src, open(dest_readme, "w", encoding="utf-8") as dst:
            dst.write(src.read())

    print(f"  ✔ Staged: plugins/{plugin_id}/{package_file.name} ({formatted_sz})")

    # 4. Update catalog.json size if present
    if CATALOG_PATH.exists():
        try:
            with open(CATALOG_PATH, "r", encoding="utf-8") as f:
                catalog = json.load(f)

            updated = False
            for item in catalog:
                if item.get("id") == plugin_id:
                    item["formattedSize"] = formatted_sz
                    item["version"] = manifest.get("version", item.get("version"))
                    updated = True
                    break

            if updated:
                with open(CATALOG_PATH, "w", encoding="utf-8") as f:
                    json.dump(catalog, f, indent=2)
                    f.write("\n")
                print(f"  ✔ Updated size in plugins/catalog.json: {formatted_sz}")
        except Exception as ex:
            print(f"  ▲ Note: Failed to update catalog.json: {ex}")

    return True


def main():
    parser = argparse.ArgumentParser(description="Package FryPDF plugins into .fryplugin archives.")
    parser.add_argument("plugin", nargs="?", help="Plugin folder name (e.g. SnakePlugin) or '--all'")
    parser.add_argument("--all", action="store_true", help="Package all plugins in examples/")
    args = parser.parse_args()

    print("=" * 60)
    print("  FryPDF Plugin Packaging Tool")
    print("=" * 60)

    if args.all or args.plugin == "all" or not args.plugin:
        example_dirs = sorted([d for d in EXAMPLES_DIR.iterdir() if d.is_dir() and (d / "plugin.json").exists()])
        success = True
        for pdir in example_dirs:
            if not package_single_plugin(pdir):
                success = False
        if not success:
            sys.exit(1)
    else:
        target = EXAMPLES_DIR / args.plugin
        if not target.is_dir():
            # Try fuzzy match
            matches = [d for d in EXAMPLES_DIR.iterdir() if args.plugin.lower() in d.name.lower()]
            if matches:
                target = matches[0]
            else:
                print(f"Error: Plugin directory '{args.plugin}' not found in {EXAMPLES_DIR}")
                sys.exit(1)
        if not package_single_plugin(target):
            sys.exit(1)

    print("\n" + "=" * 60)
    print("✨ Packaging complete!")
    print("=" * 60)


if __name__ == "__main__":
    main()
