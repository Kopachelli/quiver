# Port Spec 01 — Data Models & Filesystem Paths

Reference for the macOS Swift source under `skillz/skillz/Models/` and `skillz/skillz/Services/`, translated to a Windows-native WPF / .NET 8 app.

**Source files documented (read in full):**

- `Models/AgentPlatform.swift` — `AgentPlatform`, `CatalogSection`, `CatalogItemKind`
- `Models/CatalogItem.swift` — `CatalogItem`
- `Models/SkillItem.swift` — `SkillFrontmatter`, `SkillItem`, `SkillMarkdownFile`
- `Models/MCPItem.swift` — `MCPTransport`, `MCPItem`
- `Models/PluginItem.swift` — `PluginItem`
- `Services/AgentPaths.swift` — `AgentPaths`
- `Services/PlatformSkillPaths.swift` — `PlatformSkillPaths`
- `Services/AgentEnvironment.swift` — `AgentEnvironment`
- `Services/OpenClawConfig.swift` — `OpenClawConfig`

**Platform translation conventions used throughout:**

| macOS / Swift / AppKit / SwiftUI | Windows / .NET 8 / WPF |
| --- | --- |
| `Foundation.URL` (file URL) | `string` absolute path (or `System.Uri`). Prefer plain `string` paths; the codebase compares `.path` everywhere, so a string-based model is simpler and avoids `file://` semantics. |
| `URL.appendingPathComponent(_:isDirectory:)` | `System.IO.Path.Combine(a, b)` |
| `URL.path` | the string itself (or `Path.GetFullPath`) |
| `URL.lastPathComponent` | `Path.GetFileName(path)` |
| `URL.deletingLastPathComponent()` | `Path.GetDirectoryName(path)` |
| `FileManager.default.homeDirectoryForCurrentUser` | `Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)` -> `C:\Users\<user>` |
| `FileManager.default.fileExists(atPath:)` | `File.Exists(path)` / `Directory.Exists(path)` |
| `FileManager.urls(for: .applicationSupportDirectory, ...)` | `Environment.GetFolderPath(SpecialFolder.ApplicationData)` -> `C:\Users\<user>\AppData\Roaming` |
| `Date` / `Date?` | `System.DateTime` / `DateTime?` (use `DateTimeOffset` for fidelity) |
| `TimeInterval` (seconds, `Double`) | `TimeSpan` (or `double` seconds) |
| `enum: String, CaseIterable, Codable` | C# `enum` + a string-mapping table, or a `[JsonConverter]`; `CaseIterable` -> `Enum.GetValues<T>()` |
| `struct ... : Equatable, Sendable` | C# `record` (value equality built in; immutable with `init`-only props) |
| Swift `enum CatalogItem` with associated values | C# discriminated union — abstract base `record` + sealed subclasses, or a tagged record. See `CatalogItem` section. |
| SF Symbol name (`symbolName`) | WPF-UI `SymbolRegular` enum value (Fluent System Icons) or Segoe Fluent Icons glyph. SF Symbol names are **not portable**; remap each. |
| Asset catalog image name (`brandIconAssetName`) | WPF resource key / `pack://application:,,,/Assets/...png` |
| `nonisolated` / `Sendable` (Swift concurrency) | No direct equivalent. Records are immutable -> inherently thread-safe. Drop these annotations. |
| `JSONSerialization` | `System.Text.Json` (`JsonDocument` / `JsonSerializer`) |

