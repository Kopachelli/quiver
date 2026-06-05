using System.IO;
using System.Text.Json;
using SkillzWin.Models;

namespace SkillzWin.Services;

/// <summary>
/// Resolves OpenClaw's configurable workspace from <c>~/.openclaw/openclaw.json</c>, whose
/// <c>skills/</c> folder is an additional scan root. Mirrors macOS <c>OpenClawConfig</c>.
/// Any read/parse failure falls back to <c>~/.openclaw/workspace</c>.
/// </summary>
public sealed class OpenClawConfig
{
    private readonly IAgentEnvironment _env;

    public OpenClawConfig(IAgentEnvironment env) => _env = env;

    private string OpenClawHome => _env.HomeDirectoryFor(AgentPlatform.OpenClaw);

    public string ConfigUrl => Path.Combine(OpenClawHome, "openclaw.json");

    public string WorkspaceDirectory()
    {
        var fallback = Path.Combine(OpenClawHome, "workspace");
        try
        {
            if (!File.Exists(ConfigUrl)) return fallback;
            using var doc = JsonDocument.Parse(File.ReadAllText(ConfigUrl));
            if (!doc.RootElement.TryGetProperty("agents", out var agents) ||
                !agents.TryGetProperty("defaults", out var defaults) ||
                !defaults.TryGetProperty("workspace", out var workspace) ||
                workspace.ValueKind != JsonValueKind.String)
            {
                return fallback;
            }

            var value = workspace.GetString();
            if (string.IsNullOrEmpty(value)) return fallback;

            if (value.StartsWith("~/", StringComparison.Ordinal) || value.StartsWith("~\\", StringComparison.Ordinal))
            {
                var tail = value[2..].Replace('/', Path.DirectorySeparatorChar);
                return Path.Combine(_env.HomeDirectory, tail);
            }

            if (Path.IsPathRooted(value))
                return value;

            // Relative — resolved against the OpenClaw home.
            return Path.Combine(OpenClawHome, value.Replace('/', Path.DirectorySeparatorChar));
        }
        catch
        {
            return fallback;
        }
    }

    public string WorkspaceSkillsDirectory() => Path.Combine(WorkspaceDirectory(), "skills");
}
