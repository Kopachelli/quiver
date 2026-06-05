# Port Spec 03 — MCP Server Scanning & Plugin Discovery

Scope: precise documentation of how the macOS Skillz app discovers **MCP servers**,
**plugins**, and **installed platforms**, plus a Swift→.NET/WPF port plan.

Source files documented (read in full):

- `skillz/Services/MCPScanner.swift`
- `skillz/Services/PluginScanner.swift`
- `skillz/Services/TOMLParser.swift`
- `skillz/Services/PlatformSourceDetector.swift`

Supporting files read for path/model resolution:

- `Models/AgentPlatform.swift`, `Models/MCPItem.swift`, `Models/PluginItem.swift`
- `Services/AgentEnvironment.swift`, `Services/AgentPaths.swift`,
  `Services/PlatformSkillPaths.swift`, `Services/OpenClawConfig.swift`

---

## 0. Path foundations (where every dotfolder comes from)

All scanning is rooted in `AgentEnvironment.live` (`AgentEnvironment.swift`). The home
directory is `FileManager.default.homeDirectoryForCurrentUser` and each platform maps to a
dotfolder under it via `homeDirectory(for:)`:

| Platform enum | `displayName` | Home dir (macOS) | Home dir (Windows port) |
|---|---|---|---|
| `.cursor` | Cursor | `~/.cursor` | `C:\Users\<u>\.cursor` |
| `.claudeCode` | Claude Code | `~/.claude` | `C:\Users\<u>\.claude` |
| `.codex` | Codex | `~/.codex` | `C:\Users\<u>\.codex` |
| `.hermes` | Hermes | `~/.hermes` | `C:\Users\<u>\.hermes` |
| `.pi` | Pi | `~/.pi` | `C:\Users\<u>\.pi` |
| `.openClaw` | OpenCode | `~/.openclaw` | `C:\Users\<u>\.openclaw` |

The shared cross-tool skills root is `~/.agents/skills` (`AgentPlatform.agentsDirectory`
+ `PlatformSkillPaths.agentsSkillsDirectory`).

**Windows home resolution.** `FileManager.homeDirectoryForCurrentUser` →
`Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)` (i.e. `%USERPROFILE%`,
`C:\Users\<user>`). Centralize this in a `IAgentEnvironment` service (mirroring
`AgentEnvironment`) so it is injectable for tests, exactly like the macOS
`AgentPaths.environment` static-override pattern (`AgentEnvironment.temporary(root:)` is the
test seam — port it as a constructor that takes a root path).

> Note: `appendingPathComponent("plugins/cache")` uses forward slashes that Foundation
> resolves correctly on macOS. On Windows use `Path.Combine(home, "plugins", "cache")` —
> never embed `/` in literals. Every multi-segment `appendingPathComponent("a/b")` in the
> Swift becomes a multi-arg `Path.Combine`.

`AgentEnvironment` Swift fields → .NET service surface:

| Swift | Value (macOS) | Windows equivalent |
|---|---|---|
| `homeDirectory` | `~` | `%USERPROFILE%` |
| `applicationSupportDirectory` | `~/Library/Application Support/Skillz` | `%APPDATA%\Skillz` (`SpecialFolder.ApplicationData`) |
| `skillzHomeDirectory` | `~/.skillz` | `%USERPROFILE%\.skillz` |
| `executableSearchDirectories` | see §4 | see §4 |

---

## 1. MCP server scanning (`MCPScanner.swift` + `TOMLParser.swift`)

### 1.1 Top-level flow — `MCPScanner.scan()`

```
scan() = scanCursor() + scanClaude()
       + TOMLParser.mcpServers(from: ~/.codex/config.toml)
       sorted by name, case-insensitive ascending
```

Only **three** platforms produce MCP items: Cursor, Claude Code, Codex. Hermes / Pi /
OpenCode are **not** scanned for MCP. The result list is sorted with
`localizedCaseInsensitiveCompare` → in .NET `OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)`
(or `CurrentCultureIgnoreCase` to match "localized"; OrdinalIgnoreCase is the safe default).

### 1.2 The `MCPItem` model (`MCPItem.swift`)

```
MCPItem {
  id: String           // "mcp:{platform.rawValue}:{name}"
  platform: AgentPlatform
  name: String         // the server key
  transport: MCPTransport  // .stdio | .http | .unknown
  command: String?
  args: [String]
  url: String?
  envKeys: [String]    // SORTED KEYS ONLY — values are never read/stored
  configFileURL: URL
  modifiedAt: Date?    // file modification date
}
```

`MCPTransport` is `stdio | http | unknown`. Derived display strings:
`transportLabel` ("stdio"/"HTTP"/"Unknown"); `endpointSummary` = `url` if present,
else `command + " " + args.joined(" ")`, else `"—"`.

