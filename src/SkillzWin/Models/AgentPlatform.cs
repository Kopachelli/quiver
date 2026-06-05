namespace SkillzWin.Models;

/// <summary>
/// The six supported agent harnesses. Mirrors macOS <c>AgentPlatform</c>.
/// <para>
/// The <see cref="AgentPlatformInfo.RawValue"/> strings are the persisted identity and
/// are embedded in catalog-item IDs (<c>skill:&lt;raw&gt;:&lt;path&gt;</c>), so they MUST be
/// preserved byte-for-byte. Note the three spellings for OpenClaw: enum <c>OpenClaw</c>,
/// raw value <c>openClaw</c>, dotfolder <c>.openclaw</c>, display "OpenCode".
/// </para>
/// </summary>
public enum AgentPlatform
{
    Cursor,
    ClaudeCode,
    Codex,
    Hermes,
    Pi,
    OpenClaw,
}

/// <summary>Lookup/extension helpers for <see cref="AgentPlatform"/>.</summary>
public static class AgentPlatformInfo
{
    /// <summary>All cases, in declaration order (macOS <c>CaseIterable</c>).</summary>
    public static readonly IReadOnlyList<AgentPlatform> All = new[]
    {
        AgentPlatform.Cursor,
        AgentPlatform.ClaudeCode,
        AgentPlatform.Codex,
        AgentPlatform.Hermes,
        AgentPlatform.Pi,
        AgentPlatform.OpenClaw,
    };

    /// <summary>The Swift <c>rawValue</c> — load-bearing for IDs. Do not change.</summary>
    public static string RawValue(this AgentPlatform p) => p switch
    {
        AgentPlatform.Cursor => "cursor",
        AgentPlatform.ClaudeCode => "claudeCode",
        AgentPlatform.Codex => "codex",
        AgentPlatform.Hermes => "hermes",
        AgentPlatform.Pi => "pi",
        AgentPlatform.OpenClaw => "openClaw",
        _ => throw new ArgumentOutOfRangeException(nameof(p)),
    };

    /// <summary>Parse a <see cref="RawValue"/> back to an enum, or null if unknown.</summary>
    public static AgentPlatform? FromRawValue(string raw) => raw switch
    {
        "cursor" => AgentPlatform.Cursor,
        "claudeCode" => AgentPlatform.ClaudeCode,
        "codex" => AgentPlatform.Codex,
        "hermes" => AgentPlatform.Hermes,
        "pi" => AgentPlatform.Pi,
        "openClaw" => AgentPlatform.OpenClaw,
        _ => null,
    };

    public static string DisplayName(this AgentPlatform p) => p switch
    {
        AgentPlatform.Cursor => "Cursor",
        AgentPlatform.ClaudeCode => "Claude Code",
        AgentPlatform.Codex => "Codex",
        AgentPlatform.Hermes => "Hermes",
        AgentPlatform.Pi => "Pi",
        AgentPlatform.OpenClaw => "OpenCode",
        _ => throw new ArgumentOutOfRangeException(nameof(p)),
    };

    /// <summary>The leading-dot home folder name under %USERPROFILE% (note OpenClaw =&gt; <c>.openclaw</c>).</summary>
    public static string DotFolderName(this AgentPlatform p) => p switch
    {
        AgentPlatform.Cursor => ".cursor",
        AgentPlatform.ClaudeCode => ".claude",
        AgentPlatform.Codex => ".codex",
        AgentPlatform.Hermes => ".hermes",
        AgentPlatform.Pi => ".pi",
        AgentPlatform.OpenClaw => ".openclaw",
        _ => throw new ArgumentOutOfRangeException(nameof(p)),
    };

    /// <summary>Resource key for the brand icon PNG under Assets/PlatformIcons.</summary>
    public static string BrandIconResourceKey(this AgentPlatform p) => p switch
    {
        AgentPlatform.Cursor => "PlatformIconCursor",
        AgentPlatform.ClaudeCode => "PlatformIconClaudeCode",
        AgentPlatform.Codex => "PlatformIconCodex",
        AgentPlatform.Hermes => "PlatformIconHermes",
        AgentPlatform.Pi => "PlatformIconPi",
        AgentPlatform.OpenClaw => "PlatformIconOpenCode",
        _ => throw new ArgumentOutOfRangeException(nameof(p)),
    };
}

/// <summary>Library sections shown in the sidebar / filter tabs. Mirrors macOS <c>CatalogSection</c>.</summary>
public enum CatalogSection
{
    All,
    Skills,
    McpServers,
    Plugins,
}

public static class CatalogSectionInfo
{
    public static readonly IReadOnlyList<CatalogSection> All = new[]
    {
        CatalogSection.All,
        CatalogSection.Skills,
        CatalogSection.McpServers,
        CatalogSection.Plugins,
    };

    public static string DisplayName(this CatalogSection s) => s switch
    {
        CatalogSection.All => "All Items",
        CatalogSection.Skills => "Skills",
        CatalogSection.McpServers => "MCP Servers",
        CatalogSection.Plugins => "Plugins",
        _ => throw new ArgumentOutOfRangeException(nameof(s)),
    };
}

/// <summary>The kind of a catalog item. Mirrors macOS <c>CatalogItemKind</c>.</summary>
public enum CatalogItemKind
{
    Skill,
    Mcp,
    Plugin,
}

public static class CatalogItemKindInfo
{
    public static string DisplayName(this CatalogItemKind k) => k switch
    {
        CatalogItemKind.Skill => "Skill",
        CatalogItemKind.Mcp => "MCP Server",
        CatalogItemKind.Plugin => "Plugin",
        _ => throw new ArgumentOutOfRangeException(nameof(k)),
    };
}
