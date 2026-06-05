# 05 — UI Layout

Complete specification of the **Skills** desktop UI, derived 1:1 from the macOS SwiftUI source, with the concrete **WPF / .NET 8 / WPF-UI (lepoco/wpfui)** equivalent for every screen, control, and design token. The goal is to be detailed enough to author XAML directly.

App brand name is **"Skills"** (`AppBrand.name`). The window/app title and the sidebar header both read "Skills".

---

## 0. Source-of-truth note on home-directory paths

The macOS app reads agent dotfolders from the user home directory (`~`): `~/.cursor`, `~/.claude`, `~/.codex`, `~/.hermes`, `~/.pi`, `~/.openclaw`. The Windows port keeps the **same dotfolder convention** under the Windows home directory:

- Windows home = `Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)` → `C:\Users\<user>`.
- Paths become `C:\Users\<user>\.cursor`, `...\.claude`, `...\.codex`, `...\.hermes`, `...\.pi`, `...\.openclaw`.
- The UI displays these paths in several places (skill header subtitle, MCP/plugin "Configuration File"/"Install Location" cards, New Skill platform rows, Rename "Location"). Use **backslash Windows paths**, not POSIX. Path text uses the monospace style and is **selectable** (see `TextBlock`/read-only `TextBox` notes per screen).
- "Reveal in Finder" → **"Reveal in Explorer"** (`explorer.exe /select,"<path>"`). "Open in Default App" → `Process.Start(new ProcessStartInfo(path){ UseShellExecute = true })`. "Open in Cursor" → launch the Cursor CLI/exe with the file path (keep the literal menu label "Open in Cursor").

---

## 1. Design tokens (port these first — everything references them)

All numeric values are points in SwiftUI; treat them as **device-independent units (DIPs)** in WPF (1:1). Define them once as WPF resources (`<sys:Double>` for sizes, `Thickness` for paddings, `SolidColorBrush` for colors).

### 1.1 Spacing scale (`SkillzSpacing`)
| Token | Value | WPF resource suggestion |
|-------|-------|--------------------------|
| `xs` | 4 | `Space.Xs` |
| `sm` | 8 | `Space.Sm` |
| `md` | 12 | `Space.Md` |
| `lg` | 16 | `Space.Lg` |
| `xl` | 24 | `Space.Xl` |
| `rowMinHeight` | 52 | list row min height |
| `cardRadius` | 10 | `CornerRadius` for cards/tags-as-rect |
| `glassPadding` | 8 | toolbar group inner padding |

### 1.2 Window metrics (`SkillzWindowMetrics`)
| Token | Value | Meaning / WPF use |
|-------|-------|-------------------|
| `defaultWidth` / `defaultHeight` | 1440 × 880 | `Window.Width/Height` initial |
| `minWidth` / `minHeight` | 1200 × 720 | `Window.MinWidth/MinHeight` |
| Sidebar (col 1) | min 200, ideal 220, max 260 | first `ColumnDefinition` (`MinWidth=200`, `Width=220`, `MaxWidth=260`) |
| List (col 2) | min 300, ideal 360, max 420 | second column |
| Detail (col 3) | min 520, ideal 680 | star-sized column with `MinWidth=520` |
| Inspector | min 240, ideal 280, max 320 | optional 4th column (collapsible) |
| File tree (inside skill editor) | min 140, ideal 168, max 200 | nested splitter pane |
| Editor min | 420 | editor pane `MinWidth` |
| `trafficLightReservedWidth` | 88 | macOS-only (window traffic lights). **Windows: drop to 0** — no traffic lights on the left; use the standard right-side caption buttons. The 88pt leading inset on the toolbar is NOT needed. |
| `sidebarTopInsetPull` | 18 | macOS chrome hack — **not needed on Windows** |
| `columnHeaderTopInset` | 24 | top padding for flat column headers ("All Items", skill name, etc.) |

WPF: implement the 3 columns with a `Grid` + two `GridSplitter`s (or WPF-UI `NavigationView` for the sidebar — but the macOS layout is a true 3-pane master/detail, so a `Grid` with splitters maps more faithfully; see §3 decision). The inspector is a 4th column toggled on/off.

### 1.3 Typography (`SkillzTypography`) — ALL MONOSPACE
The entire UI uses a **monospaced** type family (SwiftUI `design: .monospaced`, i.e. SF Mono). On Windows use **Cascadia Mono** (ships with Windows 11 / Terminal) or **Consolas** as fallback. Define a shared `FontFamily` resource `MonoFont` = `"Cascadia Mono, Consolas"`.

| Role | Size | Weight | Used by |
|------|------|--------|---------|
| navigationTitle | 15 | SemiBold | window/column titles, sidebar app name, list title, skill name |
| headline | 14 | SemiBold | detail-card-less headings, sheet titles, onboarding section heads |
| title | 13 | Medium | empty-state titles, primary labels |
| listTitle | 13 | Medium (SemiBold when selected) | catalog row names |
| navItem | 12 | Regular (SemiBold when selected) | sidebar rows, file-tree rows |
| body | 12 | Regular | descriptions, form fields, toggles |
| caption | 11 | Regular | metadata, paths, counts |
| captionMedium | 11 | Medium | tag/pill labels, detail labels |
| sectionHeader | 10 | Medium, **uppercase, letter-spacing 0.6** | "LIBRARY", "PLATFORMS", card titles |
| mono | 12 | Regular | paths, code, config |
| editor | user setting (`editorFontSize`) | Regular | markdown editor body |

WPF: weights map `Regular→Normal`, `Medium→Medium`, `SemiBold→SemiBold` on `FontWeight`. Section header uppercase: set text in caps and `TextBlock` doesn't have native letter-spacing 0.6 → use a `TextOptions`/typography approach or pre-uppercase the string and accept default tracking (0.6 is subtle; acceptable to approximate).

Pill metrics (`SkillzPillMetrics`): height **32**, horizontal padding **12**, icon width 14, font = mono 12.
Tag metrics (`SkillzTagMetrics`): height **22**, horizontal padding **10**, font = captionMedium 11.

### 1.4 Colors (`SkillzColors`) — named asset colors
Eight semantic brushes, each a light/dark asset catalog color. Port to two `ResourceDictionary` theme files (Light/Dark) with WPF-UI `ApplicationThemeManager`:

