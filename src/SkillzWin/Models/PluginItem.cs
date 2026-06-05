namespace SkillzWin.Models;

/// <summary>One installed plugin. Mirrors macOS <c>PluginItem</c>.</summary>
public sealed record PluginItem(
    string Id,
    AgentPlatform Platform,
    string PluginId,
    string DisplayName,
    string Description,
    string? Version,
    string? Marketplace,
    bool IsEnabled,
    string? InstallPath,
    string? MetadataPath,
    int SkillCount,
    DateTime? ModifiedAt)
{
    public string ListSubtitle => Marketplace ?? PluginId;

    public static string MakeId(AgentPlatform platform, string pluginId, string? installPath)
        => $"plugin:{platform.RawValue()}:{pluginId}:{installPath ?? pluginId}";
}