> Security/privacy detail to preserve: **only env *key names* are stored, never values.**
> `env.keys.sorted()`. Do the same in .NET: `dict.Keys.OrderBy(k => k, StringComparer.Ordinal)`.
> Do not surface env values in the UI or logs (they hold API tokens).

ID is `mcp:{platform}:{name}` — used as a stable identity key. Port `MCPItem.makeID`
verbatim as a static method.

`MCPItem` is a Swift `struct` (value type, `Equatable`, `Sendable`). In .NET model it as a
`record` (value equality, immutable) in a `Models` folder. `URL` → `string` full path (or a
small `FileRef` wrapper); `Date?` → `DateTime?` (or `DateTimeOffset?`).

### 1.3 Cursor — `~/.cursor/mcp.json`

`scanCursor()` reads `~/.cursor/mcp.json` via `parseJSONConfig`.

### 1.4 Claude Code — `~/.claude/.mcp.json`

`scanClaude()` reads `~/.claude/.mcp.json` (note the **leading dot** in the filename) via the
same `parseJSONConfig`.

> **`~/.claude.json` clarification.** The task brief lists "Claude Code `.mcp.json` AND
> `~/.claude.json`". In the actual macOS source, `~/.claude.json` is **never referenced** —
> grep across the whole repo returns no matches. The scanner reads exactly one Claude MCP
> file: `~/.claude/.mcp.json`. Real-world Claude Code does also persist MCP servers and
> `enabledPlugins` into a top-level `~/.claude.json` (project-scoped + global), but **this
> app does not read it.** Decision for the Windows port: **match the macOS behavior exactly**
> (read only `%USERPROFILE%\.claude\.mcp.json`). Track reading `%USERPROFILE%\.claude.json`
> as an *open enhancement* (see Open Questions), not part of the 1:1 port. If added later,
> its JSON shape is `{ "mcpServers": { … } }` at the top level (same shape as below), and it
> may additionally carry per-project `projects.<path>.mcpServers`.

### 1.5 JSON config shape (Cursor + Claude Code) — `parseJSONConfig`

Both Cursor `mcp.json` and Claude `.mcp.json` share one shape. The parser:

1. `Data(contentsOf:)` → `JSONSerialization.jsonObject` cast to `[String:Any]`.
2. Require top-level key `"mcpServers"` as a `[String:Any]` dictionary; else return `[]`.
3. Read file `modificationDate` once.
4. For each `(name, value)` where `value` is a dict:
   - `url` = `dict["url"] as String?`
   - `command` = `dict["command"] as String?`
   - `args` = `dict["args"] as [String]?` ?? `[]`
   - `env` = `dict["env"] as [String:Any]?` ?? `[:]`
   - transport: `url != nil → .http`, else `command != nil → .stdio`, else `.unknown`
   - `envKeys = env.keys.sorted()`
   - emit `MCPItem`.

Any entry whose `value` is not a dict is skipped (`compactMap` returning nil).

**Example `~/.cursor/mcp.json` (and identical `~/.claude/.mcp.json`):**

```json
{
  "mcpServers": {
    "filesystem": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-filesystem", "/Users/me/work"],
      "env": { "DEBUG": "1" }
    },
    "github-remote": {
      "url": "https://mcp.example.com/github",
      "env": { "GITHUB_TOKEN": "ghp_xxx" }
    },
    "bare-entry": {
      "type": "sse"
    }
  }
}
```

Parsed result:

- `filesystem` → transport `stdio`, command `npx`,
  args `["-y","@modelcontextprotocol/server-filesystem","/Users/me/work"]`,
  envKeys `["DEBUG"]`, url nil.
- `github-remote` → transport `http`, command nil, url present,
  envKeys `["GITHUB_TOKEN"]`. (URL presence wins even if a command were also present.)
- `bare-entry` → transport `unknown` (no url, no command), args `[]`, envKeys `[]`.

> Edge cases to replicate: a missing/empty/invalid file → `[]` (swallow errors). A missing
> `mcpServers` key → `[]`. `args` of wrong type → treated as `[]` (Swift `as? [String]`
> fails to `nil` → `??[]`). In .NET, model `args` as `string[]` and tolerate the property
> being absent or a non-array (catch/return empty), matching the lenient Swift casts.

### 1.6 Codex — `~/.codex/config.toml` (TOML `[mcp_servers.*]` tables)

`MCPScanner` delegates to `TOMLParser.mcpServers(from: ~/.codex/config.toml)`. It reads the
file as UTF-8, runs `parseSections`, and keeps sections whose name starts with
`mcp_servers.`. For each:

- `name = section.name` minus the `"mcp_servers."` prefix; skip if empty.
- `command = keys["command"]`
- `url = keys["url"]`
- `args = parseArgs(keys["args"] ?? "")`
- transport: `url != nil → .http`, else `command != nil → .stdio`, else `.unknown`
- envKeys: every key starting with `env.` (with the `env.` prefix stripped) **plus** the
  literal `"env"` mapped to `"env"` if a bare `env` key exists; deduped via `Set` and
  `.sorted()`.
- platform is hardcoded `.codex`; `configFileURL` is the toml path; `modifiedAt` from file.

**Example `~/.codex/config.toml`:**

```toml
# Codex config
model = "gpt-5"

[mcp_servers.filesystem]
command = "npx"
args = ["-y", "@modelcontextprotocol/server-filesystem", "/Users/me"]
env.DEBUG = "1"
env.LOG_LEVEL = "info"

[mcp_servers.remote]
url = "https://mcp.example.com/sse"

[mcp_servers.weird]
note = "no command, no url"
```

Parsed result:

- `filesystem` → stdio, command `npx`, args `["-y","@modelcontextprotocol/server-filesystem","/Users/me"]`,
  envKeys `["DEBUG","LOG_LEVEL"]` (the `env.` prefix is stripped, then sorted).
- `remote` → http, url present.
- `weird` → unknown transport, no command/url, args `[]`, envKeys `[]`.

> Codex env is expressed as dotted keys (`env.DEBUG = "1"`), **not** an inline table. The
> mini-parser does NOT understand `env = { DEBUG = "1" }` as a table — a bare `env = {...}`
> line would land as a single key `"env"` and contribute the literal env key `"env"` (the
> `+ section.keys.keys.filter { $0 == "env" }.map { _ in "env" }` branch). This is a known
> limitation; preserve it for 1:1 fidelity (see §2.4), or fix it when porting (see Open Q).

---

## 2. The custom TOML mini-parser (`TOMLParser.swift`)

This is **not** a general TOML parser. It is a line-oriented section/key extractor built for
exactly two consumers: `mcpServers(...)` and `enabledPlugins(...)`.

### 2.1 `Section` shape

```
struct Section { let name: String; var keys: [String:String] }
```

Every value is stored as a **String** (no typed ints/bools/floats/arrays/datetimes). Array
and bool interpretation happens later in `parseArgs` / the enabled-plugin truthy check.

### 2.2 `parseSections(from content:)` grammar

Iterates `content.components(separatedBy: .newlines)`. Per line, trimmed of surrounding
whitespace:

1. **Blank line or line starting with `#`** → skipped (comments only at line start; no
   inline `# comment` stripping).
2. **`[...]`** (starts with `[` AND ends with `]`) → flush current section, start a new
   section whose `name` is the inside text, trimmed. (Note: a bare `]` or unbalanced bracket
   is not special-cased; only the start+end test matters.)
3. **Otherwise**, find the **first** `=`. Left = key (trimmed); right = value (trimmed).
   - If value is wrapped in matching `"..."` **or** `'...'`, strip the one outer quote pair.
   - Store only if a section is currently open (`currentName` non-empty) → top-of-file
     pre-section keys are dropped.
4. Lines with no `=` (and not a header/comment) → silently skipped.
5. `flush()` appends the section if its name is non-empty; final flush after the loop.

### 2.3 `parseArgs(_ raw:)` — inline-array tokenizer

Used for `args = [...]`. Requires the trimmed value to start with `[` and end with `]`,
otherwise returns `[]`. Walks the inner string char by char:

- Outside quotes: a `"` or `'` opens a quote (remembering the quote char); `,` flushes the
  accumulated token (trimmed, skipped if empty); **any whitespace char is dropped**;
  any other char is appended.
- Inside quotes: chars accumulate until the matching quote char, which closes and flushes the
  token immediately (even an empty quoted string is appended as `""`).
- After the loop, the trailing token is flushed if non-empty.

Consequences / limits:
- Quoted args preserve internal spaces; **unquoted** args have all whitespace stripped.
- No escape handling (`\"` inside a quote is not supported — the `"` just closes the token).
- Mixed quoting and bare tokens both work. Nested arrays / tables are not supported.
- A quoted empty string `""` yields an empty-string arg; an unquoted empty token is dropped.

### 2.4 `mcpServers` env-key extraction recap

```
envKeys = keys.keys.filter{ $0.hasPrefix("env.") }.map{ String($0.dropFirst(4)) }
        + keys.keys.filter{ $0 == "env" }.map{ _ in "env" }
envKeys = Array(Set(envKeys)).sorted()
```

`dropFirst(4)` strips exactly `env.` (4 chars). Dedup via `Set`, then `.sorted()`.

### 2.5 `enabledPlugins(from:)` — Codex plugin enable map

