# 02 — Skill Scanning & Editing

Port spec for the macOS `skillz` skill-discovery, frontmatter-parsing, and skill-mutation
subsystem to a Windows-native WPF / .NET 8 (LTS) app (WPF-UI Fluent, MVVM, AvalonEdit).

This document is a faithful, line-level reading of the following source files plus the
supporting model/path types they depend on:

| macOS source | Role |
|---|---|
| `Services/SkillScanner.swift` | Filesystem discovery of skills |
| `Services/FrontmatterParser.swift` | Read the mini-YAML frontmatter |
| `Services/FrontmatterWriter.swift` | Write/serialize the mini-YAML frontmatter |
| `Services/SkillFileService.swift` | Create / rename / delete / metadata-edit semantics |
| `Services/SkillNameValidator.swift` | Skill-name validation rules |
| `Services/EditorDocument.swift` | Editor buffer + debounced autosave |
| `Services/FileAccessError.swift` | Error-to-user-message mapping |
| `Models/SkillItem.swift` | `SkillItem`, `SkillFrontmatter`, `SkillMarkdownFile` |
| `Models/AgentPlatform.swift` | Platform enum + per-platform skills directories |
| `Services/PlatformSkillPaths.swift` | Scan roots, dedup, primary/shared platform |
| `Services/AgentEnvironment.swift` | Home-directory resolution per platform |
| `Services/AgentPaths.swift` | Environment indirection |
| `Services/OpenClawConfig.swift` | OpenClaw workspace resolution |
| `Theme/AppBrand.swift` | `AppBrand.name == "Skills"` |

> **macOS → Windows note convention.** Throughout, every Swift/AppKit/SwiftUI/Foundation
> construct is annotated with its `.NET 8` / WPF equivalent in a `> Windows:` callout.

---

## 0. Platform roots and home-directory resolution (foundation)

Skill discovery is rooted at six "agent platforms". Each maps to a dotfolder under the
user's home directory.

### 0.1 Home directory

`AgentEnvironment.live` resolves home via:

```swift
let home = FileManager.default.homeDirectoryForCurrentUser
```

> **Windows:** `Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)`
> i.e. `C:\Users\<user>`. Do **not** use `Environment.GetEnvironmentVariable("HOME")`
> (unset on Windows). The macOS `~/.local/bin`, `/opt/homebrew/bin`, `/usr/local/bin`
> executable-search entries in `AgentEnvironment` are irrelevant to this subsystem
> (they're for launching agents) and can be dropped.

### 0.2 Per-platform dotfolder (`AgentEnvironment.homeDirectory(for:)`)

| `AgentPlatform` case | `displayName` | Home dir (macOS) | Windows equivalent |
|---|---|---|---|
| `cursor` | "Cursor" | `~/.cursor` | `%USERPROFILE%\.cursor` |
| `claudeCode` | "Claude Code" | `~/.claude` | `%USERPROFILE%\.claude` |
| `codex` | "Codex" | `~/.codex` | `%USERPROFILE%\.codex` |
| `hermes` | "Hermes" | `~/.hermes` | `%USERPROFILE%\.hermes` |
| `pi` | "Pi" | `~/.pi` | `%USERPROFILE%\.pi` |
| `openClaw` | "OpenCode" | `~/.openclaw` | `%USERPROFILE%\.openclaw` |

> Note the display-name quirk: the enum case is `openClaw`, the dotfolder is `.openclaw`,
> but `displayName` is **"OpenCode"** and the brand/icon strings say "OpenCode". Preserve
> all three exactly.

`AgentPlatform.allCases` order is: `cursor, claudeCode, codex, hermes, pi, openClaw`.
The scanner iterates platforms in this order (matters only for first-write-wins dedup keying
— see §1.5 — and is effectively irrelevant since dedup keys by path and final list is
re-sorted).

> **Windows:** `enum AgentPlatform { Cursor, ClaudeCode, Codex, Hermes, Pi, OpenClaw }`.
> Keep a `DisplayName`, `HomeDirectory`, and `UserSkillsDirectory` per value
> (extension methods or a record/dictionary). `allCases` → `Enum.GetValues<AgentPlatform>()`.

### 0.3 `userSkillsDirectory` — the writable "create new skill here" folder

`AgentPlatform.userSkillsDirectory`:

| Platform | userSkillsDirectory |
|---|---|
| `cursor`, `claudeCode`, `codex`, `hermes` | `<home>/skills` |
| `pi` | `<home>/agent/skills` (i.e. `~/.pi/agent/skills`) |
| `openClaw` | `<home>/skills` (i.e. `~/.openclaw/skills`) |

> **Windows:** all `appendingPathComponent` chains → `Path.Combine`. e.g. Pi →
> `Path.Combine(home, ".pi", "agent", "skills")`. Use `Path.Combine` (not string concat)
> so separators are `\`.

### 0.4 Shared `~/.agents/skills`

`AgentPlatform.agentsDirectory == ~/.agents`, and
`PlatformSkillPaths.agentsSkillsDirectory == ~/.agents/skills`.

> **Windows:** `%USERPROFILE%\.agents\skills`.

---

## 1. Skill discovery (`SkillScanner.scan`)

### 1.1 Entry point and ordered sources

```swift
static func scan(hideBuiltInCursor: Bool, hideSystemCodex: Bool) -> [SkillItem]
```

The scanner aggregates skills from **four kinds of source**, in this order:

1. **Per-platform user scan roots** — for every `AgentPlatform`, every root returned by
   `PlatformSkillPaths.skillScanRoots(for:)`, scanned with
   `isBuiltIn=false, isPluginEmbedded=false`.
   For `codex` only, `hideSystem = hideSystemCodex` is passed (see §1.4).
2. **Built-in Cursor skills** — `~/.cursor/skills-cursor`, scanned with
   `isBuiltIn=true`, but **only when `hideBuiltInCursor == false`**.
3. **Plugin-embedded skills** — `<home>/plugins/cache` for `cursor`, `claudeCode`, and
   `codex` (three separate calls), via `scanPluginEmbeddedSkills` (see §1.6),
   `isPluginEmbedded=true`.

Then the combined list is run through `deduplicate(...)` (§1.5) and **sorted** by
`displayName`, case-insensitive ascending.

> **Windows:** `scan` is a pure function returning `IReadOnlyList<SkillItem>`. Make it a
> static method on a `SkillScanner` static class (Swift `enum` with only static members =
> a static/namespace class). The two `bool` params come from settings
> (`hideBuiltInCursor`, `hideSystemCodex`).
>
> Final sort: `OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)`.
> Swift's `localizedCaseInsensitiveCompare` is locale-aware; `OrdinalIgnoreCase` is the
> safe, deterministic Windows choice. For closer parity you could use
> `StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true)`, but ordinal is
> recommended for a file-list UI to avoid culture surprises.

### 1.2 Scan roots per platform (`skillScanRoots`)

| Platform | Scan roots |
|---|---|
| `cursor` | `~/.cursor/skills` |
| `claudeCode` | `~/.claude/skills` |
| `codex` | `~/.codex/skills`, **and** `~/.agents/skills` |
| `hermes` | `~/.hermes/skills` |
| `pi` | `~/.pi/agent/skills`, **and** `~/.agents/skills` |
| `openClaw` | `~/.openclaw/skills`, **and** workspace skills dir *if it exists* |

- `~/.agents/skills` is therefore scanned **twice** (once under `codex`, once under `pi`).
  Both passes produce items with the same `skillPath`; dedup collapses them to one
  (§1.5), and `platformsThatShare` marks it as available on `pi`, `codex`, `openClaw`.
- **OpenClaw workspace skills** (`OpenClawConfig.workspaceSkillsDirectory()`): only appended
  to the roots if the directory exists on disk. Resolution:
  - Read `~/.openclaw/openclaw.json`, JSON path `agents.defaults.workspace` (a string).
  - If absent/empty → default `~/.openclaw/workspace`.
  - If it starts with `~/` → expand against home.
  - If it starts with `/` → absolute (POSIX).
  - Otherwise → relative to `~/.openclaw/`.
  - Then append `skills`.

> **Windows:** `skillScanRoots` returns `IEnumerable<string>` (absolute paths).
> Build each via `Path.Combine`. For OpenClaw JSON, parse with `System.Text.Json`
> (`JsonDocument`), navigate `agents` → `defaults` → `workspace`.
> Path-prefix handling must be **Windows-aware**:
> - `~/` (or `~\`) → expand to `%USERPROFILE%`.
> - Absolute: macOS checks `hasPrefix("/")`. On Windows use `Path.IsPathRooted(workspace)`
>   (handles `C:\...`, `\\server\share`). Treat a leading `/` defensively too.
> - else relative to `.openclaw`.
> Existence check: `Directory.Exists(path)`.

### 1.3 Directory scan algorithm (`scanDirectory`)

For each root:

```swift
guard FileManager.default.fileExists(atPath: root.path) else { return [] }
let enumerator = FileManager.default.enumerator(
    at: root,
    includingPropertiesForKeys: [.contentModificationDateKey, .isRegularFileKey],
    options: [.skipsHiddenFiles])
