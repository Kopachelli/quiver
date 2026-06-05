using System.IO;
using System.Linq;
using SkillzWin.Models;

namespace SkillzWin.Services;

/// <summary>
/// Per-platform detection: which source files/skill dirs/executables prove a tool is present.
/// Mirrors macOS <c>PlatformSourceDetector</c>. Executables are resolved Windows-natively via
/// the executable search dirs × names × PATHEXT (no Unix exec bit — R/§4.7).
/// </summary>
public sealed class PlatformSourceDetector
{
    private readonly IAgentEnvironment _env;
    private readonly PlatformSkillPaths _paths;
    private readonly OpenClawConfig _openClaw;
    private readonly string[] _pathExt;

    public PlatformSourceDetector(IAgentEnvironment env, PlatformSkillPaths paths, OpenClawConfig openClaw)
    {
        _env = env;
        _paths = paths;
        _openClaw = openClaw;
        _pathExt = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public IReadOnlyList<PlatformSourceStatus> Detect(CatalogSnapshot snapshot)
    {
        return AgentPlatformInfo.All.Select(platform =>
        {
            var profile = Profile(platform);
            var signals = DetectionSignals(profile);
            var count = CatalogFilter.Items(snapshot, CatalogSection.All, platform).Count;
            return new PlatformSourceStatus(
                Platform: platform,
                IsDetected: signals.Any(s => s.IsInstallSignal),
                ScanPaths: profile.SourcePaths,
                DetectionSignals: signals,
                ItemCount: count,
                HookSupport: profile.HookSupport,
                NotDetectedHint: profile.NotDetectedHint);
        }).ToList();
    }

    public bool IsInstalled(AgentPlatform platform)
        => DetectionSignals(Profile(platform)).Any(s => s.IsInstallSignal);

    public IReadOnlyCollection<AgentPlatform> DetectedPlatforms(IEnumerable<PlatformSourceStatus> statuses)
        => statuses.Where(s => s.IsDetected).Select(s => s.Platform).ToHashSet();

    /// <summary>Don't pre-check absent tools when creating a skill — force a deliberate choice.</summary>
    public IReadOnlyCollection<AgentPlatform> DefaultNewSkillPlatforms(IEnumerable<PlatformSourceStatus> statuses)
        => DetectedPlatforms(statuses);

    public IReadOnlyList<string> AllScanPaths(AgentPlatform platform) => Profile(platform).SourcePaths;

    // --- Profile ---

    private sealed record DetectionProfile(
        AgentPlatform Platform,
        IReadOnlyList<string> SourcePaths,
        IReadOnlyList<string> ExecutableNames,
        string NotDetectedHint,
        PlatformHookSupport HookSupport);

    private DetectionProfile Profile(AgentPlatform platform) => new(
        platform,
        Unique(SourcePaths(platform)),
        ExecutableNames(platform),
        NotDetectedHint(platform),
        HookSupport(platform));

    private IReadOnlyList<string> SourcePaths(AgentPlatform platform)
    {
        var home = _env.HomeDirectoryFor(platform);
        var paths = new List<string>(_paths.SkillScanRoots(platform));

        switch (platform)
        {
            case AgentPlatform.Cursor:
                paths.Add(Path.Combine(home, "mcp.json"));
                paths.Add(Path.Combine(home, "agent-hooks.json"));
                paths.Add(Path.Combine(home, "plugins", "cache"));
                paths.Add(Path.Combine(home, "skills-cursor"));
                break;
            case AgentPlatform.ClaudeCode:
                paths.Add(Path.Combine(home, "settings.json"));
                paths.Add(Path.Combine(home, ".mcp.json"));
                paths.Add(Path.Combine(home, "plugins", "cache"));
                paths.Add(Path.Combine(home, "plugins", "installed_plugins.json"));
                break;
            case AgentPlatform.Codex:
                paths.Add(Path.Combine(home, "config.toml"));
                paths.Add(Path.Combine(home, "hooks.json"));
                paths.Add(Path.Combine(home, "plugins", "cache"));
                paths.Add(Path.Combine(home, "process_manager", "chat_processes.json"));
                break;
            case AgentPlatform.Hermes:
                paths.Add(Path.Combine(home, "config.yaml"));
                paths.Add(Path.Combine(home, "processes.json"));
                paths.Add(Path.Combine(home, "sessions"));
                paths.Add(Path.Combine(home, "plugins"));
                break;
            case AgentPlatform.Pi:
                break;
            case AgentPlatform.OpenClaw:
                paths.Add(_paths.AgentsSkillsDirectory);
                paths.Add(_openClaw.ConfigUrl);
                paths.Add(_openClaw.WorkspaceDirectory());
                paths.Add(_openClaw.WorkspaceSkillsDirectory());
                paths.Add(Path.Combine(_env.HomeDirectory, ".opencode"));
                break;
        }

        paths.Add(home);
        return paths;
    }

    private static IReadOnlyList<string> ExecutableNames(AgentPlatform platform) => platform switch
    {
        AgentPlatform.Cursor => new[] { "cursor-agent", "cursor" },
        AgentPlatform.ClaudeCode => new[] { "claude" },
        AgentPlatform.Codex => new[] { "codex" },
        AgentPlatform.Hermes => new[] { "hermes", "hermes-cli", "tirith" },
        AgentPlatform.Pi => new[] { "pi" },
        AgentPlatform.OpenClaw => new[] { "opencode", "open-code", "openclaw", "open-claw" },
        _ => Array.Empty<string>(),
    };

    private static string NotDetectedHint(AgentPlatform platform) => platform switch
    {
        AgentPlatform.Cursor => "Install Cursor or add skills under ~/.cursor/skills.",
        AgentPlatform.ClaudeCode => "Install Claude Code or add skills under ~/.claude/skills.",
        AgentPlatform.Codex => "Install Codex or add skills under ~/.codex/skills.",
        AgentPlatform.Hermes => "Install Hermes or add skills under ~/.hermes/skills.",
        AgentPlatform.Pi => "Install Pi or add skills under ~/.pi/agent/skills.",
        AgentPlatform.OpenClaw => "Install OpenCode or add skills under the legacy ~/.openclaw layout.",
        _ => "",
    };

    private static PlatformHookSupport HookSupport(AgentPlatform platform) => platform switch
    {
        AgentPlatform.Cursor or AgentPlatform.ClaudeCode or AgentPlatform.Codex => PlatformHookSupport.PreciseWaitingState,
        _ => PlatformHookSupport.ProcessFallback,
    };

    // --- Detection ---

    private IReadOnlyList<PlatformDetectionSignal> DetectionSignals(DetectionProfile profile)
    {
        var signals = new List<PlatformDetectionSignal>();

        foreach (var path in profile.SourcePaths)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) continue;
            signals.Add(new PlatformDetectionSignal(
                PlatformDetectionSignalKind.Source, SourceLabel(path), path, IsInstallSource(path)));
        }