| Token | Role | WPF brush key |
|-------|------|---------------|
| `canvas` | window/background fill (everything sits on canvas) | `Brush.Canvas` |
| `ink` | strongest fill (filled tag bg, prominent button bg) | `Brush.Ink` |
| `emphasis` | primary text / strong foreground | `Brush.Emphasis` |
| `muted` | secondary text (descriptions, icons unselected) | `Brush.Muted` |
| `sectionLabel` | section-header/caption-label text | `Brush.SectionLabel` |
| `disabled` | disabled text | `Brush.Disabled` |
| `hairline` | 1px separators, card/tag borders | `Brush.Hairline` |
| `selection` | selection/hover fill | `Brush.Selection` |

Exact RGB values are defined in the asset catalog (`Assets.xcassets`) — capture them from there in the theme/colors spec; this doc references the tokens by role. Selection fill is used at full opacity (selected), **0.5 opacity** (hover) in the list, and **0.78 opacity** (sidebar selected nav row).

> Window background uses macOS "glass"/material effects for the floating top-bar toolbar groups (`.glassEffect`/`.regularMaterial`). On Windows, apply **Mica** (WPF-UI `WindowBackdropType.Mica`) to the window, and render toolbar groups as `Border` with `Brush.Canvas` + subtle drop shadow or acrylic; the literal "glass capsule" is approximated by a rounded `Border`.

---

## 2. Reusable building-block components

These are referenced by nearly every screen. Build them as WPF `UserControl`s or styled templates first.

### 2.1 `SkillzTag` → `Tag` control
A capsule label. Props: `text`, `style ∈ {outline, filled, muted, subtle}`. Height 22 (except subtle), horizontal padding 10 (subtle: 8), font captionMedium 11 (subtle: caption 11).

| Style | Fg | Bg | Border |
|-------|----|----|--------|
| outline | emphasis | canvas | emphasis 1px |
| filled | canvas | ink | none |
| muted | muted | canvas | hairline 1px |
| subtle | muted | transparent | hairline 1px (subtle uses 1px vertical padding) |

WPF: `Border` `CornerRadius="11"` (capsule = height/2) wrapping a `TextBlock`. A `Style` with `Triggers`/`DataTrigger` on a `Tag.Variant` dependency property selects fg/bg/border brushes. `MaxLines=1`.

- `PlatformBadge` = `SkillzTag(text: platform.displayName, style: .subtle)` — used everywhere a platform is shown.
- `EnabledBadge` = `SkillzTag(text: "Enabled"/"Disabled", style: .subtle)` — plugins only.

### 2.2 `SkillzHairline` → 1px `Border`/`Rectangle`
`Height=1`, fill `Brush.Hairline`. Used under the top bar and under the skill-detail header.

### 2.3 `SkillzDetailCard` → bordered card
Vertical stack: uppercase section-header title (10pt) + content, padding `lg` (16) all around, `CornerRadius=10`, background canvas, **1px hairline border**, fills available width. WPF: `Border` + inner `StackPanel`.

### 2.4 `SkillzDetailRow` → label/value row
Horizontal, top-aligned, spacing `lg` (16): a fixed-width **88pt** label (captionMedium 11, sectionLabel color) + a flexible value (body 12 or mono 12 when `mono:true`, emphasis color). Value text is **selectable** (`textSelection(.enabled)`). WPF: `Grid` with `ColumnDefinition Width="88"` + `Width="*"`; value as a read-only borderless `TextBox` (or `TextBlock` with `IsTextSelectionEnabled` if on .NET that supports it) to allow copy.

### 2.5 `SkillzEmptyState`
Centered VStack: title (listTitle 13 medium) + message (body 12 muted, centered, max width 300). Fills the pane. WPF: centered `StackPanel` in a `Grid`.

### 2.6 `SkillzErrorBanner` → bottom toast
Bottom-anchored rounded card (corner 10, hairline border, soft drop shadow `black 0.08, radius 8, y 2`): message text (caption 11, emphasis, max 3 lines) + a "Dismiss" plain text button. Padded horizontal `lg`, vertical `md`; floats with `lg` outer margin bottom + horizontal. WPF: a `Border` in an overlay `Grid` row pinned to the bottom (`VerticalAlignment=Bottom`), shown via a bound `ErrorMessage` (see §13).

### 2.7 Toolbar button styles (top bar pills)
- `SkillzGlassToolbarButtonStyle`: text label, mono 12, horizontal padding 12, height 32, fg emphasis. `prominent:true` → fill `ink`, fg canvas (used for **Save** when dirty). Pressed → opacity 0.75. Disabled → opacity 0.45, fg muted.
- `SkillzGlassIconToolbarButtonStyle`: 32×32 square, icon fg emphasis (muted when disabled).
- `SkillzGlassToolbarGroup` / `IconToolbarGroup` / `SkillzGlassSearchField`: capsule "glass" containers (height 32). WPF: `Border CornerRadius=16` background canvas/acrylic, hosting the buttons in a horizontal `StackPanel`.

### 2.8 `SkillzTextButton` (detail-screen action buttons)
Capsule button, captionMedium 11, padding h `md`/v `sm`. Default = outline (transparent bg, emphasis text, border `emphasis*0.35`). `prominent` = filled emphasis bg + canvas text. Pressed opacity 0.75. WPF: `Button` style with rounded `Border` template.

### 2.9 `SkillzNavRow` → sidebar / file-tree row (shared)
Horizontal, spacing `sm` (8): optional **icon** (16×16; platform brand icon as template-tinted image, OR SF Symbol fallback) + **title** (navItem 12, emphasis, 1 line) + `Spacer` + optional **trailing** count text (navCount 11; emphasis when selected else muted). Padding h `sm` (8), v **7**. Background: `RoundedRectangle corner 8` filled with **selection @ 0.78 opacity** when selected, else clear. Pressed (via `SkillzNavRowButtonStyle`) → opacity 0.72.

WPF: `Button` with custom template = `Border CornerRadius=8` + `Grid` (icon | title | count). `IsSelected` trigger sets the fill. Icons: platform brand → `Image` with a tint via `OpacityMask`/recolor or pre-tinted PNG per theme; section glyphs → use **Segoe Fluent Icons** glyphs (map each SF Symbol, see §4.3).

- `SidebarNavRow` is a thin wrapper passing `count` as the trailing string.

### 2.10 `SkillzListRow` → catalog list row (see §5 for full anatomy)

