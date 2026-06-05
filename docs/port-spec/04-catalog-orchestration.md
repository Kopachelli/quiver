# 04 — Catalog Orchestration (Discovery, Store, Filter, Watcher)

Port spec for the catalog discovery + state layer of the macOS **skillz** app. This
covers four source files plus the supporting types they depend on:

- `Services/DiscoveryEngine.swift` — orchestrates the three scanners into one snapshot
- `Services/CatalogStore.swift` — the `@MainActor @Observable` store (UI state)
- `Services/CatalogFilter.swift` — section × platform × search filtering + sorting
- `Services/FSEventWatcher.swift` — FSEvents-based file watcher with debounce

Supporting types documented inline because the four files cannot be understood
without them: `CatalogSnapshot`, `CatalogItem`, `SkillItem`/`MCPItem`/`PluginItem`,
`AgentPlatform`, `CatalogSection`, `PlatformSkillPaths`, `PlatformSourceDetector`,
`AgentEnvironment`, `AppSettings`.

Target platform: **WPF on .NET 8 (LTS)**, WPF-UI (lepoco/wpfui) Fluent, MVVM,
AvalonEdit for the editor. Every macOS/Swift construct below is paired with its
Windows/.NET equivalent.

---

## 1. Path conventions (macOS → Windows)

The whole subsystem hangs off per-platform "home directories" — dotfolders under the
user's home. On macOS these are resolved from `FileManager.default.homeDirectoryForCurrentUser`.

| Platform enum | macOS home dir | Windows home dir | `displayName` |
|---|---|---|---|
| `cursor` | `~/.cursor` | `%USERPROFILE%\.cursor` | "Cursor" |
| `claudeCode` | `~/.claude` | `%USERPROFILE%\.claude` | "Claude Code" |
| `codex` | `~/.codex` | `%USERPROFILE%\.codex` | "Codex" |
| `hermes` | `~/.hermes` | `%USERPROFILE%\.hermes` | "Hermes" |
| `pi` | `~/.pi` | `%USERPROFILE%\.pi` | "Pi" |
| `openClaw` | `~/.openclaw` | `%USERPROFILE%\.openclaw` | "OpenCode" |

Shared "agents" dir: `~/.agents` → `%USERPROFILE%\.agents` (used by codex + pi +
openClaw as a shared skill source — see §6).

`AgentEnvironment.live` (in `AgentEnvironment.swift`) is the single source of truth
for path resolution. It exposes `homeDirectory`, `applicationSupportDirectory`,
`skillzHomeDirectory`, and `executableSearchDirectories`, plus
`homeDirectory(for: AgentPlatform)` which appends the dotfolder name. The struct is
swappable (`AgentPaths.environment` is a mutable static) so tests inject
`AgentEnvironment.temporary(root:)`.

**Windows mapping.** Create an `IAgentEnvironment` service (DI singleton) with:

```csharp
public interface IAgentEnvironment {
    string HomeDirectory { get; }                 // Environment.GetFolderPath(SpecialFolder.UserProfile)
    string ApplicationSupportDirectory { get; }   // SpecialFolder.LocalApplicationData + "\\Skillz"  (macOS: ~/Library/Application Support/Skillz)
    string SkillzHomeDirectory { get; }           // HomeDirectory + "\\.skillz"
    IReadOnlyList<string> ExecutableSearchDirectories { get; }
    string HomeDirectory(AgentPlatform p);        // HomeDirectory + "\\" + DotFolderName(p)
}
```

- `home` = `Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)`.
- The macOS `executableSearchDirectories` are `~/.local/bin`, `~/.opencode/bin`,
  `~/.hermes/bin`, `/opt/homebrew/bin`, `/usr/local/bin`, `/usr/bin`. On Windows
  the homebrew/usr paths are meaningless. For install detection (§7) prefer
  splitting `%PATH%` and probing for `cursor.exe`, `claude.exe`, `codex.exe`, etc.
  (`Environment.GetEnvironmentVariable("PATH").Split(';')`), plus the per-platform
  `bin` dotfolders. The `.sh`/no-extension executable names become `.exe`/`.cmd`.
