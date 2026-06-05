using System.IO;
using System.Linq;
using System.Text;
using SkillzWin.Models;

namespace SkillzWin.Services;

/// <summary>
/// Faithful port of macOS <c>TOMLParser</c> — a deliberately minimal, line-based, flat-section
/// reader (NOT a full TOML parser) used for Codex's <c>config.toml</c>. Reproduces the exact
/// macOS behavior, including the <c>env.&lt;KEY&gt;</c> → <c>KEY</c> extraction and the bare
/// <c>env</c> literal quirk, and the lenient never-throws contract.
/// </summary>
public sealed class TomlConfigReader
{
    private sealed class Section
    {
        public string Name = string.Empty;
        public Dictionary<string, string> Keys = new();
    }

    /// <summary>MCP servers from <c>[mcp_servers.&lt;name&gt;]</c> sections (always platform Codex).</summary>
    public IReadOnlyList<McpItem> McpServers(string configPath)
    {
        var content = TryRead(configPath);
        if (content is null) return Array.Empty<McpItem>();

        var sections = ParseSections(content);
        var modifiedAt = TryMtime(configPath);
        var items = new List<McpItem>();

        foreach (var section in sections)
        {
            const string prefix = "mcp_servers.";
            if (!section.Name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var name = section.Name[prefix.Length..];
            if (name.Length == 0) continue;

            section.Keys.TryGetValue("command", out var command);
            section.Keys.TryGetValue("url", out var url);
            var argsRaw = section.Keys.TryGetValue("args", out var a) ? a : string.Empty;
            var args = ParseArgs(argsRaw);

            var transport = url != null ? McpTransport.Http
                : command != null ? McpTransport.Stdio
                : McpTransport.Unknown;

            var envKeys = section.Keys.Keys
                .Where(k => k.StartsWith("env.", StringComparison.Ordinal))
                .Select(k => k[4..])
                .Concat(section.Keys.Keys.Where(k => k == "env").Select(_ => "env"))
                .Distinct()
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToList();

            items.Add(new McpItem(
                McpItem.MakeId(AgentPlatform.Codex, name), AgentPlatform.Codex, name, transport,
                command, args, url, envKeys, configPath, modifiedAt));
        }
        return items;
    }

    /// <summary>Plugin-enabled map from <c>[plugins."id"]</c> sections.</summary>
    public IReadOnlyDictionary<string, bool> EnabledPlugins(string configPath)
    {
        var content = TryRead(configPath);
        if (content is null) return new Dictionary<string, bool>();

        var sections = ParseSections(content);
        var result = new Dictionary<string, bool>();

        foreach (var section in sections)
        {
            if (!section.Name.StartsWith("plugins.\"", StringComparison.Ordinal)
                && !section.Name.StartsWith("plugins.'", StringComparison.Ordinal))
                continue;

            var pluginId = ExtractQuotedPluginId(section.Name);
            if (pluginId.Length == 0) continue;

            if (section.Keys.TryGetValue("enabled", out var enabled))
                result[pluginId] = enabled.ToLowerInvariant() is "true" or "yes" or "1";
            else
                result[pluginId] = true;
        }
        return result;
    }

    private static List<Section> ParseSections(string content)
    {
        var sections = new List<Section>();
        var currentName = string.Empty;
        var currentKeys = new Dictionary<string, string>();

        void Flush()
        {
            if (currentName.Length == 0) return;
            sections.Add(new Section { Name = currentName, Keys = currentKeys });
            currentKeys = new Dictionary<string, string>();
        }

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                Flush();
                currentName = line[1..^1].Trim();
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq < 0) continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
                value = value[1..^1];

            if (currentName.Length != 0) currentKeys[key] = value;
        }

        Flush();
        return sections;
    }

    private static string ExtractQuotedPluginId(string sectionName)
    {
        const string prefix = "plugins.";
        if (!sectionName.StartsWith(prefix, StringComparison.Ordinal)) return string.Empty;
        var remainder = sectionName[prefix.Length..];
        if (remainder.StartsWith("\"", StringComparison.Ordinal))
        {
            remainder = remainder[1..];
            var end = remainder.IndexOf('"');
            if (end >= 0) return remainder[..end];
        }
        return remainder.Trim('"', '\'');
    }

    private static IReadOnlyList<string> ParseArgs(string raw)
    {
        if (!(raw.StartsWith("[", StringComparison.Ordinal) && raw.EndsWith("]", StringComparison.Ordinal)))
            return Array.Empty<string>();

        var inner = raw[1..^1];
        var args = new List<string>();
        var current = new StringBuilder();
        var inQuote = false;
        char quoteChar = '\0';

        foreach (var ch in inner)
        {
            if (inQuote)
            {
                if (ch == quoteChar)
                {
                    inQuote = false;
                    args.Add(current.ToString());
                    current.Clear();
                }
                else current.Append(ch);
            }
            else if (ch == '"' || ch == '\'')
            {
                inQuote = true;
                quoteChar = ch;
            }
            else if (ch == ',')
            {
                var trimmed = current.ToString().Trim();
                if (trimmed.Length > 0) args.Add(trimmed);
                current.Clear();
            }
            else if (!char.IsWhiteSpace(ch))
            {
                current.Append(ch);
            }
        }

        var tail = current.ToString().Trim();
        if (tail.Length > 0) args.Add(tail);
        return args;
    }

    private static string? TryRead(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null; }
        catch { return null; }
    }

    private static DateTime? TryMtime(string path)
    {
        try { return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null; }
        catch { return null; }
    }
}