### 2.11 `SkillzListRowChrome` → list row background
`RoundedRectangle corner 8` inset horizontally by `sm` (8). Fill: selection (selected) / selection @ 0.5 (hover) / clear. Animated 0.13s ease-out on hover & selection change. WPF: `Border` in row template with `DataTrigger`s on `IsSelected`/`IsMouseOver`; animate via `ColorAnimation` (130ms) in the triggers' `EnterActions`.

### 2.12 `SharedSkillInfoButton` → info popover
Small `info.circle` glyph button (caption size, muted). On click opens a **popover** (arrow edge bottom) max width 260 with: "Shared skill file" (captionMedium strong) + a sentence: *"This file is shown under {primary} and is also read by {others}. Edits apply to all of them."* Tooltip "Shared across multiple harnesses". Shown only when a skill `hasSharedAvailability`. WPF: a `ToggleButton`/`Button` with a `Popup` (`Placement=Bottom`, `StaysOpen=False`) or WPF-UI `Flyout`. Glyph: Segoe Fluent `Info` (``).

---

## 3. Overall window shell & top bar

### 3.1 Structure (`MainWindowView`)
Outer `VStack(spacing:0)`:
1. **`topBar`** (custom bar, `zIndex 1` so it floats above the split view) — see §3.3.
2. **`NavigationSplitView`** (`.balanced` style) — 3 columns: Sidebar | ItemList | DetailContainer. Each column has min/ideal/max widths from §1.2.

The window has `minWidth 1200 × minHeight 720`, canvas background, content extends under the title bar (macOS hides the title bar; the top bar reserves 88pt on the left for traffic lights).

**Windows/WPF mapping:**
- Use a WPF-UI `FluentWindow` with `WindowBackdropType=Mica`, custom title bar via WPF-UI `TitleBar`. The app's custom top bar (§3.3) sits **below** the OS title bar (or you can host controls inside a tall custom title bar). Recommended: a thin WPF-UI `TitleBar` (just window caption buttons + app title/icon) on top, then the custom toolbar `Border` as a dedicated row beneath it.
- Drop the 88pt left traffic-light inset; Windows caption buttons are on the right. The toolbar's left group starts at the window's left edge padding instead.
- Root layout `Grid` rows: `Row 0` = title bar (Auto), `Row 1` = top toolbar (Auto), `Row 2` = 3-pane content (`*`), `Row 3`/overlay = error banner.
- 3-pane content: `Grid` with 5 `ColumnDefinition`s: sidebar (200/220/260) | `GridSplitter` (Width 4) | list (300/360/420) | `GridSplitter` | detail (`*`, MinWidth 520). The inspector is an additional column appended when toggled on.
- Alternative: WPF-UI `NavigationView` for the sidebar pane only, but the platform-filter + library sections + per-item counts + master/detail are cleaner as a hand-built `Grid`. **Decision: hand-built Grid + GridSplitters** to match the macOS three-pane master/detail exactly; reserve `NavigationView` only if you later want its built-in collapse animation.

### 3.2 Sidebar toggle behavior
macOS toggles the sidebar via `NSSplitViewController.toggleSidebar`. WPF: bind the sidebar column's `Width`/visibility to a `IsSidebarVisible` VM bool; animate width 200↔0 (or collapse the column + its splitter). Keyboard shortcut **Ctrl+Cmd+S** on macOS → on Windows use **Ctrl+B** (conventional sidebar toggle) or keep a documented shortcut; tooltip text should reflect the Windows shortcut.

### 3.3 Top bar contents — EXACT order & placement (`topBar`)
A single `HStack(spacing:0)`, padding: trailing `lg` (16), top `md` (12), bottom `sm` (8), background canvas, with a **hairline** overlaid on its bottom edge.

**LEFT cluster** (`HStack` spacing `md` (12), leading inset = traffic-light width on macOS / 0 on Windows):
1. **Sidebar toggle** — icon-only glass group, glyph `sidebar.leading` (14pt medium), 32×32. Tooltip "Toggle sidebar (⌃⌘S)". Accessibility "Toggle Sidebar". WPF glyph: Segoe Fluent `OpenPane`/`GlobalNavButton` (``) or a sidebar glyph.
2. **"New Skill"** — text glass button. Opens New Skill sheet. Tooltip "Create a new skill (⌘N)" → Windows **Ctrl+N**.
3. **"Refresh"** — text glass button. Calls `store.refresh()`. Tooltip "Refresh catalog (⌘R)" → Windows **Ctrl+R** / **F5**.

**`Spacer(minLength: xl=24)`** pushes the rest to the right.

**RIGHT cluster** (`HStack` spacing `md` (12)):
- **Shown only when a skill is selected** (`isSkillSelected`):
  - A glass group containing 3 buttons in a single capsule (`HStack spacing 0`):
    1. **"Details"** — opens Skill Details sheet. Tooltip "Edit skill metadata" (or "View skill metadata" when read-only).
    2. **"Rename"** — opens Rename sheet. **Disabled** when `!canModifySkill`. Tooltip "Rename skill folder".
    3. **"Delete"** — opens delete confirmation. **Disabled** when `!canModifySkill`. Tooltip "Delete skill folder".
  - A **separate** glass group (kept away from Delete) containing:
    4. **"Save"** — `prominent` styling when `document.isDirty` (ink fill). **Disabled** unless `canSaveCurrentSkill` (skill selected + a file loaded + dirty). Keyboard **⌘S** → Windows **Ctrl+S**. Tooltip "Save now (⌘S)".
- **Search field** — `SkillzGlassSearchField`, **fixed width 320**, prompt **"Search skills, MCPs, plugins"**, leading magnifier glyph (muted). Two-way bound to `store.searchText`. Always visible (rightmost control).

> Order left→right: `[≡ sidebar] [New Skill] [Refresh] ……… [Details|Rename|Delete] [Save] [🔍 Search]`.

`canModifySkill` = `SkillFileService.canModify(skill)` (built-in / plugin-embedded / non-writable skills are not modifiable). The Details button is always enabled (it can open read-only); Rename/Delete/Save gate on writability.

WPF: bind the right cluster's visibility to `SelectedItem is SkillViewModel`. Use `Button.IsEnabled` bindings to `CanModifySelectedSkill` / `CanSaveCurrentSkill`. Implement keyboard shortcuts via `Window.InputBindings` `KeyBinding`s (Ctrl+N, Ctrl+R, Ctrl+S, Ctrl+B).

---

## 4. Sidebar (left column) — `SidebarView`

### 4.1 Structure
`VStack(alignment:.leading, spacing:0)`:
1. **Header** block.
2. **`List`** (sidebar style, transparent scroll background) with two `Section`s.
Background canvas throughout.