- **Always build paths with `Path.Combine` / `Path.DirectorySeparatorChar`.** The
  Swift code matches substrings like `"/.cursor/"`, `"/skills-cursor/"`,
  `"/.agents/skills/"` against `url.path`. On Windows these become `\.cursor\`,
  `\skills-cursor\`, `\.agents\skills\`. **Do path classification on normalized
  path segments, not raw `Contains("/...")`** (see §6 — this is a porting hazard).

---

## 2. DiscoveryEngine — orchestration of the three scanners

### Swift behavior (exact)

`DiscoveryEngine` is a `nonisolated enum` (namespace, no instances) with one static
method:

```swift
static func discover(hideBuiltInCursor: Bool, hideSystemCodex: Bool) -> CatalogSnapshot {
    CatalogSnapshot(
        skills:  SkillScanner.scan(hideBuiltInCursor: hideBuiltInCursor, hideSystemCodex: hideSystemCodex),
        mcps:    MCPScanner.scan(),
        plugins: PluginScanner.scan()
    )
}
```

Key facts:

- **It is a pure, synchronous, side-effect-free aggregator.** It calls the three
  scanners **sequentially in declaration order** (skills, then MCPs, then plugins)
  and packs the three result arrays into a `CatalogSnapshot` value type. There is no
  cross-scanner merging at this level — each scanner owns its own list.
- It takes **two boolean toggles** that are forwarded *only* to `SkillScanner`:
  - `hideBuiltInCursor` — suppress the built-in `~/.cursor/skills-cursor` catalog.
  - `hideSystemCodex` — suppress codex's `.system` skills.
  - `MCPScanner.scan()` and `PluginScanner.scan()` take **no arguments**.
- `nonisolated` + `Sendable` means it is safe to call off the main actor; the store
  always calls it on a background `Task.detached`.

### `CatalogSnapshot` (the merge container)

```swift
struct CatalogSnapshot: Sendable {
    var skills:  [SkillItem]  = []
    var mcps:    [MCPItem]    = []
    var plugins: [PluginItem] = []

