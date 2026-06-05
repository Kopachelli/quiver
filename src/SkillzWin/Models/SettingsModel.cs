namespace SkillzWin.Models;

/// <summary>App appearance preference. Mirrors macOS <c>SkillzAppearance</c>.</summary>
public enum SkillzAppearance
{
    System,
    Light,
    Dark,
}

public static class SkillzAppearanceInfo
{
    public static readonly IReadOnlyList<SkillzAppearance> All = new[]
    {
        SkillzAppearance.System, SkillzAppearance.Light, SkillzAppearance.Dark,
    };

    public static string RawValue(this SkillzAppearance a) => a switch
    {
        SkillzAppearance.System => "system",
        SkillzAppearance.Light => "light",
        SkillzAppearance.Dark => "dark",
        _ => "system",
    };

    public static string DisplayName(this SkillzAppearance a) => a switch
    {
        SkillzAppearance.System => "System",
        SkillzAppearance.Light => "Light",
        SkillzAppearance.Dark => "Dark",
        _ => "System",
    };

    public static SkillzAppearance FromRaw(string? raw) => raw switch
    {
        "light" => SkillzAppearance.Light,
        "dark" => SkillzAppearance.Dark,
        _ => SkillzAppearance.System,
    };
}

/// <summary>
/// Serialized settings POCO persisted at <c>%APPDATA%\SkillzWin\settings.json</c>. Mirrors the
/// macOS <c>@AppStorage</c> keys/defaults exactly (notch/menu-bar/hook fields persist but are
/// inert until the deferred agent-monitor pass).
/// </summary>
public sealed class SettingsModel
{
    public bool HideBuiltInCursorSkills { get; set; } = false;
    public bool HideSystemCodexSkills { get; set; } = true;
    public double EditorFontSize { get; set; } = 14.0;
    public bool ShowInspector { get; set; } = false;
    public string Appearance { get; set; } = "system";
    public bool EnableAgentNotch { get; set; } = true;
    public string? AgentNotchDisplayUuid { get; set; }
    public bool HasCompletedOnboarding { get; set; } = false;
    public bool ShowAgentCountInMenuBar { get; set; } = true;
    public bool AutoInstallAgentHooks { get; set; } = true;
}
