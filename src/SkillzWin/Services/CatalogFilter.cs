using System.Linq;
using SkillzWin.Models;

namespace SkillzWin.Services;

/// <summary>
/// Single source of truth for library section × platform × search filtering. Mirrors macOS
/// <c>CatalogFilter</c>. Uses <see cref="StringComparison.OrdinalIgnoreCase"/> for predictability (R11).
/// </summary>
public static class CatalogFilter
{
    public static IReadOnlyList<CatalogItem> Items(
        CatalogSnapshot snapshot,
        CatalogSection section,
        AgentPlatform? platform,
        string searchText = "")
    {
        IEnumerable<CatalogItem> items = snapshot.AllItems;

        items = section switch
        {
            CatalogSection.Skills => items.Where(i => i.Kind == CatalogItemKind.Skill),
            CatalogSection.McpServers => items.Where(i => i.Kind == CatalogItemKind.Mcp),
            CatalogSection.Plugins => items.Where(i => i.Kind == CatalogItemKind.Plugin),
            _ => items,
        };

        if (platform is AgentPlatform p)
        {
            items = items.Where(i =>
                i.Platform == p || (i.AsSkill?.AlsoAvailableOn.Contains(p) ?? false));
        }

        var query = searchText.Trim();
        if (query.Length > 0)
        {
            items = items.Where(i =>
                i.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || i.DescriptionText.Contains(query, StringComparison.OrdinalIgnoreCase)
                || i.ListSubtitle.Contains(query, StringComparison.OrdinalIgnoreCase)
                || i.Platform.DisplayName().Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        return items.ToList();
    }

    public static IReadOnlyList<CatalogItem> Sorted(IEnumerable<CatalogItem> items)
        => items.OrderBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
}
