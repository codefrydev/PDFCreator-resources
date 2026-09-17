#!/usr/bin/env python3
"""Unified Standalone Application Packaging Tool for FryPDF Runners.

Packages standalone runners (e.g. FrySharp / CSharpEditor) into native
installers and distribution bundles:
- macOS: .app bundle, ad-hoc codesigning, branded .dmg via dmgbuild
- Windows: Inno Setup .exe installer, MSIX package with self-signing

Usage:
    python3 tools/package_runner.py csharpeditor [--os macos|windows|all] [--version 1.0.1]
"""

from __future__ import annotations

import argparse
import os
import pathlib
import platform
import shutil
import subprocess
import sys

REPO_ROOT = pathlib.Path(__file__).resolve().parent.parent
EXAMPLES_DIR = REPO_ROOT / "examples"
DIST_DIR = REPO_ROOT / "dist"


def parse_four_part_version(ver: str) -> str:
    clean = ver.strip().lstrip("vV").split("-")[0]
    parts = clean.split(".")
    while len(parts) < 4:
        parts.append("0")
    return ".".join(parts[:4])


def package_csharp_editor_macos(version: str, four_part: str) -> pathlib.Path:
    plugin_dir = EXAMPLES_DIR / "CSharpEditorPlugin"
    runner_proj = plugin_dir / "Runner" / "CSharpEditorPlugin.Runner.csproj"
    packaging_dir = plugin_dir / "packaging"
    macos_dir = packaging_dir / "macos"

    print("\n📦 [1/4] Ensuring FrySharp packaging artwork...")
    gen_script = packaging_dir / "tools" / "generate_assets.py"
    subprocess.run([sys.executable, str(gen_script)], check=True)

    print(f"\n🚀 [2/4] Publishing FrySharp standalone (osx-arm64, v{version})...")
    publish_dir = REPO_ROOT / "publish" / "csharpeditor" / "osx-arm64"
    if publish_dir.exists():
        shutil.rmtree(publish_dir)

    publish_cmd = [
        "dotnet",
        "publish",
        str(runner_proj),
        "-c",
        "Release",
        "-r",
        "osx-arm64",
        "--self-contained",
        "true",
        f"-p:Version={version}",
        f"-p:AssemblyVersion={four_part}",
        f"-p:FileVersion={four_part}",
        f"-p:InformationalVersion={version}",
        "-o",
        str(publish_dir),
    ]
    subprocess.run(publish_cmd, check=True)

    print("\n🍎 [3/4] Assembling and codesigning FrySharp.app bundle...")
    app_bundle = REPO_ROOT / "FrySharp.app"
    if app_bundle.exists():
        shutil.rmtree(app_bundle)

    macos_contents = app_bundle / "Contents" / "MacOS"
    resources_contents = app_bundle / "Contents" / "Resources"
    macos_contents.mkdir(parents=True, exist_ok=True)
    resources_contents.mkdir(parents=True, exist_ok=True)

    # Copy published binaries into Contents/MacOS
    for item in publish_dir.iterdir():
        dest = macos_contents / item.name
        if item.is_dir():
            shutil.copytree(item, dest)
        else:
            shutil.copy2(item, dest)

    # Template Info.plist
    plist_template = (macos_dir / "Info.plist.template").read_text(encoding="utf-8")
    plist_content = plist_template.replace("__VERSION__", version)
    (app_bundle / "Contents" / "Info.plist").write_text(plist_content, encoding="utf-8")

    # Copy icon
    shutil.copy2(macos_dir / "AppIcon.icns", resources_contents / "AppIcon.icns")

    # Ensure executable permission
    exe_path = macos_contents / "FrySharp"
    if exe_path.exists():
        exe_path.chmod(0o755)

    # Ad-hoc codesign
    subprocess.run(["codesign", "--force", "--deep", "--sign", "-", str(app_bundle)], check=True)
    print(f"  Signed: {app_bundle}")

    print("\n💿 [4/4] Creating branded macOS DMG installer...")
    DIST_DIR.mkdir(parents=True, exist_ok=True)
    dmg_out = DIST_DIR / f"FrySharp-{version}-arm64.dmg"
    make_dmg_sh = macos_dir / "make-dmg.sh"

    dmg_cmd = [
        "bash",
        str(make_dmg_sh),
        "--app",
        str(app_bundle),
        "--volname",
        "FrySharp",
        "--output",
        str(dmg_out),
    ]
    subprocess.run(dmg_cmd, check=True)

    print(f"\n✨ Successfully packaged FrySharp macOS standalone DMG: {dmg_out}")
    return dmg_out