        foreach (var root in _paths.SkillScanRoots(profile.Platform))
        {
            if (!DirectoryContainsSkillMd(root)) continue;
            var signal = new PlatformDetectionSignal(
                PlatformDetectionSignalKind.Source, SourceLabel(root), root, IsInstallSource(root));
            if (!signals.Contains(signal)) signals.Add(signal);
        }

        foreach (var exe in ExecutableCandidates(profile.ExecutableNames))
        {
            signals.Add(new PlatformDetectionSignal(
                PlatformDetectionSignalKind.Executable, "Executable", exe, IsInstallSignal: true));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return signals.Where(s => seen.Add($"{s.Kind}-{s.Url}")).ToList();
    }

    private IEnumerable<string> ExecutableCandidates(IReadOnlyList<string> names)
    {
        foreach (var dir in _env.ExecutableSearchDirectories)
        {
            foreach (var name in names)
            {
                var resolved = ResolveExecutable(dir, name);
                if (resolved is not null) yield return resolved;
            }
        }
    }

    private string? ResolveExecutable(string dir, string name)
    {
        var bare = Path.Combine(dir, name);
        if (File.Exists(bare) && Path.HasExtension(bare)) return bare;
        foreach (var ext in _pathExt)
        {
            var candidate = Path.Combine(dir, name + ext);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private string SourceLabel(string url)
    {
        if (IsSharedSkillSource(url)) return "Shared skill source";
        var name = Path.GetFileName(url);
        var normalized = url.Replace('\\', '/');
        if (name == "SKILL.md" || name == "skills" || normalized.Contains("/skills", StringComparison.Ordinal))
            return "Skill source";
        if (name is "mcp.json" or ".mcp.json" or "config.toml") return "Config";
        if (name.Contains("plugin", StringComparison.Ordinal) || normalized.Contains("/plugins", StringComparison.Ordinal))
            return "Plugin source";
        return "Home folder";
    }

    private bool IsInstallSource(string url)
        => !IsSharedSkillSource(url) && !IsBareHomeDirectory(url);

    private bool IsBareHomeDirectory(string url)
    {
        var norm = NormalizePath(url);
        return AgentPlatformInfo.All.Any(p => NormalizePath(_env.HomeDirectoryFor(p)) == norm);
    }

    private bool IsSharedSkillSource(string url)
    {
        var shared = _paths.AgentsSkillsDirectory.Replace('\\', '/');
        var u = url.Replace('\\', '/');
        return string.Equals(u, shared, StringComparison.OrdinalIgnoreCase)
            || u.StartsWith(shared + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool DirectoryContainsSkillMd(string url)
    {
        if (!Directory.Exists(url)) return false;
        return HiddenAwareWalk.Files(url)
            .Any(f => string.Equals(Path.GetFileName(f), "SKILL.md", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> Unique(IEnumerable<string> paths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return paths.Where(p => seen.Add(NormalizePath(p))).ToList();
    }

    private static string NormalizePath(string path)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch { return path; }
    }
}