    var allItems: [CatalogItem] {        // flattened, heterogeneous
        skills.map { .skill($0) } + mcps.map { .mcp($0) } + plugins.map { .plugin($0) }
    }
}
```

`allItems` concatenates in fixed order **skills → mcps → plugins**, wrapping each in
the `CatalogItem` enum (`.skill`/`.mcp`/`.plugin`). Ordering of `allItems` is not
relied upon for display — `CatalogFilter.sorted` re-sorts (§5).

### How merge/dedup actually happens (it is *inside* the scanners, not the engine)

There is **no dedup at the DiscoveryEngine level.** All merging/deduping lives inside
the individual scanners:

- **Skills** — `SkillScanner.scan` gathers from many roots (per-platform user dirs,
  the shared `~/.agents/skills`, the built-in cursor catalog, and plugin-embedded
  skills), then calls `deduplicate(_:)`:
  - Dedup key = **absolute on-disk path** of the `SKILL.md` (`item.skillPath.path`).
    A `Dictionary[pathKey] = item` collapses duplicates (last write wins). So if two
    platforms enumerate the *same* file (e.g. shared `~/.agents/skills`), only **one**
    `SkillItem` survives.
  - During dedup it recomputes the **primary platform** via
    `PlatformSkillPaths.primaryPlatform(for:)` (path-substring classification, §6) and
    fills `alsoAvailableOn` = `platformsThatShare(path:)` minus the primary. So a skill
    in `~/.agents/skills` is owned by `.pi` and `alsoAvailableOn = [.codex, .openClaw]`.
  - The final list is sorted case-insensitively by `displayName`.
- **MCPs** — `MCPScanner.scan` appends Cursor (`~/.cursor/mcp.json`), Claude
  (`~/.claude/.mcp.json`), and Codex (`~/.codex/config.toml` TOML) results. **No
  dedup** — the same server name on two platforms yields two `MCPItem`s (distinct IDs
  because IDs embed the platform). Sorted case-insensitively by `name`.
- **Plugins** — `PluginScanner.scan` appends Cursor + Claude + Codex. Dedup is
  *per-platform* inside `scanPluginMetadata` via a `seenPaths: Set<String>` on
  install path, plus Codex merges its `config.toml` enabled-list with cache-scanned
  plugins (skipping IDs already present). Sorted case-insensitively by `displayName`.

So the dedup contract is: **skills dedup globally by path; MCPs do not dedup; plugins
dedup per-platform by install path.**

### `CatalogItem` (the heterogeneous union)

`CatalogItem` is a Swift enum with three cases and computed accessors used by the
filter and UI:

| Accessor | `.skill` | `.mcp` | `.plugin` |
|---|---|---|---|
| `id` | `item.id` | `item.id` | `item.id` |
| `kind` | `.skill` | `.mcp` | `.plugin` |
| `platform` | `item.platform` | `item.platform` | `item.platform` |
| `displayName` | `item.displayName` | `item.name` | `item.displayName` |
| `descriptionText` | `item.description` | `item.endpointSummary` | `item.description` |
| `listSubtitle` | parent dir path | `configFileURL.path` | marketplace ?? pluginID |
| `modifiedAt` | `item.modifiedAt` | `item.modifiedAt` | `item.modifiedAt` |
| `skillItem`/`mcpItem`/`pluginItem` | typed unwrap | … | … |

IDs are stable strings:
- skill: `"skill:<platform>:<SKILL.md path>"`
- mcp: `"mcp:<platform>:<name>"`
- plugin: `"plugin:<platform>:<pluginID>:<installPath ?? pluginID>"`

### Windows / .NET mapping

- `DiscoveryEngine` → a stateless `CatalogDiscoveryService` (DI singleton) with
  `CatalogSnapshot Discover(bool hideBuiltInCursor, bool hideSystemCodex)`. Inject the
  three scanner services. Keep it synchronous + pure; the ViewModel wraps it in
  `Task.Run` (see §4).
- `CatalogSnapshot` → an immutable record:
  ```csharp
  public sealed record CatalogSnapshot(
      IReadOnlyList<SkillItem> Skills,
      IReadOnlyList<McpItem> Mcps,
      IReadOnlyList<PluginItem> Plugins) {
      public IEnumerable<CatalogItem> AllItems =>
          Skills.Select(CatalogItem.Skill)
          .Concat(Mcps.Select(CatalogItem.Mcp))
          .Concat(Plugins.Select(CatalogItem.Plugin));
  }
  ```
- `CatalogItem` enum → a C# discriminated union. .NET has no native DU; use a sealed
  abstract base `CatalogItem` with `SkillCatalogItem`/`McpCatalogItem`/`PluginCatalogItem`
  subclasses (or OneOf<>). Expose the same computed accessors (`Id`, `Kind`,
  `Platform`, `DisplayName`, `DescriptionText`, `ListSubtitle`, `ModifiedAt`).
- `AgentPlatform` enum → C# `enum AgentPlatform { Cursor, ClaudeCode, Codex, Hermes, Pi, OpenClaw }`
  with extension methods for `DisplayName`, `DotFolderName`, brand icon key, etc.
  (Swift's `String` raw value + `CaseIterable` → `Enum.GetValues<AgentPlatform>()`.)

---

## 3. CatalogStore — observable state shape

`CatalogStore` is `@MainActor final class … : ObservableObject` — i.e. a main-thread
view model with Combine `@Published` properties. **This maps directly to a WPF
`ObservableObject` / `INotifyPropertyChanged` ViewModel that always mutates on the UI
thread (the WPF Dispatcher).**

### Published / observable properties (the exact state surface)

| Swift property | Access | Type | Meaning | WPF mapping |
|---|---|---|---|---|
| `snapshot` | `private(set)` | `CatalogSnapshot` | current scanned data | private-set property w/ `OnPropertyChanged`; raise change on derived collection too |
| `isLoading` | `private(set)` | `Bool` | true during a non-silent refresh | `bool IsLoading` → drives a `ProgressRing` |
| `lastRefreshedAt` | `private(set)` | `Date?` | timestamp of last successful scan | `DateTime?` |
| `sourceStatuses` | `private(set)` | `[PlatformSourceStatus]` | per-platform detection/counts | `ObservableCollection<PlatformSourceStatus>` |
| `selectedSection` | read/write | `CatalogSection` (`.all`) | active library section filter | `CatalogSection SelectedSection` (two-way bound to nav) |
| `selectedPlatformFilter` | read/write | `AgentPlatform?` | active platform filter (nil = all) | `AgentPlatform? SelectedPlatformFilter` |
| `searchText` | read/write | `String` ("") | live search query | `string SearchText` (TextBox two-way) |
| `selectedItemID` | read/write | `String?` | selected row id | `string? SelectedItemId` |
| `showInspector` | read/write | `Bool` | inspector pane visibility | `bool ShowInspector`; mirror to settings |
| `lastOperationError` | read/write | `String?` | error from CRUD ops | `string? LastOperationError` → error banner |

Init seeds `showInspector` from `AppSettings.showInspector` and `sourceStatuses` from
a detector run against an **empty** snapshot (so the UI has placeholder rows before
the first scan). There are two inits: a default one using `AppSettings.shared` and a
DI one taking `settings:`.

### Derived (computed) state — important: these are NOT cached

| Member | Computes |
|---|---|
| `filteredItems: [CatalogItem]` | `CatalogFilter.sorted(CatalogFilter.items(in: snapshot, section, platform, searchText))` — the master visible list |
| `selectedItem: CatalogItem?` | first of `snapshot.allItems` whose `id == selectedItemID` |
| `detectedPlatforms: Set<AgentPlatform>` | platforms with `isDetected` in `sourceStatuses` |
| `defaultNewSkillPlatforms: Set<AgentPlatform>` | same as detected (deliberate: never pre-check absent tools) |
| `hasAnyCatalogItems: Bool` | `!snapshot.allItems.isEmpty` |
| `count(for: section)` | items count for that section honoring current platform + search |
| `count(for: platform)` | items count for that platform honoring current section + search |
| `countAllPlatforms()` | items count for current section across all platforms + search |
| `relatedPlatforms(for:excluding:)` | `alsoAvailableOn`, or other skills with same `displayName` |

**Each of these recomputes from `snapshot` on every access** — there is no memoization.
In WPF a naive port (recompute on every binding read) is fine for these list sizes,
but the cleaner MVVM idiom is to materialize `FilteredItems` into an
`ObservableCollection<CatalogItem>` (or wrap an `ICollectionView`) and **rebuild it
whenever any of `snapshot`, `SelectedSection`, `SelectedPlatformFilter`, `SearchText`
changes**. The four `count(...)` helpers drive sidebar/segment badge numbers and must
honor *the other two* active filters — replicate exactly.

### Refresh triggers (where re-scans come from)

There are two scan entry points with different threading:

1. **`refresh(silent: Bool = false)`** — async, off-main-thread scan:
   - if not silent → set `isLoading = true`.
   - snapshot `hideBuiltInCursorSkills` / `hideSystemCodexSkills` from settings and
     capture `preserveID = selectedItemID`.
   - `Task.detached(priority: .userInitiated)` runs `DiscoveryEngine.discover(...)`
     **off the main actor**, then hops back to `MainActor.run` to assign
     `snapshot`, recompute `sourceStatuses`, set `lastRefreshedAt = Date()`, clear
     `isLoading` (if not silent), and **re-resolve selection** (below).
2. **`reloadCatalog(selecting preferredID: String?)`** — synchronous, on main thread:
   - used right after a file-mutating CRUD op (create/rename/delete/update metadata)
     so the UI updates immediately and selects the affected item.
   - assigns `snapshot`, `sourceStatuses`, `lastRefreshedAt` inline, then resolves
     selection toward `preferredID`.

**Selection-preservation algorithm (identical in both paths):**
```
if preserveID/preferredID exists AND new snapshot still contains that id  → keep it
else if filteredItems.first exists                                        → select first visible
else                                                                       → selectedItemID = nil
```

**Callers of refresh (the trigger graph), from `MainWindowView` + `skillzApp`:**
- `.onAppear`: if snapshot empty → `store.refresh()`; always `store.startWatching()`.
- `.onDisappear`: `store.stopWatching()`.
- `.onChange(of: settings.hideBuiltInCursorSkills)` → `refresh()`.
- `.onChange(of: settings.hideSystemCodexSkills)` → `refresh()`.
- `NSApplication.didBecomeActiveNotification` → `store.refreshOnBecomeActive()`
  (which calls `startWatching()` then `refresh(silent: true)`).
- Menu commands "Refresh Catalog" / "Refresh All Sources" (⌘R) → `refresh()`.
- Onboarding completion / new-skill / etc. → `reloadCatalog(...)` via CRUD methods.

### Sort order

Defined entirely by `CatalogFilter.sorted` (§5): **case-insensitive ascending by
`displayName`** across the whole filtered (heterogeneous) list. There is no secondary
key, no per-kind grouping at the list level, and no user-selectable sort. (Each
scanner *also* internally sorts its own array case-insensitively by name, but that is
overridden by the final `sorted` call on the combined filtered list.)

### CRUD / file operations on the store (context, not the focus of this spec)

`createSkill`, `renameSelectedSkill`, `deleteSelectedSkill`,
`updateSelectedSkillMetadata`, `canModifySelectedSkill` — all delegate to
`SkillFileService`, then call `reloadCatalog(selecting:)` and clear
`lastOperationError`. Plus shell/Finder helpers: `revealInFinder`,
`openInDefaultApp`, `copyPath`, `openInCursor`. These are covered in the file-service
spec; relevant here only because they are the callers of synchronous `reloadCatalog`.

### Windows / .NET mapping (store)

- `@MainActor ObservableObject` → a `CatalogViewModel : ObservableObject`
  (CommunityToolkit.Mvvm) — use `[ObservableProperty]` source generators or manual
  `SetProperty`. All mutations happen on the WPF Dispatcher thread.
- `@Published private(set)` → properties with `private set` raising `PropertyChanged`.
- `Task.detached(priority:.userInitiated)` + `MainActor.run` → `await Task.Run(() =>
  _discovery.Discover(...))` then assign on the captured UI context (in an `async`
  command the continuation resumes on the Dispatcher automatically). Set
  `IsLoading = true` before, `false` in a `finally`.
- `filteredItems` recompute → on change of `SelectedSection` /
  `SelectedPlatformFilter` / `SearchText` / snapshot, call `RebuildFilteredItems()`
  that clears+refills an `ObservableCollection<CatalogItem>`. Debounce `SearchText`
  ~150–250 ms so each keystroke doesn't re-filter (the macOS app re-filters live; a
  WPF debounce is an improvement, not a behavior change).
- Selection preservation → reimplement the exact 3-branch algorithm using
  `SelectedItemId` and the freshly computed `AllItems`/`FilteredItems`.
- `lastRefreshedAt = Date()` → `DateTime.Now` (display only — fine to use local time).
- The `count(for:)` helpers → methods/derived properties bound to the WPF-UI
  `NavigationViewItem` info badges / segmented control counters.

---

## 4. Threading & lifecycle summary

| Concern | macOS | Windows / WPF |
|---|---|---|
| Where store state lives | main actor (`@MainActor`) | WPF Dispatcher (UI thread) |
| Where scanning runs | `Task.detached` background | `Task.Run` (thread pool) |
| Hop back to UI | `await MainActor.run { … }` | `await` continuation on Dispatcher / `Dispatcher.Invoke` |
| App-instance store | `@StateObject` in `@main` App | DI singleton resolved into main `Window`'s `DataContext` |
| First scan | `.onAppear` when snapshot empty | `Window.Loaded` event → `await vm.RefreshAsync()` |
| Stop watching | `.onDisappear` | `Window.Closed` / `Closing` → `vm.StopWatching()` |

---

## 5. CatalogFilter — section × platform × search

`CatalogFilter` is a `nonisolated enum` namespace with two static functions. It is the
**single source of truth** for what is visible. Logic, in order:

### `items(in:section:platform:searchText:)`

1. **Start** with `snapshot.allItems` (all skills + mcps + plugins).
2. **Section filter** (`CatalogSection`):
   - `.all` → no filter (pass through).
   - `.skills` → keep `kind == .skill`.
   - `.mcpServers` → keep `kind == .mcp`.
   - `.plugins` → keep `kind == .plugin`.
3. **Platform filter** (optional `AgentPlatform?`): if non-nil, keep an item when
   **either**:
   - `item.platform == platform`, **OR**
   - the item is a skill and `skillItem.alsoAvailableOn.contains(platform)`.
   (So a shared skill shows under *every* harness that reads it, not just its primary.
   MCPs and plugins only match by their own `platform`.)
4. **Search filter**: trim `searchText` (`.whitespacesAndNewlines`). If non-empty,
   keep items where **any** of these four fields
   `localizedCaseInsensitiveContains(query)`:
   - `item.displayName`
   - `item.descriptionText`
   - `item.listSubtitle`
   - `item.platform.displayName`

   **Search is case-insensitive, substring (contains, not prefix/fuzzy), locale-aware,
   and trims surrounding whitespace/newlines.** Searched fields **per kind** (because
   `descriptionText`/`listSubtitle` differ by kind — see the §2 table):
   - **Skill**: name, description, parent-directory path, platform display name.
   - **MCP**: name, `endpointSummary` (url, or command+args), config file path,
     platform display name.
   - **Plugin**: name, description, `marketplace ?? pluginID`, platform display name.

### `sorted(_:)`

`items.sorted { lhs.displayName.localizedCaseInsensitiveCompare(rhs.displayName) == .orderedAscending }`
— **case-insensitive ascending by display name**, applied to the heterogeneous list
after filtering. No grouping, no secondary sort.

### Where the filter is reused

- `CatalogStore.filteredItems` (master list, section+platform+search).
- The three `count(...)` helpers (each varies one axis, fixes the other two).
- `PlatformSourceDetector.detect` calls `items(in: snapshot, section: .all,
  platform: platform).count` to get each platform's `itemCount` (no search).

### Windows / .NET mapping (filter)

- Port `items(...)` as a pure `static IEnumerable<CatalogItem> Items(CatalogSnapshot,
  CatalogSection, AgentPlatform?, string searchText = "")` using LINQ `.Where(...)`.
- **Case-insensitive contains**: Swift's `localizedCaseInsensitiveContains` is
  locale-aware. The faithful .NET equivalent is
  `culture.CompareInfo.IndexOf(field, query, CompareOptions.IgnoreCase) >= 0` using
  `CultureInfo.CurrentCulture`. A simpler, predictable choice is
  `field.Contains(query, StringComparison.OrdinalIgnoreCase)` (ordinal, not
  locale-aware) — recommended unless locale collation matters; document the choice.
- **Trim**: `searchText.Trim()` (trims whitespace incl. `\r\n` by default — matches
  `.whitespacesAndNewlines`).
- **Sort**: `.OrderBy(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase)`
  (or `OrdinalIgnoreCase` to match the contains choice — keep them consistent).
- Consider exposing the filter through an `ICollectionView` with a `Filter` predicate
  + `SortDescription`, OR rebuild an `ObservableCollection` — either is fine; the
  predicate logic must be byte-for-byte the above.

---

## 6. Path classification & shared skills (porting hazard)

Two `PlatformSkillPaths` helpers do **substring matching on POSIX paths** and feed
dedup + the platform filter. These are the highest-risk part of the port.

### `primaryPlatform(for path: URL)` — owner after dedup

Checks `path.path` against ordered substrings, first match wins:
```
"/skills-cursor/" → cursor     "/.hermes/"   → hermes
"/.openclaw/"     → openClaw    "/.pi/"       → pi
"/.cursor/"       → cursor      "/.claude/"   → claudeCode
"/.codex/"        → codex       "/.agents/"   → pi
(default)         → cursor
```

### `platformsThatShare(path: URL)` — `alsoAvailableOn` source

If the path contains `"/.agents/skills/"` (or ends with `"/.agents/skills"`):
returns `[.pi, .codex, .openClaw]`; otherwise `[]`. After dedup the primary is
removed, so a `~/.agents/skills/foo/SKILL.md` is owned by `.pi`, also-on `[.codex,
.openClaw]`.

### `skillScanRoots(for:)` — where skills are read per platform

| Platform | Roots |
|---|---|
| cursor | `~/.cursor/skills` |
| claudeCode | `~/.claude/skills` |
| codex | `~/.codex/skills`, **`~/.agents/skills`** |
| hermes | `~/.hermes/skills` |
| pi | `~/.pi/agent/skills`, **`~/.agents/skills`** |
| openClaw | `~/.openclaw/skills`, + workspace skills dir if it exists |

Note `pi`'s user dir is `~/.pi/agent/skills` (extra `agent/` segment), and codex+pi
both also scan the shared `~/.agents/skills` — which is exactly why dedup-by-path and
`platformsThatShare` exist.

### Windows mapping (critical)

- These substring checks use forward slashes. On Windows paths use `\`. **Do not
  port them as raw `Contains("/.cursor/")`** — they will never match. Instead:
  - normalize the path (`Path.GetFullPath`), split on
    `Path.DirectorySeparatorChar`, and test **segment equality** (e.g. any segment ==
    `.cursor`, `.claude`, `.agents`, or `skills-cursor`), preserving the same ordered
    first-match precedence; **or**
  - normalize separators to `/` before matching and keep the substring logic.
  Prefer segment-based matching — it is robust to case and trailing separators.
- Path equality elsewhere uses `url.standardizedFileURL` (e.g.
  `isBareHomeDirectory`). On Windows use `Path.GetFullPath` + a case-insensitive
  comparer (`StringComparer.OrdinalIgnoreCase`) since NTFS is case-insensitive.

---

## 7. PlatformSourceDetector — sourceStatuses shape

Feeds `CatalogStore.sourceStatuses` (and `detectedPlatforms`). For each platform it
builds a `PlatformSourceStatus`:

```swift
struct PlatformSourceStatus {
    let platform: AgentPlatform
    let isDetected: Bool                 // any signal with isInstallSignal == true
    let scanPaths: [URL]                 // profile.sourcePaths
    let detectionSignals: [PlatformDetectionSignal]
    let itemCount: Int                   // CatalogFilter count for this platform, section .all, no search
    let hookSupport: PlatformHookSupport // .preciseWaitingState | .processFallback
}
```

Detection signals come from three sources, in order: (1) existing files among the
profile's source paths, (2) skill roots that contain a `SKILL.md`, (3) executables
found on the search path. `isInstallSignal` excludes shared-skill sources and *bare*
home directories (an empty `~/.cursor` must not count as "installed" — otherwise the
app would auto-install hooks into a tool the user never runs). Hook support:
cursor/claudeCode/codex = precise waiting-state; hermes/pi/openClaw = process
fallback.

This is supporting context for the store's `sourceStatuses` / `detectedPlatforms`.
Full detection profiles belong in the platform-detection spec; what matters here is
that `detect(snapshot:)` is re-run on **every** assignment of `snapshot` (in both
`refresh` and `reloadCatalog`), so `itemCount` stays in sync with the catalog.

**Windows mapping:** port `PlatformSourceStatus` as a record bound into an
`ObservableCollection`. Executable detection probes `%PATH%` + per-platform `bin`
dirs for `.exe`/`.cmd`. File-existence checks → `File.Exists` / `Directory.Exists`.

---

## 8. FSEventWatcher — file watching

### Swift behavior (exact)

`final class FSEventWatcher: @unchecked Sendable` wraps a Core Services
`FSEventStreamRef`.

- **Construction**: `init(paths: [URL], debounceInterval: TimeInterval = 0.3,
  onChange: @escaping @Sendable () -> Void)`. Stores paths as POSIX strings, the
  debounce interval (**default 0.3 s = 300 ms**), and the callback. A private serial
  `DispatchQueue("com.skillz.fsevents", qos: .utility)` runs the stream + debounce.
- **`start()`**:
  - no-op if `paths` is empty; calls `stop()` first (idempotent restart).
  - Creates the stream with flags `UseCFTypes | FileEvents | NoDefer`:
    - `FileEvents` → file-level (not just directory-level) events.
    - `NoDefer` → deliver the first event immediately rather than after the latency
      window.
  - **Latency** passed to `FSEventStreamCreate` = **`0.1` s** (100 ms coalescing
    window at the FSEvents layer — *separate from* the 300 ms debounce).
  - Starts from `kFSEventStreamEventIdSinceNow` (only future events).
  - Dispatches on the serial queue; on any event → `scheduleDebouncedRefresh()`.
- **Coalescing / debounce (two layers):**
  1. FSEvents native latency `0.1 s` coalesces bursts before the C callback fires.
  2. `scheduleDebouncedRefresh()`: cancels any pending `DispatchWorkItem`, schedules
     a new one `debounceInterval` (0.3 s) out; when it fires it hops to the **main
     queue** and calls `onChange()`. Rapid events keep canceling+rescheduling, so
     `onChange` fires **once, 300 ms after the last event in a burst**.
- **`stop()`**: stop+invalidate+release the stream, null it, cancel the pending work
  item. Called from `deinit` too.
- **Recursion**: FSEvents is **recursive by default** — watching a directory watches
  its whole subtree. The watcher watches *directories* (the platform home dirs and
  shared skills dirs), and recursion covers nested `skills/<name>/SKILL.md`.

### Who creates it & what it watches (from `CatalogStore.startWatching`)

```swift
let paths = PlatformSkillPaths.watchDirectories
    .filter { FileManager.default.fileExists(atPath: $0.path) }
