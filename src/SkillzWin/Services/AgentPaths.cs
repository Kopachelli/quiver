using System.IO;
using SkillzWin.Models;

namespace SkillzWin.Services;

/// <summary>
/// Constants and derived app/session/state paths off an injected <see cref="IAgentEnvironment"/>.
/// Mirrors macOS <c>AgentPaths</c>. The session/state members are used by the (deferred) agent
/// monitor; the core app only consumes <see cref="ApplicationSupportDirectory"/> as a watch root.
/// </summary>
public sealed class AgentPaths
{
    public const int StateFileVersion = 1;
    public static readonly TimeSpan StaleWorkingInterval = TimeSpan.FromSeconds(90);
    public static readonly TimeSpan StaleNeedsInputInterval = TimeSpan.FromHours(1);
    public static readonly TimeSpan StaleIdleInterval = TimeSpan.FromSeconds(8);

    private readonly IAgentEnvironment _env;

    public AgentPaths(IAgentEnvironment env) => _env = env;

    public string ApplicationSupportDirectory => _env.ApplicationSupportDirectory;
    public string AgentStateFileUrl => Path.Combine(ApplicationSupportDirectory, "agent-state.json");
    public string SkillzHomeDirectory => _env.SkillzHomeDirectory;

    /// <summary>Notify hook — a PowerShell script on Windows (was <c>.sh</c> on macOS). Deferred.</summary>
    public string NotifyScriptInstalledUrl => Path.Combine(SkillzHomeDirectory, "bin", "skillz-agent-notify.ps1");

    public string ClaudeSessionsDirectory => Path.Combine(_env.HomeDirectoryFor(AgentPlatform.ClaudeCode), "sessions");
    public string CodexSessionsDirectory => Path.Combine(_env.HomeDirectoryFor(AgentPlatform.Codex), "sessions");
    public string CodexChatProcessesFile => Path.Combine(_env.HomeDirectoryFor(AgentPlatform.Codex), "process_manager", "chat_processes.json");
    public string CursorProjectsDirectory => Path.Combine(_env.HomeDirectoryFor(AgentPlatform.Cursor), "projects");

    /// <summary>
    /// Paths to watch for the (deferred) agent-state monitor, existence-filtered exactly as macOS:
    /// the chat-processes file must itself exist; every other path is kept if its parent dir or the
    /// path itself exists.
    /// </summary>
    public IReadOnlyList<string> WatchPathsForAgents()
    {
        var candidates = new[]
        {
            ApplicationSupportDirectory,
            ClaudeSessionsDirectory,
            CodexSessionsDirectory,
            CodexChatProcessesFile,
            CursorProjectsDirectory,
        };

        return candidates.Where(path =>
        {
            if (Path.GetFileName(path) == "chat_processes.json")
                return File.Exists(path);
            var parent = Path.GetDirectoryName(path);
            return (parent is not null && Directory.Exists(parent)) || File.Exists(path) || Directory.Exists(path);
        }).ToList();
    }
}
