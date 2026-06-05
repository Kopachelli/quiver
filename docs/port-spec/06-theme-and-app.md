# 06 — Theme & App (Design System, App Lifecycle, Settings)

Port spec for the macOS **Skills** (`skillz`) app's theming layer, app lifecycle,
and settings, targeting a **Windows-native WPF / .NET 8 (LTS)** app using
**WPF-UI (lepoco/wpfui)** Fluent controls, **MVVM**, and **AvalonEdit** for the
markdown editor.

Source of truth (macOS):
- `skillz/Theme/SkillzColors.swift`
- `skillz/Theme/SkillzComponents.swift`
- `skillz/Theme/SkillzSpacing.swift`
- `skillz/Theme/SkillzTextStyles.swift`
- `skillz/Theme/SkillzTypography.swift`
- `skillz/Theme/SkillzWindowMetrics.swift`
- `skillz/Theme/AppBrand.swift`
- `skillz/skillzApp.swift`
- `skillz/Settings/AppSettings.swift`
- `skillz/Settings/SettingsView.swift`
- `skillz/Settings/SettingsPane.swift`
- `skillz/Assets.xcassets/*.colorset/Contents.json` (color definitions)

> **Design language summary:** A minimal, monochrome, monospaced editor-style UI.
> No chromatic accent — the "accent" is pure black (`#000000`). Everything is
> grayscale: black ink on white canvas in light mode, near-white ink on near-black
> canvas in dark mode. All text is **monospaced** (matching the markdown editor
> aesthetic). This is intentional and must be preserved on Windows — the app should
> NOT pick up the user's Windows accent color.

---

## 1. Color Tokens

### 1.1 Definition mechanism (macOS)

`SkillzColors.swift` defines an `enum SkillzColors` whose members reference named
colors in the asset catalog by string:

```swift
enum SkillzColors {
    static let canvas       = Color("SkillzCanvas")
    static let ink          = Color("SkillzInk")
    static let emphasis     = Color("SkillzEmphasis")
    static let muted        = Color("SkillzMuted")
    static let sectionLabel = Color("SkillzSectionLabel")
    static let disabled     = Color("SkillzDisabled")
    static let hairline     = Color("SkillzHairline")
    static let selection    = Color("SkillzSelection")
}
```

Throughout the views the colors are actually consumed as `Color.skillzCanvas`,
`Color.skillzInk`, etc. These `Color.skillz*` symbol accessors are **auto-generated
by Xcode's asset symbol generation** from the `*.colorset` folders (Xcode 15+ emits
a `.skillzCanvas` static accessor per named color). There is no hand-written
`extension Color` for them — the asset catalog is the single source of truth, and
each colorset supplies a **light (universal/`any`)** value and a **dark
(`luminosity: dark`)** value.

The colors are defined in the **sRGB** color space with float `0.0–1.0` components
and `alpha = 1.000` (fully opaque) in every case.

### 1.2 Exact color table (from asset catalog JSON → `#RRGGBB`)

All values are grayscale (R = G = B). Hex derived from `round(component × 255)`.

| Token (`Color.skillz…`) | Asset name | Light RGB (0–1) | **Light hex** | Dark RGB (0–1) | **Dark hex** | Role / Usage |
|---|---|---|---|---|---|---|
| `canvas` | `SkillzCanvas` | 1.000 / 1.000 / 1.000 | `#FFFFFF` | 0.118 / 0.118 / 0.118 | `#1E1E1E` | Primary background surface (windows, cards, panes, list rows) |
| `ink` | `SkillzInk` | 0.000 / 0.000 / 0.000 | `#000000` | 0.969 / 0.969 / 0.969 | `#F7F7F7` | Strongest fill — filled tag background, prominent button fill, link-style buttons |
| `emphasis` | `SkillzEmphasis` | 0.200 / 0.200 / 0.200 | `#333333` | 0.820 / 0.820 / 0.820 | `#D1D1D1` | Primary text color (titles, body, detail values) |
| `muted` | `SkillzMuted` | 0.325 / 0.325 / 0.325 | `#535353` | 0.616 / 0.616 / 0.616 | `#9D9D9D` | Secondary text (descriptions, captions, counts, placeholders) |
| `sectionLabel` | `SkillzSectionLabel` | 0.278 / 0.278 / 0.278 | `#474747` | 0.557 / 0.557 / 0.557 | `#8E8E8E` | Uppercase section header labels, detail row labels |
| `disabled` | `SkillzDisabled` | 0.467 / 0.467 / 0.467 | `#777777` | 0.459 / 0.459 / 0.459 | `#757575` | Disabled-state foreground (defined token; near-identical L/D) |
| `hairline` | `SkillzHairline` | 0.910 / 0.910 / 0.910 | `#E8E8E8` | 0.235 / 0.235 / 0.235 | `#3C3C3C` | 1px separators, card/tag borders |
| `selection` | `SkillzSelection` | 0.835 / 0.835 / 0.835 | `#D5D5D5` | 0.180 / 0.180 / 0.180 | `#2E2E2E` | Selected/hovered row fill, selected settings-tab fill |
| `AccentColor` | `AccentColor` | 0.000 / 0.000 / 0.000 | `#000000` | *(no dark variant — universal only)* | `#000000` | App accent. **Pure black, no chroma.** No dark override defined; same in both modes. |

> **Note on `AccentColor`:** the colorset has only a single universal entry (no
> `luminosity: dark` appearance), so the accent is `#000000` in both light and dark.
> On macOS a pure-black accent is unusual but deliberate for this monochrome design.
> On Windows do **not** bind to `SystemParameters`/`AccentColorBrush`; hardcode the
> token so the look is consistent.

