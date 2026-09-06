# STRICT MANDATE: FryPDF Plugin Authoring, Testing & Marketplace Distribution

**APPLIES TO ALL AI AGENTS AND DEVELOPERS**:
Whenever you create, modify, package, or distribute plugins in this repository, you **MUST STRICTLY COMPLY WITH THE STANDARDS AND PATTERNS DEFINED IN THIS MANDATE**.

---

## 1. Plugin Repository Architecture

FryPDF plugins are maintained and published through a dual-directory structure:

1. **Source Code & Standalone Development (`examples/<PluginName>Plugin/`)**:
   - Contains the complete C# source code, Avalonia XAML views, ViewModels, and unit/integration runner.
   - Must be independently compilable and testable.
2. **Distribution Center (`plugins/<id>/`)**:
   - Contains the compiled release archive (`<Name>.fryplugin`), distribution manifest (`plugin.json`), and markdown overview (`README.md`).
   - Referenced directly by the central registry (`plugins/catalog.json`).

---

## 2. Standard Plugin Project Layout

Every plugin under `examples/` **MUST** follow this exact folder structure:

```
examples/<PluginName>Plugin/
├── <PluginName>Plugin.slnx          # Modern XML solution file uniting Plugin + Runner
├── <PluginName>Plugin.csproj        # Dual-mode .NET 10 project with packaging target
├── <PluginName>Plugin.cs            # IFryPlugin implementation & registry mounts
├── <PluginName>View.axaml           # M3 Expressive Avalonia UI
├── <PluginName>View.axaml.cs        # Code-behind
├── <PluginName>ViewModel.cs         # CommunityToolkit.Mvvm reactive ViewModel
├── plugin.json                      # Declarative manifest and settings schema
├── README.md                        # Architecture, features, and setup instructions
└── Runner/                          # Standalone development host application
    ├── <PluginName>Plugin.Runner.csproj  # WinExe executable project
    ├── Program.cs                   # Avalonia classic desktop entry point
    ├── App.axaml                    # FluentTheme, Inter font, and Material icons
    ├── App.axaml.cs                 # App bootstrap
    ├── MainWindow.axaml             # Test window hosting the plugin view
    ├── MainWindow.axaml.cs          # Test window code-behind
    └── StandaloneSettingsStore.cs   # File-backed settings store and service provider
```

---

## 3. Dual-Mode Project Configuration (`.csproj`)

The main plugin `.csproj` must support both in-tree repository references (when the `PDFCreator` sibling repository exists) and standalone installed builds.

It must include:
1. `<DefaultItemExcludes>$(DefaultItemExcludes);Runner/**</DefaultItemExcludes>`: Isolates the runner source files from the plugin library.
2. `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`: Zero warnings permitted.
3. `<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>`: Ensures third-party package dependencies (e.g. LibVLC, TagLib) are copied to output for packaging.
4. `PackageFryPlugin` MSBuild target: Automatically packages the `.fryplugin` zip archive on `Release` builds:
   ```xml
   <Target Name="PackageFryPlugin" AfterTargets="Build" Condition="'$(Configuration)' == 'Release'">
     <PropertyGroup>
       <PluginStagingDir>$(TargetDir)staging\</PluginStagingDir>
       <OutputFryPlugin>$(TargetDir)<PluginName>.fryplugin</OutputFryPlugin>
     </PropertyGroup>
     <!-- Copies DLLs + plugin.json, then runs ZipDirectory -->
   </Target>
   ```

---

## 4. Standalone Runner Mandate

Every plugin **MUST** provide a standalone `Runner/` subproject:
- Developers must be able to open `<PluginName>Plugin.slnx`, press **F5 (Run)**, and immediately interact with the plugin view.
- The runner must supply:
  - Avalonia FluentTheme and Inter font (`WithInterFont()`).
  - Material Icons (`Material.Icons.Avalonia`).
  - A mock or file-backed `StandaloneSettingsStore` implementing `IFrySettingsStore` or minimal settings dictionary so settings can be previewed and tested.

---

## 5. Manifest Schema (`plugin.json`)

The manifest defines the plugin identity and declarative configuration schema:

```json
{
  "id": "frypdf.overlay.myfeature",
  "name": "My Feature Overlay",
  "version": "1.0.0",
  "author": "Code Fry Dev",
  "description": "Short 1-line summary for cards and lists.",
  "entryPoint": "MyFeaturePlugin.dll",
  "icon": "GamepadVariantOutline",
  "dependencies": [],
  "settingsSchema": {
    "Difficulty": {
      "type": "select",
      "label": "AI Difficulty",
      "description": "Choose opponent intelligence level",
      "default": "Smart",
      "options": ["Easy", "Smart", "Unbeatable"]
    },
    "EnableSound": {
      "type": "boolean",
      "label": "Sound Effects",
      "description": "Play interactive audio cues during gameplay",
      "default": true
    }
  }
}
```

---

## 6. Marketplace Publishing & Catalog Integration

When adding or updating a plugin:
1. **Compile & Package**:
   ```bash
   python3 tools/package_plugin.py <PluginName>Plugin
   ```
2. **Synchronize Distribution Directory**:
   Verify `plugins/<id>/` contains:
   - `<Name>.fryplugin`
   - `plugin.json`
   - `README.md`
3. **Update `plugins/catalog.json`**:
   Ensure entry contains:
   - `id`: Exact match to `plugin.json`.
   - `name`, `publisher`, `version`, `category`, `description`, `longDescription`.
   - `rating` (numeric), `ratingCount`, `installCount`, `formattedSize`.
   - `iconKind`, `iconColorHex`, `license`, `isVerified`, `isOfficial`.
   - `downloadUrl`: Raw CDN link pointing to `https://raw.githubusercontent.com/codefrydev/PDFCreator-resources/refs/heads/main/plugins/<id>/<Name>.fryplugin`.
   - `tags`, `highlights`, `contributedFeatures`, `dependencies`.

---

## 7. Verification

Always execute the automated validator before completing any work:
```bash
# Validate ecosystem integrity (catalog, packages, manifests, solution runners)
python3 tools/validate_plugins.py
```
A passing verification must exit with code 0 and display:
`✔ ALL VALIDATION CHECKS PASSED (0 errors)`
