using System.IO;
using SkillzWin.Models;

namespace SkillzWin.Services;

/// <summary>
/// Skill scan roots, the shared <c>~/.agents/skills</c> sharing/dedup rules, the ordered
/// primary-platform classifier, and the file-watch roots. Mirrors macOS <c>PlatformSkillPaths</c>
/// plus the per-platform <c>userSkillsDirectory</c> from <c>AgentPlatform</c>.
/// </summary>
public sealed class PlatformSkillPaths
{
    private readonly IAgentEnvironment _env;
    private readonly OpenClawConfig _openClaw;

    public PlatformSkillPaths(IAgentEnvironment env, OpenClawConfig openClaw)
    {
        _env = env;
        _openClaw = openClaw;
    }

    /// <summary>Shared <c>~/.agents</c> root (not under any per-platform home).</summary>
    public string AgentsDirectory => Path.Combine(_env.HomeDirectory, ".agents");

    /// <summary>Shared <c>~/.agents/skills</c> read by multiple harnesses.</summary>
    public string AgentsSkillsDirectory => Path.Combine(AgentsDirectory, "skills");

    /// <summary>User-writable skills folder for CREATING new skills (Pi nests under <c>agent\</c>).</summary>
    public string UserSkillsDirectory(AgentPlatform platform)
    {
        var home = _env.HomeDirectoryFor(platform);
        return platform == AgentPlatform.Pi
            ? Path.Combine(home, "agent", "skills")
            : Path.Combine(home, "skills");
    }

    /// <summary>Directories scanned to discover skills for a platform.</summary>
    public IReadOnlyList<string> SkillScanRoots(AgentPlatform platform)
    {
        switch (platform)
        {
            case AgentPlatform.Cursor:
                return new[] { UserSkillsDirectory(AgentPlatform.Cursor) };
            case AgentPlatform.ClaudeCode:
                return new[] { UserSkillsDirectory(AgentPlatform.ClaudeCode) };
            case AgentPlatform.Codex:
                return new[] { UserSkillsDirectory(AgentPlatform.Codex), AgentsSkillsDirectory };
            case AgentPlatform.Hermes:
                return new[] { UserSkillsDirectory(AgentPlatform.Hermes) };
            case AgentPlatform.Pi:
                return new[] { UserSkillsDirectory(AgentPlatform.Pi), AgentsSkillsDirectory };
            case AgentPlatform.OpenClaw:
                var roots = new List<string> { UserSkillsDirectory(AgentPlatform.OpenClaw) };
                var workspaceSkills = _openClaw.WorkspaceSkillsDirectory();
                if (Directory.Exists(workspaceSkills))
                    roots.Add(workspaceSkills);
                return roots;
            default:
                throw new ArgumentOutOfRangeException(nameof(platform));
        }
    }

    /// <summary>Skill-file watch roots (six homes + shared agents + OpenClaw workspace root).</summary>
    public IReadOnlyList<string> WatchDirectories()
    {
        var paths = AgentPlatformInfo.All.Select(_env.HomeDirectoryFor).ToList();

        if (Directory.Exists(AgentsSkillsDirectory))
            paths.Add(AgentsSkillsDirectory);
        else
            paths.Add(AgentsDirectory);

        var workspaceSkills = _openClaw.WorkspaceSkillsDirectory();
        if (Directory.Exists(workspaceSkills))
        {
            var parent = Path.GetDirectoryName(workspaceSkills);
            if (parent is not null) paths.Add(parent);
        }

        return paths;
    }

    // --- Pure classifiers (separator-normalized to '/', ordinal). ---

    /// <summary>
    /// Harnesses that read the same on-disk skill file as <paramref name="path"/> (the shared
    /// <c>~/.agents/skills</c>), EXCLUDING none here — caller removes the primary. Preserves the
    /// macOS literal trio (OpenClaw is intentionally included even though it doesn't scan it).
    /// </summary>
    public static IReadOnlyList<AgentPlatform> PlatformsThatShare(string path)
    {
        var p = path.Replace('\\', '/');
        var matches = p.Contains("/.agents/skills/", StringComparison.Ordinal)
            || p.EndsWith("/.agents/skills", StringComparison.Ordinal);
        return matches
            ? new[] { AgentPlatform.Pi, AgentPlatform.Codex, AgentPlatform.OpenClaw }
            : Array.Empty<AgentPlatform>();
    }

    /// <summary>Which platform "owns" a skill path for display after dedup. First match wins.</summary>
    public static AgentPlatform PrimaryPlatform(string path)
    {
        var p = path.Replace('\\', '/');
        if (p.Contains("/skills-cursor/", StringComparison.Ordinal)) return AgentPlatform.Cursor;
        if (p.Contains("/.hermes/", StringComparison.Ordinal)) return AgentPlatform.Hermes;
        if (p.Contains("/.openclaw/", StringComparison.Ordinal)) return AgentPlatform.OpenClaw;
        if (p.Contains("/.pi/", StringComparison.Ordinal)) return AgentPlatform.Pi;
        if (p.Contains("/.cursor/", StringComparison.Ordinal)) return AgentPlatform.Cursor;
        if (p.Contains("/.claude/", StringComparison.Ordinal)) return AgentPlatform.ClaudeCode;
        if (p.Contains("/.codex/", StringComparison.Ordinal)) return AgentPlatform.Codex;
        if (p.Contains("/.agents/", StringComparison.Ordinal)) return AgentPlatform.Pi;
        return AgentPlatform.Cursor;
    }

    /// <summary>Alias of <see cref="PrimaryPlatform"/> (macOS <c>platformFor</c>).</summary>
    public static AgentPlatform PlatformFor(string path) => PrimaryPlatform(path);
}
