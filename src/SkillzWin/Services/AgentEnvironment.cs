using System.IO;
using SkillzWin.Models;

namespace SkillzWin.Services;

/// <summary>
/// Abstracts the home directory and a few derived roots so discovery can run against a temp
/// root in tests. Mirrors macOS <c>AgentEnvironment</c>. Registered as a DI singleton.
/// </summary>
public interface IAgentEnvironment
{
    /// <summary>The user's home directory — <c>C:\Users\&lt;user&gt;</c>.</summary>
    string HomeDirectory { get; }

    /// <summary>App support / state directory — <c>%APPDATA%\Skillz</c>.</summary>
    string ApplicationSupportDirectory { get; }

    /// <summary>The app's own dotfolder — <c>C:\Users\&lt;user&gt;\.skillz</c>.</summary>
    string SkillzHomeDirectory { get; }

    /// <summary>Directories searched when resolving CLI executables (source detection).</summary>
    IReadOnlyList<string> ExecutableSearchDirectories { get; }

    /// <summary>The home dotfolder for a platform, e.g. <c>C:\Users\&lt;user&gt;\.cursor</c>.</summary>
    string HomeDirectoryFor(AgentPlatform platform);
}

/// <inheritdoc />
public sealed class AgentEnvironment : IAgentEnvironment
{
    public string HomeDirectory { get; }
    public string ApplicationSupportDirectory { get; }
    public string SkillzHomeDirectory { get; }
    public IReadOnlyList<string> ExecutableSearchDirectories { get; }

    public AgentEnvironment(
        string homeDirectory,
        string applicationSupportDirectory,
        string skillzHomeDirectory,
        IReadOnlyList<string> executableSearchDirectories)
    {
        HomeDirectory = homeDirectory;
        ApplicationSupportDirectory = applicationSupportDirectory;
        SkillzHomeDirectory = skillzHomeDirectory;
        ExecutableSearchDirectories = executableSearchDirectories;
    }

    public string HomeDirectoryFor(AgentPlatform platform)
        => Path.Combine(HomeDirectory, platform.DotFolderName());

    /// <summary>The live environment rooted at the real user profile / AppData.</summary>
    public static AgentEnvironment Live
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            return new AgentEnvironment(
                homeDirectory: home,
                applicationSupportDirectory: Path.Combine(appData, "Skillz"),
                skillzHomeDirectory: Path.Combine(home, ".skillz"),
                executableSearchDirectories: BuildExecutableSearchDirectories(home, localAppData, appData, programFiles));
        }
    }

    /// <summary>A test environment with everything re-rooted under <paramref name="root"/>.</summary>
    public static AgentEnvironment ForRoot(string root) => new(
        homeDirectory: root,
        applicationSupportDirectory: Path.Combine(root, "AppData", "Roaming", "Skillz"),
        skillzHomeDirectory: Path.Combine(root, ".skillz"),
        executableSearchDirectories: new[] { Path.Combine(root, ".local", "bin") });

    private static IReadOnlyList<string> BuildExecutableSearchDirectories(
        string home, string localAppData, string appData, string programFiles)
    {
        var dirs = new List<string>
        {
            Path.Combine(home, ".local", "bin"),
            Path.Combine(home, ".opencode", "bin"),
            Path.Combine(home, ".hermes", "bin"),
            Path.Combine(localAppData, "Microsoft", "WindowsApps"),
            Path.Combine(appData, "npm"),
            programFiles,
        };
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            dirs.Add(dir);
        // De-dup while preserving order.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return dirs.Where(d => !string.IsNullOrWhiteSpace(d) && seen.Add(d)).ToList();
    }
}
