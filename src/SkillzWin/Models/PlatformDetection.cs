using System.IO;
using System.Linq;

namespace SkillzWin.Models;

/// <summary>How precisely a platform's live agent activity can be observed. Mirrors macOS <c>PlatformHookSupport</c>.</summary>
public enum PlatformHookSupport
{
    PreciseWaitingState,
    ProcessFallback,
}

public static class PlatformHookSupportInfo
{
    public static string Label(this PlatformHookSupport s) => s switch
    {
        PlatformHookSupport.PreciseWaitingState => "Waiting-state hooks",
        PlatformHookSupport.ProcessFallback => "Process fallback",
        _ => "",
    };

    public static string Detail(this PlatformHookSupport s) => s switch
    {
        PlatformHookSupport.PreciseWaitingState => "Precise waiting-state hooks are available for this tool.",
        PlatformHookSupport.ProcessFallback => "Live activity uses process detection until this tool exposes stable hooks.",
        _ => "",
    };
}

public enum PlatformDetectionSignalKind
{
    Source,
    Executable,
}

/// <summary>One piece of evidence that a platform is present. Mirrors macOS <c>PlatformDetectionSignal</c>.</summary>
public sealed record PlatformDetectionSignal(
    PlatformDetectionSignalKind Kind,
    string Label,
    string Url,
    bool IsInstallSignal);

/// <summary>Per-platform detection result shown in Settings → Sources. Mirrors macOS <c>PlatformSourceStatus</c>.</summary>
public sealed record PlatformSourceStatus(
    AgentPlatform Platform,
    bool IsDetected,
    IReadOnlyList<string> ScanPaths,
    IReadOnlyList<PlatformDetectionSignal> DetectionSignals,
    int ItemCount,
    PlatformHookSupport HookSupport,
    string NotDetectedHint)
{
    public string Id => Platform.RawValue();

    public PlatformDetectionSignal? DetectedSignal =>
        DetectionSignals.FirstOrDefault(s => s.IsInstallSignal) ?? DetectionSignals.FirstOrDefault();

    public string? PrimaryPath =>
        DetectedSignal?.Url
        ?? ScanPaths.FirstOrDefault(p => File.Exists(p) || Directory.Exists(p))
        ?? ScanPaths.FirstOrDefault();

    public string DetectionLabel => DetectedSignal?.Label ?? "No install signal found";

    public string StatusLabel => IsDetected ? "Found" : "Not detected";

    public string HookSupportLabel => HookSupport.Label();
}
