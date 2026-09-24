# Release Tagging Mandate — PDFCreator-resources

> [!CAUTION]
> NEVER create tags with prefixes like `csharp-v*`, `plugin/*`, `frysharp-v*`, or any other
> prefix format. ONLY use `vX.Y.Z`. Violating this pollutes the tag list and breaks CI.

---

## 1. The One and Only Tag Format

```
vX.Y.Z
```

**Examples of CORRECT tags:**
- `v1.0.3` ✅
- `v0.1.2` ✅
- `v1.1.0` ✅

**Examples of WRONG tags — NEVER CREATE THESE:**
- `csharp-v1.0.3` ❌
- `plugin/csharpeditor/v1.0.3` ❌
- `frysharp-v1.0.3` ❌
- `csharp-studio-v1.0.3` ❌

---

## 2. What a Single `vX.Y.Z` Tag Does

Pushing `vX.Y.Z` to `PDFCreator-resources` triggers **both** CI workflows simultaneously:

| Workflow | What it builds | Release assets |
|---|---|---|
| `FrySharp & C# Studio Release` | macOS DMG, Windows EXE + MSIX, .fryplugin | All attached to GitHub Release `vX.Y.Z` |
| `Publish Plugins to NuGet & Marketplace` | NuGet package | Published to NuGet |

One tag → everything. Do NOT create separate tags to "trigger" individual workflows.

---

## 3. Full Release Checklist (in order)

```bash
# Step 1 — bump versions in the submodule (CSharpPlayground)
# Edit: examples/CSharpEditorPlugin/plugin.json          → "version": "X.Y.Z"
# Edit: examples/CSharpEditorPlugin/CSharpEditorPlugin.csproj → <Version>X.Y.Z</Version>
# Edit: examples/CSharpEditorPlugin/Runner/CSharpEditorPlugin.Runner.csproj → <Version>X.Y.Z</Version>

# Step 2 — build and test
dotnet build examples/CSharpEditorPlugin/CSharpEditorPlugin.slnx -c Release
dotnet test  examples/CSharpEditorPlugin/CSharpEditorPlugin.slnx -c Release

# Step 3 — stage plugin distribution in resources repo
mkdir -p plugins/com.frypdf.plugin.csharpeditor/vX.Y.Z
cp examples/CSharpEditorPlugin/bin/Release/net10.0/CSharpEditor.fryplugin \
   plugins/com.frypdf.plugin.csharpeditor/vX.Y.Z/
cp examples/CSharpEditorPlugin/plugin.json \
   plugins/com.frypdf.plugin.csharpeditor/vX.Y.Z/

# Step 4 — update plugins/catalog.json
#   • top-level "version", "downloadUrl", "sha256"
#   • prepend new entry to "versions[]" array
#   Verify: python3 -c "import json; json.load(open('plugins/catalog.json'))"

# Step 5 — validate
python3 tools/validate_plugins.py   # must show 0 errors

# Step 6 — commit in SUBMODULE first, push submodule
cd examples/CSharpEditorPlugin
git add -A
git commit -m "chore: bump version to X.Y.Z"
git push origin main

# Step 7 — commit in resources repo, push
cd ../..  (back to PDFCreator-resources root)
git add plugins/
git commit -m "release: C# Code Studio plugin vX.Y.Z"
git push origin main

# Step 8 — push ONE tag: vX.Y.Z  (THIS triggers CI and creates the GitHub Release)
git tag vX.Y.Z
git push origin vX.Y.Z
```

**That's it. CI does the rest — DMG, EXE, MSIX, GitHub Release.**

---

## 4. Version Bump Rules

| Change type | Which part to bump | Example |
|---|---|---|
| Bug fixes, refactors, stability | patch (Z) | `1.0.2` → `1.0.3` |
| New features, UI additions | minor (Y) | `1.0.3` → `1.1.0` |
| Breaking API / major redesign | major (X) | `1.1.0` → `2.0.0` |

All three version files must always be in sync:
- `examples/CSharpEditorPlugin/plugin.json`
- `examples/CSharpEditorPlugin/CSharpEditorPlugin.csproj`
- `examples/CSharpEditorPlugin/Runner/CSharpEditorPlugin.Runner.csproj`

---

## 5. What NOT to Do (lessons learned)

| Mistake | Why it's wrong |
|---|---|
| `git tag csharp-v1.0.3` | Creates junk tag, CI creates release with wrong name |
| `git tag plugin/csharpeditor/v1.0.3` | Pollutes tag list, creates a release nobody wants |
| `gh release create ...` manually before CI | CI will create its own and conflict or duplicate |
| Pushing tag before committing version bumps | CI builds with stale version numbers |
| Forgetting `validate_plugins.py` | Ships broken catalog.json to marketplace |