> **Path separator note:** every macOS path below uses `/`. On Windows use `\`. All the substring checks in `primaryPlatform(for:)` and `platformsThatShare(path:)` match on `/.cursor/`, `/.agents/skills/`, etc. — these MUST be rewritten to match `\` (or be made separator-agnostic). See the dedicated subsection.

---

## 1. Data Models

### 1.1 `AgentPlatform` (enum)

Swift: `enum AgentPlatform: String, CaseIterable, Identifiable, Codable, Sendable`. Six cases. Raw string values are the persisted/codable identity and are used to build catalog-item IDs (`skill:<raw>:<path>`, etc.), so **the raw strings must be preserved byte-for-byte** in the C# port.

| Case | Raw value (`rawValue` / `id`) | `displayName` | `symbolName` (SF Symbol) | `brandIconAssetName` |
| --- | --- | --- | --- | --- |
| `cursor` | `cursor` | `Cursor` | `cursorarrow.rays` | `PlatformIconCursor` |
| `claudeCode` | `claudeCode` | `Claude Code` | `bubble.left.and.bubble.right` | `PlatformIconClaudeCode` |
| `codex` | `codex` | `Codex` | `terminal` | `PlatformIconCodex` |
| `hermes` | `hermes` | `Hermes` | `bolt.fill` | `PlatformIconHermes` |
| `pi` | `pi` | `Pi` | `laptopcomputer` | `PlatformIconPi` |
| `openClaw` | `openClaw` | `OpenCode` | `antenna.radiowaves.left.and.right` | `PlatformIconOpenCode` |

> **Naming quirks to preserve exactly:**
> - The case is `openClaw` (raw value `openClaw`) but the home directory is `.openclaw` (all lowercase) and the `displayName` is `OpenCode` and the brand asset is `PlatformIconOpenCode`. Three different spellings for one platform. Keep each where it belongs.
> - `claudeCode` raw value is camelCase `claudeCode`, not `claude` or `claude-code`.

Computed members:

- `id: String { rawValue }` -> C#: `Id => RawValue`.
- `homeDirectory: URL` -> delegates to `AgentPaths.environment.homeDirectory(for: self)`. See §2 for the resolved value per platform.
- `userSkillsDirectory: URL` — user-writable skills folder for **creating** new skills:
  - `cursor`, `claudeCode`, `codex`, `hermes`, `openClaw` -> `homeDirectory + "skills"`
  - `pi` -> `homeDirectory + "agent/skills"` (note nested `agent/` segment — on Windows `agent\skills`)
- `static agentsDirectory: URL` -> `AgentPaths.environment.homeDirectory + ".agents"` (the shared `~/.agents` root, **not** under any per-platform home).

**Windows/.NET mapping:**

```csharp
public enum AgentPlatform { Cursor, ClaudeCode, Codex, Hermes, Pi, OpenClaw }
```

Provide an extension/lookup class `AgentPlatformInfo` exposing: `RawValue` (must equal the Swift raw strings: `"cursor"`, `"claudeCode"`, `"codex"`, `"hermes"`, `"pi"`, `"openClaw"`), `DisplayName`, `IconGlyph` (WPF-UI `SymbolRegular`), `BrandIconResourceKey`, `HomeDirectory`, `UserSkillsDirectory`. `CaseIterable` -> `Enum.GetValues<AgentPlatform>()`. Implement `IconGlyph` by remapping SF Symbols to WPF-UI Fluent icons (suggested: Cursor->`Cursor24`, ClaudeCode->`Chat24`, Codex->`WindowConsole20`/`Code24`, Hermes->`Flash24`, Pi->`Laptop24`, OpenClaw->`LiveStream24`/`Rss24` — the exact glyph is a UI decision, not load-bearing).

### 1.2 `CatalogSection` (enum)

Swift: `enum CatalogSection: String, CaseIterable, Identifiable, Sendable`. Drives the sidebar / filter tabs.

| Case | Raw value | `displayName` | `symbolName` |
| --- | --- | --- | --- |
| `all` | `all` | `All Items` | `square.grid.2x2` |
| `skills` | `skills` | `Skills` | `sparkles` |
| `mcpServers` | `mcpServers` | `MCP Servers` | `server.rack` |
| `plugins` | `plugins` | `Plugins` | `puzzlepiece.extension` |

**Windows/.NET:** `enum CatalogSection { All, Skills, McpServers, Plugins }` + info table. Icon remap suggestion: All->`Grid24`, Skills->`Sparkle24`, McpServers->`Server24`, Plugins->`PuzzlePiece24`.

### 1.3 `CatalogItemKind` (enum)

Swift: `enum CatalogItemKind: String, Sendable`. No `CaseIterable`/`Identifiable`.

| Case | Raw value | `displayName` |
| --- | --- | --- |
| `skill` | `skill` | `Skill` |
| `mcp` | `mcp` | `MCP Server` |
| `plugin` | `plugin` | `Plugin` |

**Windows/.NET:** `enum CatalogItemKind { Skill, Mcp, Plugin }`.

### 1.4 `CatalogItem` (discriminated union)

Swift: `enum CatalogItem: Identifiable, Equatable, Sendable` with three associated-value cases:

```
case skill(SkillItem)
case mcp(MCPItem)
case plugin(PluginItem)
```

Computed properties (all switch over the case):

| Member | Type | Behavior |
| --- | --- | --- |
| `id` | `String` | underlying item's `id` |
| `kind` | `CatalogItemKind` | `.skill` / `.mcp` / `.plugin` |
| `platform` | `AgentPlatform` | underlying item's `platform` |
| `displayName` | `String` | skill -> `item.displayName`; mcp -> `item.name`; plugin -> `item.displayName` |
| `descriptionText` | `String` | skill -> `item.description`; mcp -> `item.endpointSummary`; plugin -> `item.description` |
| `listSubtitle` | `String` | skill -> `item.listSubtitle`; mcp -> `item.configFileURL.path`; plugin -> `item.listSubtitle` |
| `modifiedAt` | `Date?` | underlying item's `modifiedAt` |
| `symbolName` | `String` | by `kind`: skill->`sparkles`, mcp->`server.rack`, plugin->`puzzlepiece.extension` |
| `skillItem` | `SkillItem?` | non-nil only for `.skill` |
| `mcpItem` | `MCPItem?` | non-nil only for `.mcp` |
| `pluginItem` | `PluginItem?` | non-nil only for `.plugin` |

**Windows/.NET — discriminated union pattern.** C# has no native sum type. Two viable patterns:

1. **Abstract base record + sealed subclasses (recommended).** Define `abstract record CatalogItem` exposing all the common projected members as abstract/virtual props (`Id`, `Kind`, `Platform`, `DisplayName`, `DescriptionText`, `ListSubtitle`, `ModifiedAt`, `IconGlyph`), with `SkillCatalogItem`, `McpCatalogItem`, `PluginCatalogItem` wrapping the respective domain record. `skillItem`/`mcpItem`/`pluginItem` accessors become `as`-pattern matches or `is` checks. This binds cleanly to a WPF `ItemsControl`/`ListView` via `DataTemplate` selection by concrete type.
2. **Tagged record** with a `Kind` discriminator and nullable `Skill`/`Mcp`/`Plugin` payloads. Simpler but loses exhaustiveness.

Equality: Swift `Equatable` -> C# `record` value equality (covers wrapped item). `IconGlyph` derives from `Kind` (Skill->`Sparkle24`, Mcp->`Server24`, Plugin->`PuzzlePiece24`).

### 1.5 `SkillFrontmatter` (struct)

Swift: `struct SkillFrontmatter: Equatable, Sendable`. Parsed from a skill markdown file's YAML frontmatter. All fields optional.

| Field | Type | Notes |
| --- | --- | --- |
| `name` | `String?` | frontmatter `name` |
| `description` | `String?` | frontmatter `description` |
| `version` | `String?` | frontmatter `version` |
| `disableModelInvocation` | `Bool?` | frontmatter flag controlling auto-invocation |

**Windows/.NET:** `record SkillFrontmatter(string? Name, string? Description, string? Version, bool? DisableModelInvocation);` Parse YAML with `YamlDotNet` (the frontmatter block between leading `---` fences). All mutable in Swift (`var`) but treat as immutable parse output in C#.

### 1.6 `SkillItem` (struct)

Swift: `struct SkillItem: Identifiable, Equatable, Sendable`. All stored properties are `let` (immutable).

| Field | Type | Notes |
| --- | --- | --- |
| `id` | `String` | built via `makeID` |
| `platform` | `AgentPlatform` | owning/primary platform |
| `skillPath` | `URL` | path to the skill's primary markdown file (e.g. `SKILL.md`) |
| `rootDirectory` | `URL` | the skill's containing directory |
| `displayName` | `String` | resolved name (frontmatter or folder name) |
| `description` | `String` | resolved description |
| `version` | `String?` | resolved version |
| `isBuiltIn` | `Bool` | bundled/built-in skill vs user skill |
| `isPluginEmbedded` | `Bool` | skill that lives inside a plugin |
| `frontmatter` | `SkillFrontmatter` | parsed frontmatter |
| `modifiedAt` | `Date?` | file mtime |
| `alsoAvailableOn` | `[AgentPlatform]` | other harnesses that read the same `skillPath` (shared `~/.agents/skills`); excludes the primary platform |

Computed / static members:

- `listSubtitle: String` -> `skillPath.deletingLastPathComponent().path` (the parent directory path). C#: `Path.GetDirectoryName(SkillPath)`.
- `hasSharedAvailability: Bool` -> `!alsoAvailableOn.isEmpty`. C#: `AlsoAvailableOn.Count > 0`.
- `static makeID(platform:path:) -> String` returns `"skill:\(platform.rawValue):\(path.path)"`.
  - C#: `$"skill:{platform.RawValue()}:{path}"`. **The `path` here is the full string path** (`URL.path`). On Windows the embedded path uses `\`; that is fine as long as ID generation and any later comparison use the same form consistently.

**Windows/.NET:**

```csharp
public record SkillItem(
    string Id,
    AgentPlatform Platform,
    string SkillPath,
    string RootDirectory,
    string DisplayName,
    string Description,
    string? Version,
    bool IsBuiltIn,
    bool IsPluginEmbedded,
    SkillFrontmatter Frontmatter,
    DateTime? ModifiedAt,
    IReadOnlyList<AgentPlatform> AlsoAvailableOn)
{
    public string ListSubtitle => Path.GetDirectoryName(SkillPath) ?? SkillPath;
    public bool HasSharedAvailability => AlsoAvailableOn.Count > 0;
    public static string MakeId(AgentPlatform p, string path) => $"skill:{p.RawValue()}:{path}";
}
```

> Note `[AgentPlatform]` value-equality: Swift array equality is element-wise. C# `record` does **not** give structural equality for `IReadOnlyList<>` by default — if catalog dedup relies on `SkillItem` equality, supply a custom `Equals`/`GetHashCode` or use an equatable collection wrapper. The macOS dedup keys off `id` (which encodes platform+path), so equality of `alsoAvailableOn` is usually not load-bearing for identity, but is for `Equatable` conformance / change detection in the UI.

### 1.7 `SkillMarkdownFile` (struct)

Swift: `struct SkillMarkdownFile: Identifiable, Equatable, Sendable`. Represents one markdown file belonging to a skill (a skill folder may contain several `.md` files; one is primary).

| Field | Type | Notes |
| --- | --- | --- |
| `id` | `String` | `url.path` |
| `url` | `URL` | file URL |
| `displayName` | `String` | `url.lastPathComponent` (filename) |
| `isPrimary` | `Bool` | whether this is the skill's primary file |

Custom init: `init(url:isPrimary: Bool = false)` sets `id = url.path`, `displayName = url.lastPathComponent`.

**Windows/.NET:**

```csharp
public record SkillMarkdownFile
{
    public string Id { get; }
    public string Url { get; }          // absolute path
    public string DisplayName { get; }
    public bool IsPrimary { get; }
    public SkillMarkdownFile(string url, bool isPrimary = false)
    { Url = url; Id = url; DisplayName = Path.GetFileName(url); IsPrimary = isPrimary; }
}
```

### 1.8 `MCPTransport` (enum)

Swift: `enum MCPTransport: String, Sendable`.

| Case | Raw value | `transportLabel` (via `MCPItem`) |
| --- | --- | --- |
| `stdio` | `stdio` | `stdio` |
| `http` | `http` | `HTTP` |
| `unknown` | `unknown` | `Unknown` |

**Windows/.NET:** `enum McpTransport { Stdio, Http, Unknown }`.

### 1.9 `MCPItem` (struct)

Swift: `struct MCPItem: Identifiable, Equatable, Sendable`. One MCP server entry parsed from a platform's MCP config file.

| Field | Type | Notes |
| --- | --- | --- |
| `id` | `String` | built via `makeID` |
| `platform` | `AgentPlatform` | owning platform |
| `name` | `String` | server name (the config key) |
| `transport` | `MCPTransport` | stdio / http / unknown |
| `command` | `String?` | executable for stdio transport |
| `args` | `[String]` | command args |
| `url` | `String?` | endpoint URL for http transport |
| `envKeys` | `[String]` | names of env vars passed (keys only, not values) |
| `configFileURL` | `URL` | the config file this entry came from |
| `modifiedAt` | `Date?` | config file mtime |

Computed / static members:

- `transportLabel: String` — see table above.
- `endpointSummary: String` — if `url` non-empty -> `url`; else if `command` -> `command + (args joined by " ")`; else `"—"` (em dash U+2014). Used as `CatalogItem.descriptionText` for MCP items.
- `static makeID(platform:name:) -> String` returns `"mcp:\(platform.rawValue):\(name)"`.

**Windows/.NET:**

```csharp
public record McpItem(
    string Id, AgentPlatform Platform, string Name, McpTransport Transport,
    string? Command, IReadOnlyList<string> Args, string? Url,
    IReadOnlyList<string> EnvKeys, string ConfigFileUrl, DateTime? ModifiedAt)
{
    public string TransportLabel => Transport switch
        { McpTransport.Stdio => "stdio", McpTransport.Http => "HTTP", _ => "Unknown" };
    public string EndpointSummary =>
        !string.IsNullOrEmpty(Url) ? Url! :
        Command is not null ? Command + (Args.Count == 0 ? "" : " " + string.Join(" ", Args)) : "—";
    public static string MakeId(AgentPlatform p, string name) => $"mcp:{p.RawValue()}:{name}";
}
```

(Same `IReadOnlyList` structural-equality caveat as `SkillItem`.)

### 1.10 `PluginItem` (struct)

Swift: `struct PluginItem: Identifiable, Equatable, Sendable`. One installed plugin.

| Field | Type | Notes |
| --- | --- | --- |
| `id` | `String` | built via `makeID` |
| `platform` | `AgentPlatform` | owning platform |
| `pluginID` | `String` | plugin identifier (note camelCase `pluginID`) |
| `displayName` | `String` | resolved name |
| `description` | `String` | resolved description |
| `version` | `String?` | plugin version |
| `marketplace` | `String?` | marketplace/source name |
| `isEnabled` | `Bool` | enabled flag |
| `installPath` | `URL?` | install directory (optional) |
| `metadataPath` | `URL?` | metadata file path (optional) |
| `skillCount` | `Int` | number of skills the plugin embeds |
| `modifiedAt` | `Date?` | mtime |

Computed / static members:

- `listSubtitle: String` -> `marketplace ?? pluginID`.
- `static makeID(platform:pluginID:installPath:) -> String`:
  - `let path = installPath?.path ?? pluginID`
  - returns `"plugin:\(platform.rawValue):\(pluginID):\(path)"`.

**Windows/.NET:**

```csharp
public record PluginItem(
    string Id, AgentPlatform Platform, string PluginId, string DisplayName,
    string Description, string? Version, string? Marketplace, bool IsEnabled,
    string? InstallPath, string? MetadataPath, int SkillCount, DateTime? ModifiedAt)
{
    public string ListSubtitle => Marketplace ?? PluginId;
    public static string MakeId(AgentPlatform p, string pluginId, string? installPath)
        => $"plugin:{p.RawValue()}:{pluginId}:{installPath ?? pluginId}";
}
```

---

## 2. Filesystem Path Resolution

### 2.1 `AgentEnvironment` (struct)

Swift: `struct AgentEnvironment: Sendable` — injectable environment that abstracts the home directory and a handful of derived roots, so tests can run against a temp root. `AgentPaths` holds a mutable static `environment` (defaults to `.live`). All path resolution funnels through here.

Stored fields:

| Field | Type | `.live` value (macOS) | Windows equivalent |
| --- | --- | --- | --- |
| `homeDirectory` | `URL` | `FileManager.default.homeDirectoryForCurrentUser` -> `/Users/<user>` | `Environment.GetFolderPath(SpecialFolder.UserProfile)` -> `C:\Users\<user>` |
| `applicationSupportDirectory` | `URL` | `~/Library/Application Support/Skillz` | **See note below** — `C:\Users\<user>\AppData\Roaming\Skillz` |
| `skillzHomeDirectory` | `URL` | `~/.skillz` | `C:\Users\<user>\.skillz` |
| `executableSearchDirectories` | `[URL]` | see below | see below |

`.live.executableSearchDirectories` (macOS): `~/.local/bin`, `~/.opencode/bin`, `~/.hermes/bin`, `/opt/homebrew/bin`, `/usr/local/bin`, `/usr/bin`.

- Windows: the absolute Homebrew/Unix bin dirs do not apply. Replace with the platform-appropriate search list: `C:\Users\<user>\.local\bin`, `C:\Users\<user>\.opencode\bin`, `C:\Users\<user>\.hermes\bin`, plus `%LOCALAPPDATA%\Microsoft\WindowsApps`, `%ProgramFiles%`, and the directories on `PATH`. This list is used for resolving CLI executables (e.g. to launch a harness); exact contents are a runtime concern, not load-bearing for the model layer.

`homeDirectory(for: platform) -> URL` — **the core per-platform home mapping**:

| Platform | macOS home dir | Windows home dir (under `C:\Users\<user>`) |
| --- | --- | --- |
| `cursor` | `~/.cursor` | `C:\Users\<user>\.cursor` |
| `claudeCode` | `~/.claude` | `C:\Users\<user>\.claude` |
| `codex` | `~/.codex` | `C:\Users\<user>\.codex` |
| `hermes` | `~/.hermes` | `C:\Users\<user>\.hermes` |
| `pi` | `~/.pi` | `C:\Users\<user>\.pi` |
| `openClaw` | `~/.openclaw` | `C:\Users\<user>\.openclaw` |

> **Application Support translation decision.** macOS uses `~/Library/Application Support/Skillz` for app state (the agent-state JSON, notify script home is separate). On Windows the idiomatic location is `Environment.GetFolderPath(SpecialFolder.ApplicationData)` = `C:\Users\<user>\AppData\Roaming`, so `applicationSupportDirectory` -> `C:\Users\<user>\AppData\Roaming\Skillz`. (`LocalApplicationData` = `...\AppData\Local` is an alternative for machine-local, non-roaming state; Roaming matches the macOS "Application Support" intent more closely.) This directory is also one of the file-watch roots (§2.5).

`temporary(root:)` factory (test-only): re-roots everything under a passed-in directory (`root/Library/Application Support/Skillz`, `root/.skillz`, `root/.local/bin`, ...). For the WPF port, expose `AgentEnvironment` as an injectable service (DI) with a `Live` default and a test constructor that takes a root path — same pattern.

**Windows/.NET mapping:**

```csharp
public sealed record AgentEnvironment(
    string HomeDirectory, string ApplicationSupportDirectory,
    string SkillzHomeDirectory, IReadOnlyList<string> ExecutableSearchDirectories)
{
    public static AgentEnvironment Live { get { /* GetFolderPath(UserProfile), etc. */ } }
    public string HomeDirectoryFor(AgentPlatform p) => p switch {
        AgentPlatform.Cursor     => Path.Combine(HomeDirectory, ".cursor"),
        AgentPlatform.ClaudeCode => Path.Combine(HomeDirectory, ".claude"),
        AgentPlatform.Codex      => Path.Combine(HomeDirectory, ".codex"),
        AgentPlatform.Hermes     => Path.Combine(HomeDirectory, ".hermes"),
        AgentPlatform.Pi         => Path.Combine(HomeDirectory, ".pi"),
        AgentPlatform.OpenClaw   => Path.Combine(HomeDirectory, ".openclaw"),
        _ => throw new ArgumentOutOfRangeException(nameof(p)) };
}
```

In macOS the mutable `AgentPaths.environment` static is `nonisolated(unsafe)`. In WPF register `AgentEnvironment` as a singleton in the DI container; do not use a mutable global.

### 2.2 `AgentPaths` (enum used as namespace)

Swift: `enum AgentPaths` (no cases — a namespace for statics). Constants and derived paths.

Constants:

| Member | Type | Value | Windows |
| --- | --- | --- | --- |
| `stateFileVersion` | `Int` | `1` | `const int = 1` |
| `staleWorkingInterval` | `TimeInterval` | `90` (sec) | `TimeSpan.FromSeconds(90)` |
| `staleNeedsInputInterval` | `TimeInterval` | `60*60` = `3600` (sec) | `TimeSpan.FromHours(1)` |
| `staleIdleInterval` | `TimeInterval` | `8` (sec) | `TimeSpan.FromSeconds(8)` |
| `environment` | `AgentEnvironment` | `.live` (mutable static) | DI singleton |

Derived paths (all relative to `environment`):

| Member | macOS resolved path | Windows resolved path |
| --- | --- | --- |
| `applicationSupportDirectory` | `~/Library/Application Support/Skillz` | `C:\Users\<user>\AppData\Roaming\Skillz` |
| `agentStateFileURL` | `…/Skillz/agent-state.json` | `C:\Users\<user>\AppData\Roaming\Skillz\agent-state.json` |
| `skillzHomeDirectory` | `~/.skillz` | `C:\Users\<user>\.skillz` |
| `notifyScriptInstalledURL` | `~/.skillz/bin/skillz-agent-notify.sh` | `C:\Users\<user>\.skillz\bin\skillz-agent-notify.sh` — **shell-script-specific; see note** |
| `claudeSessionsDirectory` | `~/.claude/sessions` | `C:\Users\<user>\.claude\sessions` |
| `codexSessionsDirectory` | `~/.codex/sessions` | `C:\Users\<user>\.codex\sessions` |
| `codexChatProcessesFile` | `~/.codex/process_manager/chat_processes.json` | `C:\Users\<user>\.codex\process_manager\chat_processes.json` |
| `cursorProjectsDirectory` | `~/.cursor/projects` | `C:\Users\<user>\.cursor\projects` |

> **`notifyScriptInstalledURL` translation.** This is a `.sh` notify hook installed into `~/.skillz/bin`. On Windows a `.sh` will not run under cmd/PowerShell without a POSIX shell. For the port this becomes a `.ps1`/`.cmd`/`.bat` (e.g. `skillz-agent-notify.ps1`) OR is dropped if the notify-hook integration is out of scope for the models/paths milestone. Flagged as an open question.

`watchPathsForAgents() -> [URL]` — returns the subset of these paths that should be file-watched (used by the agent-state watcher, distinct from skill-file watching in §2.5). Candidate list:

```
applicationSupportDirectory
claudeSessionsDirectory
codexSessionsDirectory
codexChatProcessesFile
cursorProjectsDirectory
```

Filter rule (preserve exactly):

- For `chat_processes.json` (matched by `lastPathComponent == "chat_processes.json"`): include only if **the file itself exists** (`File.Exists`).
- For every other path: include if **its parent directory exists** OR the path itself exists. I.e. `Directory.Exists(Path.GetDirectoryName(p)) || File.Exists(p) || Directory.Exists(p)`.

**Windows/.NET:** `static class AgentPaths` with the constants and derived-path properties off the injected `AgentEnvironment`. `watchPathsForAgents()` returns the filtered list; feed into a `FileSystemWatcher` per directory (see §2.5 watcher note).

### 2.3 `OpenClawConfig` — workspace resolution

Swift: `enum OpenClawConfig`. OpenClaw (raw value `openClaw`, home `~/.openclaw`) supports a configurable workspace whose `skills/` folder is an **additional** scan root.

- `configURL` -> `~/.openclaw/openclaw.json` (Windows: `C:\Users\<user>\.openclaw\openclaw.json`).
- `workspaceDirectory() -> URL` — reads `openclaw.json` and resolves the workspace:
  - JSON path: `json["agents"]["defaults"]["workspace"]` (a `String`).
  - **Fallback** (file missing, unreadable, malformed JSON, missing key, or empty string): `~/.openclaw/workspace` (`C:\Users\<user>\.openclaw\workspace`).
  - If the value starts with `~/`: expand to `home + value.dropFirst(1)` (i.e. drop the `~`, keep the `/…` -> `home + "/…"`). **Windows:** drop the `~`, then the remainder is a POSIX-style relative tail — `Path.Combine(home, remainder.TrimStart('/').Replace('/', '\\'))`, or normalize separators.
  - If the value starts with `/` (absolute POSIX): use as-is. **Windows:** a rooted Windows path check is `Path.IsPathRooted(value)` (handles `C:\…` and UNC). Decide whether to also accept POSIX-absolute values written by a cross-platform config; safest is `Path.IsPathRooted`.
  - Otherwise (relative): `~/.openclaw + value` (resolve relative to the openClaw home).
- `workspaceSkillsDirectory() -> URL` -> `workspaceDirectory() + "skills"`.

**Windows/.NET:** parse with `System.Text.Json` (`JsonDocument.Parse`); navigate `agents` -> `defaults` -> `workspace`. Wrap reads in try/catch and apply the fallback on any failure (mirrors the Swift `guard let … else` returning the default). The `~/` and absolute-path branches must be Windows-aware as above.

### 2.4 `PlatformSkillPaths` — scan roots, sharing, dedup

Swift: `enum PlatformSkillPaths`. The heart of skill discovery and the shared `~/.agents/skills` behavior.

- `agentsSkillsDirectory: URL` -> `AgentPlatform.agentsDirectory + "skills"` = `~/.agents/skills` (Windows: `C:\Users\<user>\.agents\skills`). This is the **shared** skills folder read by multiple harnesses.

#### `skillScanRoots(for: platform) -> [URL]` — full table

The directories scanned to discover skills for each platform. (`userSkillsDirectory` values come from §1.1.)

| Platform | Scan roots (macOS) | Scan roots (Windows under `C:\Users\<user>`) |
| --- | --- | --- |
| `cursor` | `~/.cursor/skills` | `.cursor\skills` |
| `claudeCode` | `~/.claude/skills` | `.claude\skills` |
| `codex` | `~/.codex/skills`, **`~/.agents/skills`** | `.codex\skills`, `.agents\skills` |
| `hermes` | `~/.hermes/skills` | `.hermes\skills` |
| `pi` | `~/.pi/agent/skills`, **`~/.agents/skills`** | `.pi\agent\skills`, `.agents\skills` |
| `openClaw` | `~/.openclaw/skills`, **+ workspace skills** (`OpenClawConfig.workspaceSkillsDirectory()` if it exists on disk) | `.openclaw\skills`, + resolved workspace `skills\` if it exists |

Notes:
- `codex` and `pi` both additionally scan the shared `~/.agents/skills`. (`openClaw` does **not** scan `~/.agents/skills`; instead it conditionally adds its workspace skills dir.)
- `pi`'s own user dir is the nested `~/.pi/agent/skills` (the `userSkillsDirectory` for `pi`).
- `openClaw` workspace root is appended **only if `Directory.Exists`** at scan time.

#### `platformsThatShare(path:) -> [AgentPlatform]`

> Returns the harnesses that read the same on-disk skill file as the given path **(excluding the primary platform)**. Used to populate `SkillItem.alsoAvailableOn`.

Exact logic:

```swift
let pathString = path.path
guard pathString.contains("/.agents/skills/") || pathString.hasSuffix("/.agents/skills") else {
    return []
}
return [.pi, .codex, .openClaw]
```

- If the path is inside (or is) the shared `~/.agents/skills` directory -> returns `[.pi, .codex, .openClaw]`. Otherwise empty.
- **Quirk:** the returned set is the literal `[.pi, .codex, .openClaw]`, even though `skillScanRoots` only adds `~/.agents/skills` to `pi` and `codex` (not `openClaw`). This is an asymmetry in the source — `openClaw` is listed as a sharer here but does not actually scan `~/.agents/skills` in `skillScanRoots`. Preserve the literal list as-is (do **not** "fix" it to match scan roots) unless product explicitly decides otherwise — flagged as an open question.
- The caller is responsible for removing the primary platform from this list before assigning to `alsoAvailableOn` (the doc comment says "excluding the primary platform"; the function itself returns the full trio).

**Windows translation of the substring checks:** the literals `"/.agents/skills/"` and `"/.agents/skills"` use `/`. On Windows paths use `\`. Rewrite to match `\.agents\skills\` / suffix `\.agents\skills`, OR normalize the path separators to `/` before comparing (e.g. `path.Replace('\\', '/')`) and keep the original literals. **Recommended:** normalize-to-`/` once at the top of each matcher, so all the `Contains("/.x/")` checks port verbatim. This single decision covers both `platformsThatShare` and `primaryPlatform`.

#### `primaryPlatform(for: path) -> AgentPlatform`

> Which platform "owns" a skill path for display after deduplication. First match wins (ordered).

Exact ordered logic (preserve order — earlier checks win):

| # | Substring in path | -> Platform |
| --- | --- | --- |
| 1 | `/skills-cursor/` | `.cursor` |
| 2 | `/.hermes/` | `.hermes` |
| 3 | `/.openclaw/` | `.openClaw` |
| 4 | `/.pi/` | `.pi` |
| 5 | `/.cursor/` | `.cursor` |
| 6 | `/.claude/` | `.claudeCode` |
| 7 | `/.codex/` | `.codex` |
| 8 | `/.agents/` | `.pi` |
| — | (no match) | `.cursor` (default) |

Notes:
- Order matters: `/skills-cursor/` is checked before `/.cursor/`; a path containing both resolves to cursor either way, but the special `skills-cursor` marketplace folder is matched first.
- `/.agents/` (the shared dir) resolves its **primary** owner to `.pi` (consistent with `pi` being the canonical owner of `~/.agents/skills` for display). The other sharers (`codex`, `openClaw`) come from `platformsThatShare`/`alsoAvailableOn`.
- Default when nothing matches: `.cursor`.
- `platformFor(path:)` is a thin alias: `return primaryPlatform(for: path)`.

**Windows/.NET:**

```csharp
public static AgentPlatform PrimaryPlatform(string path)
{
    var p = path.Replace('\\', '/');           // normalize once
    if (p.Contains("/skills-cursor/")) return AgentPlatform.Cursor;
    if (p.Contains("/.hermes/"))       return AgentPlatform.Hermes;
    if (p.Contains("/.openclaw/"))     return AgentPlatform.OpenClaw;
    if (p.Contains("/.pi/"))           return AgentPlatform.Pi;
    if (p.Contains("/.cursor/"))       return AgentPlatform.Cursor;
    if (p.Contains("/.claude/"))       return AgentPlatform.ClaudeCode;
    if (p.Contains("/.codex/"))        return AgentPlatform.Codex;
    if (p.Contains("/.agents/"))       return AgentPlatform.Pi;
    return AgentPlatform.Cursor;
}
```

> Use ordinal, case-sensitive `Contains` (`StringComparison.Ordinal`). macOS substring matching is case-sensitive on `.path`; Windows paths are case-insensitive at the FS level but the dotfolder names are conventionally lowercase, so ordinal match is correct and matches Swift behavior. Do not switch to `OrdinalIgnoreCase` without a reason.

#### Dedup / primary-platform rules (summary)

The discovery pipeline (consumer of these helpers, not shown in these files) works as:
1. For each platform, scan its `skillScanRoots`, find skill files.
2. The same physical file under `~/.agents/skills` will be discovered by multiple platforms (`pi`, `codex`). To avoid duplicate catalog entries, **dedup by physical path**: `primaryPlatform(for: path)` decides the single owning `SkillItem.platform`, and `platformsThatShare(path:)` (minus the primary) populates `alsoAvailableOn`.
3. `SkillItem.id` (`skill:<platform.rawValue>:<path>`) makes the owner-platform + path the identity. `SkillItem.hasSharedAvailability` lights up the "also available on" UI badge.

### 2.5 `PlatformSkillPaths.watchDirectories` — skill-file watch roots

Swift static computed:

```swift
static var watchDirectories: [URL] {
    var paths = AgentPlatform.allCases.map(\.homeDirectory)   // all 6 platform homes
    if FileManager.default.fileExists(atPath: agentsSkillsDirectory.path) {
        paths.append(agentsSkillsDirectory)                  // ~/.agents/skills if it exists…
    } else {
        paths.append(AgentPlatform.agentsDirectory)          // …else ~/.agents
    }
    let workspaceSkills = OpenClawConfig.workspaceSkillsDirectory()
    if FileManager.default.fileExists(atPath: workspaceSkills.path) {
        paths.append(workspaceSkills.deletingLastPathComponent())  // openClaw workspace root (parent of skills/)
    }
    return paths
}
```

Resolved watch directory list (Windows under `C:\Users\<user>`):

| # | Directory | Condition |
| --- | --- | --- |
| 1 | `.cursor` | always (all platform homes) |
| 2 | `.claude` | always |
| 3 | `.codex` | always |
| 4 | `.hermes` | always |
| 5 | `.pi` | always |
| 6 | `.openclaw` | always |
| 7 | `.agents\skills` **or** `.agents` | if `.agents\skills` exists -> watch it; else watch `.agents` |
| 8 | `<openClaw workspace>` (parent of the workspace `skills\`) | only if the workspace `skills\` dir exists |

Notes:
- Watches the six **platform home roots** (not the `skills/` subfolders) — so it catches new `skills/` dirs appearing, plus everything else under each home. A recursive watch is implied (skills live in nested `skills/` subfolders).
- The `~/.agents` entry degrades gracefully: watch the concrete `skills/` if present, otherwise the parent so creation of `skills/` is observed.
- The openClaw workspace entry adds the **parent** of `workspace/skills` (i.e. the workspace root itself) so the appearance of `skills/` is observed.

**Windows/.NET — `FileSystemWatcher`.** macOS uses an FSEvents/DispatchSource-style watcher (whatever the consumer wires up). On Windows create one `System.IO.FileSystemWatcher` per directory in this list with `IncludeSubdirectories = true`, `NotifyFilter = FileName | DirectoryName | LastWrite`, and `EnableRaisingEvents = true`. Coalesce/debounce events (FSW fires multiple per save) on a `DispatcherTimer` before rescanning, and guard against the watched root not existing yet (recreate the watcher when the home dir appears). `deletingLastPathComponent()` -> `Path.GetDirectoryName`. Watch the directory, not individual files, except the `chat_processes.json` agent-state case in §2.2 which is keyed on the file.

---

## 3. Consolidated Windows Path Table (all 6 platforms)

Base = `C:\Users\<user>` (`Environment.GetFolderPath(SpecialFolder.UserProfile)`). `+wsExists` means appended only when that directory exists on disk at scan/watch time.

| Platform | Home dir | User-skills dir (create target) | Skill scan roots | Watch root |
| --- | --- | --- | --- | --- |
| Cursor | `.cursor` | `.cursor\skills` | `.cursor\skills` | `.cursor` |
| Claude Code | `.claude` | `.claude\skills` | `.claude\skills` | `.claude` |
| Codex | `.codex` | `.codex\skills` | `.codex\skills`, `.agents\skills` | `.codex` |
| Hermes | `.hermes` | `.hermes\skills` | `.hermes\skills` | `.hermes` |
| Pi | `.pi` | `.pi\agent\skills` | `.pi\agent\skills`, `.agents\skills` | `.pi` |
| OpenClaw | `.openclaw` | `.openclaw\skills` | `.openclaw\skills`, `<workspace>\skills` (+wsExists) | `.openclaw` |

Shared / app-level (not per-platform):

| Purpose | Windows path |
| --- | --- |
| Shared agents root | `C:\Users\<user>\.agents` |
| Shared skills (read by Codex + Pi; primary owner = Pi) | `C:\Users\<user>\.agents\skills` |
| OpenClaw config | `C:\Users\<user>\.openclaw\openclaw.json` |
| OpenClaw default workspace | `C:\Users\<user>\.openclaw\workspace` |
| OpenClaw workspace skills | `<resolved workspace>\skills` |
| App support / state dir | `C:\Users\<user>\AppData\Roaming\Skillz` |
| Agent state file | `C:\Users\<user>\AppData\Roaming\Skillz\agent-state.json` |
| Skillz home | `C:\Users\<user>\.skillz` |
| Notify script (was `.sh`) | `C:\Users\<user>\.skillz\bin\skillz-agent-notify.(ps1\|cmd)` |
| Claude sessions | `C:\Users\<user>\.claude\sessions` |
| Codex sessions | `C:\Users\<user>\.codex\sessions` |
| Codex chat-processes file | `C:\Users\<user>\.codex\process_manager\chat_processes.json` |
| Cursor projects | `C:\Users\<user>\.cursor\projects` |

Agent-state watch set (`AgentPaths.watchPathsForAgents`, existence-filtered): App-support dir, Claude sessions, Codex sessions, Codex `chat_processes.json` (file must exist), Cursor projects.

Skill-file watch set (`PlatformSkillPaths.watchDirectories`): all 6 platform homes + (`.agents\skills` or `.agents`) + openClaw workspace root (if its `skills\` exists).

---

## 4. Open Questions / Decisions for the Port

1. **`platformsThatShare` lists `openClaw` but `skillScanRoots` does not give openClaw the `~/.agents/skills` root.** Asymmetry in the source. Preserve literally, or reconcile? (Recommend: preserve literal `[.pi, .codex, .openClaw]` to match macOS behavior exactly; revisit if it produces phantom "also available on OpenClaw" badges.)
2. **`notifyScriptInstalledURL` is a `.sh` hook.** Does the Windows port ship a `.ps1`/`.cmd` notify hook, or is the notify integration out of scope for this milestone? Affects whether `~/.skillz/bin` is created.
3. **Application Support location:** confirm `AppData\Roaming\Skillz` (roaming) vs `AppData\Local\Skillz` (local). Spec assumes Roaming to match macOS "Application Support" intent.
4. **Path-separator normalization strategy:** spec recommends normalizing to `/` before all `Contains("/.x/")` checks so `primaryPlatform`/`platformsThatShare` port verbatim. Confirm this over rewriting every literal to `\`.
5. **OpenClaw workspace absolute-path detection:** macOS checks `hasPrefix("/")`. Windows should use `Path.IsPathRooted`. Decide whether to also accept POSIX-absolute (`/...`) values that a cross-platform `openclaw.json` might contain.
6. **`IReadOnlyList` structural equality** on `SkillItem.AlsoAvailableOn` / `McpItem.Args` / `McpItem.EnvKeys` — provide custom equality or a sequence-equal wrapper if `record` value-equality is relied upon by UI change detection.
7. **SF Symbol -> WPF-UI `SymbolRegular` glyph remap** for every `symbolName` (platforms, sections, kinds) is a UI decision; table above gives suggestions but exact glyphs need design sign-off.
8. **`executableSearchDirectories`** Windows contents (PATH, WindowsApps, Program Files) — finalize when the harness-launch feature is specced; not load-bearing for models/paths.