fsWatcher?.stop()
fsWatcher = FSEventWatcher(paths: paths) { [weak self] in
    Task { @MainActor [weak self] in self?.refresh(silent: true) }   // SILENT refresh
}
fsWatcher?.start()
```

`PlatformSkillPaths.watchDirectories` =
- **every platform home dir** (`~/.cursor`, `~/.claude`, `~/.codex`, `~/.hermes`,
  `~/.pi`, `~/.openclaw`),
- plus `~/.agents/skills` **if it exists, else** `~/.agents`,
- plus the OpenClaw workspace parent dir **if the workspace skills dir exists**.

`startWatching` then filters to **only paths that currently exist** before handing
them to the watcher. The callback always does a **silent** refresh (no spinner).

### refresh-on-app-active

Wired in `MainWindowView`:
```swift
.onReceive(NotificationCenter.default.publisher(for: NSApplication.didBecomeActiveNotification)) { _ in
    store.refreshOnBecomeActive()   // -> startWatching() then refresh(silent: true)
}
```
So when the app is reactivated it **rebuilds the watcher** (picks up newly-created
home dirs that didn't exist at launch) and does a silent rescan. The watcher is also
started on `.onAppear` and stopped on `.onDisappear`.

### Windows / .NET mapping (watcher)

`FSEvents` has no single-watcher-multiple-roots equivalent. Use **one
`System.IO.FileSystemWatcher` per existing root**, all sharing one debounce timer:

```csharp
public sealed class CatalogFileWatcher : IDisposable {
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly DispatcherTimer _debounce;   // Interval = 300 ms, Tick fires once
    private readonly Action _onChange;