def package_csharp_editor_windows(version: str, four_part: str) -> None:
    plugin_dir = EXAMPLES_DIR / "CSharpEditorPlugin"
    runner_proj = plugin_dir / "Runner" / "CSharpEditorPlugin.Runner.csproj"
    packaging_dir = plugin_dir / "packaging"
    windows_dir = packaging_dir / "windows"

    print("\n📦 [1/3] Ensuring FrySharp packaging artwork...")
    gen_script = packaging_dir / "tools" / "generate_assets.py"
    subprocess.run([sys.executable, str(gen_script)], check=True)

    print(f"\n🚀 [2/3] Publishing FrySharp standalone (win-x64, v{version})...")
    publish_dir = REPO_ROOT / "publish" / "csharpeditor" / "win-x64"
    if publish_dir.exists():
        shutil.rmtree(publish_dir)

    publish_cmd = [
        "dotnet",
        "publish",
        str(runner_proj),
        "-c",
        "Release",
        "-r",
        "win-x64",
        "--self-contained",
        "true",
        f"-p:Version={version}",
        f"-p:AssemblyVersion={four_part}",
        f"-p:FileVersion={four_part}",
        f"-p:InformationalVersion={version}",
        "-o",
        str(publish_dir),
    ]
    subprocess.run(publish_cmd, check=True)

    DIST_DIR.mkdir(parents=True, exist_ok=True)
    print("\n🪟 [3/3] Windows packaging scripts staged:")
    print(f"  - Inno Setup Script: {windows_dir / 'installer.iss'}")
    print(f"  - MSIX Script: {windows_dir / 'msix' / 'build-msix.ps1'}")
    print(f"  - Publish Directory: {publish_dir}")

    # If running on Windows with Inno Setup installed, compile installer.iss
    iscc_path = pathlib.Path(r"C:\Program Files (x86)\Inno Setup 6\ISCC.exe")
    if iscc_path.exists():
        print("  Running Inno Setup compiler...")
        cmd = [
            str(iscc_path),
            str(windows_dir / "installer.iss"),
            f"/DMyAppVersion={version}",
            f"/DMyAppVersionNumeric={four_part}",
            f"/DMyPublishDir={publish_dir}",
        ]
        subprocess.run(cmd, check=True)
        setup_exe = windows_dir / f"FrySharp-Setup-{version}.exe"
        if setup_exe.exists():
            shutil.move(str(setup_exe), str(DIST_DIR / setup_exe.name))
            print(f"  ✨ Windows Installer created: {DIST_DIR / setup_exe.name}")
    else:
        print("  (ISCC.exe not found locally; ready for execution on Windows CI runner)")


def main() -> int:
    parser = argparse.ArgumentParser(description="Package standalone FryPDF runner applications")
    parser.add_argument("target", choices=["csharpeditor"], help="Target runner to package")
    parser.add_argument("--os", choices=["macos", "windows", "all"], default=None, help="Target OS")
    parser.add_argument("--version", default="1.0.1", help="Release version (e.g. 1.0.1)")

    args = parser.parse_args()
    four_part = parse_four_part_version(args.version)
    target_os = args.os or ("macos" if platform.system() == "Darwin" else "windows")

    print(f"=== Packaging {args.target} Standalone (Version {args.version}, 4-Part: {four_part}) ===")

    if target_os in ("macos", "all") and platform.system() == "Darwin":
        package_csharp_editor_macos(args.version, four_part)

    if target_os in ("windows", "all"):
        package_csharp_editor_windows(args.version, four_part)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
