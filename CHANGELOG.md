# Changelog

All notable changes to Quiver are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/), and the project uses
[semantic versioning](https://semver.org/).

## [0.1.2] — 2026-06-06

### Fixed
- App could close unexpectedly when selecting an MCP server or plugin (a XAML resource‑resolution
  bug in the detail card). The detail panes now render correctly.

### Added
- Global crash handling: unexpected errors are logged to `%APPDATA%\Quiver\crash.log` and shown in a
  message instead of silently closing the app.
- GitHub Actions release pipeline (`.github/workflows/release.yml`) that builds the installer and
  portable exe and publishes the release on a tag push, with **SignPath (free OSS) code signing
  pre‑wired** (see `docs/SIGNING.md`). Plus a CI build workflow.
- Privacy policy (`PRIVACY.md` + a page on the site). Quiver collects no data and makes no network
  connections.

## [0.1.1] — 2026-06-05

### Added
- **Sync skills across tools** — copy a skill (its whole folder, references included) into your other
  agents in one click. The dialog shows where the skill already exists and installs it everywhere you
  pick. Available from the toolbar (⇄ Sync) and the right‑click menu.

## [0.1.0] — 2026-06-05

### Added
- First public release. A native Windows (WPF / .NET 8) app to browse, edit, and manage local AI‑tool
  assets across Cursor, Claude Code, Codex, Hermes, Pi, and OpenCode:
  - Catalog discovery of skills (`SKILL.md` + frontmatter), MCP servers (`mcp.json` / `.mcp.json` /
    `config.toml`), and plugins, plus installed‑vs‑not source detection.
  - Three‑pane Fluent browser with search, platform/section filters, and light/dark/system themes
    (indigo accent).
  - Built‑in `SKILL.md` editor (AvalonEdit) with frontmatter, a multi‑file tree, and debounced
    autosave.
  - MCP and plugin detail views; New / Rename / Delete / Edit‑metadata dialogs.
  - First‑run onboarding and a settings window.
  - Live file watching and refresh on window activation.
  - Self‑contained installer and portable single‑file exe.

[0.1.2]: https://github.com/Kopachelli/quiver/releases/tag/v0.1.2
[0.1.1]: https://github.com/Kopachelli/quiver/releases/tag/v0.1.1
[0.1.0]: https://github.com/Kopachelli/quiver/releases/tag/v0.1.0
