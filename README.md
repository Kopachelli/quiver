# Skills for Windows

A native **Windows** desktop app for browsing, editing, and managing local AI‑tool assets —
**skills**, **MCP servers**, and **plugins** — across Cursor, Claude Code, Codex, Hermes, Pi,
and OpenCode. It is a faithful Windows port of the macOS app
[skillz-macos](https://github.com/robzilla1738/skillz-macos), rebuilt as a proper native app
(no web view, no Electron).

The app scans the agent dotfolders under your home directory
(`%USERPROFILE%\.cursor`, `\.claude`, `\.codex`, `\.hermes`, `\.pi`, `\.openclaw`, plus the
shared `\.agents\skills`) and gives you one place to inspect and edit the files these tools
otherwise scatter across hidden folders.

## Tech stack

| Concern | Choice |
|---|---|
| Runtime / UI | **WPF on .NET 8 (LTS)** — genuinely native |
| Fluent theming / window | **WPF‑UI** (`FluentWindow`, Mica backdrop, `TitleBar`, `SymbolIcon`) |
| MVVM | **CommunityToolkit.Mvvm** |
| DI | **Microsoft.Extensions.DependencyInjection** |
| Markdown editor | **AvalonEdit** |
| Config parsing | **System.Text.Json** (mcp.json / plugin manifests) + a faithful port of the macOS mini‑TOML parser for Codex `config.toml` |

The UI is intentionally **monochrome and monospaced** (Cascadia Code / Consolas), matching the
original's editor‑style design; the accent is pure black, independent of the Windows accent color.

## Build & run

Requires the **.NET 8 SDK** and the official NuGet source.

```powershell
dotnet build src/SkillzWin/SkillzWin.csproj -c Debug
dotnet run   --project src/SkillzWin/SkillzWin.csproj
```

The built executable lands at `src/SkillzWin/bin/Debug/net8.0-windows/SkillzWin.exe`.

## Distribution

Two distributable artifacts (both self‑contained — the target PC needs **nothing installed**):

**1. Portable single‑file exe** (~71 MB, double‑click to run, copy anywhere):
```powershell
dotnet publish src/SkillzWin/SkillzWin.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o publish
# -> publish\SkillzWin.exe
```

**2. Installer** (`Skills-Setup-x.y.z.exe`, ~51 MB — Start‑menu shortcut, optional desktop icon,
uninstaller, app icon; installs per‑user without admin or all‑users via the privileges dialog):
```powershell
# first publish the folder payload the installer packages:
dotnet publish src/SkillzWin/SkillzWin.csproj -c Release -r win-x64 --self-contained true -o publish-app
# then compile the Inno Setup script:
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer\Skills.iss
# -> dist\Skills-Setup-0.1.0.exe
```

Both are **unsigned**, so Windows SmartScreen shows a one‑time "Windows protected your PC" prompt
(*More info → Run anyway*). Code‑signing removes it.

## What's implemented

- **Catalog discovery** across all six tools: skills (`SKILL.md` + frontmatter, built‑in Cursor,
  plugin‑embedded, shared `~/.agents/skills` dedup), MCP servers (Cursor `mcp.json`, Claude
  `.mcp.json`, Codex `config.toml`), and plugins (Cursor cache, Claude `installed_plugins.json`
  + enable map, Codex config + synthetic entries).
- **Source detection** (installed vs. not) via config files, skill content, and PATH/PATHEXT
  executable resolution.
- **Three‑pane browser** — sidebar (library sections + platform filters with live counts),
  searchable/filterable item list, and a detail pane.
- **Skill editor** — AvalonEdit with frontmatter‑aware files, multi‑file tree, 1.2 s debounced
  autosave, atomic UTF‑8 (no BOM) `\n`‑preserving writes.
- **MCP & plugin detail** views (read‑only cards + reveal/open/copy actions).
- **CRUD** — New / Rename / Delete / Edit‑metadata dialogs with validation (incl. Win32
  reserved‑name rejection) and capability gating.
- **Live refresh** — `FileSystemWatcher` per root with a shared 300 ms debounce; refresh on window
  activation.
- **Keyboard shortcuts** — Ctrl+N (new), Ctrl+R / F5 (refresh), Ctrl+S (save), Ctrl+B (toggle
  sidebar), Ctrl+Alt+I (toggle inspector).
- **Settings persistence** at `%APPDATA%\SkillzWin\settings.json`.

## Not yet built (follow‑ups)

- Inspector panel (the optional 4th column; the toggle exists).
- First‑run onboarding screen and the Settings window (appearance / hide‑flags / editor font size).
- Platform **brand icons** (currently Fluent glyphs; the original SVGs are staged in
  `assets-staging/`).
- **Code signing** (to remove the SmartScreen prompt) and installer **auto‑update**.
- The agent‑session **tray monitor + hooks** (intentionally deferred — see `docs/port-spec/00-PLAN.md`).

## Project layout

```
src/SkillzWin/
  Models/      domain records + enums (platforms, catalog items, detection, settings)
  Services/    paths, scanners (skill/mcp/plugin), detector, store deps, file I/O, watcher
  ViewModels/  catalog store, shell, skill editor, dialogs
  Views/       MainWindow, sidebar/list/detail panes, components, dialogs
  Themes/      color/typography/spacing/control dictionaries + converters
docs/port-spec/  the reverse‑engineering spec + implementation plan this port was built from
```

Detailed reverse‑engineering notes for each subsystem live in `docs/port-spec/`.