Reads `config.toml`, parses sections, and for each section whose name starts with
`plugins."` or `plugins.'` extracts the quoted plugin id and records enablement:

- `extractQuotedPluginID`: drop the `plugins.` prefix; if remainder starts with `"`, take up
  to the next `"`; otherwise trim surrounding quotes. (Handles `plugins."foo@bar"`.)
- If the section has an `enabled` key, it is truthy when lowercased ∈ `{"true","yes","1"}`;
  otherwise (section present, no `enabled` key) defaults to `true`.

**Example `~/.codex/config.toml` plugins block:**

```toml
[plugins."my-plugin@acme-marketplace"]
enabled = true

[plugins."disabled-one@acme-marketplace"]
enabled = false

[plugins."implicitly-on@acme-marketplace"]
# no enabled key → treated as enabled
```

→ `{ "my-plugin@acme-marketplace": true, "disabled-one@acme-marketplace": false,
"implicitly-on@acme-marketplace": true }`.

### 2.6 Windows TOML decision: **use Tomlyn, do not port the mini-parser**

The mini-parser's limits (no inline tables, no escapes, single-quote-pair stripping, no typed
values, no nested arrays) are bug-compatibility hazards, not features. Recommendation for the
WPF port:

- **Use [`Tomlyn`](https://github.com/xoofx/Tomlyn)** (`Tomlyn.Toml.Parse` →
  `TomlTable` model). It is a maintained, spec-compliant .NET TOML library, handles real
  inline tables (`env = { DEBUG = "1" }`), dotted keys (`env.DEBUG`), comments, typed values,
  and arrays — i.e. a strict **superset** of what the mini-parser accepts.
- Map Codex tables: iterate the parsed `TomlTable`. The `[mcp_servers.X]` and `[plugins."Y"]`
  appear as nested tables under `mcp_servers` / `plugins`. With Tomlyn you read
  `((TomlTable)root["mcp_servers"])` and enumerate child keys → server names; `args` becomes a
  real `TomlArray` (no hand-tokenizing); `env` works whether dotted or inline-table.
- **Behavioral superset note:** Tomlyn will *correctly* parse `env = { DEBUG = "1" }` (which
  the mini-parser mangles) and inline `# comments`. This is strictly better. The only place to
  be careful is the `enabled` truthiness: real TOML `enabled = true` is a *boolean*, so read it
  as `bool`; also accept string forms `"yes"`/`"1"`/`"true"` for parity with the macOS lenient
  rule (Codex users may have hand-written strings). Default to `true` when the `[plugins."…"]`
  table exists without an `enabled` key (preserve macOS default).
- If a dependency-free build is mandated, port `parseSections`/`parseArgs` faithfully — but
  flag the inline-table case as a known gap. **Preferred: Tomlyn.**

JSON side uses **`System.Text.Json`** (`JsonDocument`/`JsonNode`), no third-party dep. The
lenient `as?` casts become `TryGetProperty` + `ValueKind` checks returning defaults.

---

## 3. Plugin discovery (`PluginScanner.swift`)

### 3.1 Top-level flow — `PluginScanner.scan()`

```
scan() = scanCursor() + scanClaude() + scanCodex()
       sorted by displayName, case-insensitive ascending
```

### 3.2 The `PluginItem` model (`PluginItem.swift`)

```
PluginItem {
  id: String           // "plugin:{platform}:{pluginID}:{installPath?.path ?? pluginID}"
  platform: AgentPlatform
  pluginID: String     // e.g. "name@marketplace"
  displayName: String
  description: String
  version: String?
  marketplace: String?  // substring after last '@' in pluginID
  isEnabled: Bool
  installPath: URL?
  metadataPath: URL?
  skillCount: Int       // # of subdirectories under installPath/skills
  modifiedAt: Date?
}
```

`listSubtitle` = `marketplace` if present else `pluginID`. Port as a .NET `record`.

### 3.3 Cursor plugins — `~/.cursor/plugins/cache`

`scanCursor()` calls `scanPluginMetadata(in: ~/.cursor/plugins/cache, platform: .cursor,
enabledMap: [:], defaultEnabled: true)`. Cursor plugins discovered purely from on-disk
metadata, **all default to enabled** (no enable map), `isEnabled = true`.

### 3.4 Claude plugins — `~/.claude/plugins/installed_plugins.json` (+ `settings.json`)

`scanClaude()`:

1. Load the **enabled map** from `~/.claude/settings.json` key `enabledPlugins`
   (`[String:Bool]`); missing/invalid → `[:]`.
2. Read `~/.claude/plugins/installed_plugins.json`, requiring top-level `"plugins"` as
   `[String:Any]`. **If that file is absent/invalid → fall back** to
   `scanPluginMetadata(in: ~/.claude/plugins/cache, …, defaultEnabled: false)` (so even with no
   installed-plugins manifest, cache dirs are still surfaced, but disabled-by-default).