### 1.3 Common derived/opacity variants used in code

These are computed at call sites, not stored as tokens. Reproduce them as brushes
or via opacity in WPF:

| Derived value | Where used |
|---|---|
| `skillzSelection.opacity(0.58)` | Selected settings tab background fill |
| `skillzSelection.opacity(0.5)` | Hovered (not selected) list-row background |
| `skillzEmphasis.opacity(0.35)` | Non-prominent text-button border |
| `black.opacity(0.08)` | Error-banner drop shadow color |

### 1.4 Windows / WPF equivalent — ResourceDictionary

macOS named colors with light/dark appearances map directly to **two merged
`ResourceDictionary` theme files** (`Light.xaml` / `Dark.xaml`) swapped at runtime,
plus a shared dictionary with non-themed keys. WPF-UI ships a theme manager
(`ApplicationThemeManager.Apply(ApplicationTheme.Light/Dark)`); back each named color
with both a `Color` and a `SolidColorBrush` resource so XAML can use either.

`Themes/Colors.Light.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Color x:Key="SkillzCanvasColor">#FFFFFFFF</Color>
    <Color x:Key="SkillzInkColor">#FF000000</Color>
    <Color x:Key="SkillzEmphasisColor">#FF333333</Color>
    <Color x:Key="SkillzMutedColor">#FF535353</Color>
    <Color x:Key="SkillzSectionLabelColor">#FF474747</Color>
    <Color x:Key="SkillzDisabledColor">#FF777777</Color>
    <Color x:Key="SkillzHairlineColor">#FFE8E8E8</Color>
    <Color x:Key="SkillzSelectionColor">#FFD5D5D5</Color>
    <Color x:Key="SkillzAccentColor">#FF000000</Color>

    <SolidColorBrush x:Key="SkillzCanvasBrush"       Color="{StaticResource SkillzCanvasColor}"/>
    <SolidColorBrush x:Key="SkillzInkBrush"          Color="{StaticResource SkillzInkColor}"/>
    <SolidColorBrush x:Key="SkillzEmphasisBrush"     Color="{StaticResource SkillzEmphasisColor}"/>
    <SolidColorBrush x:Key="SkillzMutedBrush"        Color="{StaticResource SkillzMutedColor}"/>
    <SolidColorBrush x:Key="SkillzSectionLabelBrush" Color="{StaticResource SkillzSectionLabelColor}"/>
    <SolidColorBrush x:Key="SkillzDisabledBrush"     Color="{StaticResource SkillzDisabledColor}"/>
    <SolidColorBrush x:Key="SkillzHairlineBrush"     Color="{StaticResource SkillzHairlineColor}"/>
    <SolidColorBrush x:Key="SkillzSelectionBrush"    Color="{StaticResource SkillzSelectionColor}"/>
    <SolidColorBrush x:Key="SkillzAccentBrush"       Color="{StaticResource SkillzAccentColor}"/>

    <!-- Derived (opacity-baked) brushes -->
    <SolidColorBrush x:Key="SkillzSelectionTabBrush"   Color="#D5D5D5" Opacity="0.58"/>
    <SolidColorBrush x:Key="SkillzSelectionHoverBrush" Color="#D5D5D5" Opacity="0.5"/>
    <SolidColorBrush x:Key="SkillzEmphasisBorderBrush" Color="#333333" Opacity="0.35"/>
</ResourceDictionary>
```

`Themes/Colors.Dark.xaml` is identical structure with the dark hexes:
`Canvas #1E1E1E`, `Ink #F7F7F7`, `Emphasis #D1D1D1`, `Muted #9D9D9D`,
`SectionLabel #8E8E8E`, `Disabled #757575`, `Hairline #3C3C3C`, `Selection #2E2E2E`,
`Accent #000000` (unchanged), and the derived brushes recomputed off `#2E2E2E`
(`SelectionTab`/`SelectionHover`) and `#D1D1D1` (`EmphasisBorder`).