### 4.2 Header
VStack spacing 3: app name **"Skills"** (navigationTitle 15 semibold, emphasis) + caption `"{N} catalog items"` (caption 11 muted) where N = `store.snapshot.allItems.count`. Padding: horizontal `lg` (16), top `md`, bottom `md`. Background canvas.

WPF: a `StackPanel` in `Grid.Row 0` of the sidebar; bind count `TextBlock` to `Catalog.AllItems.Count`.

### 4.3 Section 1 — "Library" (`CatalogSection.allCases`)
Header text **"LIBRARY"** (sectionHeader style, uppercase), with extra top padding `sm`. Rows, in order:

| Section | Display | SF Symbol | → Segoe Fluent glyph (suggested) |
|---------|---------|-----------|----------------------------------|
| `all` | All Items | `square.grid.2x2` | `` (Tiles / GridView) |
| `skills` | Skills | `sparkles` | `` (Sparkle/Lightbulb) — or a custom |
| `mcpServers` | MCP Servers | `server.rack` | `` (Server / Storage) |
| `plugins` | Plugins | `puzzlepiece.extension` | `` (Puzzle) |

Each row is a `SidebarNavRow` (icon + title + trailing **count** = `store.count(for: section)`), selected when `store.selectedSection == section`, fills selection @0.78 when selected. Clicking sets `selectedSection`. Row insets: leading/trailing `lg` (16); top inset = `sm` for the first ("All Items") row else 2; separators hidden; clear row background.

### 4.4 Section 2 — "Platforms"
Header text **"PLATFORMS"** (sectionHeader uppercase), extra top padding `lg`. Rows, in order:

1. **"All Platforms"** — symbol `square.stack.3d.up` (Segoe Fluent `` Stack / ``), trailing count = `store.countAllPlatforms()`, selected when `selectedPlatformFilter == nil`. Top inset `sm`. Click sets filter to `nil`.
2. One row per `AgentPlatform.allCases`, in this order with these display names (uses **brand icon** asset, not SF Symbol):
   | Platform | Display name | Brand icon asset | Home dotfolder (Windows) |
   |----------|--------------|------------------|---------------------------|
   | `cursor` | Cursor | `PlatformIconCursor` | `%USERPROFILE%\.cursor` |
   | `claudeCode` | Claude Code | `PlatformIconClaudeCode` | `%USERPROFILE%\.claude` |
   | `codex` | Codex | `PlatformIconCodex` | `%USERPROFILE%\.codex` |
   | `hermes` | Hermes | `PlatformIconHermes` | `%USERPROFILE%\.hermes` |
   | `pi` | Pi | `PlatformIconPi` | `%USERPROFILE%\.pi` |
   | `openClaw` | **OpenCode** | `PlatformIconOpenCode` | `%USERPROFILE%\.openclaw` |

   (Note the enum case `openClaw` displays as "OpenCode".) Each row: brand icon (16×16, template-tinted: emphasis when selected, muted otherwise), title, trailing count `store.count(for: platform)`. Selected when `selectedPlatformFilter == platform`.

WPF: bind both sections to `ItemsControl`s (or a single `ListBox` per section) with the `SkillzNavRow` template. `SelectedSection` and `SelectedPlatformFilter` are VM properties; selection visuals via `IsSelected`. Counts bind to computed VM methods (`CountFor(section)`, `CountForPlatform(p)`, `CountAllPlatforms`). Ship the 6 brand icons as theme-aware PNG/SVG assets; section glyphs as Segoe Fluent.

---

## 5. Item list (center column) — `ItemListView`

### 5.1 Sticky header (top inset)
A `safeAreaInset(.top)`: `listTitle` (navigationTitle 15) + `Spacer`. Padding: horizontal `lg`, top `columnHeaderTopInset` (24), bottom `md`. Background canvas. The **title** is computed:
- Joins `[selectedSection.displayName (if != All)]` + `[platform.displayName (if filter set)]` with `" · "`.
- If both empty → "All Items".
e.g. "Skills · Cursor", or "Cursor", or "All Items".

WPF: a pinned header `Border` (Grid.Row 0 of the center pane) with a `TextBlock` bound to a `ListTitle` VM string.

### 5.2 States
1. **Loading** (`isLoading && filteredItems empty`): centered small `ProgressView` + "Scanning…" (body secondary). WPF: WPF-UI `ProgressRing` (small) + text.
2. **Empty — global welcome** (`isGlobalEmpty`: no catalog items at all AND no search AND section=all AND no platform filter): centered welcome state, max width 340 text blocks:
   - "Welcome to Skills" (listTitle 13).
   - Body: *"Skills scans your agent harness folders automatically - skills, MCP servers, and plugins from Cursor, Claude Code, Codex, and more."*
   - If `detectedPlatforms` non-empty: a "DETECTED ON THIS MAC" section header + each detected platform's display name (caption). **Windows: change copy to "Detected on this PC".**
   - Else: *"No agent harness folders found yet. Install a tool like Cursor or Claude Code, or create your first skill."*
   - Footer caption: *"Open Settings → Sources to see all scan paths."*
3. **Empty — filtered** (`SkillzEmptyState`, title "No Items"): message = `"No results for \"{searchText}\"."` if searching, else `"Try another section or platform filter, or refresh the catalog."`
4. **List**: a `ScrollView` → `LazyVStack(spacing: xs=4)` of rows, with `padding(.vertical: sm)`.

WPF: a `ContentControl`/`Grid` swapping states by VM flags (use a `DataTrigger`/visibility). For the list, use a **`ListView`/`ListBox`** with `VirtualizingStackPanel` (the macOS `LazyVStack` is virtualized) bound to `FilteredItems`; item spacing via item-container `Margin` (4 vertical). Selection via `SelectedItem` two-way bound to `SelectedItemId`/`SelectedItem`.

### 5.3 Row anatomy (`SkillzListRow`)
Each row is a `Button` (plain) wrapping the row content with:
- Horizontal padding `lg` (16), vertical padding **10**, fills width, left-aligned.
- Background = `SkillzListRowChrome` (rounded selection/hover fill, see §2.11).
- `onHover` tracks `hoveredItemID`. `contextMenu` = `ItemContextMenu` (§11).
- Clicking sets `selectedItemID`.