for case let fileURL as URL in enumerator {
    guard fileURL.lastPathComponent == "SKILL.md" else { continue }
    ...
}
```

Key facts to preserve exactly:

- **Recursion: fully recursive.** `FileManager.enumerator` does a deep recursive walk of the
  entire subtree rooted at the scan root — there is **no depth limit**. Any `SKILL.md` at
  any nesting level is discovered.
- **Filename match is `SKILL.md` — case-SENSITIVE, exact.** The comparison is
  `fileURL.lastPathComponent == "SKILL.md"` (Swift `String ==` is an exact byte/scalar
  comparison). `skill.md`, `Skill.md`, `SKILL.MD` are **not** matched. (macOS HFS+/APFS is
  usually case-insensitive at the filesystem level, but the *string comparison here* is
  case-sensitive, so a file literally named `skill.md` on disk would still equal the
  lowercase string only if the OS returns the on-disk casing — practically, the scanner
  treats `SKILL.md` as the canonical, case-sensitive name.)
- **Hidden files skipped:** `options: [.skipsHiddenFiles]` skips dot-prefixed entries
  (and macOS hidden-flagged files) during enumeration. This is how `.system` subfolders are
  *not* automatically excluded (they're matched by name later) but dotfiles are.
- **`hideSystem` filter (codex only):** after finding a `SKILL.md`, compute
  `relative = fileURL.deletingLastPathComponent().path` (the containing dir). If
  `hideSystem && (relative.contains("/.system/") || relative.hasSuffix("/.system"))`, skip.
- Each surviving `SKILL.md` becomes a `SkillItem` via `makeSkillItem` (§1.7).

> **Windows mapping (important — this is the highest-risk port detail):**
>
> - Recursive enumeration → `Directory.EnumerateFiles(root, "SKILL.md", SearchOption.AllDirectories)`
>   **does NOT preserve case sensitivity** — Windows file matching and the `SKILL.md`
>   search pattern are case-**insensitive**, so it would also match `skill.md`, `Skill.md`,
>   etc. To **faithfully** reproduce the macOS exact-name behavior, enumerate then filter
>   by ordinal comparison:
>   ```csharp
>   foreach (var file in Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories))
>       if (Path.GetFileName(file).Equals("SKILL.md", StringComparison.Ordinal)) { ... }
>   ```
>   **DECISION for Windows:** because NTFS stores the real casing but matches
>   case-insensitively, and because users on Windows will overwhelmingly create `SKILL.md`
>   from this very app, the recommendation is to match **case-insensitively**
>   (`StringComparison.OrdinalIgnoreCase`) for discovery robustness, while always *writing*
>   `SKILL.md` (uppercase). Document this as an intentional, minor behavior relaxation.
>   If strict parity is required, use `StringComparison.Ordinal` as above. Flag this for
>   the reviewer.
> - `.skipsHiddenFiles` → there is no built-in equivalent. Manually skip entries whose name
>   starts with `.` and/or whose `FileAttributes` include `Hidden`. The dot-prefix check is
>   the load-bearing one (e.g. `.system`, `.git`). Implement:
>   ```csharp
>   bool IsHidden(string p) =>
>       Path.GetFileName(p).StartsWith(".", StringComparison.Ordinal) ||
>       (File.GetAttributes(p) & FileAttributes.Hidden) != 0;
>   ```
>   and skip any path that contains a hidden segment. Simplest faithful approach: do a manual
>   recursive walk that prunes directories whose name starts with `.` (mirrors how
>   `.skipsHiddenFiles` prevents descending into `.git` etc.). **Caution:** the `.system`
>   directory does **not** start with a dot in the way `.git` does — wait, it does
>   (`.system`). So `.skipsHiddenFiles` would normally skip `.system` too. But the codex
>   `hideSystem` logic explicitly re-checks for `.system`, implying `.system` skill folders
>   *are* enumerated. Resolve this by **not** pruning `.system` during the walk but pruning
>   other dot-dirs — or, more faithfully, replicate Foundation's behavior: `.skipsHiddenFiles`
>   skips files/dirs with the POSIX hidden (dot) prefix **only at the leaf the enumerator
>   yields**, but it still descends. In practice, to match observed behavior, **descend into
>   all directories** (including `.system`) and only skip yielding hidden *files*. Keep the
>   explicit `.system` filter for codex. Flag for verification against real `~/.codex/skills`.
> - `relative.contains("/.system/")` / `hasSuffix("/.system")`: on Windows the separator is
>   `\`. Port to a normalized check:
>   ```csharp
>   var dir = Path.GetDirectoryName(file)!;
>   var norm = dir.Replace('\\', '/');
>   bool isSystem = norm.Contains("/.system/") || norm.EndsWith("/.system");
>   ```
>   (Normalize to `/` so the literal macOS substrings still match, or rewrite with `\`.)
> - `contentModificationDate` → `File.GetLastWriteTimeUtc(path)` (a `DateTime`; macOS uses
>   `Date`). Store as `DateTimeOffset?` for clarity.
> - `fileExists(atPath:)` → `Directory.Exists` (these roots are directories).

### 1.4 `hideSystemCodex` semantics

Only the `codex` platform passes `hideSystem = hideSystemCodex`. When the user setting
"hide system codex skills" is on, any skill whose containing directory path includes a
`.system` segment is dropped. All other platforms always pass `hideSystem = false`.

### 1.5 Deduplication (`deduplicate`)

```swift
var byPath: [String: SkillItem] = [:]
for item in items {
    let pathKey = item.skillPath.path
    let primary = PlatformSkillPaths.primaryPlatform(for: item.skillPath)
    let also = PlatformSkillPaths.platformsThatShare(path: item.skillPath)
                 .filter { $0 != primary }
    let deduped = SkillItem(/* same fields, but */ platform: primary,
                            id: makeID(platform: primary, path), alsoAvailableOn: also)
    byPath[pathKey] = deduped     // last write wins
}
return Array(byPath.values)
```

- **Dedup key:** the absolute `skillPath` (the path to the `SKILL.md` file). Two scan passes
  hitting the same file collapse to one entry.
- **Last write wins** for the dictionary value, but all the colliding items share identical
  fields *except* platform, which is overwritten anyway by `primaryPlatform`. So the result
  is deterministic regardless of order.
- The deduped item's `platform` is re-assigned to `primaryPlatform(for: path)`, its `id` is
  regenerated, and `alsoAvailableOn` is set to the *other* sharing platforms.
- **`Array(byPath.values)` returns in dictionary (unordered) order** — but the caller then
  sorts by `displayName`, so final order is stable.

#### `primaryPlatform(for path:)` — substring routing (ordered, first match wins)

| If path contains… | primary platform |
|---|---|
| `/skills-cursor/` | `.cursor` |
| `/.hermes/` | `.hermes` |
| `/.openclaw/` | `.openClaw` |
| `/.pi/` | `.pi` |
| `/.cursor/` | `.cursor` |
| `/.claude/` | `.claudeCode` |
| `/.codex/` | `.codex` |
| `/.agents/` | `.pi` |
| (none) | `.cursor` (fallback) |

#### `platformsThatShare(path:)`

Returns `[.pi, .codex, .openClaw]` **only if** the path contains `/.agents/skills/` or ends
with `/.agents/skills`; otherwise `[]`. (For an `~/.agents/skills` skill, primary is `.pi`,
so `alsoAvailableOn` = `[.codex, .openClaw]`.)

> **Windows:** dedup → `Dictionary<string, SkillItem>` keyed by the normalized full path.
> **Normalize keys** to avoid Windows-specific collisions: use
> `Path.GetFullPath(path)` and compare with `StringComparer.OrdinalIgnoreCase` (Windows
> paths are case-insensitive — two passes yielding `C:\Users\x\.agents\skills\foo\SKILL.md`
> vs a differently-cased variant must collapse). Recommend
> `new Dictionary<string, SkillItem>(StringComparer.OrdinalIgnoreCase)`.
>
> The substring tables: rewrite the literals with `\` **or** normalize the candidate path's
> separators to `/` before testing (recommended — keeps the table identical to macOS):
> ```csharp
> var p = fullPath.Replace('\\', '/');
> if (p.Contains("/skills-cursor/")) return Cursor;
> // ... in the same order ...
> ```
> Use `StringComparison.OrdinalIgnoreCase` for the `Contains`/`EndsWith` checks since the
> dotfolders may differ in case on disk. Order matters — `.pi` is checked before `.cursor`
> etc.; preserve exact order.

### 1.6 Plugin-embedded skills (`scanPluginEmbeddedSkills`)

Scans `<home>/plugins/cache` for `cursor`, `claudeCode`, `codex`.

Algorithm:

```swift
let enumerator = enumerator(at: root, keys: [.isDirectoryKey], [.skipsHiddenFiles])
for case let dirURL as URL in enumerator {
    let name = dirURL.lastPathComponent
    guard name == "skills" || name.hasSuffix("skills") else { continue }   // a "skills" dir
    guard isDirectory else { continue }
    let skillFiles = contentsOfDirectory(at: dirURL, ...)                   // shallow listing
    // (a) every immediate SUBDIR that contains SKILL.md → a skill
    for skillDir in skillFiles where skillDir.hasDirectoryPath {
        let skillMD = skillDir.appendingPathComponent("SKILL.md")
        if fileExists(skillMD) { makeSkillItem(skillMD, isPluginEmbedded: true) }
    }
    // (b) a SKILL.md sitting directly in the skills dir → a skill
    for file in skillFiles where file.lastPathComponent == "SKILL.md" {
        makeSkillItem(file, isPluginEmbedded: true)
    }
}
```

- Recursively finds any directory named `skills` or ending in `skills` (e.g.
  `my-plugin-skills`) anywhere under `plugins/cache`.
- For each such dir it does a **one-level** listing and admits both
  `(<skillsDir>/<sub>/SKILL.md)` and a bare `(<skillsDir>/SKILL.md)`.
- All produced items are `isPluginEmbedded = true`, `isBuiltIn = false`.

> **Windows:**
> - `<home>\plugins\cache` for each of the three platforms.
> - Recursive dir enumeration → `Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)`.
> - `name == "skills" || name.hasSuffix("skills")` → `name.Equals("skills", OrdinalIgnoreCase) || name.EndsWith("skills", OrdinalIgnoreCase)`.
> - "shallow listing" → `Directory.EnumerateDirectories(dir)` (immediate subdirs) and
>   `Directory.EnumerateFiles(dir)` (immediate files). `hasDirectoryPath` is implicit when
>   iterating `EnumerateDirectories`.
> - Same `SKILL.md` case caveat as §1.3.

### 1.7 Building a `SkillItem` (`makeSkillItem`)

```swift
guard let content = try? String(contentsOf: skillPath, encoding: .utf8) else { return nil }
let (frontmatter, body) = FrontmatterParser.parse(from: content)
let folderName = skillPath.deletingLastPathComponent().lastPathComponent
let displayName = frontmatter.name ?? folderName
let description = frontmatter.description ?? FrontmatterParser.firstParagraph(from: body)
let rootDirectory = skillPath.deletingLastPathComponent()
let modifiedAt = try? skillPath.resourceValues(...).contentModificationDate
return SkillItem(
    id: makeID(platform, skillPath), platform, skillPath, rootDirectory,
    displayName,
    description: description.isEmpty ? "No description" : description,
    version: frontmatter.version,
    isBuiltIn, isPluginEmbedded, frontmatter, modifiedAt, alsoAvailableOn: [])
