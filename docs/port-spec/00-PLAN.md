# 00 — Consolidated Implementation Plan (Skills for Windows / WPF .NET 8)

Lead-architect plan for porting **skillz-macos** to a native Windows desktop app
("**Skills**", product brand `AppBrand.Name = "Skills"`; Windows app/folder name
`SkillzWin`). This plan consolidates specs 01–06 into a single, implementation-ready
blueprint.

**Scope of this plan = CORE-FIRST.** Deliver: catalog discovery for all 6 tools,
the 3-column browser, the SKILL.md editor (frontmatter + new/rename/delete), the
Fluent monochrome theme, and onboarding. The **agent-session tray monitor + hooks +
notch + auto-update** subsystem is explicitly **DEFERRED** to a later pass (see §3,
Deferred Pass).

---

## 1. Final Tech-Stack Decision Summary

| Concern | Decision | Rationale (from specs) |
|---|---|---|
| Runtime / UI | **WPF on .NET 8 (LTS)** | All six specs target this exactly. |
| Fluent controls / theming | **WPF-UI (lepoco/wpfui)** `FluentWindow`, `TitleBar`, `ApplicationThemeManager`, `SymbolIcon` | 05/06 specify Mica window + Fluent chrome; theme manager swaps Light/Dark dictionaries. |
| MVVM | **CommunityToolkit.Mvvm** (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`, `WeakReferenceMessenger`) | 02/04/06 map `@Observable`/`ObservableObject` and `NotificationCenter` → messenger. |
| DI | **Microsoft.Extensions.DependencyInjection** | 04/06: `@StateObject` app-wide stores → DI singletons; injectable `IAgentEnvironment` test seam. |
| Markdown editor | **AvalonEdit** (`ICSharpCode.AvalonEdit`) | 02/05/06: editor pane, mono font, `FontSize` from settings; optional `.xshd` markdown highlight. |
| JSON | **System.Text.Json** (built-in, no 3rd-party dep) | 01/03/04: MCP `mcp.json`/`.mcp.json`, plugin manifests, `openclaw.json`, settings. Lenient reads via `JsonNode`/`TryGetProperty`. |
| TOML | **Tomlyn** | 03 §2.6: spec-compliant superset of the macOS mini-parser; correctly handles inline tables/dotted keys/arrays. Re-implement only the `enabled` truthiness + default-enabled rule on top. |
| YAML frontmatter | **Custom mini-parser/emitter (NO YamlDotNet)** | 02 §9: the macOS grammar is intentionally non-standard and lossy; a real YAML lib would change byte output and corrupt round-tripped files. Port ~80 lines faithfully + CRLF fix. |
| Tray icon (deferred) | **H.NotifyIcon.Wpf** (or WPF-UI `NotifyIcon`) | 06: `MenuBarExtra` → tray. Deferred pass only. |
| Auto-update (deferred) | **NetSparkleUpdater** or **Velopack** | 06: Sparkle → Windows updater. Deferred pass only. |

**NuGet packages (core scope):** `WPF-UI`, `ICSharpCode.AvalonEdit`,
`CommunityToolkit.Mvvm`, `Microsoft.Extensions.DependencyInjection`,
`Microsoft.Extensions.Hosting` (optional, for generic host), `Tomlyn`.
`System.Text.Json` is in the BCL. **Deferred packages:** `H.NotifyIcon.Wpf`,
`NetSparkleUpdater.SparkleUpdater.WinForms`/`Velopack`.

**Fonts:** Mono everywhere — `FontFamily="Cascadia Code, Consolas"` (`SkillzMonoFont`);
`Segoe UI Variable, Segoe UI` (`SkillzUiFont`) only for native WPF-UI chrome.

**Cross-cutting decisions locked from the specs (apply throughout):**
- Paths modeled as plain `string` (absolute), never `file://`; always build with
  `Path.Combine` (never embed `/`). (01, 03, 04)
- Path-classification substring checks: **normalize separators to `/` once**
  (`path.Replace('\\','/')`) then keep the macOS `Contains("/.x/")` literals verbatim,
  using **ordinal** comparison; dedup dictionaries keyed `OrdinalIgnoreCase`. (01 §2.4, 04 §6)
- File writes: **atomic, UTF-8 no-BOM, `\n` line endings preserved** (write temp →
  `File.Replace`/`File.Move`). Do not let AvalonEdit rewrite to CRLF. (02 §4.5)
- Search/sort: **`OrdinalIgnoreCase`** (predictable) over locale-aware, documented. (02, 04)
- Accent is **pure black `#000000`**, never the Windows accent color. (06 §1.2)
- Replace macOS copy: "Finder"→"File Explorer", "this Mac"→"this PC", "menu bar"→"tray". (02, 05)

---

## 2. Complete C# Solution / Project Structure

**Solution:** `SkillzWin.sln` → single WPF project **`SkillzWin`** (`net8.0-windows`,
`<UseWPF>true</UseWPF>`). Folders: `Models`, `Services`, `ViewModels`, `Views`
(+ `Views/Components`, `Views/Dialogs`), `Themes`, `Assets`. Each file below lists a
one-line purpose and its macOS counterpart.

### 2.1 `Models/` (records — immutable, value equality)

| File | Purpose | macOS counterpart |
|---|---|---|
| `AgentPlatform.cs` | `enum AgentPlatform { Cursor, ClaudeCode, Codex, Hermes, Pi, OpenClaw }`. | `Models/AgentPlatform.swift` (enum) |
| `AgentPlatformInfo.cs` | Lookup/extensions: `RawValue` (`cursor`/`claudeCode`/…), `DisplayName`, `DotFolderName`, `IconGlyph`, `BrandIconResourceKey`, `UserSkillsDirectory`. | `AgentPlatform.swift` computed members |
| `CatalogSection.cs` | `enum CatalogSection { All, Skills, McpServers, Plugins }` + info (display, glyph). | `AgentPlatform.swift` (`CatalogSection`) |
| `CatalogItemKind.cs` | `enum CatalogItemKind { Skill, Mcp, Plugin }` + display names. | `AgentPlatform.swift` (`CatalogItemKind`) |
| `CatalogItem.cs` | Discriminated union: `abstract record CatalogItem` + `SkillCatalogItem`/`McpCatalogItem`/`PluginCatalogItem`; projected `Id/Kind/Platform/DisplayName/DescriptionText/ListSubtitle/ModifiedAt/IconGlyph`. | `Models/CatalogItem.swift` (enum) |
| `SkillFrontmatter.cs` | `record SkillFrontmatter(string? Name, string? Description, string? Version, bool? DisableModelInvocation)`. | `Models/SkillItem.swift` |
| `SkillItem.cs` | `record SkillItem(...)` + `ListSubtitle`, `HasSharedAvailability`, `MakeId`. Sequence-equal `AlsoAvailableOn`. | `Models/SkillItem.swift` |
| `SkillMarkdownFile.cs` | `record` for one `.md` file (`Url`, `DisplayName`, `IsPrimary`); editor file-list entry. | `Models/SkillItem.swift` |
| `McpItem.cs` | `record McpItem(...)` + `TransportLabel`, `EndpointSummary`, `MakeId`. | `Models/MCPItem.swift` |
| `McpTransport.cs` | `enum McpTransport { Stdio, Http, Unknown }`. | `Models/MCPItem.swift` |
| `PluginItem.cs` | `record PluginItem(...)` + `ListSubtitle`, `MakeId`. | `Models/PluginItem.swift` |
| `CatalogSnapshot.cs` | `record CatalogSnapshot(Skills, Mcps, Plugins)` + `AllItems` (skills→mcps→plugins). | `Services/DiscoveryEngine.swift` (`CatalogSnapshot`) |
| `PlatformSourceStatus.cs` | `record` per-platform detection: `IsDetected, ScanPaths, DetectionSignals, ItemCount, HookSupport` + labels. | `PlatformSourceDetector.swift` |
| `PlatformDetectionModels.cs` | `PlatformDetectionSignal`, `PlatformDetectionProfile`, `PlatformHookSupport` enum, signal-kind enum. | `PlatformSourceDetector.swift` |
| `SettingsModel.cs` | POCO mirroring all `@AppStorage` keys/defaults (§7.1 of spec 06). | `Settings/AppSettings.swift` |
| `SaveStatus.cs` | `record SaveStatus(SaveStatusKind Kind, string? Error)` + `enum { Saved, Saving, Failed }`. | `Services/EditorDocument.swift` |

### 2.2 `Services/`

| File | Purpose | macOS counterpart |
|---|---|---|
| `IAgentEnvironment.cs` / `AgentEnvironment.cs` | Injectable home/app-support/skillz-home/exec-search resolution; `HomeDirectoryFor(platform)`; `Live` + test-root ctor. | `Services/AgentEnvironment.swift` |
| `AgentPaths.cs` | Constants (state version=1, stale intervals) + derived app/state/session paths; `WatchPathsForAgents()`. | `Services/AgentPaths.swift` |
| `PlatformSkillPaths.cs` | `SkillScanRoots(p)`, `PrimaryPlatform(path)`, `PlatformsThatShare(path)`, `AgentsSkillsDirectory`, `WatchDirectories`. | `Services/PlatformSkillPaths.swift` |
| `OpenClawConfig.cs` | Resolve `openclaw.json` → workspace dir + `WorkspaceSkillsDirectory()` (fallbacks, `~/`/abs/relative). | `Services/OpenClawConfig.swift` |
| `FrontmatterParser.cs` | Custom mini-YAML reader (4 keys, block-scalar→empty, first-colon split, CRLF-safe). | `Services/FrontmatterParser.swift` |
| `FrontmatterWriter.cs` | Mini-YAML emitter (`Update`, `Apply`, `Serialize`, `QuoteIfNeeded`, `Make`); `\n` bytes. | `Services/FrontmatterWriter.swift` |
| `SkillScanner.cs` | Recursive `SKILL.md` discovery, hidden/`.system` handling, plugin-embedded + built-in cursor, dedup, `MarkdownFiles`. | `Services/SkillScanner.swift` |
| `SkillNameValidator.cs` | Trim/empty/dot rules + `[A-Za-z0-9-_]`; **+ Win32 reserved-name rejection**. | `Services/SkillNameValidator.swift` |
| `SkillFileService.cs` | Create/rename/delete/update-metadata + capability gates (`CanModify`/`CanEditMetadata`), atomic writes. | `Services/SkillFileService.swift` |
| `FileAccessError.cs` | Exception→user-message map (UnauthorizedAccess/NotFound/read-only/HResults); "File Explorer" copy. | `Services/FileAccessError.swift` |
| `SkillFileException.cs` | `Exception` w/ `Kind { Blocked, DuplicateName, Validation }`. | `Services/SkillFileService.swift` (error enum) |
| `EditorDocument.cs` | Editor buffer VM-service: load/updateText/save/saveImmediately/discard, pause/resume, **1.2 s debounced autosave**, dirty tracking. | `Services/EditorDocument.swift` |
| `TomlConfigReader.cs` | Tomlyn-based reader: `McpServers(tomlPath)`, `EnabledPlugins(tomlPath)` (truthiness + default-enabled). | `Services/TOMLParser.swift` |
| `McpScanner.cs` | Cursor `mcp.json` + Claude `.mcp.json` (System.Text.Json) + Codex TOML; env-keys-only, transport precedence, sort. | `Services/MCPScanner.swift` |
| `PluginScanner.cs` | Cursor/Claude/Codex plugin discovery; enable maps, `scanPluginMetadata`, `inferPluginID`, `countSkills`, sort. | `Services/PluginScanner.swift` |
| `PlatformSourceDetector.cs` | Per-platform detection signals (source files, skill content, executables via PATH/PATHEXT), `IsInstalled`, profiles. | `Services/PlatformSourceDetector.swift` |
| `CatalogDiscoveryService.cs` | Pure aggregator: `Discover(hideBuiltInCursor, hideSystemCodex)` → `CatalogSnapshot` (skills→mcps→plugins). | `Services/DiscoveryEngine.swift` |
| `CatalogFilter.cs` | `Items(snapshot, section, platform?, search)` + `Sorted(...)`; section×platform(+alsoAvailableOn)×4-field search. | `Services/CatalogFilter.swift` |
| `CatalogFileWatcher.cs` | One `FileSystemWatcher` per existing root, recursive, **shared 300 ms DispatcherTimer debounce**, `Error` recovery. | `Services/FSEventWatcher.swift` |
| `ISettingsService.cs` / `SettingsService.cs` | Load/save `%APPDATA%\SkillzWin\settings.json`, observable props, debounced write-through, defaults. | `Settings/AppSettings.swift` |
| `ShellService.cs` | Reveal-in-Explorer (`explorer /select`), open-default, copy-path, open-in-cursor. | `skillzApp.swift` helpers / store shell methods |
| `AppBrand.cs` | `const string Name = "Skills"`. | `Theme/AppBrand.swift` |

### 2.3 `ViewModels/`

| File | Purpose | macOS counterpart |
|---|---|---|
| `CatalogViewModel.cs` | The store: snapshot, `IsLoading`, `LastRefreshedAt`, `SourceStatuses`, `SelectedSection/PlatformFilter/SearchText/SelectedItemId/ShowInspector`, `FilteredItems`, counts, `RefreshAsync`/`ReloadCatalog`, selection-preservation, CRUD delegates, watcher wiring. | `Services/CatalogStore.swift` |
| `CatalogItemViewModel.cs` | Wraps a `CatalogItem` for list/detail binding (display, badges, kind flags, `HasSharedAvailability`). | (SwiftUI row binding) |
| `SkillDetailViewModel.cs` | Skill editor screen: header, markdown-file list, selected file, save-status chip, file-switch (save-then-load), `EditorDocument` binding. | `SkillDetailView`/`MarkdownEditorView` |
| `McpDetailViewModel.cs` | MCP detail projection + action commands. | `MCPDetailView` |
| `PluginDetailViewModel.cs` | Plugin detail projection + action commands. | `PluginDetailView` |
| `InspectorViewModel.cs` | Type-specific inspector sections + related platforms. | `InspectorView` |
| `NewSkillViewModel.cs` | New-skill form: name/description/body template, per-platform toggles seeded from detected set, `CanCreate`. | `NewSkillSheet` |
| `RenameSkillViewModel.cs` | Rename form (folder name, location), `CanRename`. | `RenameSkillSheet` |
| `SkillDetailsViewModel.cs` | Metadata edit form (name/description/version), read-only gating. | `SkillDetailsSheet` |
| `OnboardingViewModel.cs` | Detected-tools cards + setup toggles, finish/complete. | `OnboardingView` |
| `SettingsViewModel.cs` | Tabbed settings (General/Sources/Editor; Agents deferred). | `SettingsView`/`SettingsPane` |
| `MainViewModel.cs` | Shell-level: owns `CatalogViewModel`, dialog/messenger orchestration, top-bar command enablement, shortcuts. | `MainWindowView` + `skillzApp` commands |

### 2.4 `Views/`

| File | Purpose | macOS counterpart |
|---|---|---|
| `MainWindow.xaml(.cs)` | `FluentWindow` (Mica) shell: TitleBar + custom toolbar row + 3-pane Grid/GridSplitters + error-banner overlay; `Loaded`/`Activated`/`Closed` wiring. | `MainWindowView` |
| `SidebarView.xaml` | Header + "LIBRARY" + "PLATFORMS" sections (nav rows, counts). | `SidebarView` |
| `ItemListView.xaml` | Pinned header + state-swap (ProgressRing/welcome/empty/list) + virtualized list of `SkillzListRow`. | `ItemListView` |
| `DetailContainerView.xaml` | `ContentControl` + `DataTemplateSelector` (skill/mcp/plugin/empty) + optional inspector column. | `DetailContainerView` |
| `SkillDetailView.xaml` | Header + file-tree splitter + AvalonEdit editor + save-status chip. | `SkillDetailView` |
| `McpDetailView.xaml` | Scroll + header + 2 detail cards + action row. | `MCPDetailView` |
| `PluginDetailView.xaml` | Scroll + header + 2 detail cards + action row. | `PluginDetailView` |
| `InspectorView.xaml` | Collapsible column: Details + type-specific section. | `InspectorView` |
| `OnboardingView.xaml` | Non-dismissable modal: two-column (detected cards / setup toggles) + footer buttons. | `OnboardingView` |
| `SettingsView.xaml` | Custom tab bar + panes (General/Sources/Editor). | `SettingsView`/`SettingsPane` |

`Views/Components/` (reusable controls, build first):

| File | Purpose | macOS counterpart |
|---|---|---|
| `Tag.xaml(.cs)` | Capsule badge, `Variant {Outline,Filled,Muted,Subtle}`; `PlatformBadge`/`EnabledBadge` wrappers. | `SkillzTag`/`SkillzComponents` |
| `Hairline.xaml` | 1px border separator. | `SkillzHairline` |
| `DetailCard.xaml` | Bordered card (uppercase header + content). | `SkillzDetailCard` |
| `DetailRow.xaml` | 88-wide label + selectable value (mono flag). | `SkillzDetailRow` |
| `EmptyState.xaml` | Centered title + message. | `SkillzEmptyState` |
| `ErrorBanner.xaml` | Bottom toast (message + Dismiss, drop shadow). | `SkillzErrorBanner` |
| `NavRow.xaml(.cs)` | Sidebar/file-tree row (icon + title + count, selection fill). | `SkillzNavRow` |
| `ListRow.xaml` | Catalog list-row template (title+badges, 2-line subtitle, chrome). | `SkillzListRow`/`SkillzListRowChrome` |
| `SharedSkillInfoButton.xaml(.cs)` | Info glyph + popover for shared skills. | `SharedSkillInfoButton` |
| `ToolbarButtons.xaml` | Glass toolbar button/group/search-field styles. | `SkillzComponents` toolbar styles |
| `CatalogItemTemplateSelector.cs` | DataTemplateSelector for detail/inspector by item kind. | (SwiftUI switch) |

`Views/Dialogs/` (WPF-UI `ContentDialog`s):

| File | Purpose | macOS counterpart |
|---|---|---|
| `NewSkillDialog.xaml` | New-skill sheet (520×520). | `NewSkillSheet` |
| `RenameSkillDialog.xaml` | Rename sheet (440). | `RenameSkillSheet` |
| `SkillDetailsDialog.xaml` | Metadata edit sheet (440). | `SkillDetailsSheet` |
| `DeleteConfirmDialog.xaml` | Destructive delete confirm. | `confirmationDialog` |
| `SaveFailedDialog.xaml` | Save-failed alert (or reuse `MessageBox`). | Save-failed alert |

### 2.5 `Themes/`

| File | Purpose | macOS counterpart |
|---|---|---|
| `Colors.Light.xaml` | 9 `Color`+`SolidColorBrush` keys (light hex) + derived opacity brushes. | `SkillzColors.swift` + `*.colorset` (light) |
| `Colors.Dark.xaml` | Same keys, dark hex. | `*.colorset` (dark) |
| `Typography.xaml` | Mono/UI font families, size doubles, weights. | `SkillzTypography`/`SkillzTextStyles` |
| `Spacing.xaml` | Spacing doubles, `CornerRadius`, `Thickness`, window-metric doubles. | `SkillzSpacing`/`SkillzWindowMetrics` |
| `Controls.xaml` | Styles for Tag/NavRow/ListRow/Card/buttons/toolbar pills. | `SkillzComponents.swift` |
| `Converters.cs` | `ToUpperConverter`, null/empty→Visibility, mono-path-compact (middle ellipsis), bool→weight, opacity helpers. | (SwiftUI modifiers) |

### 2.6 `Assets/`

| File | Purpose | macOS counterpart |
|---|---|---|
| `PlatformIconCursor.png` … `PlatformIconOpenCode.png` (×6, theme-aware) | Sidebar brand icons (template-tinted). | `Assets.xcassets` brand icons |
| `AppIcon.ico` | App/window/taskbar icon. | `AppIcon` |
| `Markdown.xshd` (optional) | AvalonEdit markdown syntax highlight. | (n/a) |

### 2.7 Root

| File | Purpose | macOS counterpart |
|---|---|---|
| `App.xaml(.cs)` | Application bootstrap: DI container, theme apply, merged dictionaries, main window, onboarding gate. | `skillzApp.swift` |
| `SkillzWin.csproj` | `net8.0-windows`, WPF, NuGet refs. | `.xcodeproj` |

---

## 3. Milestone Build Order (CORE-FIRST)

Each milestone is independently verifiable. **Deferred** items (tray monitor, hooks,
notch, auto-update) are NOT built here.

**M0 — Project scaffold & DI.** Create `SkillzWin.sln`/`.csproj` (`net8.0-windows`),
add NuGet packages (§1), wire `Microsoft.Extensions.DependencyInjection` in
`App.OnStartup`, register placeholder services, empty `FluentWindow` with Mica. Verify
it builds and launches.

**M1 — Theme & design tokens.** Author `Themes/Colors.Light.xaml` + `Colors.Dark.xaml`
(exact hexes from §4), `Typography.xaml`, `Spacing.xaml`, `Converters.cs`. Wire
`ApplicationThemeManager` + appearance setting (system/light/dark). All XAML uses
`{DynamicResource Skillz*Brush}`. Verify light/dark swap at runtime; accent stays black.

**M2 — Models & path layer.** Implement all `Models/` records/enums and the path
services: `AgentEnvironment` (injectable, test-root ctor), `AgentPaths`,
`PlatformSkillPaths` (scan roots, `PrimaryPlatform`, `PlatformsThatShare`,
`WatchDirectories`), `OpenClawConfig`. Unit-test the path table (§4) and the ordered
substring classifier against fixture paths.

**M3 — Frontmatter + skill scanning.** `FrontmatterParser`/`FrontmatterWriter`
(CRLF-safe, byte-parity, lossy block-scalar behavior preserved), `SkillNameValidator`
(+ Win32 reserved names), `SkillScanner` (recursive `SKILL.md`, hidden/`.system`,
built-in cursor, plugin-embedded, dedup by full path OrdinalIgnoreCase, `MarkdownFiles`).
Unit-test against the §8 example files in spec 02.

**M4 — MCP + plugin + detection scanners.** `TomlConfigReader` (Tomlyn),
`McpScanner` (env-keys-only, transport precedence, sort), `PluginScanner`
(enable-map defaults: Cursor=true, Claude/Codex=false; synthetic config-only Codex;
`inferPluginID`/`countSkills`), `PlatformSourceDetector` (PATH/PATHEXT executable
probe, install-signal rules). Unit-test against spec 03 §6 fixtures.

**M5 — Catalog orchestration & store.** `CatalogDiscoveryService` (pure aggregator),
`CatalogFilter` (section×platform(+alsoAvailableOn)×4-field search, sort),
`CatalogViewModel` (background scan via `Task.Run`, selection-preservation 3-branch
algorithm, counts, `IsLoading` only on non-silent, source-statuses recompute each
snapshot), `ISettingsService`. Verify discovery populates a snapshot end-to-end
(headless/log test).

**M6 — Shell, 3-pane layout & sidebar.** `MainWindow` (FluentWindow + TitleBar +
toolbar row + 3-pane Grid/GridSplitters + error-banner overlay), `SidebarView`
(LIBRARY + PLATFORMS, counts, selection), reusable components (`Tag`, `NavRow`,
`Hairline`, `EmptyState`, `ErrorBanner`, toolbar pills). Top-bar commands + keyboard
(Ctrl+N/R/S/B, F5). Verify navigation drives `SelectedSection`/`SelectedPlatformFilter`.

**M7 — Item list & read-only detail panes.** `ItemListView` (virtualized, state-swap:
loading/welcome/empty/list, `ListRow` with platform/enabled/shared badges, 2-line
subtitle), `DetailContainerView` + `McpDetailView` + `PluginDetailView` + `EmptyState`,
`InspectorView` (toggle persists to settings), context menu (reveal/copy/open).
Verify selecting items shows correct details + counts.

**M8 — Skill editor (AvalonEdit).** `SkillDetailView` + `EditorDocument` integration:
AvalonEdit two-way text sync (no CRLF rewrite), file-tree splitter (shown when >1 md
file), save-status chip, 1.2 s debounced autosave, save-then-load on file switch,
Save-Failed dialog, reveal-folder button. Verify edit→autosave→disk round-trip is
byte-clean.

**M9 — New / Rename / Delete / Metadata (CRUD).** `SkillFileService` +
`NewSkillDialog`/`RenameSkillDialog`/`SkillDetailsDialog`/`DeleteConfirmDialog`,
`FileAccessError` mapping, capability gates, `ReloadCatalog(selecting:)` after each
op, autosave pause/resume around dialogs, case-only-rename NTFS handling. Verify all
four operations across multiple platforms incl. partial-success create.

**M10 — File watching & live refresh.** `CatalogFileWatcher` (per-root recursive,
shared 300 ms DispatcherTimer debounce, `Error` recovery), wire to silent refresh on
file events + `Window.Activated` (rebuild watchers) + `Window.Loaded` (first scan) +
`Window.Closed` (stop). Verify external file changes refresh the catalog within ~300 ms.

**M11 — Onboarding & Settings.** `OnboardingView` (first-run gate on
`HasCompletedOnboarding`, non-dismissable, detected-tool cards, setup toggles —
hooks/tray toggles present but inert in core), `SettingsView` (General/Sources/Editor
tabs; Agents tab stubbed). Verify first-run shows onboarding; settings persist to JSON
and live-toggling hide flags triggers refresh.

**M12 — Polish & parity pass.** Empty/welcome copy ("this PC"), error banner,
keyboard/shortcut tooltips, theme-aware brand icons, virtualization perf, OrdinalIgnoreCase
audit, atomic-write/no-BOM audit. Smoke-test against real `~/.cursor`/`~/.claude`/etc.

> **DEFERRED PASS (not in core scope):** agent-session tray monitor (`AgentSessionStore`,
> `NotifyIcon`, waiting-count badge), agent hooks install/repair (`AgentHookStore`,
> notify-script `.ps1`), `agent-state.json` watcher (`AgentPaths.WatchPathsForAgents`),
> macOS-notch overlay, Sparkle/NetSparkle auto-update, Settings "Agents" tab. The core
> app must function fully without these; onboarding's hook/tray toggles persist settings
> but perform no action until this pass.

---

## 4. Windows Path Table & Config Formats (Services layer contract)

Base = `Environment.GetFolderPath(SpecialFolder.UserProfile)` → `C:\Users\<user>`.
Always `Path.Combine`. `+wsExists`/`+exists` = appended only if the dir exists at scan time.

### 4.1 Per-platform paths (all 6)

| Platform | Raw value (ID) | Display | Home dir | User-skills dir (create target) | Skill scan roots | Watch root |
|---|---|---|---|---|---|---|
| Cursor | `cursor` | Cursor | `.cursor` | `.cursor\skills` | `.cursor\skills` | `.cursor` |
| Claude Code | `claudeCode` | Claude Code | `.claude` | `.claude\skills` | `.claude\skills` | `.claude` |
| Codex | `codex` | Codex | `.codex` | `.codex\skills` | `.codex\skills`, `.agents\skills` | `.codex` |
| Hermes | `hermes` | Hermes | `.hermes` | `.hermes\skills` | `.hermes\skills` | `.hermes` |
| Pi | `pi` | Pi | `.pi` | `.pi\agent\skills` | `.pi\agent\skills`, `.agents\skills` | `.pi` |
| OpenClaw | `openClaw` | **OpenCode** | `.openclaw` | `.openclaw\skills` | `.openclaw\skills`, `<workspace>\skills` (+wsExists) | `.openclaw` |

> Preserve exactly: enum case `openClaw`, dotfolder `.openclaw`, display "OpenCode",
> brand asset `PlatformIconOpenCode`; raw ID `claudeCode` (camelCase). Pi's nested
> `agent\` segment. Raw values are byte-for-byte the catalog-item ID identity.

### 4.2 Shared / app-level paths

| Purpose | Windows path |
|---|---|
| Shared agents root | `C:\Users\<user>\.agents` |
| Shared skills (Codex+Pi read; primary owner = Pi; alsoOn Codex+OpenClaw) | `C:\Users\<user>\.agents\skills` |
| OpenClaw config | `C:\Users\<user>\.openclaw\openclaw.json` |
| OpenClaw default workspace (fallback) | `C:\Users\<user>\.openclaw\workspace` |
| OpenClaw workspace skills | `<resolved workspace>\skills` |
| Built-in Cursor catalog (gated by `hideBuiltInCursor`) | `C:\Users\<user>\.cursor\skills-cursor` |
| Plugin-embedded scan (Cursor/Claude/Codex) | `<home>\plugins\cache` |
| App support / settings dir | `C:\Users\<user>\AppData\Roaming\SkillzWin` |
| Settings file | `…\AppData\Roaming\SkillzWin\settings.json` |
| App-state file *(deferred)* | `…\AppData\Roaming\Skillz\agent-state.json` |
| Skillz home *(deferred)* | `C:\Users\<user>\.skillz` |
| Notify script *(deferred, was `.sh`)* | `C:\Users\<user>\.skillz\bin\skillz-agent-notify.ps1` |

### 4.3 Watch sets
- **Skill-file watch (`PlatformSkillPaths.WatchDirectories`, existence-filtered):** all
  6 platform homes + (`.agents\skills` if it exists, else `.agents`) + OpenClaw workspace
  parent (only if its `skills\` exists). Recursive; shared 300 ms debounce.
- **Agent-state watch (deferred):** app-support dir, Claude sessions, Codex sessions,
  Codex `process_manager\chat_processes.json` (file must exist), Cursor projects.

### 4.4 Ordered path classifier (`PrimaryPlatform`) — preserve order, first match wins
Normalize `path.Replace('\\','/')`, ordinal `Contains`:
`/skills-cursor/`→Cursor, `/.hermes/`→Hermes, `/.openclaw/`→OpenClaw, `/.pi/`→Pi,
`/.cursor/`→Cursor, `/.claude/`→ClaudeCode, `/.codex/`→Codex, `/.agents/`→Pi, default→Cursor.
`PlatformsThatShare`: returns `[Pi, Codex, OpenClaw]` iff path contains `/.agents/skills/`
or ends `/.agents/skills`, else empty (caller removes primary → `alsoAvailableOn`).
**Quirk preserved:** OpenClaw is listed as a sharer though it doesn't scan `.agents\skills`.

### 4.5 MCP config formats (Services must parse)

**Scanned platforms: Cursor, Claude Code, Codex only.** Sort by name OrdinalIgnoreCase.
Transport precedence: `url`⇒Http, else `command`⇒Stdio, else Unknown. **Store env KEY
NAMES only (sorted), never values.** Swallow per-read errors → empty list.

- **Cursor** — `.cursor\mcp.json` (System.Text.Json): top-level `"mcpServers"` object,
  per entry `{ url?, command?, args?: string[], env?: {k:v} }`. Missing/wrong-type → defaults.
- **Claude Code** — `.claude\.mcp.json` (leading dot), **same JSON shape**.
  (Do NOT read `~/.claude.json` — match macOS exactly; tracked as enhancement.)
- **Codex** — `.codex\config.toml` (Tomlyn): tables `[mcp_servers.<name>]` with
  `command`/`url`/`args` (real TOML array) and env as dotted `env.KEY` or inline
  `env = { KEY = "v" }` (Tomlyn handles both; strip `env.` prefix; sort key names).

Example shapes:
```json
// .cursor\mcp.json  /  .claude\.mcp.json
{ "mcpServers": {
  "filesystem":     { "command": "npx", "args": ["-y","@mcp/fs","C:\\work"], "env": { "DEBUG": "1" } },
  "github-remote":  { "url": "https://mcp.example.com/github", "env": { "GITHUB_TOKEN": "..." } },
  "bare-entry":     { "type": "sse" } } }
```
```toml
# .codex\config.toml
[mcp_servers.filesystem]
command = "npx"
args = ["-y", "@mcp/fs", "C:\\work"]
env.DEBUG = "1"
env.LOG_LEVEL = "info"
[mcp_servers.remote]
url = "https://mcp.example.com/sse"
```

### 4.6 Plugin config formats (Services must parse)

**Scanned platforms: Cursor, Claude, Codex.** Sort by displayName OrdinalIgnoreCase.
`pluginID` = `name@marketplace`; `marketplace` = substring after **last** `@`.
`skillCount` = subdir count under `<installPath>\skills`. Metadata file =
`.claude-plugin\plugin.json` then `.codex-plugin\plugin.json` (`name`/`description`/`version`).

- **Cursor** — crawl `.cursor\plugins\cache` for metadata dirs; all `IsEnabled=true`.
- **Claude** — enable map from `.claude\settings.json` key `enabledPlugins` (`{id:bool}`);
  primary source `.claude\plugins\installed_plugins.json` top-level `"plugins"` →
  `{ id: [ { installPath, version? } ] }` (take first entry, require `installPath`);
  **default disabled** if not in map; fall back to `plugins\cache` (default disabled) if
  manifest absent.
- **Codex** — enable map from `.codex\config.toml` `[plugins."id"]` (`enabled` truthy
  ∈ {true,"yes","1"}; **table present w/o `enabled` ⇒ true**); crawl `.codex\plugins\cache`
  (default disabled); then append synthetic items for enabled-map IDs absent from cache.

```json
// .claude\plugins\installed_plugins.json
{ "plugins": { "code-reviewer@acme": [ { "installPath": "C:\\Users\\me\\.claude\\plugins\\cache\\acme\\code-reviewer", "version": "1.2.0" } ] } }
// .claude\settings.json
{ "enabledPlugins": { "code-reviewer@acme": true, "docs-helper@community": false } }
```
```toml
# .codex\config.toml
[plugins."my-plugin@acme"]   # enabled = true
[plugins."off-one@acme"]
enabled = false
[plugins."implicit-on@acme"] # no key → enabled
```

### 4.7 Executable detection (source detector, Windows-native)
No Unix exec bit. Resolve via `%PATH%` (split `;`) × name × `%PATHEXT%` (`.COM;.EXE;.BAT;.CMD;…`)
+ per-tool dirs (`.local\bin`, `.opencode\bin`, `.hermes\bin`, `%LOCALAPPDATA%\Programs\<tool>`,
`%APPDATA%\npm`, `%ProgramFiles%\<tool>`). "Executable" = `File.Exists(candidate)` with a
PATHEXT extension. Names per platform: Cursor `cursor-agent`/`cursor`; Claude `claude`;
Codex `codex`; Hermes `hermes`/`hermes-cli`/`tirith`; Pi `pi`; OpenClaw
`opencode`/`open-code`/`openclaw`/`open-claw`. Bare/empty dotfolder is NOT an install
signal; an executable always is.

---

## 5. Key Risks & Decisions

| # | Risk / Decision | Resolution |
|---|---|---|
| R1 | **TOML parsing choice.** Mini-parser is lossy (no inline tables/escapes/typed values); porting it copies bugs. | **Use Tomlyn** (spec-compliant superset). Re-implement only `enabled` truthiness (accept `true`/`"yes"/"1"/"true"`) + default-enabled-when-table-present. JSON via System.Text.Json. (03 §2.6) |
| R2 | **Frontmatter parser fidelity.** Real YAML would change byte output (block scalars, type coercion, quoting) and corrupt cross-platform files. | **Port the custom mini-parser/emitter faithfully** (4 keys, first-colon split, block-scalar→empty on read, fixed key order on write, `quoteIfNeeded`, fallback `name: skill`). **Necessary divergence:** normalize/trim `\r` for delimiter compares so CRLF files parse. Keep the lossy multi-line-description round-trip behavior. (02 §2, §9) |
| R3 | **FileSystemWatcher debounce.** FSEvents (single stream, native 100 ms coalesce + 300 ms debounce, recursive) has no 1:1 analog; FSW fires multiple events per save and can buffer-overflow. | **One `FileSystemWatcher` per existing root**, `IncludeSubdirectories=true`, `NotifyFilter = FileName\|DirectoryName\|LastWrite\|Size`, all sharing **one 300 ms `DispatcherTimer`** (Stop/Start per event → fires once on UI thread → silent refresh). Handle `Error` by dispose+recreate; bump `InternalBufferSize`. Rebuild on `Window.Activated`. (04 §8) |
| R4 | **Theme hex values.** Monochrome, grayscale, pure-black accent; must NOT adopt Windows accent. | Lock exact hexes: **Light** Canvas `#FFFFFF`, Ink `#000000`, Emphasis `#333333`, Muted `#535353`, SectionLabel `#474747`, Disabled `#777777`, Hairline `#E8E8E8`, Selection `#D5D5D5`, Accent `#000000`. **Dark** Canvas `#1E1E1E`, Ink `#F7F7F7`, Emphasis `#D1D1D1`, Muted `#9D9D9D`, SectionLabel `#8E8E8E`, Disabled `#757575`, Hairline `#3C3C3C`, Selection `#2E2E2E`, Accent `#000000`. Derived: SelectionTab `Selection@0.58`, SelectionHover `@0.5`, EmphasisBorder `Emphasis@0.35`, banner shadow `Black@0.08`. `{DynamicResource}` only. (06 §1.2) |
| R5 | **SKILL.md case sensitivity.** macOS string-compares `== "SKILL.md"` (case-sensitive); NTFS matches case-insensitively. | **Match case-insensitively** for discovery robustness, **always WRITE uppercase `SKILL.md`**. Documented intentional relaxation. (02 §1.3) |
| R6 | **Path classification on `\`.** macOS `Contains("/.cursor/")` never matches Windows `\` paths. | Normalize `path.Replace('\\','/')` once, keep literals verbatim, ordinal compare; OR segment-match. Dedup dicts `OrdinalIgnoreCase`. Preserve ordered first-match precedence. (01 §2.4, 04 §6) |
| R7 | **Atomic UTF-8 no-BOM + `\n`.** Default encodings/AvalonEdit can add BOM or CRLF, breaking byte-parity with macOS files. | Write temp → `File.Replace`/`File.Move`; `new UTF8Encoding(false)`; emit `\n` literally (never `Environment.NewLine`); prevent AvalonEdit newline rewrite. (02 §4.5) |
| R8 | **POSIX write-permission gate.** No `access(W_OK)` equivalent on NTFS. | Best-effort: read-only-attribute check + try/catch on actual op (`UnauthorizedAccessException` → `FileAccessError`). Document non-exact parity. (02 §4.1) |
| R9 | **Win32 reserved names.** Validator allows folder names that NTFS rejects (`CON`, `NUL`, `COM1`…). | **Add** reserved-name rejection beyond the macOS rules to avoid create-time `IOException`. (02 §7) |
| R10 | **`reloadCatalog` synchronous scan** can briefly block UI on large catalogs (macOS accepts it for CRUD immediacy). | Default: match macOS synchronous reselect; if catalog is large, `await Task.Run` + reselect while preserving immediate-select UX. (04 §11) |
| R11 | **Locale vs ordinal search/sort.** macOS uses localized compare. | **OrdinalIgnoreCase** for predictability across search, sort, dedup, contains — keep consistent everywhere. (02, 04) |
| R12 | **Section-header tracking 0.6 & `.continuous` squircles** have no WPF equivalent; pt vs DIP sizing. | Accept minor drift for v1 (uppercase via converter); keep raw numeric font sizes, validate visually. (06 §9) |
| R13 | **Tag `IReadOnlyList` value-equality.** C# `record` won't structurally compare `AlsoAvailableOn`/`Args`/`EnvKeys`; UI change-detection may rely on it. | Provide sequence-equal wrapper or custom `Equals`/`GetHashCode`; identity keys off `Id` (platform+path), so equality is load-bearing only for change detection. (01 §1.6) |
| R14 | **`openClaw` sharer asymmetry** (listed in `platformsThatShare` but not in its scan roots) could produce phantom "also on OpenCode" badges. | Preserve literal `[Pi, Codex, OpenClaw]` to match macOS; revisit only if phantom badges observed. (01 §4.1) |
| R15 | **App-support location** (Roaming vs Local). | Use `%APPDATA%\SkillzWin` (Roaming, matches macOS "Application Support" intent) for settings.json. (01 §2.1) |

---

## 6. Summary

The port is a faithful, behavior-preserving reimplementation: a single `net8.0-windows`
WPF project (WPF-UI Fluent + CommunityToolkit.Mvvm + AvalonEdit + Tomlyn + System.Text.Json)
organized as Models / Services / ViewModels / Views / Themes / Assets. The core-first
13-milestone path (M0–M12) delivers full catalog discovery across all six harnesses, the
three-pane browser, the SKILL.md editor with frontmatter + new/rename/delete, the
monochrome Fluent theme, file-watching, and onboarding — while explicitly deferring the
agent-session tray monitor, hooks, notch, and auto-update. The load-bearing fidelity
decisions are: **Tomlyn** (not the mini-parser), a **faithful custom frontmatter
parser/emitter with a CRLF fix** (not YamlDotNet), **per-root FileSystemWatchers with a
shared 300 ms UI-thread debounce**, **exact monochrome theme hexes with a pure-black
accent**, normalize-to-`/` path classification, and atomic UTF-8 no-BOM `\n` writes.
