#!/usr/bin/env python3
"""
FryPDF Typography & Font Resource Validator
Verifies font files in fonts/ directory for valid format, existence, and sizes.
"""

from pathlib import Path
import sys

ROOT_DIR = Path(__file__).resolve().parent.parent
FONTS_DIR = ROOT_DIR / "fonts"


def main():
    print("=" * 60)
    print("  FryPDF Typography & Font Resource Verification")
    print("=" * 60)

    if not FONTS_DIR.exists():
        print(f"Error: fonts directory not found at {FONTS_DIR}")
        sys.exit(1)

    font_files = sorted(
        [f for f in FONTS_DIR.iterdir() if f.is_file() and f.suffix.lower() in [".ttf", ".otf"]],
        key=lambda f: f.name.lower()
    )

    total_size = sum(f.stat().st_size for f in font_files)
    total_mb = total_size / (1024 * 1024)

    print(f"\nFound {len(font_files)} fonts ({total_mb:.2f} MB total):\n")

    corrupt_count = 0
    for font in font_files:
        size_kb = font.stat().st_size / 1024
        # Basic binary header check for TTF (0x00010000 or 'true') / OTF ('OTTO')
        with open(font, "rb") as fp:
            header = fp.read(4)
        is_valid = header in [b"\x00\x01\x00\x00", b"true", b"OTTO"]
        status = "✔" if is_valid else "✖"
        if not is_valid:
            corrupt_count += 1
        print(f"  {status} {font.name:<32} ({size_kb:>7.1f} KB)")

    print("\n" + "=" * 60)
    if corrupt_count == 0:
        print(f"\033[32m✔ ALL {len(font_files)} FONTS VALIDATED SUCCESSFULLY ({total_mb:.2f} MB)\033[0m")
        print("=" * 60)
        sys.exit(0)
    else:
        print(f"\033[31m✖ {corrupt_count} FONT(S) FAILED VALIDATION\033[0m")
        print("=" * 60)
        sys.exit(1)


if __name__ == "__main__":
    main()
