using System.IO;
using System.Linq;
using System.Text.Json;
using SkillzWin.Models;

namespace SkillzWin.Services;

/// <summary>
/// Discovers installed plugins across Cursor (cache crawl, all enabled), Claude Code
/// (<c>installed_plugins.json</c> + <c>settings.json</c> enable map, default disabled), and Codex
/// (<c>config.toml</c> enable map + cache + synthetic config-only entries). Mirrors macOS
/// <c>PluginScanner</c>. Sorted by display name, case-insensitive.
/// </summary>
public sealed class PluginScanner
{
    private readonly IAgentEnvironment _env;
    private readonly TomlConfigReader _toml;

    public PluginScanner(IAgentEnvironment env, TomlConfigReader toml)
    {
        _env = env;
        _toml = toml;
    }

    private sealed record PluginMetadata(string Name, string Description, string? Version);

    public IReadOnlyList<PluginItem> Scan()
    {
        var items = new List<PluginItem>();
        items.AddRange(ScanCursor());
        items.AddRange(ScanClaude());
        items.AddRange(ScanCodex());
        return items.OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private List<PluginItem> ScanCursor()
        => ScanPluginMetadata(PluginCache(AgentPlatform.Cursor), AgentPlatform.Cursor,
            new Dictionary<string, bool>(), defaultEnabled: true);

    private List<PluginItem> ScanClaude()
    {
        var home = _env.HomeDirectoryFor(AgentPlatform.ClaudeCode);
        var installedUrl = Path.Combine(home, "plugins", "installed_plugins.json");
        var settingsUrl = Path.Combine(home, "settings.json");
        var enabledMap = LoadClaudeEnabledPlugins(settingsUrl);

        if (!TryReadObject(installedUrl, out var root) ||
            !root.TryGetProperty("plugins", out var plugins) || plugins.ValueKind != JsonValueKind.Object)
        {
            return ScanPluginMetadata(PluginCache(AgentPlatform.ClaudeCode), AgentPlatform.ClaudeCode, enabledMap, defaultEnabled: false);
        }

        var items = new List<PluginItem>();
        foreach (var prop in plugins.EnumerateObject())
        {
            var pluginId = prop.Name;
            if (prop.Value.ValueKind != JsonValueKind.Array) continue;
            var first = prop.Value.EnumerateArray().FirstOrDefault();
            if (first.ValueKind != JsonValueKind.Object) continue;

            var installPathString = GetString(first, "installPath");
            if (installPathString is null) continue;

            var metadataPath = FindPluginJson(installPathString);
            var metadata = metadataPath is null ? null : LoadPluginMetadata(metadataPath);
            var version = GetString(first, "version") ?? metadata?.Version;
            var isEnabled = enabledMap.TryGetValue(pluginId, out var en) && en;

            items.Add(new PluginItem(
                Id: PluginItem.MakeId(AgentPlatform.ClaudeCode, pluginId, installPathString),
                Platform: AgentPlatform.ClaudeCode,
                PluginId: pluginId,
                DisplayName: metadata?.Name ?? pluginId,
                Description: metadata?.Description ?? string.Empty,
                Version: version,
                Marketplace: Marketplace(pluginId),
                IsEnabled: isEnabled,
                InstallPath: installPathString,
                MetadataPath: metadataPath,
                SkillCount: CountSkills(installPathString),
                ModifiedAt: TryMtime(installPathString)));
        }
        return items;
    }

    private List<PluginItem> ScanCodex()
    {
        var configUrl = Path.Combine(_env.HomeDirectoryFor(AgentPlatform.Codex), "config.toml");
        var enabledMap = _toml.EnabledPlugins(configUrl);
        var items = ScanPluginMetadata(PluginCache(AgentPlatform.Codex), AgentPlatform.Codex, enabledMap, defaultEnabled: false);

        foreach (var (pluginId, enabled) in enabledMap)
        {
            if (items.Any(p => p.PluginId == pluginId)) continue;
            items.Add(new PluginItem(
                Id: PluginItem.MakeId(AgentPlatform.Codex, pluginId, null),
                Platform: AgentPlatform.Codex,
                PluginId: pluginId,
                DisplayName: pluginId,
                Description: string.Empty,
                Version: null,
                Marketplace: Marketplace(pluginId),
                IsEnabled: enabled,
                InstallPath: null,
                MetadataPath: null,
                SkillCount: 0,
                ModifiedAt: TryMtime(configUrl)));
        }
        return items;
    }

    private List<PluginItem> ScanPluginMetadata(string root, AgentPlatform platform, IReadOnlyDictionary<string, bool> enabledMap, bool defaultEnabled)
    {
        var items = new List<PluginItem>();
        if (!Directory.Exists(root)) return items;

        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in HiddenAwareWalk.Directories(root))
        {
            var metadataUrl = FindPluginJson(dir);
            if (metadataUrl is null) continue;
            if (!seenPaths.Add(dir)) continue;

            var metadata = LoadPluginMetadata(metadataUrl);
            var parentName = Path.GetFileName(Path.GetDirectoryName(dir) ?? string.Empty);
            var pluginName = metadata?.Name ?? parentName;
            var pluginId = InferPluginID(pluginName, dir, platform);
            var isEnabled = enabledMap.TryGetValue(pluginId, out var en) ? en : defaultEnabled;

            items.Add(new PluginItem(
                Id: PluginItem.MakeId(platform, pluginId, dir),
                Platform: platform,
                PluginId: pluginId,
                DisplayName: metadata?.Name ?? pluginName,
                Description: metadata?.Description ?? string.Empty,
                Version: metadata?.Version,
                Marketplace: Marketplace(pluginId),
                IsEnabled: isEnabled,
                InstallPath: dir,
                MetadataPath: metadataUrl,
                SkillCount: CountSkills(dir),
                ModifiedAt: TryMtime(dir)));
        }
        return items;
    }