Row content `VStack(alignment:.leading, spacing: sm=8)`, min height `rowMinHeight` (52), top-leading aligned:
1. **Title line** — `HStack(.firstTextBaseline, spacing: sm)`:
   - **Title** = `item.displayName` (listTitle 13; **SemiBold when selected**), 1 line.
   - **`PlatformBadge`** (subtle tag of `item.platform.displayName`), fixed size.
   - If item is a **plugin**: **`EnabledBadge`** ("Enabled"/"Disabled" subtle tag), fixed size.
   - If item is a **skill with `hasSharedAvailability`**: **`SharedSkillInfoButton`** (info glyph + popover), fixed size.
   - `Spacer(minLength: sm)`.
2. **Subtitle** — `subtitleText` (body secondary 12 muted), **exactly 2 lines reserved** (`lineLimit(2, reservesSpace: true)`, tail truncation, left aligned) so every row is the same height/grid. Subtitle logic: prefer trimmed `descriptionText`; else `listSubtitle` (if non-empty and not equal to the name); else literal **"No description"**.

> There is **no leading icon** in the list row — the row leads with the title text. (Icons appear only in the sidebar and file tree.) The "icon" of the row is effectively the platform badge inline with the title.

WPF: a `DataTemplate` for `CatalogItemViewModel`. Root `Border` (chrome) → `Grid`/`StackPanel`:
- Row 0: horizontal `StackPanel` (or `WrapPanel`) — title `TextBlock` (weight switches on `IsSelected` via trigger), then `Tag` controls (platform; enabled if plugin; shared-info button if skill+shared). Use `DataTrigger`s bound to a `Kind` enum / `IsPlugin` / `HasSharedAvailability` to show the conditional tags.
- Row 1: subtitle `TextBlock`, `TextWrapping=Wrap`, `MaxLines=2`, `TextTrimming=CharacterEllipsis`, with a fixed min height to reserve 2 lines.
- `MinHeight=52`. Selection/hover handled by the chrome `Border` triggers (§2.11) with 130ms color animation.

---

## 6. Detail container (right column) — `DetailContainerView`

Switches on `store.selectedItem`:
- **nil** → `SkillzEmptyState` title "No Selection", message "Select a skill, MCP server, or plugin to view details."
- **skill** → `SkillDetailView` (§7)
- **mcp** → `MCPDetailView` (§8)
- **plugin** → `PluginDetailView` (§9)

Wraps the content in an **`.inspector`** presented when `store.showInspector` (4th column, widths min240/ideal280/max320) hosting `InspectorView` (§10).

WPF: a `ContentControl` with a `DataTemplateSelector` choosing the skill/mcp/plugin/empty template by the selected VM type. The inspector is the optional 5th grid column (after a `GridSplitter`), `Visibility` bound to `ShowInspector` (which persists to settings).

---

## 7. Skill detail / editor screen — `SkillDetailView` + `MarkdownEditorView`

The most complex screen. `VStack(spacing:0)`:

### 7.1 Sticky top header (safeAreaInset .top)
`HStack`: VStack(spacing: xs)
- **Skill display name** (navigationTitle 15, 1 line).
- A **plain button** showing the skill's full **root directory path** (caption 11 muted, 1 line, **middle truncation**); clicking does **"Reveal in Finder"** → Windows **Reveal in Explorer**. Tooltip "Reveal skill folder in Finder" → "Reveal skill folder in Explorer".
Padding: horizontal `xl` (24), top `columnHeaderTopInset` (24), bottom `md`. Canvas bg.

### 7.2 Sub-header block (`header`)
Padding horizontal `xl`, vertical `lg`. `VStack(.leading, spacing: sm)`:
- **Description** (`skill.description`, body secondary 12, **max 3 lines**).
- `HStack(spacing: sm)`: `PlatformBadge` + (if `skill.isBuiltIn`) a **"Built-in"** muted tag + **save-status chip**.
- **Save-status chip** (`saveStatusChip`) by `document.saveStatus`:
  - `.saved` + dirty → "Unsaved" (muted tag)
  - `.saved` + clean → "Saved" (muted tag)
  - `.saving` → "Saving…" (muted tag)
  - `.failed` → "Save failed" (**outline** tag)

Then a **`SkillzHairline`** under the header.

### 7.3 Body: editor (+ optional file tree)
`markdownFiles` = all markdown files in the skill folder. If **more than one** markdown file → show an **`HSplitView`**:
- **Left: file tree** (`fileTree`), widths min140/ideal168/max200.
- **Right: editor pane** (min 420, layout priority 1).
Else just the editor pane (min 420).

**File tree** (`fileTree`): a `ScrollView` → VStack(spacing: sm):
- "FILES" section header (uppercase), padding h `sm`, top `lg`.
- One `SkillzNavRow` per markdown file: title = path **relative to the skill root** (e.g. `SKILL.md`, `references/foo.md`), selected when `selectedFileID == file.id`. Clicking attempts to switch files (saving the current file first if dirty; on save failure shows a "Save Failed" alert and keeps the old selection). Padding h `sm`, bottom `lg`. Canvas bg.

**Editor pane** (`MarkdownEditorView`): a `TextEditor` bound to `document.text` (get/`updateText` set), font = editor(`settings.editorFontSize`) monospace, emphasis fg, transparent scroll bg, padding `lg` (16), canvas bg. Accessibility label "Markdown editor".

**Initial file selection** (`selectInitialFile`/`loadSkillContent`): on appear, if the document already points inside this skill folder and isn't dirty, keep it; else select the **primary** markdown file (the one flagged `isPrimary`, typically `SKILL.md`) or the first file, and `document.load(url:)`. On skill change (`onChange(of: skill.id)`): if dirty, save immediately (alert on failure), then re-select initial file.

**Save Failed alert**: title "Save Failed", message = failure text (or "Could not save changes."), OK button.

