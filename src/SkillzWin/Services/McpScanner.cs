using System.IO;
using System.Linq;
using System.Text.Json;
using SkillzWin.Models;

namespace SkillzWin.Services;

/// <summary>
/// Discovers MCP servers from Cursor's <c>mcp.json</c>, Claude Code's <c>.mcp.json</c>
/// (System.Text.Json), and Codex's <c>config.toml</c> (via <see cref="TomlConfigReader"/>).
/// Mirrors macOS <c>MCPScanner</c>. Stores env KEY NAMES only (never values); per-read errors
/// are swallowed to an empty list. Sorted by name, case-insensitive.
/// </summary>
public sealed class McpScanner
{
    private readonly IAgentEnvironment _env;
    private readonly TomlConfigReader _toml;

    public McpScanner(IAgentEnvironment env, TomlConfigReader toml)
    {
        _env = env;
        _toml = toml;
    }

    public IReadOnlyList<McpItem> Scan()
    {
        var items = new List<McpItem>();
        items.AddRange(ParseJsonConfig(Path.Combine(_env.HomeDirectoryFor(AgentPlatform.Cursor), "mcp.json"), AgentPlatform.Cursor));
        items.AddRange(ParseJsonConfig(Path.Combine(_env.HomeDirectoryFor(AgentPlatform.ClaudeCode), ".mcp.json"), AgentPlatform.ClaudeCode));
        items.AddRange(_toml.McpServers(Path.Combine(_env.HomeDirectoryFor(AgentPlatform.Codex), "config.toml")));
        return items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<McpItem> ParseJsonConfig(string path, AgentPlatform platform)
    {
        var items = new List<McpItem>();
        if (!File.Exists(path)) return items;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(File.ReadAllText(path)); }
        catch { return items; }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return items;
            if (!doc.RootElement.TryGetProperty("mcpServers", out var servers) || servers.ValueKind != JsonValueKind.Object)
                return items;

            var modifiedAt = TryMtime(path);
            foreach (var prop in servers.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                var v = prop.Value;

                var url = GetString(v, "url");
                var command = GetString(v, "command");
                var args = GetStringArray(v, "args");
                var envKeys = GetEnvKeys(v);
                var transport = url != null ? McpTransport.Http
                    : command != null ? McpTransport.Stdio
                    : McpTransport.Unknown;

                items.Add(new McpItem(
                    McpItem.MakeId(platform, prop.Name), platform, prop.Name, transport,
                    command, args, url, envKeys, path, modifiedAt));
            }
        }
        return items;
    }

    private static string? GetString(JsonElement obj, string key)
        => obj.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static IReadOnlyList<string> GetStringArray(JsonElement obj, string key)
    {
        if (!obj.TryGetProperty(key, out var p) || p.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        var list = new List<string>();
        foreach (var el in p.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.String) return Array.Empty<string>();  // macOS `as? [String]`
            list.Add(el.GetString() ?? string.Empty);
        }
        return list;
    }

    private static IReadOnlyList<string> GetEnvKeys(JsonElement obj)
    {
        if (!obj.TryGetProperty("env", out var env) || env.ValueKind != JsonValueKind.Object)
            return Array.Empty<string>();
        return env.EnumerateObject()
            .Select(p => p.Name)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
    }

    private static DateTime? TryMtime(string path)
    {
        try { return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null; }
        catch { return null; }
    }
}
