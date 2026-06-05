using System.IO;
using System.Linq;
using System.Text;
using SkillzWin.Models;

namespace SkillzWin.Services;

/// <summary>
/// Discovers skills (folders containing a primary <c>SKILL.md</c>) across all six platforms,
/// the built-in cursor catalog, and plugin-embedded skills, then dedups shared
/// <c>~/.agents/skills</c> entries. Mirrors macOS <c>SkillScanner</c>.
/// <para>Windows behavior: enumeration skips hidden dot-dirs (macOS <c>skipsHiddenFiles</c>) but
/// still descends into <c>.system</c> so the <c>hideSystemCodex</c> toggle remains meaningful (R5);
/// <c>SKILL.md</c> is matched case-insensitively.</para>
/// </summary>
public sealed class SkillScanner
{
    private readonly IAgentEnvironment _env;
    private readonly PlatformSkillPaths _paths;

    public SkillScanner(IAgentEnvironment env, PlatformSkillPaths paths)
    {
        _env = env;
        _paths = paths;
    }

    public IReadOnlyList<SkillItem> Scan(bool hideBuiltInCursor, bool hideSystemCodex)
    {
        var items = new List<SkillItem>();

        foreach (var platform in AgentPlatformInfo.All)
        {
            var hideSystem = platform == AgentPlatform.Codex && hideSystemCodex;
            foreach (var root in _paths.SkillScanRoots(platform))
                items.AddRange(ScanDirectory(root, platform, isBuiltIn: false, isPluginEmbedded: false, hideSystem));
        }

        if (!hideBuiltInCursor)
        {
            var builtIn = Path.Combine(_env.HomeDirectoryFor(AgentPlatform.Cursor), "skills-cursor");
            items.AddRange(ScanDirectory(builtIn, AgentPlatform.Cursor, isBuiltIn: true, isPluginEmbedded: false, hideSystem: false));
        }

        items.AddRange(ScanPluginEmbeddedSkills(PluginCache(AgentPlatform.Cursor), AgentPlatform.Cursor));
        items.AddRange(ScanPluginEmbeddedSkills(PluginCache(AgentPlatform.ClaudeCode), AgentPlatform.ClaudeCode));
        items.AddRange(ScanPluginEmbeddedSkills(PluginCache(AgentPlatform.Codex), AgentPlatform.Codex));

        return Deduplicate(items)
            .OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>All markdown files in a skill folder (primary first, then by path). For the editor file tree.</summary>
    public IReadOnlyList<SkillMarkdownFile> MarkdownFiles(string rootDirectory)
    {
        if (!Directory.Exists(rootDirectory))
            return new[] { new SkillMarkdownFile(Path.Combine(rootDirectory, "SKILL.md"), isPrimary: true) };

        var files = HiddenAwareWalk.Files(rootDirectory, allowSystem: true)
            .Where(f => string.Equals(Path.GetExtension(f), ".md", StringComparison.OrdinalIgnoreCase))
            .Select(f => new SkillMarkdownFile(f, string.Equals(Path.GetFileName(f), "SKILL.md", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (files.Count == 0)
            return new[] { new SkillMarkdownFile(Path.Combine(rootDirectory, "SKILL.md"), isPrimary: true) };

        return files
            .OrderByDescending(f => f.IsPrimary)
            .ThenBy(f => f.Url, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private string PluginCache(AgentPlatform platform)
        => Path.Combine(_env.HomeDirectoryFor(platform), "plugins", "cache");

    private List<SkillItem> ScanDirectory(string root, AgentPlatform platform, bool isBuiltIn, bool isPluginEmbedded, bool hideSystem)
    {
        var items = new List<SkillItem>();
        if (!Directory.Exists(root)) return items;

        foreach (var file in HiddenAwareWalk.Files(root, allowSystem: true))
        {
            if (!string.Equals(Path.GetFileName(file), "SKILL.md", StringComparison.OrdinalIgnoreCase))
                continue;

            var relative = (Path.GetDirectoryName(file) ?? string.Empty).Replace('\\', '/');
            if (hideSystem &&
                (relative.Contains("/.system/", StringComparison.Ordinal) || relative.EndsWith("/.system", StringComparison.Ordinal)))
                continue;

            var item = MakeSkillItem(file, platform, isBuiltIn, isPluginEmbedded);
            if (item is not null) items.Add(item);
        }
        return items;
    }

    private List<SkillItem> ScanPluginEmbeddedSkills(string root, AgentPlatform platform)
    {
        var items = new List<SkillItem>();
        if (!Directory.Exists(root)) return items;

        foreach (var dir in HiddenAwareWalk.Directories(root, allowSystem: true))
        {
            var name = Path.GetFileName(dir);
            if (!(name == "skills" || name.EndsWith("skills", StringComparison.Ordinal)))
                continue;

            string[] children;
            try { children = Directory.GetFileSystemEntries(dir); }
            catch { continue; }

            foreach (var child in children)
            {
                if (Path.GetFileName(child).StartsWith(".", StringComparison.Ordinal)) continue;
                if (!Directory.Exists(child)) continue;
                var skillMd = Path.Combine(child, "SKILL.md");
                if (!File.Exists(skillMd)) continue;
                var item = MakeSkillItem(skillMd, platform, isBuiltIn: false, isPluginEmbedded: true);
                if (item is not null) items.Add(item);
            }

            foreach (var child in children)
            {
                if (Directory.Exists(child)) continue;
                if (!string.Equals(Path.GetFileName(child), "SKILL.md", StringComparison.OrdinalIgnoreCase)) continue;
                var item = MakeSkillItem(child, platform, isBuiltIn: false, isPluginEmbedded: true);
                if (item is not null) items.Add(item);
            }
        }
        return items;
    }

    private static SkillItem? MakeSkillItem(string skillPath, AgentPlatform platform, bool isBuiltIn, bool isPluginEmbedded)
    {
        string content;
        try { content = File.ReadAllText(skillPath, Encoding.UTF8); }
        catch { return null; }

        var (frontmatter, body) = FrontmatterParser.Parse(content);
        var rootDirectory = Path.GetDirectoryName(skillPath) ?? skillPath;
        var folderName = Path.GetFileName(rootDirectory);
        var displayName = frontmatter.Name ?? folderName;
        var description = frontmatter.Description ?? FrontmatterParser.FirstParagraph(body);
        if (description.Length == 0) description = "No description";

        DateTime? modifiedAt = null;
        try { modifiedAt = File.GetLastWriteTimeUtc(skillPath); }
        catch { /* ignore */ }

        return new SkillItem(
            Id: SkillItem.MakeId(platform, skillPath),
            Platform: platform,
            SkillPath: skillPath,
            RootDirectory: rootDirectory,
            DisplayName: displayName,
            Description: description,
            Version: frontmatter.Version,
            IsBuiltIn: isBuiltIn,
            IsPluginEmbedded: isPluginEmbedded,
            Frontmatter: frontmatter,
            ModifiedAt: modifiedAt,
            AlsoAvailableOn: Array.Empty<AgentPlatform>());
    }

    /// <summary>Dedup by physical path; primary platform owns the entry, the rest become "also available on".</summary>
    private static List<SkillItem> Deduplicate(List<SkillItem> items)
    {
        var byPath = new Dictionary<string, SkillItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            var primary = PlatformSkillPaths.PrimaryPlatform(item.SkillPath);
            var also = PlatformSkillPaths.PlatformsThatShare(item.SkillPath)
                .Where(p => p != primary)
                .ToList();

            byPath[item.SkillPath] = item with
            {
                Id = SkillItem.MakeId(primary, item.SkillPath),
                Platform = primary,
                AlsoAvailableOn = also,
            };
        }

        return byPath.Values.ToList();
    }
}