**Theme switching:** Keep the brush *keys* identical across both dictionaries and
swap the merged dictionary at runtime (or use WPF-UI's `ApplicationThemeManager`).
All XAML uses `{DynamicResource SkillzCanvasBrush}` (never `StaticResource` for
themed brushes) so a theme change re-renders without recreating views.

---

## 2. Typography

### 2.1 Type scale (macOS — `SkillzTypography.swift`)

**All roles use `design: .monospaced`** (SF Mono / the system monospaced face).
This is the defining characteristic — it must carry over to Windows.

| Role | Token | Size (pt) | Weight | Usage |
|---|---|---|---|---|
| Navigation title | `navigationTitle` | 15 | semibold | Window / column titles, settings pane title |
| Headline | `headline` | 14 | semibold | Detail-page headings |
| Title | `title` | 13 | medium | Empty states, primary labels |
| List title | `listTitle` | 13 | medium | Catalog row names |
| Nav item | `navItem` | 12 | regular | Sidebar rows |
| Body | `body` | 12 | regular | Descriptions, form controls |
| Caption | `caption` | 11 | regular | Metadata, paths, counts |
| Caption strong | `captionMedium` | 11 | medium | Tags, pill labels, detail-row labels |
| Section header | `sectionHeader` | 10 | medium | Uppercase section labels (tracking 0.6, uppercased) |
| Mono | `mono` | 12 | regular | Paths, code, detail values when `mono == true` |

**Selection-aware variants** (bump weight when a row is selected):
- `listTitle(selected:)` → medium normally, **semibold** when selected.
- `navItem(selected:)` → regular normally, **semibold** when selected.
- `navCount(selected:)` → regular normally, **medium** when selected (size 11).

**Editor font** (dynamic): `editor(size:)` → `Font.system(size:, weight:.regular,
design:.monospaced)`. Size is driven by `AppSettings.editorFontSize` (default 14,
range 10–20). Consumed by `MarkdownEditorView`, `NewSkillSheet`, `SkillDetailView`.

### 2.2 Pill & tag metrics

```swift
enum SkillzPillMetrics {            // toolbar pill buttons / search field
    static let font = .system(size: 12, weight: .regular, design: .monospaced)
    static let height: CGFloat = 32
    static let horizontalPadding: CGFloat = 12
    static let iconWidth: CGFloat = 14
}
enum SkillzTagMetrics {             // capsule tags
    static let height: CGFloat = 22
    static let horizontalPadding: CGFloat = 10
    static let font = SkillzTypography.captionMedium   // 11pt medium mono
}
```

### 2.3 Text style modifiers (`SkillzTextStyles.swift`)

Each `Text` modifier pairs a font with a color (and sometimes casing/tracking):

| Modifier | Font | Foreground | Extra |
|---|---|---|---|
| `skillzNavigationTitleStyle` | navigationTitle | emphasis | — |
| `skillzHeadlineStyle` | headline | emphasis | — |
| `skillzTitleStyle` | title | emphasis | — |
| `skillzListTitleStyle(isSelected:)` | listTitle(selected) | emphasis | — |
| `skillzNavItemStyle(isSelected:)` | navItem(selected) | emphasis | — |
| `skillzNavCountStyle(isSelected:)` | navCount(selected) | emphasis if selected else muted | — |
| `skillzBodyStyle` | body | emphasis | — |
| `skillzBodySecondaryStyle` | body | muted | — |
| `skillzCaptionStyle` | caption | muted | — |
| `skillzCaptionStrongStyle` | captionMedium | muted | — |
| `skillzSectionHeaderStyle` | sectionHeader | sectionLabel | `tracking(0.6)`, `textCase(.uppercase)` |
| `skillzMonoStyle` | mono | emphasis | — |
| `skillzDetailLabelStyle` | captionMedium | sectionLabel | — |
| `skillzDetailValueStyle(mono:)` | mono or body | emphasis | — |

### 2.4 Windows / WPF equivalent — typography

- **Mono face:** SwiftUI `design: .monospaced` → **Cascadia Code** (preferred,
  ships with Windows Terminal / VS) with **Consolas** fallback. AvalonEdit editor
  uses the same: `FontFamily = "Cascadia Code, Consolas"`.
- **UI face for non-mono chrome (if any):** **Segoe UI Variable** (Windows 11
  default). *But note: this app uses mono everywhere, so most text should stay
  Cascadia/Consolas.* Only fall back to Segoe UI Variable for native WPF-UI
  controls where mono would look wrong (e.g. window chrome buttons).
- **Sizes:** WPF uses **device-independent units (1/96 in)**, not points. macOS
  points are 1/72 in. Convert with `px = pt × 96/72 = pt × 4/3`. Recommendation:
  keep the same numeric values (12→12) for visual parity since macOS @1x≈Windows DIP
  in practice and the design was tuned by eye; if exact metric matching is required,
  multiply by 1.333 (12pt→16, 13→17.33, 14→18.67, 15→20, 11→14.67, 10→13.33).
  **Recommended: keep the raw numbers and validate visually.**
- **Weights:** semibold → `FontWeights.SemiBold`, medium → `FontWeights.Medium`,
  regular → `FontWeights.Normal`.
- Express as resources, e.g. `Themes/Typography.xaml`:

```xml
<FontFamily x:Key="SkillzMonoFont">Cascadia Code, Consolas</FontFamily>
<FontFamily x:Key="SkillzUiFont">Segoe UI Variable, Segoe UI</FontFamily>

<sys:Double x:Key="SkillzFontNavTitle">15</sys:Double>   <!-- SemiBold -->
<sys:Double x:Key="SkillzFontHeadline">14</sys:Double>   <!-- SemiBold -->
<sys:Double x:Key="SkillzFontTitle">13</sys:Double>      <!-- Medium -->
<sys:Double x:Key="SkillzFontBody">12</sys:Double>       <!-- Normal -->
<sys:Double x:Key="SkillzFontCaption">11</sys:Double>    <!-- Normal/Medium -->
<sys:Double x:Key="SkillzFontSection">10</sys:Double>    <!-- Medium, upper, tracking -->
```

- **Tracking 0.6** on section headers → WPF `TextBlock` has no direct letter-spacing
  property; implement with a `TextOptions`/`Typography` approach or a small attached
  property that inserts spacing, or accept that WPF can't kern arbitrarily and add a
  custom `TextBlock` subclass. Simplest pragmatic route: bake spacing via a converter
  or use the run-level `BaselineAlignment`/manual spacing; many ports just drop the
  0.6 tracking — note as an **open question** if pixel-perfect.
- **Uppercasing** → use a `ValueConverter` (`ToUpperConverter`) bound on the section
  label text, or uppercase in the ViewModel.
- **Selection-aware weight bump** → `DataTrigger` on `IsSelected` swapping
  `FontWeight` between Normal/Medium and SemiBold.

---

## 3. Spacing & Radii

### 3.1 macOS (`SkillzSpacing.swift`)

```swift
enum SkillzSpacing {
    static let xs: CGFloat = 4
    static let sm: CGFloat = 8
    static let md: CGFloat = 12
    static let lg: CGFloat = 16
    static let xl: CGFloat = 24
    static let rowMinHeight: CGFloat = 52
    static let cardRadius: CGFloat = 10
    static let glassPadding: CGFloat = 8
}
```

| Token | Value | Typical use |
|---|---|---|
| `xs` | 4 | Tight vertical gaps, micro padding |
| `sm` | 8 | Inter-element spacing, row-chrome corner radius, horizontal row insets |
| `md` | 12 | Card content spacing, banner vertical padding |
| `lg` | 16 | Card padding, detail-row column gap, section spacing |
| `xl` | 24 | Settings pane padding, tab-bar horizontal padding |
| `rowMinHeight` | 52 | Minimum list-row height |
| `cardRadius` | 10 | Card / error-banner corner radius |
| `glassPadding` | 8 | Glass toolbar group inner padding |

Additional radii at call sites: list-row chrome uses `cornerRadius: SkillzSpacing.sm`
(8); settings tab uses `cornerRadius: SkillzSpacing.sm` with `.continuous` style.

### 3.2 Windows / WPF equivalent — spacing

Define as a `Thickness`/`Double` resource set. WPF has no global spacing scale, so
declare doubles and `CornerRadius` resources:

```xml
<sys:Double x:Key="SpaceXs">4</sys:Double>
<sys:Double x:Key="SpaceSm">8</sys:Double>
<sys:Double x:Key="SpaceMd">12</sys:Double>
<sys:Double x:Key="SpaceLg">16</sys:Double>
<sys:Double x:Key="SpaceXl">24</sys:Double>
<sys:Double x:Key="RowMinHeight">52</sys:Double>
<CornerRadius x:Key="CardRadius">10</CornerRadius>
<CornerRadius x:Key="RowRadius">8</CornerRadius>
<Thickness x:Key="CardPadding">16</Thickness>
<Thickness x:Key="PanePadding">24</Thickness>
```

macOS `.continuous` corner style (squircle) has no WPF equivalent — WPF
`CornerRadius` is a circular arc only. Accept the minor difference.

---

## 4. Window Metrics

### 4.1 macOS (`SkillzWindowMetrics.swift`)

```swift
enum SkillzWindowMetrics {
    static let defaultWidth: CGFloat  = 1440
    static let defaultHeight: CGFloat = 880
    static let minWidth: CGFloat  = 1200
    static let minHeight: CGFloat = 720

    static let sidebarIdeal: CGFloat = 220   // navigation sidebar
    static let sidebarMin: CGFloat   = 200
    static let sidebarMax: CGFloat   = 260

    static let listIdeal: CGFloat = 360      // item list column
    static let listMin: CGFloat   = 300
    static let listMax: CGFloat   = 420

    static let detailMin: CGFloat   = 520    // detail column
    static let detailIdeal: CGFloat = 680

    static let inspectorIdeal: CGFloat = 280 // right inspector
    static let inspectorMin: CGFloat   = 240
    static let inspectorMax: CGFloat   = 320

    static let fileTreeIdeal: CGFloat = 168  // file-tree sub-panel
    static let fileTreeMin: CGFloat   = 140
    static let fileTreeMax: CGFloat   = 200

    static let editorMin: CGFloat = 420

    static let trafficLightReservedWidth: CGFloat = 88  // macOS close/min/zoom cluster
    static let sidebarTopInsetPull: CGFloat = 18        // pull split view up under titlebar
    static let columnHeaderTopInset: CGFloat = 24       // flat column header top gap
}
```

| Metric | Value | Meaning |
|---|---|---|
| Default window | 1440 × 880 | Initial size (`.defaultSize` in `skillzApp`) |
| Min window | 1200 × 720 | Minimum size |
| Sidebar | min 200 / ideal 220 / max 260 | Left navigation column |
| Item list | min 300 / ideal 360 / max 420 | Middle catalog column |
| Detail | min 520 / ideal 680 | Detail/editor column |
| Inspector | min 240 / ideal 280 / max 320 | Right metadata inspector |
| File tree | min 140 / ideal 168 / max 200 | Sub-panel inside detail |
| Editor min | 420 | Minimum editor width |
| Traffic-light reserve | 88 | Horizontal space cleared for macOS window controls |
| Sidebar top-inset pull | 18 | Upward offset tucking the inset-sidebar card under the divider |
| Column header top inset | 24 | Extra top gap for flat (non-card) column headers |

### 4.2 Windows / WPF equivalent — window metrics

WPF-UI provides `FluentWindow` (titlebar with native min/max/close via the WPF-UI
`TitleBar` control). The macOS "hidden titlebar + content into titlebar" style maps
to `WindowStyle="None"` + WPF-UI `TitleBar`, or `ExtendsContentIntoTitleBar`-style
with a custom `TitleBar`.

- `defaultWidth/Height` → `Window.Width="1440" Height="880"`.
- `minWidth/Height` → `Window.MinWidth="1200" MinHeight="720"`.
- Multi-column layout → a `Grid` with `ColumnDefinition`s and `GridSplitter`s, or
  WPF-UI `NavigationView`. Min/ideal/max map to `ColumnDefinition`
  `MinWidth` / `Width="220"` / `MaxWidth`. WPF columns don't have a separate "ideal
  vs max" the way SwiftUI `NavigationSplitView` does, so set `Width` to the *ideal*
  star/pixel value and clamp with `MinWidth`/`MaxWidth`; `GridSplitter` enforces the
  bounds.
- **`trafficLightReservedWidth` (88) is macOS-specific** and should be **dropped on
  Windows** — Windows window controls (min/max/close) sit on the **right**, not the
  left. Instead reserve right-side space if a custom titlebar overlaps the caption
  buttons; WPF-UI's `TitleBar` handles caption button layout automatically. Note this
  as a translation decision: no left-side inset needed.
- `sidebarTopInsetPull` (18) and `columnHeaderTopInset` (24) are tuning offsets to
  align content under the macOS unified titlebar; re-tune for the WPF-UI `TitleBar`
  height (default ~32 DIP) rather than copying the literal values.

---

## 5. Reusable Component Styles (`SkillzComponents.swift`)

### 5.1 `SkillzHairline`
1px `Rectangle` filled `skillzHairline`. → WPF: `<Border Height="1"
Background="{DynamicResource SkillzHairlineBrush}"/>` or a `Separator` restyled.

### 5.2 `SkillzTag` (capsule badge)
Four styles. Single-line, capsule shape, height ≈22, h-padding per style.

| Style | Foreground | Background | Border |
|---|---|---|---|
| `.filled` | canvas | ink | none |
| `.outline` | emphasis | canvas | emphasis, 1px |
| `.muted` | muted | canvas | hairline, 1px |
| `.subtle` | muted | clear | hairline, 1px (caption font, h-pad `sm`, v-pad 1) |

Font: `SkillzTagMetrics.font` (11 medium mono) except `.subtle` uses `caption`
(11 regular). H-padding: `SkillzTagMetrics.horizontalPadding` (10) except `.subtle`
uses `sm` (8). → WPF: a `Border` with `CornerRadius` = half its height (pill) wrapping
a `TextBlock`; expose `TagStyle` enum via a styled `UserControl`/`ContentControl`
with `DataTrigger`s, or 4 named `Style`s keyed `SkillzTagFilled`, etc. Use
`CornerRadius="11"` to fake a capsule at height 22.

### 5.3 `SkillzDetailCard<Content>`
Vertical stack: uppercase section-header title + content. Padding `lg` (16),
background `canvas`, clipped to `RoundedRectangle(cornerRadius: cardRadius=10)`,
1px `hairline` border overlay. → WPF: WPF-UI `Card` control, or a `Border`
(`Background=SkillzCanvasBrush`, `CornerRadius=10`, `BorderBrush=SkillzHairlineBrush`,
`BorderThickness=1`, `Padding=16`) containing a header `TextBlock` + content presenter.

### 5.4 `SkillzEmptyState`
Centered title (list-title style) + secondary message (max width 300), canvas
background, fills available space. → WPF: centered `StackPanel` in a `Grid`,
`MaxWidth=300` on the message.

### 5.5 `SkillzErrorBanner`
Horizontal: message (caption, emphasis, ≤3 lines) + `Spacer` + "Dismiss" plain
button (caption, emphasis). Padding h-`lg`/v-`md`, canvas background, rounded
(`cardRadius`), 1px hairline border, **drop shadow** `black @0.08, radius 8, y 2`,
outer padding h-`lg`/bottom-`lg`. → WPF: `Border` with `Effect` =
`<DropShadowEffect Color="Black" Opacity="0.08" BlurRadius="8"
ShadowDepth="2" Direction="270"/>`. (WPF blur radius ≈ macOS radius; tune.)

### 5.6 Glass toolbar groups (`SkillzGlassToolbarGroup`, `…IconToolbarGroup`, `…SearchField`)
macOS 26 "Liquid Glass" (`GlassEffectContainer` / `.glassEffect`) with a
`.regularMaterial` capsule/circle fallback on older OSes. Pill height 32. Search
field has a `magnifyingglass` icon (muted, 14 wide) + plain `TextField` (emphasis),
min width 240, padding h = `SkillzPillMetrics.horizontalPadding` (12).
→ **Windows:** WPF-UI supports **Mica / Acrylic** backdrop on `FluentWindow`. For
in-content "glass" capsules there is no per-element acrylic; use a semi-transparent
`Border` with `CornerRadius` (capsule) over the Mica window, or WPF-UI
`CardControl`. The `magnifyingglass` SF Symbol → WPF-UI `SymbolIcon`
(`SymbolRegular.Search24`) or a `TextBox` with WPF-UI `AutoSuggestBox`. Pill height
32 → `Height="32"`, `CornerRadius="16"`.

### 5.7 Button styles
- **`SkillzGlassToolbarButtonStyle`** (`prominent` flag): pill font (12 mono), single
  line, h-pad 12, height 32. Prominent + enabled → `ink` capsule fill, foreground
  `canvas`; else foreground `emphasis`. Disabled → `muted`, opacity 0.45. Pressed →
  opacity 0.75.
- **`SkillzGlassIconToolbarButtonStyle`**: 32×32, foreground emphasis (muted if
  disabled), pressed 0.75 / disabled 0.45 opacity.
- **`SkillzTextButtonStyle`** (`prominent` flag): font `captionMedium` (11 medium),
  padding h-`md`/v-`sm`, capsule. Enabled+prominent → `emphasis` fill, `canvas` text,
  no border. Enabled+non-prominent → clear fill, `emphasis` text, border
  `emphasis @0.35`. Disabled → `muted` text, clear fill, `hairline` border.
  Pressed → opacity 0.75. Exposed via `SkillzTextButton` wrapper.

→ WPF: derive from WPF-UI `Button`/`ToggleButton` styles. Encode `prominent` as an
attached property or a custom `ButtonStyleKind` enum on a `Button` subclass; use
`Style.Triggers` (`IsEnabled`, `IsPressed`) to swap `Background`/`Foreground`/
`BorderBrush`/`Opacity`. Capsule = `CornerRadius` equal to half the control height.

### 5.8 `SkillzSectionHeader` / `SkillzTextButton`
Thin wrappers around the section-header text style and text-button style.

### 5.9 `SkillzDetailRow`
Two-column key/value: label (detail-label style, **fixed width 88**, leading) +
value (detail-value style, selectable, fills remaining, `mono` flag). → WPF: `Grid`
with `ColumnDefinition Width="88"` + `*`; value `TextBlock` with
`IsTextSelectionEnabled` (or use a read-only `TextBox` styled flat for selection).

### 5.10 `SkillzListRowChrome` (`Views/Components/`)
Row background: `RoundedRectangle(cornerRadius: sm=8)`, h-pad `sm`. Fill:
selected → `selection`; hovered → `selection @0.5`; else clear. Animated
`easeOut 0.13s` on hover/selection change. → WPF: `Border CornerRadius=8` with
`DataTrigger`s on `IsSelected`/`IsMouseOver` swapping `Background`; animate via
`VisualStateManager` or a `ColorAnimation` (130ms ease-out) in the control template.

### 5.11 `SkillzCanvasBackground` modifier + `.skillzCanvas()`
Applies `canvas` background. → WPF: set `Background="{DynamicResource
SkillzCanvasBrush}"` on the root panel, or an attached helper.

---

## 6. App Lifecycle (`skillzApp.swift`, `AppBrand.swift`)

### 6.1 App brand
`AppBrand.name = "Skills"` — the user-facing product name (binary/target is
`skillz`). Used in menu titles, settings copy, menu-bar labels. → WPF: a constant
`AppBrand.Name = "Skills"` (e.g. static class or `App` resource). The app folder name
on Windows is `SkillzWin` (see §7.4).

### 6.2 Scene graph (SwiftUI `App`)
`@main struct skillzApp: App` owns six `@StateObject`s injected app-wide:
`CatalogStore`, `EditorDocument`, `AppSettings.shared`, `AgentSessionStore`,
`AgentHookStore`, `SparkleUpdater.shared`.

Scenes:
1. **`WindowGroup`** → `MainWindowView`, with
   `.preferredColorScheme(settings.appearance.colorScheme)`, a hidden
   `SkillzStartupConfigurator` in the background for startup wiring,
   `.defaultSize(1440×880)`, `.windowStyle(.hiddenTitleBar)`.
2. **`MenuBarExtra`** → agent status menu (`AgentMenuBarView` + `AgentMenuBarLabel`).
   Label shows a monochrome icon + an orange waiting-count when
   `showAgentCountInMenuBar` and `needsInputCount > 0`.
3. **`Settings`** scene → `SettingsView`, also pinned to
   `settings.appearance.colorScheme`.

**Commands** (menu/keyboard):
- `SidebarCommands()`, `TextEditingCommands()` (system).
- Replace Save: **⌘S** — saves the selected skill; disabled unless dirty + has file +
  selection is a skill.
- **File menu:** New Skill… (**⌘N**), Reveal in Finder (**⇧⌘R**), Edit Details…,
  Rename Skill…, Delete Skill… (last three posting `NotificationCenter` events;
  enabled by selection/permission predicates).
- **View menu:** Refresh Catalog (**⌘R**), Show Inspector toggle (**⌥⌘I**).
- **"Skills" app menu:** Check for Updates… (Sparkle), New Skill… (**⇧⌘N**), Refresh
  All Sources, Refresh Agents.

Command actions fan out via `Notification.Name` extensions:
`skillzNewSkill`, `skillzEditDetails`, `skillzRenameSkill`, `skillzDeleteSkill`,
`skillzShowOnboarding`, `skillzOnboardingCompleted` (plus
`skillzOpenSettingsTab` referenced in `SettingsView`).

Helper methods: `saveSkill()`, `revealSelection()` (Finder reveal per item type:
skill/mcp/plugin), `activateMainWindow()`, `openAgentSession()` (activates owning
app, else reveals cwd in Finder, else focuses main window).

### 6.3 Windows / WPF equivalent — lifecycle
- **`@main App` + scenes** → WPF `App.xaml` / `Application`. Main `WindowGroup` →
  the primary `FluentWindow` (`MainWindow`). `.defaultSize/.minSize` → window
  `Width/Height/MinWidth/MinHeight`. `.hiddenTitleBar` → WPF-UI `FluentWindow` with
  a custom `TitleBar` (or `WindowStyle=None`).
- **`@StateObject` DI** → register stores/services in a DI container (e.g.
  `Microsoft.Extensions.DependencyInjection` `IServiceCollection` in
  `App.OnStartup`); inject into ViewModels. `AppSettings.shared` → a singleton
  `ISettingsService`.
- **`MenuBarExtra`** → a **Windows system tray icon** (`NotifyIcon` via
  `H.NotifyIcon.Wpf` / WPF-UI `NotifyIcon`) with a context menu mirroring the agent
  menu; the orange waiting-count → tray icon badge/overlay or menu header text.
- **`Settings` scene** → a separate `Window` (or a WPF-UI `NavigationView` page)
  opened on demand; equivalent of `SettingsWindowOpener`.
- **`.commands` / keyboard shortcuts** → WPF `InputBindings` (`KeyBinding` with
  `ModifierKeys`) + a menu (WPF `Menu`/WPF-UI menu). Map ⌘→**Ctrl**. ⌘S→Ctrl+S,
  ⌘N→Ctrl+N, ⇧⌘R→Ctrl+Shift+R, ⌘R→Ctrl+R (refresh), ⌥⌘I→Ctrl+Alt+I, ⇧⌘N→
  Ctrl+Shift+N.
- **`NotificationCenter` events** → a lightweight event aggregator / messenger
  (`CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger`) with message types
  per notification (`NewSkillMessage`, `EditDetailsMessage`, etc.).
- **"Reveal in Finder"** → "Show in Explorer":
  `Process.Start("explorer.exe", $"/select,\"{path}\"")`.
- **Sparkle (`SparkleUpdater`)** → **NetSparkle/NetSparkleUpdater** (.NET port of
  Sparkle) or Squirrel/Velopack for Windows auto-update.
- **`preferredColorScheme`** → WPF-UI `ApplicationThemeManager.Apply(...)` driven by
  the persisted appearance setting (see §7).

---

## 7. Settings & Persistence

### 7.1 `AppSettings` (`Settings/AppSettings.swift`)
`@MainActor final class AppSettings: ObservableObject`, singleton `.shared`, private
init. Every property is `@AppStorage` → backed by **`UserDefaults.standard`** (macOS
preferences plist, app-domain). Keys and defaults:

| `@AppStorage` key | Swift property | Type | Default | Meaning |
|---|---|---|---|---|
| `hideBuiltInCursorSkills` | `hideBuiltInCursorSkills` | Bool | `false` | Hide Cursor's built-in skills |
| `hideSystemCodexSkills` | `hideSystemCodexSkills` | Bool | `true` | Hide Codex `.system` skills |
| `editorFontSize` | `editorFontSize` | Double | `14.0` | Markdown editor font size (range 10–20, step 1) |
| `showInspector` | `showInspector` | Bool | `false` | Show inspector by default |
| `skillzAppearance` | `appearanceRaw` | String | `"system"` | Appearance mode (`system`/`light`/`dark`) |
| `enableAgentNotch` | `enableAgentNotch` | Bool | `true` | Enable the agent "notch" overlay |
| `agentNotchDisplayUUID` | `agentNotchDisplayUUID` | String? | `nil` | Which display shows the notch |
| `hasCompletedOnboarding` | `hasCompletedOnboarding` | Bool | `false` | First-run onboarding completed |
| `showAgentCountInMenuBar` | `showAgentCountInMenuBar` | Bool | `true` | Show waiting-agent count in menu bar |
| `autoInstallAgentHooks` | `autoInstallAgentHooks` | Bool | `true` | Auto-install agent hooks |

`appearance: SkillzAppearance` is a computed wrapper over `appearanceRaw`.
`SkillzAppearance` enum (`system`/`light`/`dark`, `CaseIterable`, `Identifiable`)
provides `displayName` and `colorScheme` (`nil`/`.light`/`.dark`).

### 7.2 `SettingsView` (`Settings/SettingsView.swift`)
Custom tabbed settings (not the system `TabView`): a top **tab bar** of plain pill
buttons (`General`, `Sources`, `Agents`, `Editor`) over a `Divider`, then the
selected pane padded `xl` (24), all on canvas. Fixed frame **640 × 560**. Selected
tab pill: `selection @0.58` fill, `emphasis` text; unselected: `muted` text, clear.
Tab height 32, min width 76, corner radius `sm` (8, `.continuous`).

Reacts to `skillzOpenSettingsTab` notifications and a `SettingsWindowOpener` pending
destination (e.g. deep-link to the Agents tab). Panes:
- **General:** Appearance `Picker` (bound to `appearanceRaw`); Library toggles
  (`hideBuiltInCursorSkills`, `hideSystemCodexSkills`); "Show onboarding again"
  (resets `hasCompletedOnboarding`, posts `skillzShowOnboarding`).
- **Sources:** detected-platform count tag + Refresh; per-platform
  `PlatformSourceRow`s (display name, hook-support tag, status tag, item count,
  detection label, primary path with Reveal, not-detected hint).
- **Agents:** `AgentHooksSettingsSection` (separate file).
- **Editor:** `editorFontSize` `Stepper` + `Slider` (10–20, step 1, "{n} pt"
  readout); "Show inspector by default" toggle (`showInspector`).

Forms use `.formStyle(.grouped)`, hidden scroll background, canvas background.

### 7.3 `SettingsPane<Content>` (`Settings/SettingsPane.swift`)
Reusable pane scaffold: title (navigationTitle, emphasis) + subtitle
(body-secondary, wraps) + content, top-leading aligned, fills space, `lg` (16)
inter-spacing, `xs` horizontal inset on the header block.

### 7.4 Home-directory / dotfolder convention
The app scans agent config folders **in the user's home directory**: `~/.cursor`,
`~/.claude`, `~/.codex`, `~/.hermes`, `~/.pi`, `~/.openclaw`. On **Windows** these
live under `%USERPROFILE%`, e.g. `C:\Users\<user>\.cursor`, `…\.claude`, etc.
Resolve home with `Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)`
(NOT `ApplicationData`). The same `.<agent>` dotfolder naming applies — Windows
tolerates leading-dot folder names.

### 7.5 Windows / WPF equivalent — settings persistence
- **`UserDefaults` → a JSON settings file** at
  `%APPDATA%\SkillzWin\settings.json`
  (`Path.Combine(Environment.GetFolderPath(SpecialFolder.ApplicationData),
  "SkillzWin", "settings.json")`). Use `System.Text.Json` to (de)serialize a
  `SettingsModel` POCO whose properties mirror the table in §7.1.
- Implement an `ISettingsService` singleton: load on startup (create defaults if the
  file is missing), expose observable properties (`INotifyPropertyChanged` /
  CommunityToolkit `ObservableObject`), and **debounce-save** to JSON on change. This
  reproduces `@AppStorage`'s "write-through on mutation" behavior.
- **Defaults** must match exactly: `HideBuiltInCursorSkills=false`,
  `HideSystemCodexSkills=true`, `EditorFontSize=14`, `ShowInspector=false`,
  `Appearance="system"`, `EnableAgentNotch=true`, `AgentNotchDisplayUuid=null`,
  `HasCompletedOnboarding=false`, `ShowAgentCountInMenuBar=true`,
  `AutoInstallAgentHooks=true`.
- **Appearance** drives WPF-UI theme: `system` → follow OS
  (`ApplicationThemeManager.GetSystemTheme()` + listen for changes), `light`/`dark`
  → `ApplicationThemeManager.Apply(ApplicationTheme.Light/Dark)`.
- **Editor font size** binds to AvalonEdit `TextEditor.FontSize` (range 10–20). The
  editor `FontFamily` stays Cascadia Code/Consolas (mono).
- **"agentNotch" / display UUID** is macOS notch hardware-specific; on Windows there
  is no notch. Either drop `EnableAgentNotch`/`AgentNotchDisplayUuid` or repurpose as
  a generic "agent overlay" toggle keyed to a monitor (`Screen.DeviceName`). Flag as
  an open question.

---

## 8. macOS → Windows translation summary (quick reference)

| macOS / Swift / SwiftUI / AppKit | Windows / .NET 8 / WPF / WPF-UI |
|---|---|
| Asset-catalog named colors (light + dark appearances) | Two `ResourceDictionary` theme files (Light/Dark) of `Color` + `SolidColorBrush`, swapped via `ApplicationThemeManager` |
| `Color.skillz*` generated accessors | `{DynamicResource Skillz*Brush}` |
| `Font.system(design: .monospaced)` | `FontFamily="Cascadia Code, Consolas"` |
| Segoe-equivalent UI face (rarely used here) | `Segoe UI Variable` |
| pt sizes (1/72") | DIP (1/96"); keep raw numbers, validate visually (or ×1.333) |
| `.semibold/.medium/.regular` | `FontWeights.SemiBold/Medium/Normal` |
| `tracking(0.6)` letter spacing | No direct property — attached-prop hack or drop (open question) |
| `textCase(.uppercase)` | `ToUpperConverter` or VM-side uppercasing |
| `NavigationSplitView` (sidebar/list/detail/inspector) | `Grid` + `GridSplitter` columns, or WPF-UI `NavigationView` |
| `.windowStyle(.hiddenTitleBar)` + traffic lights (left) | WPF-UI `FluentWindow` + custom `TitleBar`; caption buttons on **right**; drop `trafficLightReservedWidth` |
| Liquid Glass / `.regularMaterial` | WPF-UI Mica/Acrylic backdrop; in-content "glass" = translucent `Border` |
| `MenuBarExtra` | System tray `NotifyIcon` + context menu |
| `.commands` + ⌘ shortcuts | `Menu` + `InputBindings`/`KeyBinding`, ⌘→Ctrl |
| `NotificationCenter` `Notification.Name` | `WeakReferenceMessenger` messages |
| Reveal in Finder | `explorer.exe /select,"<path>"` |
| Sparkle auto-update | NetSparkleUpdater / Velopack |
| `UserDefaults` + `@AppStorage` | `System.Text.Json` settings file at `%APPDATA%\SkillzWin\settings.json` via `ISettingsService` |
| `~/.cursor`, `~/.claude`, … (home dotfolders) | `%USERPROFILE%\.cursor`, `\.claude`, … via `SpecialFolder.UserProfile` |
| `preferredColorScheme` | `ApplicationThemeManager.Apply(...)` |
| `.formStyle(.grouped)` settings forms | WPF-UI `CardControl`/`CardExpander` grouped settings |
| Markdown editor (mono) | AvalonEdit `TextEditor`, mono font, `FontSize` from settings |
| macOS notch overlay (`agentNotch`) | No equivalent — drop or repurpose as monitor overlay (open question) |

---

## 9. Open Questions / Decisions to Confirm

1. **Section-header letter spacing (0.6) & `.continuous` corner squircles** have no
   first-class WPF equivalent — accept minor visual drift, or build attached-property
   helpers? (Recommend: accept drift for v1.)
2. **pt vs DIP sizing:** keep raw numeric font sizes (recommended for parity) vs
   multiply by 1.333 for true metric matching — needs a visual check on real Windows
   displays.
3. **Agent notch** (`enableAgentNotch`, `agentNotchDisplayUUID`) is macOS-hardware
   specific. Drop entirely, or reimplement as a floating always-on-top overlay window
   bound to a chosen monitor on Windows?
4. **`SkillzDisabled`** token is defined but I did not find it consumed by the Theme
   files read here (disabled states compute `muted`/opacity inline). Confirm whether
   any non-theme view uses `Color.skillzDisabled`; if unused, it can be carried as a
   token but is not load-bearing.
5. **AccentColor** has no dark variant (pure black both modes). Confirm we intend to
   override the OS/Windows accent everywhere and never expose accent customization.
6. **Glass/material fidelity:** how close must in-content "glass" pills look to the
   Mica window — translucent `Border` is the pragmatic answer; confirm acceptable.
7. **Settings window** as separate `Window` vs an in-app `NavigationView` page —
   macOS uses a dedicated `Settings` scene (640×560 fixed). Match the fixed size?