3. When present, `plugins` maps `pluginID → [ {entry}, … ]` (array of entries). For each:
   - take the **first** entry; require `installPath: String`; skip if absent.
   - `installPath` → URL; find metadata via `findPluginJSON` (looks for
     `.claude-plugin/plugin.json`, then `.codex-plugin/plugin.json`).
   - `metadata` = `loadPluginMetadata` (name/description/version from that json).
   - `skillCount = countSkills(installPath)` (subdirs of `installPath/skills`).
   - `version` = entry `version` ?? metadata version.
   - `isEnabled = enabledMap[pluginID] ?? false` (default **disabled** if not listed).
   - `displayName` = metadata name ?? pluginID; `description` = metadata desc ?? "".

**Example `~/.claude/plugins/installed_plugins.json`:**

```json
{
  "plugins": {
    "code-reviewer@acme-marketplace": [
      {
        "installPath": "/Users/me/.claude/plugins/cache/acme-marketplace/code-reviewer",
        "version": "1.2.0"
      }
    ],
    "docs-helper@community": [
      { "installPath": "/Users/me/.claude/plugins/cache/community/docs-helper" }
    ]
  }
}
```

**Example `~/.claude/settings.json` (enable state):**

```json
{
  "enabledPlugins": {
    "code-reviewer@acme-marketplace": true,
    "docs-helper@community": false
  }
}
```

**Example plugin metadata `…/code-reviewer/.claude-plugin/plugin.json`:**

```json
{
  "name": "code-reviewer",
  "description": "Reviews diffs for bugs.",
  "version": "1.2.0"
}
```

> Marketplace identity: `marketplace(from:)` returns the substring after the **last** `@` in
> the pluginID (`"code-reviewer@acme-marketplace"` → `"acme-marketplace"`). There is no
> separate marketplace registry file parsed; the marketplace name is purely derived from the
> `name@marketplace` id convention. The id itself comes from the keys of
> `installed_plugins.json` (Claude) or is inferred for cache-only scans (see `inferPluginID`).

### 3.5 Codex plugins — `~/.codex/config.toml` + `~/.codex/plugins/cache`

`scanCodex()`:

1. `enabledMap = TOMLParser.enabledPlugins(~/.codex/config.toml)` (see §2.5).
2. `scanPluginMetadata(in: ~/.codex/plugins/cache, platform: .codex, enabledMap, defaultEnabled:false)`.
3. **Then** for every `pluginID` in `enabledMap` not already represented by a cache item,
   append a synthetic `PluginItem` with no install path/metadata, `skillCount 0`, `version nil`,
   `displayName = pluginID`, `isEnabled` from the map, `modifiedAt` = config.toml's date.
   (So a plugin enabled in config but absent from cache still shows up.)

### 3.6 `scanPluginMetadata` — generic on-disk crawler

For any cache root:

1. If the root path does not exist → `[]`.
2. Enumerate recursively (`FileManager.enumerator`, skipping hidden files).
3. For each directory URL, check for `.claude-plugin/plugin.json` then `.codex-plugin/plugin.json`;
   if neither exists, skip. (So a "plugin dir" is any dir containing one of those metadata files.)
4. Dedup by install path (`seenPaths` set).
5. `metadata = loadPluginMetadata`; `pluginName` = metadata name ?? the **parent dir name**
   of installPath (`installPath.deletingLastPathComponent().lastPathComponent`).
6. `pluginID = inferPluginID(name, path, platform)`:
   - parent dir name contains `@` → `"{name}@{parentSuffixAfter@}"`
   - else → `"{name}@{platform.displayName.lowercased(), spaces→dashes}"`
     (e.g. `"code-reviewer@claude-code"`, `"foo@cursor"`, `"bar@codex"`).
7. `isEnabled = enabledMap[pluginID] ?? defaultEnabled`.
8. `skillCount = countSkills(installPath)`; emit item.

> The recursive enumerator means metadata can be **nested** several levels deep under
> `plugins/cache/<marketplace>/<plugin>/…`. The "install path" is the directory that *contains*
> the `.claude-plugin`/`.codex-plugin` folder.

### 3.7 Metadata + skill counting helpers

- `loadPluginMetadata(url)`: parse json; `name` = `json["name"]` ?? grandparent dir name
  (`url.deletingLastPathComponent().deletingLastPathComponent().lastPathComponent` — i.e. the
  plugin dir name, since `url` is `<plugin>/.claude-plugin/plugin.json`); `description` ?? "";
  `version` = `json["version"]` (optional).
- `countSkills(installPath)`: list `installPath/skills`; count entries that are directories.
  Missing `skills` dir → 0. (Each subdir = one embedded skill.)