```

Field derivation (port these exactly):

- **Read failure → skip.** If the file can't be read as UTF-8, the skill is silently
  dropped (`return nil`).
- **`displayName`** = frontmatter `name` if present, else the **containing folder name**.
- **`description`** = frontmatter `description` if present, else the body's *first paragraph*
  (`firstParagraph`, §2.4). If the result is empty → literal `"No description"`.
- **`version`** = frontmatter `version` (nullable).
- **`rootDirectory`** = the directory containing `SKILL.md`.
- **`id`** = `"skill:<platform.rawValue>:<absolutePath>"` (`SkillItem.makeID`).
- `isBuiltIn` / `isPluginEmbedded` are passed in by the caller (NOT derived from content;
  see §3).
- `alsoAvailableOn` is `[]` here and filled in only during dedup.

> **Windows:**
> - Read UTF-8: `File.ReadAllText(path, new UTF8Encoding(false))` wrapped in try/catch
>   returning `null` on `IOException`/`UnauthorizedAccessException`/`DecoderFallbackException`.
>   macOS `String(contentsOf:encoding:.utf8)` fails on invalid UTF-8; to match, use a
>   throwing decoder: `new UTF8Encoding(encoderShouldEmitUTF8Identifier:false,
>   throwOnInvalidBytes:true)`. (If you prefer leniency, default `File.ReadAllText` uses a
>   replacement-char decoder and will *not* drop the skill — a minor behavior difference;
>   recommend the throwing decoder for parity, but it's low-stakes.)
> - `deletingLastPathComponent().lastPathComponent` → for the folder name use
>   `new DirectoryInfo(Path.GetDirectoryName(skillPath)!).Name`; for `rootDirectory` use
>   `Path.GetDirectoryName(skillPath)!`.
> - `SkillItem` → a C# `record` (immutable, value equality matches Swift `Equatable`).
>   `makeID` → string interpolation `$"skill:{platform}:{path}"`. Use the enum's string form
>   matching Swift `rawValue` (`cursor`, `claudeCode`, …) — implement a `RawValue` extension
>   so IDs are stable/portable, **not** `.ToString()` (which yields `Cursor`, `ClaudeCode`).

### 1.8 `markdownFiles(in:)` — the editor's file list

```swift
static func markdownFiles(in rootDirectory: URL) -> [SkillMarkdownFile]
```

- Recursively enumerates `rootDirectory`, collecting every file whose extension is `md`
  (case-insensitive: `pathExtension.lowercased() == "md"`).
- `isPrimary = (lastPathComponent == "SKILL.md")`.
- If enumeration fails, returns a single synthetic entry pointing at
  `rootDirectory/SKILL.md` marked primary.
- **Sort:** primary first (`lhs.isPrimary` wins), then by path
  case-insensitive ascending.

> **Windows:** `Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)` filtered
> by `Path.GetExtension(f).Equals(".md", OrdinalIgnoreCase)`. `isPrimary` via filename
> ordinal-equals `SKILL.md`. Sort: `OrderByDescending(f => f.IsPrimary)
> .ThenBy(f => f.Url, OrdinalIgnoreCase)`. Fallback entry when the directory is unreadable.
> `SkillMarkdownFile` → record with `Id = path`, `DisplayName = Path.GetFileName(path)`.

---

## 2. Frontmatter parsing (`FrontmatterParser`) — the mini-YAML grammar

This is **NOT a YAML library.** It is a tiny, deliberately limited line parser. The exact
grammar (port faithfully):

### 2.1 Delimiter detection

```swift
guard content.hasPrefix("---") else { return (SkillFrontmatter(), content) }
let lines = content.split(separator: "\n", omittingEmptySubsequences: false)
guard lines.count >= 2 else { return (empty, content) }
// find first line (index >= 1) that EQUALS "---"
```

- The file must **start** with `---` (prefix, not exact-line — `------` or `--- x` also
  passes `hasPrefix`, though only an exact `---` closes it).
- Lines are split on `\n` (LF), keeping empty lines.
- The closing delimiter is the **first line (starting at index 1) that is exactly `"---"`**
  (after the split — note: no trimming, so a line `"--- "` with trailing space does NOT
  close it; `"---\r"` on CRLF files would NOT match — see Windows note).
- If no closing `---` is found → entire content treated as body, empty frontmatter.

> **Windows / line-ending caveat (CRITICAL):** macOS files are LF. Windows editors
> (including AvalonEdit defaults and Notepad) often produce **CRLF**. The macOS parser
> splits on `\n` only, leaving a trailing `\r` on each line; then `lines[index] == "---"`
> would compare `"---\r" == "---"` → **false**, so a CRLF file's frontmatter would never
> close and would be treated entirely as body. **You MUST normalize line endings or trim
> `\r` to match intent.** Recommended port:
> ```csharp
> var lines = content.Split('\n');           // keep empties
> string Norm(string s) => s.TrimEnd('\r');   // strip CR for delimiter compare
> ```
> Compare `Norm(line) == "---"` for the close, and likewise trim `\r` when extracting
> values. This is a *necessary divergence* to keep behavior correct on Windows; document it.
> Keep `content.StartsWith("---", StringComparison.Ordinal)` for the opening check.

### 2.2 Per-line key/value extraction

For each line strictly between the delimiters (`yamlLines = lines[1..<endIndex]`):

```swift
let trimmed = line.trimmingCharacters(in: .whitespaces)
if trimmed.isEmpty || trimmed.hasPrefix("#") { continue }   // skip blank & comment lines
if let colon = trimmed.firstIndex(of: ":") {
    let key   = trimmed[..<colon].trimmed
    var value = trimmed[after colon...].trimmed
    if value.hasPrefix(">") || value.hasPrefix("|") { value = "" }   // block scalars → empty
    value = value.trimmingCharacters(in: CharacterSet(charactersIn: "\"'"))   // strip quotes
    switch key { name / description / version / disable-model-invocation }
}
```

Grammar rules to preserve **exactly**:

1. Each line is leading/trailing whitespace-trimmed.
2. Blank lines and lines beginning with `#` (after trim) are skipped (comments).
3. A line is a key/value **iff it contains a `:`**. Split on the **first** colon. (So a value
   may contain colons; the key is everything before the first colon.)
