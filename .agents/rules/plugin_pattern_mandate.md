# STRICT ARCHITECTURE MANDATE: Modular Microkernel Plugin Pattern ("Everything is a Plugin")

**APPLIES TO ALL AI AGENTS AND DEVELOPERS**:
Whenever you add features, create tools, contribute UI components, introduce file formats, integrate AI or OCR models, or refactor existing subsystems in FryPDF, you **MUST STRICTLY FOLLOW THE MODULAR MICROKERNEL PLUGIN PATTERN**.
Never write monolithic switch-case statements, god classes, or hardcoded UI component instantiations.

---

## 1. The "Everything is a Plugin" Principle
FryPDF's architecture is inspired by **DeepSeek Harness (`deepseek-harness`)** and the **Cordis** framework:
- The core runtime does not privilege any single subsystem.
- All tools, workspace pages, sidebar panels, ribbon buttons, inspector property sections, canvas elements, AI providers, OCR engines, data connectors, and file importers/exporters are **modular plugins mounted on `IFryPluginContext`**.

---

## 2. Core Implementation Rules

### A. Inherit from `IFryPlugin` or `ToolPluginBase`
- For PDF tools, inherit from `ToolPluginBase` (which provides automated descriptor registration into `IPdfToolRegistry`).
- For other capabilities, implement `IFryPlugin` directly (`Id`, `Name`, `Version`, `RequiredServices`, `ApplyAsync`).

### B. Mount Through the 12 Dynamic Registry Pillars
Never instantiate or route components via hardcoded `switch` statements. Always register through the dynamic registry corresponding to the capability:
1. **Tools**: `IPdfToolRegistry` via `ctx.RegisterTool(...)` / `PdfToolDescriptor`
2. **Ribbon Tabs & Groups**: `IRibbonRegistry` via `ctx.RegisterRibbonContribution(...)`
3. **Sidebar Panels**: `ISidebarRegistry` via `ctx.RegisterSidebarTab(...)`
4. **Inspector Sections**: `IInspectorRegistry` via `ctx.RegisterInspectorSection(...)`
5. **Workspace Pages**: `INavigationRegistry` via `ctx.RegisterNavigationItem(...)`
6. **Canvas Elements**: `ICanvasElementRegistry` via `ctx.RegisterCanvasElement(...)`
7. **Document Importers**: `IDocumentImporterRegistry`
8. **Document Exporters**: `IDocumentExporterRegistry`
9. **AI Providers**: `IAiProviderRegistry`
10. **OCR Engines**: `IOcrEngineRegistry`
11. **Data Connectors**: `IDataConnectorRegistry`
12. **Status Bar & Modals**: `IStatusBarRegistry`, `IDialogRegistry`

### C. Reversible Effects (`PluginScope` & LIFO Rollback)
Every registration—whether an event listener, a message broker subscription, a status bar widget, or an unmanaged cache—**MUST be registered through `PluginScope` (`ctx.RegisterEffect(...)`)**.
When a plugin is unmounted or reloaded, its effects unwind in reverse order to ensure **100% clean teardown and zero memory leaks**.

### D. Group Capabilities into Bundles (`IFryPluginBundle`)
Group related plugins into a bundle (e.g. `ToolsOrganizeBundle`, `CanvasElementsBundle`, `WorkspacePagesBundle`). Bundles allow composition into runtime profiles (`desktop.profile.json`, `headless.profile.json`).

### E. Declarative Settings Schema
If a plugin requires configuration (API keys, URLs, model names, options), declare them via `IFryPlugin.SettingsSchema` using `PluginSettingDefinition`. The UI automatically renders an M3 Expressive configuration card in `PluginsDialog.axaml` without manual XAML forms.

---

## 3. Verification
- `dotnet build` on the plugin solution (`<PluginName>Plugin.slnx`) must succeed with **0 warnings and 0 errors** (`TreatWarningsAsErrors=true`).
- Test locally using the standalone runner (`Runner/<PluginName>Plugin.Runner.csproj`).
- Run ecosystem validation:
  ```bash
  python3 tools/validate_plugins.py
  ```

