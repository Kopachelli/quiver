using System.IO;

namespace SkillzWin.Models;

/// <summary>Parsed YAML-ish frontmatter of a skill markdown file. Mirrors macOS <c>SkillFrontmatter</c>.</summary>
public sealed record SkillFrontmatter(
    string? Name,
    string? Description,
    string? Version,
    bool? DisableModelInvocation)
{
    public static readonly SkillFrontmatter Empty = new(null, null, null, null);
}

/// <summary>
/// One discovered skill (a folder containing a primary <c>SKILL.md</c>). Mirrors macOS <c>SkillItem</c>.
/// </summary>
public sealed record SkillItem(
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
    /// <summary>Parent directory of the primary skill file (shown as the list subtitle).</summary>
    public string ListSubtitle => Path.GetDirectoryName(SkillPath) ?? SkillPath;

    /// <summary>True when other harnesses read the same on-disk file (shared ~/.agents/skills).</summary>
    public bool HasSharedAvailability => AlsoAvailableOn.Count > 0;

    public static string MakeId(AgentPlatform platform, string path)
        => $"skill:{platform.RawValue()}:{path}";
}

/// <summary>One markdown file belonging to a skill folder. Mirrors macOS <c>SkillMarkdownFile</c>.</summary>
public sealed record SkillMarkdownFile
{
    public string Id { get; }
    public string Url { get; }
    public string DisplayName { get; }
    public bool IsPrimary { get; }

    public SkillMarkdownFile(string url, bool isPrimary = false)
    {
        Url = url;
        Id = url;
        DisplayName = Path.GetFileName(url);
        IsPrimary = isPrimary;
    }
}