### 7.4 WPF mapping for the editor screen
- **Markdown editor → AvalonEdit** (`ICSharpCode.AvalonEdit.TextEditor`): set `FontFamily=MonoFont`, `FontSize` bound to `Settings.EditorFontSize`, `ShowLineNumbers` optional, `WordWrap=true` (the macOS `TextEditor` wraps). Two-way bind `Document.Text` ↔ VM via a behavior (AvalonEdit's `Text` isn't a DP by default; use a `TextChanged` handler or the common `AvalonEditBehaviour` attached-property pattern). Apply Markdown syntax highlighting via an `.xshd` definition (optional but recommended for parity-plus). Padding 16, background canvas.
- **HSplitView → `Grid` + `GridSplitter`** inside the detail pane: file-tree column (140/168/200) | splitter | editor column (`*`, MinWidth 420). Show the splitter+tree only when `MarkdownFiles.Count > 1` (bind column width / visibility).
- **File tree → `ListBox`/`ItemsControl`** with `SkillzNavRow` template bound to `MarkdownFiles`; `SelectedFile` two-way; switching triggers the save-then-load command.
- **Reveal path button → `Hyperlink`/borderless `Button`** invoking `RevealInExplorer(rootDirectory)`; middle-truncate the path (WPF lacks native middle ellipsis — implement a converter that elides the middle, or use `TextTrimming=CharacterEllipsis` and accept end-trim, or a custom path-compacting converter using `PathCompactPathEx`).
- **Save-status chip → `Tag`** bound to `SaveStatus` enum via `DataTrigger`s.
- **Save Failed alert → WPF-UI `ContentDialog`** (or `MessageBox`) with an OK button.
- Header description: `TextBlock MaxLines=3 TextTrimming=CharacterEllipsis`.

---

## 8. MCP detail — `MCPDetailView`

`ScrollView` → `VStack(.leading, spacing: xl=24)`, outer padding `xl`. Sticky top header = MCP `name` (navigationTitle, 1 line). Sections:

1. **Header**: `HStack(spacing: sm)`: `PlatformBadge` + a muted tag = `mcp.transportLabel`. Below: `mcp.endpointSummary` (body secondary, **selectable**).
2. **`SkillzDetailCard` "Server Details"** with `SkillzDetailRow`s (only present when value exists):
   - "Name" = `mcp.name`
   - "Transport" = `mcp.transportLabel`
   - "Command" = `mcp.command` (mono) — if present
   - "Arguments" = `mcp.args joined by space` (mono) — if non-empty
   - "URL" = `mcp.url` (mono) — if present
   - "Environment" = `envKeys joined by ", " + " (values hidden)"` — if non-empty (values never shown)
3. **`SkillzDetailCard` "Configuration File"**: the `configFileURL.path` (mono, selectable, full width).
4. **Actions** `HStack(spacing: md)` of `SkillzTextButton`s:
   - "Reveal Config" → reveal in Explorer
   - "Open in Default App" → shell-open
   - "Copy Path" → copy to clipboard

WPF: scrollable `StackPanel`; two `SkillzDetailCard` user controls; detail rows shown/hidden via `Visibility` converters on null/empty. Action buttons bound to commands (`RevealConfigCommand`, `OpenDefaultCommand`, `CopyPathCommand`). Path text as selectable read-only `TextBox`.

---

## 9. Plugin detail — `PluginDetailView`

`ScrollView` → `VStack(.leading, spacing: xl)`, padding `xl`. Sticky header = `plugin.displayName`. Sections:

1. **Header**: `HStack(spacing: sm)`: `PlatformBadge` + `EnabledBadge(isEnabled)`. Below: description (body secondary) or "No description available." when empty.
2. **`SkillzDetailCard` "Plugin Details"** rows:
   - "ID" = `pluginID` (mono)
   - "Marketplace" = `marketplace` — if present
   - "Version" = `version` — if present
   - "Skills" = `skillCount` (number)
   - "Status" = "Enabled"/"Disabled"
3. **`SkillzDetailCard` "Install Location"** (only if `installPath` present): the path (mono, selectable, full width).
4. **Actions** `HStack(spacing: md)`:
   - "Reveal in Finder" (→ "Reveal in Explorer") — if `installPath`
   - "Open Metadata" (shell-open `metadataPath`) — if present
   - "Copy Path" (copies `installPath ?? metadataPath`) — if either present

WPF mapping mirrors §8.

---

## 10. Inspector (optional 4th column) — `InspectorView`

A `ScrollView` → `VStack(.leading, spacing: xl)`, padding `lg`, canvas bg, `minWidth 240`. Shown when `showInspector` (toggled via onboarding/settings; the macOS app has no explicit toolbar toggle in these files — it persists `store.showInspector` to settings). Content:

1. **"Details" section** (every item): a `SkillzDetailRow` "Type" = `item.kind.displayName` ("Skill"/"MCP Server"/"Plugin"); then a custom platform row: a fixed-88 "Platform" label + `PlatformBadge` + (skills with shared availability) `SharedSkillInfoButton`.
2. **Type-specific section**:
   - **Skill** → "Skill" section: "Version" (if present), "Source" = "Cursor Built-in" (if `isBuiltIn`) or "Plugin" (if `isPluginEmbedded`), "Path" (mono). Then, if `store.relatedPlatforms(...)` non-empty, an **"Also Available On"** section: a row of `PlatformBadge`s + caption *"Edits to this file apply to every harness listed above."*
   - **MCP** → "MCP" section: "Transport", "Endpoint" (mono), "Env Keys" (joined, if any), "Config" (mono path).
   - **Plugin** → "Plugin" section: "ID" (mono), "Status", "Marketplace" (if), "Version" (if), "Skills" (count), "Path" (mono, if).

Each section uses a `SkillzSectionHeader` (uppercase 10pt) + a VStack of `SkillzDetailRow`s (spacing `sm+2`=10).

WPF: a `ScrollViewer` + `StackPanel`; a `DataTemplateSelector` (or `ContentControl`) for the type-specific section; reuse the `SkillzDetailRow` and `Tag` controls. Toggle the column via `ShowInspector` bool persisted to settings.

---

## 11. Context menu — `ItemContextMenu`

Right-click on a list row. Items (in order):
- **For skills only**:
  - "Edit Details…" → selects the item, posts `skillzEditDetails` (opens Details sheet).
  - If `canModify`: "Rename Skill…", a separator, then **"Delete Skill…"** (destructive role).
  - A trailing separator.
- **All items**:
  - "Reveal in Finder" (→ "Reveal in Explorer") — reveals skillPath / configFileURL / installPath∥metadataPath by type.
  - "Copy Path" — copies the same path.
  - **Skills only**: "Open in Cursor" → `openInCursor(skillPath)`.

WPF: a `ContextMenu` on the row template with `MenuItem`s; build dynamically (or use `Visibility`/`DataTrigger`s) based on the item type and `CanModify`. The macOS flow routes through `NotificationCenter`; in WPF route directly to commands on the parent VM (`EditDetailsCommand`, `RenameCommand`, `DeleteCommand`, `RevealCommand`, `CopyPathCommand`, `OpenInCursorCommand`), each taking the item as parameter. Destructive "Delete Skill…" → can style red; confirm via dialog (§12.3).

