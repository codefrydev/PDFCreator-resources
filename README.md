# FryPDF Font Resources Repository

Official open-source typography and multilingual font library for **FryPDF / PDFCreator**.

- **Total Fonts**: 67 TrueType & OpenType fonts
- **Total Library Size**: 80.69 MB
- **Raw CDN Endpoint**: `https://raw.githubusercontent.com/codefrydev/PDFCreator-resources/refs/heads/main/fonts/{fileName}`

---

## Licensing Information

All fonts in this repository are distributed under permissive open-source licenses:
- **SIL Open Font License 1.1** (64 fonts): See [`OFL.txt`](./OFL.txt)
- **Apache License 2.0** (`Roboto.ttf`, `RobotoMono.ttf`): See [`LICENSE-Apache-2.0.txt`](./LICENSE-Apache-2.0.txt)
- **Ubuntu Font Licence 1.0** (`Ubuntu.ttf`): See [`LICENSE-Ubuntu.txt`](./LICENSE-Ubuntu.txt)

For full copyright details and author attributions, see [`LICENSE`](./LICENSE).

---

## Directory Structure

```
PDFCreator-resources/
├── LICENSE                    # Comprehensive license attributions
├── OFL.txt                    # SIL Open Font License 1.1 text
├── LICENSE-Apache-2.0.txt     # Apache License 2.0 text
├── LICENSE-Ubuntu.txt         # Ubuntu Font Licence text
├── README.md                  # This file
├── fonts/                     # 67 TrueType font binaries
│   ├── BeVietnamPro.ttf
│   ├── Inter.ttf
│   ├── NotoSansDevanagari.ttf
│   └── ...
└── tools/                     # Diagnostic & validation utilities
    └── verify_fonts.py
```

---

## Usage in FryPDF

In FryPDF, fonts are retrieved on demand using `FontPackageService.cs`:
```csharp
public const string FontCdnBaseUrl = "https://raw.githubusercontent.com/codefrydev/PDFCreator-resources/refs/heads/main/fonts";
```
Downloaded fonts are cached locally in `AppData/FryPDF/FontPackages/` and dynamically registered into QuestPDF and Avalonia graphics pipelines without requiring application restarts.