4. Key and value are each whitespace-trimmed.
5. **Block-scalar indicators:** if the value begins with `>` or `|` (YAML folded/literal
   block markers), the value is set to **empty string** — the parser does **NOT** read the
   following indented continuation lines. Multi-line frontmatter values are effectively
   collapsed to empty on read (the *writer* can emit them via `>` — §2.5 — but the *reader*
   then returns empty, losing the value on round-trip; this is existing behavior).
6. **Quote stripping:** leading/trailing `"` and `'` characters are stripped from the value
   (via `CharacterSet` trim — note this strips *any* leading/trailing run of `"`/`'`, not a
   balanced pair, and does **not** unescape `\"`).
7. Only four keys are recognized; everything else is ignored:

| Key | Field | Conversion |
|---|---|---|
| `name` | `frontmatter.name` | `value.isEmpty ? nil : value` |
| `description` | `frontmatter.description` | `value.isEmpty ? nil : value` |
| `version` | `frontmatter.version` | `value.isEmpty ? nil : value` |
| `disable-model-invocation` | `frontmatter.disableModelInvocation` (`Bool`) | `["true","yes","1"].contains(value.lowercased())` |

> Note the on-disk key is **`disable-model-invocation`** (hyphenated, lowercase). The Swift
> field is `disableModelInvocation`. The task brief mentions `disableModelInvocation` — that
> is the *field* name; the *YAML key* is the hyphenated form. Recognize only the hyphenated
> key on read.

### 2.3 Body extraction

```swift
let bodyStart = endIndex + 1
let body = bodyStart < lines.count ? lines[bodyStart...].joined(separator: "\n") : ""
```

Everything after the closing `---` line, re-joined with `\n`. Leading blank line(s) after the
delimiter are preserved here (trimming happens later in the writer).

> **Windows:** join with `"\n"` to keep internal representation LF-normalized (recommended),
> or `Environment.NewLine`. Recommend **always normalizing to `\n` internally** and only
> converting on final write if desired — but note the original always writes `\n`, so keep
> `\n` end-to-end for byte-parity (§4.5).

### 2.4 `firstParagraph(from body:)`

Used to synthesize a description when frontmatter has none:

```swift
let trimmed = body.trimmingCharacters(in: .whitespacesAndNewlines)
guard !trimmed.isEmpty else { return "" }
let paragraphs = trimmed.components(separatedBy: "\n\n")   // split on blank line
let first = paragraphs.first.trimmed
let singleLine = first.split(on newlines)
    .map { trim each line }
    .filter { !empty && !hasPrefix("#") }   // drop heading lines
    .joined(separator: " ")
return String(singleLine.prefix(280))       // cap at 280 chars
```

- Splits the body into paragraphs on the first blank line (`\n\n`).
- Takes the first paragraph, drops any heading lines (`#`…), joins remaining lines with a
  single space, caps to 280 characters.