---

## 12. Sheets (modal dialogs) → WPF-UI `ContentDialog`

All four macOS `.sheet`s become WPF-UI `ContentDialog`s (or modal `FluentWindow`s). The macOS sheets are presented from `MainWindowView` and dismissed via `@Environment(\.dismiss)`. They share: title (headline 14), a subtitle/description, a form, optional error text (caption, sectionLabel color), and a Cancel/primary button footer. Background canvas, padding `xl` (24).

### 12.1 New Skill sheet — `NewSkillSheet` (size 520 × 520)
- Title **"New Skill"** (headline). Subtitle: *"Creates a skill folder with SKILL.md in each selected platform's skills directory."*
- **Form** (grouped style):
  - **TextField "Name"** (prompt "e.g. code-review"), body font.
  - **TextField "Description"** (vertical, 2–4 lines).
  - **Section "Platforms"**: one **Toggle** per `AgentPlatform.allCases`:
    - Toggle label = `PlatformBadge` + the platform's **user skills directory path** (caption, 1 line). Windows path e.g. `C:\Users\<user>\.cursor\skills` (Pi uses `...\.pi\agent\skills`).
    - If not detected: a caption **"Not detected on this Mac"** (→ Windows "Not detected on this PC") in sectionLabel color.
    - Default selected platforms = `store.defaultNewSkillPlatforms` (configured once on appear).
  - **Section "Markdown"**: a `TextEditor` (editor font, min height 160) seeded with a template:
    ```
    # {Name or "Skill Name"}

    Describe when to use this skill.
    ```
- **Error text** if `errorMessage` set.
- **Footer**: "Cancel" (cancel action / Esc) — left; `Spacer`; **"Create"** (default action / Enter) — right; disabled while `isCreating` or `!canCreate`. `canCreate` = non-empty trimmed name AND ≥1 platform selected.
- On Create: `store.createSkill(name, description, body, platforms)`, then `document.load` the new file and dismiss; on error show message.

WPF: a `ContentDialog` (fixed ~520 wide) or modal window. Use WPF-UI `TextBox`/`TextBox AcceptsReturn` (description), a list of WPF-UI `ToggleSwitch`es (one per platform) each with the badge + path caption + "not detected" hint, and an **AvalonEdit** (or multiline `TextBox`) for the markdown body (min height 160). "Create" is the dialog primary button (`IsEnabled` bound to `CanCreate`), "Cancel" the secondary/close. Seed body template via VM on open. Show error in a caption `TextBlock`.

### 12.2 Rename sheet — `RenameSkillSheet` (width 440)
- Title **"Rename Skill"**. Subtitle: *"Renames the folder on disk and updates the name in SKILL.md."*
- "Platform" label + `PlatformBadge`.
- "Location" label + the **parent directory path** (mono, ≤2 lines), e.g. `C:\Users\<user>\.cursor\skills`.
- **TextField "Folder name"** (body), seeded with the current folder name.
- Error text if any.
- Footer: "Cancel" (Esc) / **"Rename"** (Enter, disabled while renaming or name blank).
- On Rename: `store.renameSelectedSkill(to:)`, dismiss; pauses/resumes autosave around it.

WPF: `ContentDialog` width ~440; `TextBox` for folder name; reuse path/label/badge layout. Primary "Rename" enabled when name non-blank.

### 12.3 Skill Details sheet — `SkillDetailsSheet` (width 440)
- Title **"Skill Details"**. If not editable (`!canModify` via `SkillFileService.canEditMetadata`): show the blocked-reason text (body secondary).
- **Form** (disabled when `!canModify || isSaving`):
  - TextField "Name" — seeded with `frontmatter.name ?? folder name`.
  - TextField "Description" (vertical 3–6 lines) — seeded with description ("" if it was "No description").
  - TextField "Version (optional)" — seeded with `skill.version`.
  - `LabeledContent "Platform"` → `PlatformBadge`.
  - `LabeledContent "Path"` → root directory path (mono, ≤3 lines).
- Error text if any.
- Footer: "Cancel" (Esc) / **"Save"** (Enter, disabled when `!canModify || isSaving`).
- On Save: `store.updateSelectedSkillMetadata(name, description, version?)`, reload the document if it points at this skill, dismiss.

WPF: `ContentDialog` width ~440; form `TextBox`es; whole form `IsEnabled` bound to `CanModify && !IsSaving`. Show blocked-reason text when read-only.

### 12.4 Delete confirmation — `confirmationDialog`
Not a sheet — a macOS `confirmationDialog`. Title: `Delete "{skill name}"?`, visible title. Buttons: **"Delete Skill"** (destructive) + "Cancel" (cancel). Message: *"This permanently deletes the skill folder at:\n{rootDirectory.path}"*.

WPF: WPF-UI `ContentDialog` (or `MessageBox`) with a destructive-styled primary "Delete Skill" button and a Cancel; message shows the Windows folder path. On confirm: pause autosave, `store.deleteSelectedSkill()`, resume.

> All sheets pause `document` autosave during the operation and resume after (mirror this in the VM).

---

## 13. Onboarding — `OnboardingView` (sheet, 720 × 560, **not interactively dismissable**)

Shown on first launch when `!settings.hasCompletedOnboarding`. Macos uses `.interactiveDismissDisabled()` — the user must click a button. Layout `VStack(.leading, spacing: xl)`, padding `xl`, canvas bg:

1. **Top bar row**: app name **"Skills"** (24pt **semibold monospaced**, emphasis) on the left; "Agent Library" (caption) on the right.
2. **Two-column body** `HStack(.top, spacing: xl)`:
   - **Left column (width 410): "Detected Tools"**
     - Header "Detected Tools" (headline) + a muted tag `"{N} found"` (= `detectedPlatforms.count`).
     - If none detected: body text *"No agents detected yet — you can still create and manage skills, and Skills will pick them up as you install tools."*
     - A `ScrollView` of **`OnboardingSourceStatusRow`** (one per `store.sourceStatuses`), each a bordered rounded card (corner 10, hairline border, padding `md`):
       - Top line: `PlatformBadge` + `Spacer` + status tag (`statusLabel`, muted if detected else subtle) + a subtle tag `"{itemCount} items"`.
       - Second line: `detectionLabel` (caption, 1 line) + `Spacer` + `hookSupportLabel` (caption, sectionLabel).
       - If not detected: a `notDetectedHint` (caption sectionLabel, ≤2 lines).
   - **Right column (flexible): "Agent Setup"**
     - A 44×44 rounded (corner 10) `selection`-filled tile with a `dot.radiowaves.left.and.right` glyph (24pt, emphasis) → Windows Segoe Fluent broadcast/antenna glyph.
     - "Agent Setup" (headline).
     - Body: *"Skills reads local agent folders immediately. Waiting-state hooks are installed only for tools that support them."*
     - Three **Toggles** (body font):
       - "Show waiting count in menu bar" ↔ `settings.showAgentCountInMenuBar` (→ Windows: "Show waiting count in **tray**" / taskbar).
       - "Show inspector by default" ↔ `settings.showInspector`.
       - "Install or repair hooks automatically" ↔ `settings.autoInstallAgentHooks` — **disabled** unless a hook-capable tool is detected (`hookSupport == .preciseWaitingState`).