    public CatalogFileWatcher(IEnumerable<string> roots, Action onChange, int debounceMs = 300) {
        _onChange = onChange;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(debounceMs) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); _onChange(); };   // back on UI thread already (DispatcherTimer)
        foreach (var root in roots.Where(Directory.Exists)) {
            var w = new FileSystemWatcher(root) {
                IncludeSubdirectories = true,                              // == FSEvents recursive default
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                             | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            w.Changed += OnFsEvent; w.Created += OnFsEvent; w.Deleted += OnFsEvent;
            w.Renamed += OnFsEvent; w.Error += OnError;
            w.EnableRaisingEvents = true;
            _watchers.Add(w);
        }
    }
    private void OnFsEvent(object s, FileSystemEventArgs e) =>
        Application.Current.Dispatcher.Invoke(() => { _debounce.Stop(); _debounce.Start(); });  // restart debounce
    // OnError: dispose + recreate that watcher (buffer overflow / dir deleted).
}
```

Mapping notes:
- **Per-root watcher** vs one FSEvents stream: build the same root list as
  `watchDirectories` (every existing platform home dir + `.agents`/`.agents\skills` +
  openclaw workspace parent), filtered to existing dirs.
- **Recursion**: `IncludeSubdirectories = true` matches the FSEvents recursive default.
- **NotifyFilters**: SKILL.md create/delete/rename/edit + nested dir changes → include
  `FileName`, `DirectoryName`, `LastWrite`, `Size`. (FSEvents `FileEvents` flag ≈
  file-level granularity.)
- **Debounce**: a single shared `DispatcherTimer` (300 ms) Stop/Start on each event →
  fires once after the last event, **on the UI thread** (so the silent refresh kick is
  marshaled correctly — `FileSystemWatcher` events arrive on a thread-pool thread,
  unlike the `DispatcherTimer.Tick`). This reproduces both the 300 ms debounce and the
  "deliver on main queue" hop. (The native 100 ms FSEvents coalescing has no direct
  analog; the 300 ms debounce already coalesces bursts — acceptable.)
- **`FileSystemWatcher.Error`** (internal buffer overflow when many events fire, or the
  watched dir is deleted): FSEvents handles this transparently; `FileSystemWatcher`
  does not. Handle `Error` by disposing+recreating that watcher and triggering a
  refresh. Optionally bump `InternalBufferSize` (e.g. 64 KB) for busy skill dirs.
- **restart semantics**: `startWatching` stops the old watcher and rebuilds from the
  current existing-dir list — port as `Dispose()` + recreate. Call this from
  `Window.Activated` (the "refresh on app active" hook) and on first `Window.Loaded`.
- **refresh-on-active** (`NSApplication.didBecomeActiveNotification`) → **`Window.Activated`**
  event: rebuild watchers (catch newly-created dotfolders) + `await
  RefreshAsync(silent: true)`. Stop on `Window.Closed`/`Deactivated` as appropriate
  (the macOS app stops on window disappear, not deactivate — match `Window.Closed`).
- **`@weak self`** capture → in C# the watcher holds an `Action`; ensure the ViewModel
  unsubscribes/disposes the watcher on close to avoid leaks (no ARC weak refs).

---

## 9. Settings inputs consumed by this subsystem

From `AppSettings` (macOS `@AppStorage` → Windows: `settings.json` / app config bound
through an `ISettingsService`):

| Setting | Default | Effect here |
|---|---|---|
| `hideBuiltInCursorSkills` | `false` | forwarded to `SkillScanner` (suppress `~/.cursor/skills-cursor`); change triggers `refresh()` |
| `hideSystemCodexSkills` | `true` | forwarded to `SkillScanner` (suppress codex `.system` skills); change triggers `refresh()` |
| `showInspector` | `false` | seeds + mirrors `CatalogStore.showInspector` |

`@AppStorage` (UserDefaults) → on Windows persist via a JSON settings file under
`%LOCALAPPDATA%\Skillz\settings.json` (or `Properties.Settings`). Toggling either
"hide" flag must re-trigger `RefreshAsync()`, exactly like the macOS `.onChange`
handlers.

---

## 10. Behavior-parity checklist (for the WPF implementation)

- [ ] `Discover` calls scanners in order skills→mcps→plugins; no cross-scanner merge.
- [ ] Skills dedup **globally by absolute SKILL.md path**; recompute primary platform
      + `alsoAvailableOn` during dedup.
- [ ] MCPs do **not** dedup (platform-qualified IDs); plugins dedup per-platform by
      install path; Codex merges config.toml enabled list with cache scan.
- [ ] `FilteredItems` = section filter → platform filter (incl. `alsoAvailableOn` for
      skills) → 4-field case-insensitive substring search → ascending name sort.
- [ ] Search trims whitespace/newlines; empty query = no search filter.
- [ ] Selection preservation: keep id if still present, else first visible, else null
      — in both async refresh and sync reload.
- [ ] `IsLoading` only set for non-silent refresh; watcher + app-active use silent.
- [ ] `SourceStatuses` recomputed on every snapshot assignment.
- [ ] One `FileSystemWatcher` per existing root, recursive, shared 300 ms debounce on
      the UI thread; rebuilt on activate; handle `Error` (overflow/deleted dir).
- [ ] Watch roots = all existing platform home dirs + `.agents`(/`skills`) + openclaw
      workspace parent.
- [ ] `Window.Activated` rebuilds watchers + silent refresh; `Window.Loaded` does
      first scan + start; `Window.Closed` stops.
- [ ] All path classification done on normalized **segments**, not `/`-substring
      `Contains`.

---

## 11. Open questions / decisions to confirm

1. **Locale-aware vs ordinal search/sort.** macOS uses
   `localizedCaseInsensitiveContains` / `localizedCaseInsensitiveCompare`. Recommend
   `OrdinalIgnoreCase` on Windows for predictability; confirm whether locale collation
   (e.g. accented chars, Turkish-i) matters for the target audience.
2. **Native 100 ms FSEvents coalescing.** No direct `FileSystemWatcher` analog; the
   300 ms debounce subsumes it. Confirm 300 ms is acceptable, or expose as config.
3. **`appearance` / theming** and other `AppSettings` fields (notch, hooks, menu bar)
   are out of scope for this catalog subsystem and live in other specs.
4. **Workspace skills dir** (`OpenClawConfig.workspaceSkillsDirectory()` /
   `workspaceDirectory()`) — referenced by `skillScanRoots`/`watchDirectories` but
   `OpenClawConfig` was not read for this spec; confirm its Windows path resolution in
   the OpenClaw/platform-paths spec.
5. **`reloadCatalog` synchronous scan on the UI thread** can briefly block on large
   catalogs. macOS accepts this for CRUD immediacy; consider `await Task.Run` + reselect
   on the WPF side if the catalog is large, while preserving the immediate-select UX.
