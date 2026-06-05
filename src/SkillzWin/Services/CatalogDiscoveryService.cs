using SkillzWin.Models;

namespace SkillzWin.Services;

/// <summary>
/// Pure aggregator that runs the three scanners and returns a <see cref="CatalogSnapshot"/>.
/// Mirrors macOS <c>DiscoveryEngine</c>.
/// </summary>
public sealed class CatalogDiscoveryService
{
    private readonly SkillScanner _skills;
    private readonly McpScanner _mcps;
    private readonly PluginScanner _plugins;

    public CatalogDiscoveryService(SkillScanner skills, McpScanner mcps, PluginScanner plugins)
    {
        _skills = skills;
        _mcps = mcps;
        _plugins = plugins;
    }

    public CatalogSnapshot Discover(bool hideBuiltInCursor, bool hideSystemCodex)
        => new(
            _skills.Scan(hideBuiltInCursor, hideSystemCodex),
            _mcps.Scan(),
            _plugins.Scan());
}