3. **Footer row**: caption *"Sources, appearance, hooks, and editor settings stay available after setup."* + `Spacer` + **"Settings"** button (finishes onboarding, completes, opens Settings → Agents tab) + **"Get Started"** button (`.borderedProminent`; finishes onboarding + completes). Both call `finish()` which sets `hasCompletedOnboarding = true`.

WPF: a modal `ContentDialog` or borderless `FluentWindow` (720×560), not closable via Esc/X (`ContentDialog` with no close, or handle the window `Closing` to block until a button). Left card list = `ItemsControl` of source-status cards; right column = `StackPanel` with the tile, text, and three WPF-UI `ToggleSwitch`es. "Get Started" = primary (accent) button; "Settings" = secondary. **Rewrite "menu bar"/"this Mac" copy for Windows** (tray / this PC). The "Agents" settings tab is covered in the Settings spec.

---

## 14. Notable interactions, states & keyboard

- **Hover**: list rows highlight at selection@0.5 on mouse-over (`onHover` → `IsMouseOver`), animated 130ms. Sidebar/file-tree rows have no hover fill (only selected fill).
- **Selection**: list row selected = selection fill + SemiBold title; sidebar nav row selected = selection@0.78 + emphasis icon/count; file-tree row selected = selection@0.78.
- **Pressed**: glass toolbar buttons & text buttons dim to 0.75; nav rows dim to 0.72.
- **Disabled**: glass toolbar buttons dim to 0.45 + muted fg; text buttons → muted fg + hairline border.
- **Keyboard shortcuts** (macOS → Windows): Save ⌘S → **Ctrl+S**; New ⌘N → **Ctrl+N**; Refresh ⌘R → **Ctrl+R/F5**; Toggle sidebar ⌃⌘S → **Ctrl+B**. Sheets: Enter = primary action, Esc = Cancel. Implement via `Window.InputBindings` and dialog default/cancel buttons.
- **Auto-refresh**: macOS refreshes on app-becoming-active and watches the filesystem (FSEvents). Windows: refresh on window activation + a `FileSystemWatcher` per scan root.
- **Error banner** (§2.6): bottom toast surfaces `store.lastOperationError` or a failed save status; dismiss clears it (and remembers the dismissed save message until next successful save).
- **`onChange(selectedItemID)`**: closes the Details/Rename sheets when the selection changes.
- **Live settings reactions**: toggling `hideBuiltInCursorSkills` / `hideSystemCodexSkills` triggers `store.refresh()`; `showInspector` persists to settings.

---

## 15. WPF screen-by-screen summary (implementation checklist)

| macOS screen | WPF realization |
|--------------|-----------------|
| `MainWindowView` shell | `FluentWindow` (Mica) + WPF-UI `TitleBar` + custom toolbar `Border` + 3-pane `Grid`/`GridSplitter` + bottom error-banner overlay |
| Top bar | Horizontal `StackPanel` of pill `Border`s: sidebar-toggle icon button, "New Skill", "Refresh", spacer, conditional Details/Rename/Delete + Save group, 320-wide search box |
| `SidebarView` | Sidebar `Grid`: header `StackPanel` + two `ListBox`/`ItemsControl` sections (Library, Platforms) with `SkillzNavRow` template, counts, single-selection each |
| `ItemListView` | Pinned header + state-swapping `Grid` (ProgressRing / welcome / empty / `ListView`) bound to `FilteredItems`, `SkillzListRow` `DataTemplate`, virtualized |
| `DetailContainerView` | `ContentControl` + `DataTemplateSelector` (skill/mcp/plugin/empty) + optional inspector column |
| `SkillDetailView` + editor | Header + `Grid`/`GridSplitter` (file-tree `ListBox` | **AvalonEdit**) + save-status `Tag` + Save-Failed `ContentDialog` |
| `MCPDetailView` | `ScrollViewer` + header + 2 `SkillzDetailCard`s + action button row |
| `PluginDetailView` | `ScrollViewer` + header + 2 `SkillzDetailCard`s + action button row |
| `InspectorView` | Collapsible column: `ScrollViewer` + Details section + type-specific section |
| `ItemContextMenu` | `ContextMenu` with type/permission-driven `MenuItem`s bound to commands |
| New / Rename / Details sheets | WPF-UI `ContentDialog`s with forms |
| Delete confirm | WPF-UI `ContentDialog` (destructive) |
| `OnboardingView` | Non-dismissable modal: two-column layout, source-status cards, three `ToggleSwitch`es, Settings/Get-Started buttons |

---

## 16. Open questions / cross-references
- Exact RGB for the 8 `SkillzColors` and the 6 platform brand icons live in the asset catalog → capture in the **theme/colors** spec (02-theme or similar). This doc references them by token.
- `CatalogStore` filtering, counts, watching, and command behaviors (`createSkill`, `renameSelectedSkill`, `updateSelectedSkillMetadata`, `deleteSelectedSkill`, `relatedPlatforms`, `defaultNewSkillPlatforms`, reveal/copy/open helpers) belong in the **services/state** spec; the UI binds to them.
- `AppSettings` fields (`editorFontSize`, `showInspector`, `showAgentCountInMenuBar`, `autoInstallAgentHooks`, `hideBuiltInCursorSkills`, `hideSystemCodexSkills`, `hasCompletedOnboarding`) belong in the **settings** spec.
- The **menu bar / notch / agent-status** UI (`Notch/*`, menu-bar count) is a separate macOS-specific subsystem; the Windows equivalent (tray icon / taskbar badge) is out of scope for this layout doc and should be specified separately.