- `loadClaudeEnabledPlugins(url)`: from `settings.json`, key `enabledPlugins` as `[String:Bool]`.
- `marketplace(from:)`: substring after last `@`, else nil.
- `modificationDate(for:)`: file's `.modificationDate`.

### 3.8 Default-enabled semantics summary

| Source | enable map | default when not in map |
|---|---|---|
| Cursor cache | none (`[:]`) | **true** (all enabled) |
| Claude `installed_plugins.json` | `settings.json.enabledPlugins` | **false** |
| Claude cache fallback (no manifest) | `settings.json.enabledPlugins` | **false** |
| Codex cache | `config.toml [plugins."…"]` | **false** |
| Codex config-only (synthetic) | `config.toml [plugins."…"]` | from map |

---

## 4. Platform "installed" detection (`PlatformSourceDetector.swift`)

### 4.1 Public surface

- `detect(snapshot:) -> [PlatformSourceStatus]` — one status per `AgentPlatform.allCases`,
  carrying `isDetected`, `scanPaths`, `detectionSignals`, `itemCount` (from the catalog
  snapshot via `CatalogFilter`), and `hookSupport`.
- `isInstalled(platform:) -> Bool` — true iff any detection signal is an **install signal**.
- `detectedPlatforms(from:)` / `defaultNewSkillPlatforms(from:)` — set of detected platforms;
  the "default new-skill platforms" deliberately equals the detected set (never pre-checks
  absent tools, to avoid writing skill folders for uninstalled tools).
- `allScanPaths(for:)` — the profile's source paths.

`PlatformSourceStatus` derived props: `detectedSignal` (first install signal, else first
signal), `primaryPath`, `detectionLabel`, `statusLabel` ("Found"/"Not detected"),
`hookSupportLabel`, `notDetectedHint`.

### 4.2 Per-platform `PlatformDetectionProfile`

Built by `profile(for:)` — `sourcePaths` (deduped), `executableNames`, `notDetectedHint`,
`hookSupport`.

**`sourcePaths(for:)`** starts from `PlatformSkillPaths.skillScanRoots(for:)` then appends
platform-specific config/dirs, and finally the bare home dir:

| Platform | Skill roots | Appended source paths (relative to home dir) |
|---|---|---|
| cursor | `~/.cursor/skills` | `mcp.json`, `agent-hooks.json`, `plugins/cache/`, `skills-cursor/` |
| claudeCode | `~/.claude/skills` | `settings.json`, `.mcp.json`, `plugins/cache/`, `plugins/installed_plugins.json` |
| codex | `~/.codex/skills`, `~/.agents/skills` | `config.toml`, `hooks.json`, `plugins/cache/`, `process_manager/chat_processes.json` |
| hermes | `~/.hermes/skills` | `config.yaml`, `processes.json`, `sessions/`, `plugins/` |
| pi | `~/.pi/agent/skills`, `~/.agents/skills` | (none extra) |
| openClaw | `~/.openclaw/skills` (+ workspace skills if present) | `~/.agents/skills`, `~/.openclaw/openclaw.json`, workspace dir, workspace `skills/`, `~/.opencode/` |

…then **every** platform appends its bare `homeDirectory` last. Deduped by path string.

**`executableNames(for:)`** (probed against the executable search dirs):

| Platform | Executable names |
|---|---|
| cursor | `cursor-agent`, `cursor` |
| claudeCode | `claude` |
| codex | `codex` |
| hermes | `hermes`, `hermes-cli`, `tirith` |
| pi | `pi` |
| openClaw | `opencode`, `open-code`, `openclaw`, `open-claw` |

**`hookSupport(for:)`**: cursor/claudeCode/codex → `.preciseWaitingState`;
hermes/pi/openClaw → `.processFallback`. (Display-only flag for live-activity capability.)

### 4.3 How a signal is produced — `detectionSignals(for:)`

Three signal sources, combined and deduped by `"{kind}-{path}"`:

1. **Source file/dir exists.** For each `profile.sourcePaths` where
   `FileManager.fileExists` → a `.source` signal. `isInstallSignal` is
   `isInstallSource(path)` (see §4.4).
2. **Skill content.** For each skill scan root where `directoryContainsSkillMD(root)` is true
   (recursive search for any file literally named `SKILL.md`) → a `.source` signal.
3. **Executable present.** `executableCandidates(named:)` = every `executableSearchDirectory`
   × every executable name. For each where `FileManager.isExecutableFile(path)` → an
   `.executable` signal with `isInstallSignal = true` (always an install signal).

`executableSearchDirectories` (from `AgentEnvironment.live`):

```
~/.local/bin, ~/.opencode/bin, ~/.hermes/bin,
/opt/homebrew/bin, /usr/local/bin, /usr/bin
```