> **Windows:** `body.Trim()` (Swift `.whitespacesAndNewlines` ≈ `char.IsWhiteSpace`; use
> `Trim()`). Split paragraphs on `"\n\n"` (after CRLF→LF normalization, else also handle
> `"\r\n\r\n"`). Lines → `string.Split('\n')`, trim each, filter empties and
> `StartsWith("#")`, `string.Join(" ", …)`, then `.Substring(0, Math.Min(280, len))` or
> `s.Length <= 280 ? s : s[..280]`. **Normalize CRLF first** so `\n\n` paragraph splitting
> works (another reason to LF-normalize on read).

### 2.5 Round-trip caveat to carry over

Because the reader collapses `>`/`|` block scalars to empty (§2.2 rule 5) but the writer can
*emit* a multi-line description as a `>` block (§3.3), a multi-line description **does not
survive a read→write→read cycle**: it is written as a block, then read back as empty. This is
existing macOS behavior. **Preserve it** unless the reviewer wants it fixed (it's arguably a
bug; flag it but match by default).

---

## 3. Frontmatter writing (`FrontmatterWriter`)

### 3.1 `Update` struct

```swift
struct Update { var name, description, version: String?; var disableModelInvocation: Bool? }
```

A partial patch — only non-nil fields are applied.

> **Windows:** `record struct FrontmatterUpdate(string? Name, string? Description,
> string? Version, bool? DisableModelInvocation)` or a class with nullable props.

### 3.2 `apply(to:update:)` — merge + reserialize

```swift
let (existing, body) = FrontmatterParser.parse(from: content)
var merged = existing
if let name = update.name { merged.name = name }
if let description = update.description { merged.description = description }
if let version = update.version { merged.version = version.isEmpty ? nil : version }   // empty clears
if let disable = update.disableModelInvocation { merged.disableModelInvocation = disable }
let yaml = serialize(merged)
let trimmedBody = body.trimmingCharacters(in: .newlines)
if trimmedBody.isEmpty { return "---\n\(yaml)---\n" }
return "---\n\(yaml)---\n\n\(trimmedBody)\n"
```

- Re-parses existing content, overlays the non-nil update fields.
- **`version`**: an empty-string update clears it to `nil` (other fields set even if empty —
  but `serialize` then skips empty `name`/`description`, see §3.3).
- Body is trimmed of surrounding newlines. If empty → `---\n<yaml>---\n` (no body). Else
  `---\n<yaml>---\n\n<body>\n` (exactly one blank line between frontmatter and body, trailing
  newline).

> **Windows:** all literal `"\n"` must stay LF for byte-parity (see §4.5). Build with a
> `StringBuilder` or interpolated strings using `"\n"` explicitly (NOT `Environment.NewLine`,
> which is `\r\n` on Windows and would change output bytes). `body.Trim('\n', '\r')` for the
> trim (Swift `.newlines` ≈ trim `\n` and `\r`).

### 3.3 `serialize(_:)` — the emitter

```swift
var lines: [String] = []
if let name = fm.name, !name.isEmpty        { lines.append("name: \(quoteIfNeeded(name))") }
if let description = fm.description, !description.isEmpty {
    if description.contains("\n") {
        lines.append("description: >")
        lines.append(contentsOf: description.components(separatedBy: "\n"))   // raw, unindented
    } else {
        lines.append("description: \(quoteIfNeeded(description))")
    }
}
if let version = fm.version, !version.isEmpty { lines.append("version: \(quoteIfNeeded(version))") }
if let disable = fm.disableModelInvocation { lines.append("disable-model-invocation: \(disable ? "true" : "false")") }
if lines.isEmpty { lines.append("name: skill") }    // never emit empty frontmatter
return lines.joined(separator: "\n") + "\n"
```

Rules:

- Emits keys in fixed order: `name`, `description`, `version`, `disable-model-invocation`.
- Empty/nil `name`, `description`, `version` are omitted.
- **Multi-line description** → emitted as a YAML folded block: a line `description: >` then
  each original line appended **verbatim and unindented** (note: this produces technically
  malformed YAML indentation, and is exactly what the reader then ignores → empty on
  re-read; §2.5). Single-line description → inline with quoting.
- `disable-model-invocation` is only emitted when non-nil, as literal `true`/`false`.
- If nothing was emitted, fall back to `name: skill` (guarantees valid non-empty
  frontmatter).
- Result ends with a trailing `\n`.

### 3.4 `quoteIfNeeded(_:)`

```swift
if value.contains(":") || value.contains("#") || value.hasPrefix(" ") {
    return "\"\(value.replacingOccurrences(of: "\"", with: "\\\""))\""
}
return value
```

- Wraps the value in double quotes (escaping inner `"` as `\"`) **iff** it contains `:`, `#`,
  or starts with a space. Otherwise emits raw.

> **Windows:** `value.Contains(':') || value.Contains('#') || value.StartsWith(" ")` →
> `"\"" + value.Replace("\"", "\\\"") + "\""`. Direct translation.

### 3.5 `make(name:description:body:)` — new-file template

```swift
let trimmedBody = body.trimmingCharacters(in: .whitespacesAndNewlines)
let bodyText = trimmedBody.isEmpty
    ? "# \(name)\n\nDescribe when to use this skill."
    : trimmedBody
return apply(to: "---\nname: skill\n---\n\n\(bodyText)\n",
             update: Update(name: name, description: description))
```

- Builds a seed document with placeholder frontmatter `name: skill` and either the provided
  body or a default `"# <name>\n\nDescribe when to use this skill."`, then runs it through
  `apply` with the real name+description. Net result: a fully formed `SKILL.md` with correct
  `name`/`description` and the body.

> **Windows:** straightforward; keep `\n` literals.

---

## 4. Create / Rename / Delete / Metadata semantics (`SkillFileService`)

`AppBrand.name == "Skills"`, interpolated into all user-facing messages.

> **Windows:** keep `AppBrand.Name` as a constant; user messages reference "Skills". Replace
> macOS-specific phrasing: "Check folder permissions in Finder" → "Check folder permissions
> in File Explorer"; "on this Mac" (NewSkillSheet) → "on this PC". (These appear in
> `FileAccessError` and `SkillFileService`/UI; update copy for Windows.)

### 4.1 Capability gates

**`canModify(skill)`** (controls rename + delete):
```swift
guard !skill.isBuiltIn, isFolderBackedSkill(skill) else { return false }
return isWritableFile(skill.rootDirectory) && isWritableFile(skill.rootDirectory.parent)
```
- Must NOT be built-in.
- Must be **folder-backed** (`isFolderBackedSkill`, §4.6).
- Both the skill's folder *and its parent* must be writable.

**`canEditMetadata(skill)`**: `isWritableFile(skill.skillPath)` — just the `SKILL.md` file
must be writable. (Plugin-embedded/single-file skills can still have metadata edited if the
file is writable.)

> **Windows:** `FileManager.isWritableFile(atPath:)` checks POSIX write permission. There is
> **no direct .NET equivalent**. Faithful port options:
> 1. **Attribute check (cheap, partial):** `(File.GetAttributes(p) & FileAttributes.ReadOnly) == 0`
>    plus `Directory.Exists`/`File.Exists`. This catches the read-only flag but NOT NTFS ACL
>    denials.
> 2. **ACL probe (accurate):** attempt to open the file/dir for write
>    (`new FileStream(p, FileMode.Open, FileAccess.Write)` in a try/catch) or, for dirs, try
>    creating+deleting a temp file. This mirrors POSIX write-permission semantics best.
> 3. **Optimistic:** skip the pre-check and rely on the actual operation throwing
>    `UnauthorizedAccessException`, mapped via `FileAccessError` (§5).
> Recommended: combine (1) for the obvious read-only case + rely on try/catch for ACLs, and
> expose `canModify`/`canEditMetadata` as best-effort. Document that exact parity with POSIX
> `access(W_OK)` is not guaranteed on NTFS.

