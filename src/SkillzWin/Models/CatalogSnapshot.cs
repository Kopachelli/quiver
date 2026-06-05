namespace SkillzWin.Models;

/// <summary>
/// The full result of one discovery pass. Mirrors macOS <c>DiscoveryEngine.CatalogSnapshot</c>.
/// <see cref="AllItems"/> concatenates skills, then MCPs, then plugins (the macOS ordering).
/// </summary>
public sealed record CatalogSnapshot(
    IReadOnlyList<SkillItem> Skills,
    IReadOnlyList<McpItem> Mcps,
    IReadOnlyList<PluginItem> Plugins)
{
    public static readonly CatalogSnapshot Empty =
        new(Array.Empty<SkillItem>(), Array.Empty<McpItem>(), Array.Empty<PluginItem>());

    /// <summary>All items as a single sequence: skills → MCPs → plugins.</summary>
    public IReadOnlyList<CatalogItem> AllItems
    {
        get
        {
            var list = new List<CatalogItem>(Skills.Count + Mcps.Count + Plugins.Count);
            foreach (var s in Skills) list.Add(CatalogItem.From(s));
            foreach (var m in Mcps) list.Add(CatalogItem.From(m));
            foreach (var p in Plugins) list.Add(CatalogItem.From(p));
            return list;
        }
    }
}