`isInstalled` / `isDetected` is true iff **any** signal has `isInstallSignal == true`.

### 4.4 What counts as an "install signal" — `isInstallSource`

```
isInstallSource(url) = !isSharedSkillSource(url) && !isBareHomeDirectory(url)
```

- **`isBareHomeDirectory`**: url equals any platform's `homeDirectory` (standardized). The bare
  `~/.cursor` (etc.) is shown for context but **does not** count as installed — an empty
  leftover dotfolder must not trigger auto-install of hooks. Real config files, skill dirs, and
  executables still count.
- **`isSharedSkillSource`**: url is `~/.agents/skills` or a child of it. The shared cross-tool
  skills dir is not, by itself, proof a specific tool is installed.
- `sourceLabel(for:)` classification (display only): shared skill source; `SKILL.md`/`skills`/
  contains `/skills` → "Skill source"; `mcp.json`/`.mcp.json`/`config.toml` → "Config";
  contains `plugin` → "Plugin source"; else "Home folder".

### 4.5 `directoryContainsSkillMD`

Recursively enumerate `url` (skip hidden); return true on the first file whose
`lastPathComponent == "SKILL.md"`. Missing dir → false.

---

## 5. Windows / .NET / WPF port plan

### 5.1 Construct mapping (Swift/Foundation → .NET)

| Swift / Foundation | .NET / WPF equivalent |
|---|---|
| `enum MCPScanner { static func scan() }` | `static class McpScanner` or DI `IMcpScanner` |
| `URL` (file) | `string` full path; helpers via `System.IO.Path` |
| `URL.appendingPathComponent("a/b")` | `Path.Combine(root, "a", "b")` (split on `/`!) |
| `FileManager.default.fileExists(atPath:)` | `File.Exists` / `Directory.Exists` |
| `FileManager.enumerator(at:options:.skipsHiddenFiles)` | `Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)` + filter `FileAttributes.Hidden` |
| `attributesOfItem[.modificationDate]` | `File.GetLastWriteTime(path)` (`DateTime`) / `…Utc` |
| `Data(contentsOf:)` + `JSONSerialization` | `File.ReadAllText` + `System.Text.Json` (`JsonDocument`/`JsonNode`) |
| `String(contentsOf:encoding:.utf8)` | `File.ReadAllText(path, Encoding.UTF8)` |
| `try?` (swallow errors → nil/[]) | `try { … } catch { return empty; }` per read |
| `dict["x"] as? String` lenient cast | `node["x"]?.GetValue<string>()` inside try/`ValueKind` check |
| `[String:Any]` json object | `JsonObject` / `Dictionary<string,JsonNode>` |
| `struct … : Equatable, Sendable` | `record` (immutable, value equality) |
| `enum MCPTransport` | `enum McpTransport { Stdio, Http, Unknown }` |
| `localizedCaseInsensitiveCompare` sort | `OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)` |
| `Set<String>` dedup | `HashSet<string>` |
| `FileManager.isExecutableFile` | see §5.3 (no exec bit on Windows) |
| `AgentEnvironment.temporary(root:)` test seam | constructor/`IAgentEnvironment` with injectable root |
| `homeDirectoryForCurrentUser` | `Environment.GetFolderPath(SpecialFolder.UserProfile)` |

UI/threading: the macOS `scan()` is `nonisolated` (off-main, callable from background). In WPF,
run scans on a background thread (`Task.Run`) and marshal results to the UI via the MVVM
`Dispatcher`/`ObservableCollection` on the captured `SynchronizationContext`. The detector's
"snapshot in → statuses out" shape maps cleanly to an MVVM service returning a list the
viewmodel binds to (WPF-UI Fluent list controls / `DataGrid`).

### 5.2 JSON & TOML libraries

- **JSON**: `System.Text.Json` only. Mirror the lenient reads: top-level object →
  `JsonNode.Parse`; require `mcpServers`/`plugins`/`enabledPlugins` to be a `JsonObject`,
  else return empty. Per-entry, tolerate missing/wrong-typed fields (return defaults). **Never
  read or store env *values*** — only `((JsonObject)env).Select(kv => kv.Key)` sorted.
- **TOML**: **Tomlyn** (recommended — see §2.6). Provides real inline-table/dotted-key/array
  support, a strict superset of the mini-parser; only re-implement the lenient `enabled`
  truthiness and the `[plugins."…"]` default-enabled rule on top of it. Avoid porting the
  bug-compatible hand parser unless a zero-dependency constraint is imposed.

### 5.3 Windows path & CLI-detection equivalents (source detection)