### 4.2 Blocked-reason messages

- `modificationBlockedReason`: built-in → "Built-in Cursor skills cannot be renamed or
  deleted from Skills."; not folder-backed → "This skill is stored as a single SKILL.md
  file, so only its metadata can be edited from Skills."; else → "This skill folder is not
  writable by Skills. Check file permissions or edit it in its install folder."
- `metadataBlockedReason`: empty if editable; else "This SKILL.md file is not writable by
  Skills. Check file permissions or edit it in its install folder."

### 4.3 `renameSkill(skill, newFolderName) -> URL`

```swift
guard canModify(skill) else { throw .blocked(modificationBlockedReason) }
let validated = try SkillNameValidator.validate(newFolderName).get()
let parent = skill.rootDirectory.parent
let newRoot = parent/validated
if fileExists(newRoot) { throw .duplicateName("A skill named \"<validated>\" already exists in this location.") }
moveItem(skill.rootDirectory -> newRoot)            // renames the directory
let newSkillPath = newRoot/"SKILL.md"
if fileExists(newSkillPath) {
    let content = read(newSkillPath)
    let updated = FrontmatterWriter.apply(content, Update(name: validated))   // sync frontmatter name
    write(updated -> newSkillPath, atomic, utf8)
}
return newRoot
```

- Gate on `canModify`; validate the name; reject if target folder already exists.
- **Renames the entire skill folder** (move), then rewrites the `SKILL.md` frontmatter
  `name` to the new folder name.
- Returns the new root URL.

> **Windows:** `Directory.Move(oldRoot, newRoot)`. Pre-check `Directory.Exists(newRoot)` for
> the duplicate error. **Case-only renames** (e.g. `foo` → `Foo`) are a Windows hazard:
> `Directory.Move` to a path that differs only by case throws `IOException` because
> `Directory.Exists` is case-insensitive and reports the source as already existing. Handle
> case-only renames specially (move to temp then to final, or detect `OrdinalIgnoreCase`
> equality of old/new and allow). The atomic write → write to temp + `File.Replace`/`File.Move`
> with overwrite, or `File.WriteAllText` (see §4.5). Read uses UTF-8 (§1.7).

### 4.4 `deleteSkill(skill)`

```swift
guard canModify(skill) else { throw .blocked(...) }
removeItem(skill.rootDirectory)     // deletes the whole folder
```

> **Windows:** `Directory.Delete(rootDirectory, recursive: true)`. Consider routing to the
> Recycle Bin instead of permanent delete for a better Windows UX (optional — macOS does a
> hard `removeItem`; for parity, hard-delete, but a Recycle-Bin option via
> `Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(..., RecycleOption.SendToRecycleBin)`
> is a reasonable, user-friendly enhancement — flag as a product decision).

### 4.5 `updateMetadata(skill, name, description, version)`

```swift
guard canEditMetadata(skill) else { throw .blocked(metadataBlockedReason) }
let validatedName = try SkillNameValidator.validate(name).get()
let content = read(skill.skillPath)
let updated = FrontmatterWriter.apply(content, Update(
    name: validatedName,
    description: description.trimmed(.whitespacesAndNewlines),
    version: version?.trimmed(.whitespacesAndNewlines)))
write(updated -> skill.skillPath, atomic, utf8)
```

- Validates name; trims description and (optional) version; applies via the writer; writes
  back **atomically**. Does **not** rename the folder (metadata-only).

> **Atomic write (applies to rename, updateMetadata, create, EditorDocument.save):**
> Swift `write(to:atomically:true)` writes to a temp file then renames into place. On
> Windows, faithful atomic-ish replace:
> ```csharp
> var tmp = path + ".tmp";
> File.WriteAllText(tmp, content, new UTF8Encoding(false)); // no BOM
> if (File.Exists(path)) File.Replace(tmp, path, null);     // atomic on same volume
> else File.Move(tmp, path);
> ```
> **No BOM:** macOS writes UTF-8 without BOM. Use `new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)`
> — the default `Encoding.UTF8` emits a BOM via some APIs; `File.WriteAllText(path, s)` with
> the default does NOT add a BOM, but be explicit. **Line endings:** the writer emits `\n`;
> `File.WriteAllText` writes the string verbatim, so `\n` stays `\n` (good — byte parity).
> Do not let AvalonEdit or any normalization convert to `\r\n` on save unless you also accept
> the divergence.

### 4.6 `isFolderBackedSkill(skill)` (private)

```swift
skill.rootDirectory/"SKILL.md" == skill.skillPath          // the SKILL.md is the canonical file
  && skill.rootDirectory.lastPathComponent != "skills"     // not directly inside a "skills" dir
  && !skill.rootDirectory.lastPathComponent.hasSuffix("skills")
```

A skill is "folder-backed" (and thus renamable/deletable) iff its `SKILL.md` lives in a
dedicated folder whose name isn't `skills`/`*skills`. This excludes the plugin-embedded
"bare `SKILL.md` directly in a skills dir" case (§1.6b) from folder operations.

> **Windows:** compare `Path.Combine(rootDirectory, "SKILL.md")` to `skillPath` with
> `OrdinalIgnoreCase` (Windows paths). `new DirectoryInfo(rootDirectory).Name` for
> `lastPathComponent`; `Equals("skills", OrdinalIgnoreCase)` and `EndsWith("skills", OrdinalIgnoreCase)`.

### 4.7 `createSkill(name, description, body, platforms) -> [URL]`

```swift
guard !platforms.isEmpty else { throw .validation("Select at least one platform.") }
let validatedName = try SkillNameValidator.validate(name).get()
let fileContent = FrontmatterWriter.make(name: validatedName, description: trimmedDescription, body: body)
for platform in platforms.sorted(by displayName) {
    let skillsRoot = platform.userSkillsDirectory
    createDirectory(skillsRoot, withIntermediateDirectories: true)
    let skillDir = skillsRoot/validatedName
    if fileExists(skillDir) { duplicatePlatforms.append(platform.displayName); continue }
    createDirectory(skillDir, withIntermediateDirectories: true)
    write(fileContent -> skillDir/"SKILL.md", atomic, utf8)
    createdPaths.append(skillDir/"SKILL.md")
}
if createdPaths.isEmpty {
    throw .duplicateName("A skill named \"<name>\" already exists on: <dup platforms>.")
}
return createdPaths
```

Semantics:

- At least one platform required.
- For each selected platform (iterated sorted by `displayName`): ensure
  `userSkillsDirectory` exists, then create `<userSkillsDirectory>/<validatedName>/SKILL.md`.
- If the skill folder already exists for a platform, that platform is **skipped** and noted;
  other platforms still proceed (partial success).
- If **every** platform was a duplicate (`createdPaths` empty), throw `duplicateName` listing
  them. Otherwise return the list of created `SKILL.md` paths.
- The created file is the `FrontmatterWriter.make` template.

> **Windows:** `Directory.CreateDirectory` (idempotent, = `withIntermediateDirectories`).
> Duplicate check `Directory.Exists(skillDir)`. Sort platforms by `DisplayName`
> (`OrderBy(p => p.DisplayName, StringComparer.Ordinal)` — Swift `<` on String is Unicode
> codepoint order; `StringComparer.Ordinal` matches more closely than culture-aware).
> Return `IReadOnlyList<string>`.

