# STRICT MANDATE: Google Material Design 3 (M3) Expressive UI

**APPLIES TO ALL AI AGENTS AND DEVELOPERS**:
Whenever you create new UI, modify existing UI, add dialogs, sidebars, buttons, controls, or adjust styles/themes in FryPDF, you **MUST STRICTLY FOLLOW GOOGLE MATERIAL DESIGN 3 (M3) EXPRESSIVE** guidelines.
Never use sharp, rigid, boxy, or flat legacy styling.

---

## 1. Shape Scale Hierarchy (NEVER HARDCODE SMALL CORNER RADII)
Never write arbitrary `CornerRadius="2"`, `"4"`, `"5"` on interactive controls or containers. Always reference the tokens in `Material3ExpressiveTokens.axaml`:

- **Pill Contours (`CornerRadius="{StaticResource M3ShapeCornerFull}"` or `9999`)**:
  - Primary, accent, tonal, and action buttons (`primary-btn`, `accent-btn`, `action-pill-btn`, `m3-filled-btn`, `m3-tonal-btn`)
  - Segmented button capsules (`Border.m3-segmented-container` + `Button.m3-segment-btn`)
  - Global search fields (`TextBox.m3-search`, Command Palette search bar)
  - Floating Action Buttons (`m3-fab`, `m3-fab-extended`)
  - Chubby slider thumbs
- **28px Extra-Large Contours (`CornerRadius="{StaticResource M3ShapeCornerExtraLarge}"`)**:
  - Modal dialog cards (`Border.m3-dialog-card`, `AboutDialog`, `CommandPaletteDialog`, `AiAssistantDialog`)
  - Large hero promo cards and dashboard banners
- **16px Large Contours (`CornerRadius="{StaticResource M3ShapeCornerLarge}"`)**:
  - Content cards (`m3-card-elevated`, `m3-card-filled`, `m3-card-outlined`)
  - Form fields and inputs (`TextBox.m3-outlined`, `ComboBox`, `NumericUpDown`)
  - Inspector section cards
- **12px Medium Contours (`CornerRadius="{StaticResource M3ShapeCornerMedium}"`)**:
  - Context menus, dropdown menus, flyouts, tooltips, list items
- **8px Small Contours (`CornerRadius="{StaticResource M3ShapeCornerSmall}"`)**:
  - Chips (`m3-chip`, `m3-filter-chip`), thumbnail cards, status badges

---

## 2. Color Roles (NO HARDCODED LIGHT/DARK HEX COLORS)
Never hardcode hex colors (like `#FFFFFF`, `#000000`, `#F1F5F9`) in templates or hover triggers that break dark mode adaptability. Always use `{DynamicResource ...}` tokens:

- **Primary Actions**: `M3PrimaryBrush`, `M3OnPrimaryBrush`, `M3PrimaryContainerBrush`, `M3OnPrimaryContainerBrush`
- **Secondary / Active Accents**: `M3SecondaryBrush`, `M3SecondaryContainerBrush`, `M3OnSecondaryContainerBrush`
- **Surfaces & Layers**: `M3SurfaceBrush`, `M3SurfaceDimBrush`, `M3SurfaceContainerLowestBrush` through `M3SurfaceContainerHighestBrush`
- **Borders & Dividers**: `M3OutlineBrush`, `M3OutlineVariantBrush`
- **Legacy Aliases**: When maintaining existing views, `WinBgBrush`, `WinPanelBrush`, `WinBorderBrush`, `WinAccentBrush`, `WinTextBrush`, `WinMutedBrush`, `WinHoverBrush`, `WinActiveBrush`, `WinInputBgBrush` map automatically to M3 tokens.

---

## 3. Standard Component Classes (`Material3ExpressiveStyles.axaml`)
- **Buttons**: `m3-filled-btn`, `m3-tonal-btn`, `m3-elevated-btn`, `m3-outlined-btn`, `m3-text-btn`, `m3-fab`, `m3-icon-btn`
- **Capsules & Chips**: `Border.m3-segmented-container`, `Button.m3-segment-btn`, `Button.m3-chip`, `ToggleButton.m3-filter-chip`
- **Cards & Dialogs**: `Border.m3-card-elevated`, `Border.m3-dialog-card`
- **Inputs & Search**: `TextBox.m3-outlined`, `TextBox.m3-search`
- **Tactile Sliders**: Chubby 8-10px tracks with 20-22px thumbs and interactive scale transitions.

---

## 4. MVVM: Maximum CommunityToolkit.Mvvm Utilization
Take full advantage of **`CommunityToolkit.Mvvm`** across all ViewModels:
- **`[ObservableProperty]`**: Generate observable properties from private fields (`[ObservableProperty] private string _title;`).
- **`[NotifyPropertyChangedFor]` & `[NotifyCanExecuteChangedFor]`**: Cascade dependent updates and automatically refresh button commands without manual boilerplate.
- **`[RelayCommand]` & Async Commands**: Decorate methods directly with `[RelayCommand]`. Asynchronous tasks (`async Task DoWorkAsync()`) automatically handle `IsRunning`, cancellation, and locking.
- **`WeakReferenceMessenger`**: Use decoupled pub/sub messaging (`WeakReferenceMessenger.Default.Send(...)` / `Register(...)`) or inherit from `ObservableRecipient` to prevent memory leaks and strong references.
- **`ObservableValidator`**: Use for forms, dialogs, and validated inputs with standard DataAnnotations (`[Required]`, `[Range]`, etc.).

---

## 5. Verification
After making any UI changes:
1. `dotnet build` on the plugin solution (`<PluginName>Plugin.slnx`) must succeed with **0 warnings and 0 errors** (`TreatWarningsAsErrors=true`).
2. Run the standalone runner (`Runner/<PluginName>Plugin.Runner.csproj`) to inspect visual fidelity, token inheritance, and light/dark theme adaptability.
3. Run `python3 tools/validate_plugins.py` to ensure package and manifest integrity.