**Paths.** All `~/.x` dotfolders live under `%USERPROFILE%` (`C:\Users\<user>\.cursor`, etc.).
Use `Path.Combine`. Config filenames are identical (`mcp.json`, `.mcp.json`,
`config.toml`, `settings.json`, `installed_plugins.json`, `plugins/cache`,
`process_manager/chat_processes.json`). The leading-dot `.mcp.json` and dotfolders are valid on
NTFS.

**Executable detection.** Windows has no Unix exec bit, so replace
`FileManager.isExecutableFile` and the hardcoded `/usr/local/bin` list:

1. **`where.exe` / PATH probe** (preferred Windows-idiomatic check): for each executable name,
   resolve via PATH — either shell out to `where.exe <name>` (exit 0 = found, parse first line
   as the resolved path) or replicate it in-process: split `%PATH%` on `;`, and for each dir
   test `name + ext` for each ext in `%PATHEXT%` (default `.COM;.EXE;.BAT;.CMD;...`). Most CLIs
   install a `<name>.exe`, `<name>.cmd`, or `<name>.bat` shim.
2. **Per-tool install dirs** (the Windows analog of the macOS `executableSearchDirectories`
   list). Probe these in addition to PATH:
   - `%USERPROFILE%\.local\bin` (Python/pipx-style installs, mirrors `~/.local/bin`)
   - `%USERPROFILE%\.opencode\bin`, `%USERPROFILE%\.hermes\bin` (mirror the macOS entries)
   - `%LOCALAPPDATA%\Programs\<tool>` (npm/standalone installers, e.g.
     `%LOCALAPPDATA%\Programs\cursor`, `…\claude`)
   - `%APPDATA%\npm` (global npm `.cmd` shims — common for `@anthropic-ai/claude-code` etc.)
   - `%ProgramFiles%\<tool>` / `%ProgramFiles(x86)%\<tool>` (MSI/standalone)
3. Build `executableCandidates` = each search dir × each name × each `PATHEXT` extension; a
   candidate "exists & is executable" reduces to `File.Exists(candidate)` (the file existing
   with an executable extension is the Windows notion of "executable"). Keep the executable
   names per platform unchanged (`cursor-agent`/`cursor`, `claude`, `codex`,
   `hermes`/`hermes-cli`/`tirith`, `pi`, `opencode`/`open-code`/`openclaw`/`open-claw`) but
   expect `.exe`/`.cmd` suffixes on disk.

**`isExecutableFile` →** `File.Exists(path)` for a candidate already suffixed with a `PATHEXT`
extension (optionally confirm the extension is in `%PATHEXT%`).

**`isBareHomeDirectory` / `isSharedSkillSource`** port directly using normalized full paths
(`Path.GetFullPath` + `StringComparer.OrdinalIgnoreCase` — Windows paths are case-insensitive,
unlike the macOS case-sensitive `==`; use OrdinalIgnoreCase for the equality/prefix tests).

**`%PATH%` note:** Windows path separator in `%PATH%` is `;` (not `:`). Read via
`Environment.GetEnvironmentVariable("PATH")` and `Environment.GetEnvironmentVariable("PATHEXT")`.

### 5.4 Behaviors to preserve exactly

- MCP scanned only for Cursor/Claude/Codex; sort by name (case-insensitive).
- Transport precedence: `url` ⇒ http, else `command` ⇒ stdio, else unknown.
- Store **env key names only**, sorted; never values.
- Claude reads `~/.claude/.mcp.json` (leading dot), not `~/.claude.json` (see §1.4 + Open Qs).
- Plugin enable defaults: Cursor true; Claude/Codex false (except synthetic config-only Codex).
- `skillCount` = number of subdirectories under `<installPath>/skills`.
- pluginID/marketplace derived from `name@marketplace` convention; marketplace = after last `@`.
- A bare/empty dotfolder is **not** an install signal; executables always are.
- Errors are swallowed per-read → empty results, never crashes.

---

## 6. Concrete config snippet index (copy targets for fixtures/tests)

Windows fixture roots: place under a temp `%USERPROFILE%`-style root (inject via
`IAgentEnvironment`), e.g. `C:\Users\<u>\.cursor\mcp.json`, `…\.claude\.mcp.json`,
`…\.claude\settings.json`, `…\.claude\plugins\installed_plugins.json`,
`…\.codex\config.toml`, plus `…\plugins\cache\<marketplace>\<plugin>\.claude-plugin\plugin.json`.

- §1.5 — `mcp.json` / `.mcp.json` (stdio + http + bare).
- §1.6 — `config.toml` `[mcp_servers.*]` (dotted env keys).
- §2.5 — `config.toml` `[plugins."…"]` enable map.
- §3.4 — `installed_plugins.json`, `settings.json` (`enabledPlugins`), `plugin.json`.

These are the minimal fixtures needed to exercise every branch of the three scanners.