### 4.8 Error type

```swift
enum SkillFileError: LocalizedError { case blocked(String), duplicateName(String), validation(String) }
```
`errorDescription` returns the wrapped message.

> **Windows:** a custom `class SkillFileException : Exception` with a `Kind`
> (`Blocked|DuplicateName|Validation`) enum, or three subclasses. `errorDescription` →
> `Exception.Message`. Surface via the ViewModel to a WPF-UI `InfoBar`/dialog.

---

## 5. Error mapping (`FileAccessError`)

```swift
static func userMessage(for error: Error) -> String
```
Maps `NSError`:
- `NSCocoaErrorDomain` + `NSFileReadNoPermissionError`/`NSFileWriteNoPermissionError` → "Skills
  doesn't have permission to access this file. Check folder permissions in Finder."
- `NSFileWriteVolumeReadOnlyError` → "This volume is read-only. Skills can't save changes
  here."
- `NSFileNoSuchFileError` → "The file no longer exists. Try refreshing the catalog."
- `NSPOSIXErrorDomain` + `EACCES` → same permission message.
- else → `error.localizedDescription`.

> **Windows mapping table:**
> | macOS error | .NET exception | Windows message |
> |---|---|---|
> | NSFileRead/WriteNoPermissionError, EACCES | `UnauthorizedAccessException`, `IOException` w/ ACCESS_DENIED (HResult `0x80070005`) | "Skills doesn't have permission to access this file. Check folder permissions in File Explorer." |
> | NSFileWriteVolumeReadOnlyError | `IOException` w/ ERROR_WRITE_PROTECT (HResult `0x80070013`), or read-only attribute | "This volume is read-only. Skills can't save changes here." |
> | NSFileNoSuchFileError | `FileNotFoundException`, `DirectoryNotFoundException` | "The file no longer exists. Try refreshing the catalog." |
> | else | any | `exception.Message` |
> Implement by `switch (error)` on exception type, plus inspecting `ex.HResult` for the
> IOException sub-cases. Replace "Finder" → "File Explorer".

---

## 6. Editor buffer & autosave (`EditorDocument`)

A `@MainActor ObservableObject` backing the markdown editor.

State:
- `text` (current buffer), `fileURL`, `isDirty`, `saveStatus` (`saved|saving|failed(String)`),
  private `savedText`, `autosaveTask`, `autosavePaused`, `debounceSeconds = 1.2`.

Behavior:
- **`load(url)`**: cancel autosave; set `fileURL`; read file UTF-8 (empty string on failure);
  `text = savedText = content`; `isDirty=false`; `saveStatus=.saved`.
- **`updateText(new)`**: set `text`; `isDirty = (text != savedText)`. If not dirty →
  `saved`. If dirty and not paused → `scheduleAutosave`.
- **`pauseAutosave` / `resumeAutosave`**: pause flag + cancel; resume re-schedules if dirty.
- **`save()`** (throws): `saveStatus=.saving`; `text.write(fileURL, atomic, utf8)`;
  `savedText=text`; `isDirty=false`; `saved`.
- **`saveImmediately()`**: cancel autosave; if not dirty → `saved`, return true; else try
  `save()`, on error `saveStatus=.failed(FileAccessError.userMessage(error))`, return
  false.
- **`discardChanges()`**: cancel autosave; `text=savedText`; not dirty; `saved`.
- **`scheduleAutosave()`**: cancel existing; `saving`; spawn a `Task` that sleeps
  **1.2 s** (hardcoded `1.2 * 1e9` ns — note `debounceSeconds` constant exists but the sleep
  uses the literal), checks cancellation, then `performAutosave` on MainActor.
- **`performAutosave()`**: if not paused and still dirty → `save()` (catch → `.failed(...)`).

> **Windows / WPF (MVVM):**
> - `ObservableObject` → WPF-UI / CommunityToolkit.Mvvm `ObservableObject` with
>   `[ObservableProperty]` for `Text`, `FileUrl`, `IsDirty`, `SaveStatus`.
> - `@MainActor` → all mutation on the WPF UI/dispatcher thread. The autosave timer callback
>   must marshal back via `Dispatcher.Invoke`/`async` continuation on the captured
>   `SynchronizationContext`.
> - Debounced autosave → a `System.Threading.Timer`/`DispatcherTimer` reset on each
>   `UpdateText`, **or** a `CancellationTokenSource` + `Task.Delay(1200, token)` pattern that
>   mirrors the Swift cancel/reschedule exactly:
>   ```csharp
>   _cts?.Cancel(); _cts = new();
>   var token = _cts.Token;
>   _ = Task.Delay(1200, token).ContinueWith(t => {
>       if (!t.IsCanceled) Dispatcher.Invoke(PerformAutosave);
>   }, TaskScheduler.Default);
>   ```
>   Use `1200` ms (the literal the source actually sleeps); expose `DebounceSeconds = 1.2`.
> - `SaveStatus` → an enum + optional message, or a small DU-like record
>   (`record SaveStatus(SaveStatusKind Kind, string? Error)`).
> - File write → atomic UTF-8 no-BOM, `\n` preserved (§4.5).
> - **AvalonEdit integration:** AvalonEdit's `TextEditor.Document.Text` is the source of
>   truth. Bind/sync it to `EditorDocument.Text`. On `TextChanged`, call `UpdateText`.
>   Set `Options.ConvertTabsToSpaces` etc. as desired, but ensure the editor does NOT rewrite
>   line endings on you (set `document.Text` from the LF-normalized string; AvalonEdit
>   preserves the existing newline style unless told otherwise — watch this for parity).
> - The macOS UI (`MarkdownEditorView`, `NewSkillSheet`) uses SwiftUI `TextEditor`. On
>   Windows these become a WPF-UI page hosting `AvalonEdit` for the markdown body and
>   WPF-UI `TextBox`es for name/description, with platform checkboxes (the New Skill sheet)
>   bound to `ObservableCollection`/flags.

---

## 7. Skill-name validation (`SkillNameValidator`)

```swift
static func validate(_ name: String) -> Result<String, SkillNameValidationError>
```

Rules, in order:

1. **Trim** leading/trailing whitespace and newlines → `trimmed`.
2. `trimmed.isEmpty` → fail **"Name cannot be empty."**
3. `trimmed == "." || trimmed == ".."` → fail **"Name is not valid."**
4. `trimmed.hasPrefix(".")` → fail **"Name cannot start with a dot."**
5. **Allowed characters:** `[A-Za-z0-9-_]` only (ASCII letters, digits, hyphen, underscore).
   Any character outside this set → fail **"Use only letters, numbers, hyphens, and
   underscores."**
6. Success → return the **trimmed** name (this trimmed value is what gets used as the folder
   name and written to frontmatter).