    private string PluginCache(AgentPlatform platform)
        => Path.Combine(_env.HomeDirectoryFor(platform), "plugins", "cache");

    private static IReadOnlyDictionary<string, bool> LoadClaudeEnabledPlugins(string url)
    {
        var result = new Dictionary<string, bool>();
        if (!TryReadObject(url, out var root)) return result;
        if (!root.TryGetProperty("enabledPlugins", out var enabled) || enabled.ValueKind != JsonValueKind.Object)
            return result;
        foreach (var prop in enabled.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.True) result[prop.Name] = true;
            else if (prop.Value.ValueKind == JsonValueKind.False) result[prop.Name] = false;
        }
        return result;
    }

    private static string? FindPluginJson(string installPath)
    {
        var claude = Path.Combine(installPath, ".claude-plugin", "plugin.json");
        if (File.Exists(claude)) return claude;
        var codex = Path.Combine(installPath, ".codex-plugin", "plugin.json");
        if (File.Exists(codex)) return codex;
        return null;
    }

    private static PluginMetadata? LoadPluginMetadata(string url)
    {
        if (!TryReadObject(url, out var root)) return null;
        // Fallback name: the install dir (grandparent of plugin.json).
        var fallbackName = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(url)) ?? string.Empty);
        var name = GetString(root, "name") ?? fallbackName;
        var description = GetString(root, "description") ?? string.Empty;
        var version = GetString(root, "version");
        return new PluginMetadata(name, description, version);
    }

    private static int CountSkills(string installPath)
    {
        var skillsDir = Path.Combine(installPath, "skills");
        try { return Directory.Exists(skillsDir) ? Directory.GetDirectories(skillsDir).Length : 0; }
        catch { return 0; }
    }

    private static string InferPluginID(string name, string installPath, AgentPlatform platform)
    {
        var parent = Path.GetFileName(Path.GetDirectoryName(installPath) ?? string.Empty);
        if (parent.Contains('@'))
            return $"{name}@{parent.Split('@').Last()}";
        return $"{name}@{platform.DisplayName().ToLowerInvariant().Replace(" ", "-")}";
    }

    private static string? Marketplace(string pluginId)
    {
        var at = pluginId.LastIndexOf('@');
        return at < 0 ? null : pluginId[(at + 1)..];
    }

    private static bool TryReadObject(string path, out JsonElement root)
    {
        root = default;
        if (!File.Exists(path)) return false;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            root = doc.RootElement.Clone();
            return true;
        }
        catch { return false; }
    }

    private static string? GetString(JsonElement obj, string key)
        => obj.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static DateTime? TryMtime(string path)
    {
        try
        {
            if (File.Exists(path)) return File.GetLastWriteTimeUtc(path);
            if (Directory.Exists(path)) return Directory.GetLastWriteTimeUtc(path);
            return null;
        }
        catch { return null; }
    }
}