Notes:
- **No length limit** is enforced (the brief asked about length — there is none).
- **No reserved-name list** beyond `.`, `..`, and the dot-prefix rule. (No `CON`, `NUL`,
  etc. — that's a Windows concern; see below.)
- Spaces are not allowed (space is not in the allowed set), and trimming only affects
  the ends, so `"a b"` fails on the space.

> **Windows:** direct port. Allowed set:
> ```csharp
> static bool IsAllowed(char c) =>
>     (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
>     (c >= '0' && c <= '9') || c == '-' || c == '_';
> ```
> Trim with `name.Trim()`. Return a `Result`-like type (e.g. `OneOf`, or
> `(bool ok, string value, string? error)`, or throw a `SkillNameValidationException`).
>
> **Windows reserved-name hazard (RECOMMENDED ADDITION, not in macOS source):** the macOS
> validator does NOT block Windows-reserved device names, but since the validated name
> becomes a **directory name** on disk, you should additionally reject the Win32 reserved
> names to avoid `IOException` at create time: `CON, PRN, AUX, NUL, COM1..COM9, LPT1..LPT9`
> (case-insensitive, with or without extension). Also note Windows already disallows trailing
> dots/spaces in folder names — but the allowed-char set already forbids dots/spaces, so the
> only net-new risk is the reserved device names. Flag this as a Windows-specific hardening
> beyond the macOS spec.

---

## 8. Concrete `SKILL.md` examples (input/output)

### 8.1 Typical input file

```markdown
---
name: code-review
description: Review the current diff for bugs and cleanups.
version: 1.2.0
disable-model-invocation: false
---

# Code Review

Use this skill when the user asks to review a diff or PR for correctness.

## Steps
1. ...
```

Parsed `SkillFrontmatter`:
- `name = "code-review"`, `description = "Review the current diff for bugs and cleanups."`,
  `version = "1.2.0"`, `disableModelInvocation = false`.

Resulting `SkillItem`:
- `displayName = "code-review"`, `description` = the frontmatter description,
  `version = "1.2.0"`, `rootDirectory` = the folder, `isBuiltIn/isPluginEmbedded` per source.

### 8.2 No frontmatter (description synthesized from body)

Input:
```markdown
# My Helper

This helper formats JSON.

Second paragraph ignored.
```
- `hasPrefix("---")` is false → empty frontmatter, body = whole file.
- `displayName` falls back to the **folder name**.
- `description` = `firstParagraph` = `"This helper formats JSON."` (heading dropped, first
  paragraph only, ≤280 chars). If somehow empty → `"No description"`.

### 8.3 Quoting on write

`updateMetadata` with `name="my-skill"`, `description="Handles a:b and # tags"`,
`version=""` produces (note version cleared, description quoted because it contains `:` and
`#`):
```markdown
---
name: my-skill
description: "Handles a:b and # tags"
---

<existing body, trimmed>
```

### 8.4 Multi-line description (round-trip lossiness — preserve behavior)

If a description containing a newline is written, the emitter produces:
```markdown
---
name: x
description: >
First line.
Second line.
---
```
…but the **parser** reads `description: >` → value `""` and ignores the continuation lines, so
on re-scan `description` is empty and falls back to the body's first paragraph or
`"No description"`. (Documented existing behavior — see §2.5.)

### 8.5 Newly created skill (`FrontmatterWriter.make`) with empty body

`createSkill(name:"hello-world", description:"Greets the user", body:"")` writes:
```markdown
---
name: hello-world
description: Greets the user
---

# hello-world

Describe when to use this skill.
```
…at `<userSkillsDirectory>/hello-world/SKILL.md` for each selected platform.

---

## 9. Should Windows use YamlDotNet?

**Recommendation: NO — port the custom mini-parser/emitter faithfully.** Reasons:

- **The grammar is intentionally non-standard and lossy.** Behaviors that real YAML would
  change: block scalars (`>`/`|`) are collapsed to empty on read; quote-stripping strips any
  leading/trailing `"`/`'` run without balanced-pair or escape handling; only four keys are
  recognized; unknown keys silently ignored; the value is everything after the *first* colon;
  comments (`#`) handled only as full-line. A real YAML lib (YamlDotNet) would: parse block
  scalars correctly (changing `description` round-trip), interpret types (e.g. `version: 1.0`
  → a float/double, `disable-model-invocation: yes` → boolean per YAML 1.1, `name: true` →
  bool), error on the deliberately-malformed unindented block the *writer* emits, reorder/
  reformat keys, and handle quoting differently. Any of these **changes observed behavior**
  and could corrupt user files written by the macOS app.
- **The writer emits technically-invalid YAML on purpose** (unindented `>` block). YamlDotNet
  would refuse to parse files this app itself produced.
- **Determinism & byte-parity matter** for round-tripping files shared between the macOS and
  Windows apps.

So: implement `FrontmatterParser` / `FrontmatterWriter` as a direct C# line-by-line port
(≈80 lines), matching the rules in §2–§3 exactly, with the **CRLF normalization** fix
(§2.1 Windows note) as the one necessary, well-documented divergence. If a future cleanup
wants real YAML, that's a product decision that must account for the round-trip behavior
changes above — flag it, don't silently adopt YamlDotNet.

---

## 10. Port checklist (load-bearing details not to lose)

- [ ] Six platforms, dotfolders under `%USERPROFILE%`; `userSkillsDirectory` exceptions:
      Pi = `.pi\agent\skills`; others = `<home>\skills`.
- [ ] Scan roots include shared `~/.agents\skills` for codex+pi, and OpenClaw workspace
      skills *if present*.
- [ ] Fully recursive walk; match file named `SKILL.md` (decide case policy, recommend
      case-insensitive match but always **write** uppercase `SKILL.md`).
- [ ] Skip hidden (dot) entries; descend into `.system` but apply codex `hideSystem` filter.
- [ ] Built-in Cursor scan = `~/.cursor\skills-cursor` with `isBuiltIn=true`, gated by
      `hideBuiltInCursor`.
- [ ] Plugin-embedded scan = `<home>\plugins\cache`, find dirs named `*skills`, admit
      `<dir>\<sub>\SKILL.md` and bare `<dir>\SKILL.md`, `isPluginEmbedded=true`.
- [ ] Dedup by full path (OrdinalIgnoreCase), reassign `platform=primaryPlatform`, set
      `alsoAvailableOn` from `platformsThatShare`; preserve the ordered substring tables.
- [ ] `displayName = name ?? folderName`; `description = description ?? firstParagraph ?? "No description"`.
- [ ] Mini-YAML: 4 keys (`name`, `description`, `version`, `disable-model-invocation`);
      block scalars → empty; first-colon split; quote-trim; comment/blank skip.
- [ ] Writer: fixed key order; omit empty name/desc/version; multi-line desc → `>` block
      (verbatim, lossy); `quoteIfNeeded` on `:`/`#`/leading-space; fallback `name: skill`.
- [ ] CRLF normalization in parser (necessary Windows divergence).
- [ ] Atomic UTF-8 **no-BOM** writes, `\n` line endings preserved (don't let AvalonEdit
      rewrite to CRLF).
- [ ] Create: per-platform folder+SKILL.md, partial success, duplicate listing error.
- [ ] Rename: gate→validate→reject-existing→move-folder→rewrite frontmatter name; handle
      case-only renames on NTFS.
- [ ] Delete: recursive folder delete (consider Recycle Bin as enhancement).
- [ ] Metadata edit: file-writable gate; validate name; trim desc/version; atomic write.
- [ ] `isFolderBackedSkill` excludes `skills`/`*skills` dirs and single-file skills from
      folder ops.
- [ ] Name validation: trim; non-empty; not `.`/`..`; no leading dot; `[A-Za-z0-9-_]` only;
      no length limit; ADD Win32 reserved-name rejection.
- [ ] EditorDocument: 1.2 s debounced autosave, pause/resume, dirty tracking,
      saveImmediately/discard, error→`FileAccessError.userMessage`.
- [ ] FileAccessError: map `UnauthorizedAccessException`/`FileNotFoundException`/read-only/
      HResults to the four messages; "Finder" → "File Explorer".
- [ ] Replace macOS copy ("on this Mac", "in Finder") with Windows wording.
```
