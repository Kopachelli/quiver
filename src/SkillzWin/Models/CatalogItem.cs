namespace SkillzWin.Models;

/// <summary>
/// A unified catalog entry — skill, MCP server, or plugin. C# discriminated-union port of the
/// macOS <c>enum CatalogItem</c> with associated values: an abstract base exposing the common
/// projected members, plus three sealed wrappers. Bind to lists via concrete-type DataTemplates.
/// </summary>
public abstract record CatalogItem
{
    public abstract string Id { get; }
    public abstract CatalogItemKind Kind { get; }
    public abstract AgentPlatform Platform { get; }
    public abstract string DisplayName { get; }
    public abstract string DescriptionText { get; }
    public abstract string ListSubtitle { get; }
    public abstract DateTime? ModifiedAt { get; }

    public SkillItem? AsSkill => (this as SkillCatalogItem)?.Skill;
    public McpItem? AsMcp => (this as McpCatalogItem)?.Mcp;
    public PluginItem? AsPlugin => (this as PluginCatalogItem)?.Plugin;

    public static CatalogItem From(SkillItem skill) => new SkillCatalogItem(skill);
    public static CatalogItem From(McpItem mcp) => new McpCatalogItem(mcp);
    public static CatalogItem From(PluginItem plugin) => new PluginCatalogItem(plugin);
}

public sealed record SkillCatalogItem(SkillItem Skill) : CatalogItem
{
    public override string Id => Skill.Id;
    public override CatalogItemKind Kind => CatalogItemKind.Skill;
    public override AgentPlatform Platform => Skill.Platform;
    public override string DisplayName => Skill.DisplayName;
    public override string DescriptionText => Skill.Description;
    public override string ListSubtitle => Skill.ListSubtitle;
    public override DateTime? ModifiedAt => Skill.ModifiedAt;
}

public sealed record McpCatalogItem(McpItem Mcp) : CatalogItem
{
    public override string Id => Mcp.Id;
    public override CatalogItemKind Kind => CatalogItemKind.Mcp;
    public override AgentPlatform Platform => Mcp.Platform;
    public override string DisplayName => Mcp.Name;
    public override string DescriptionText => Mcp.EndpointSummary;
    public override string ListSubtitle => Mcp.ConfigFileUrl;
    public override DateTime? ModifiedAt => Mcp.ModifiedAt;
}

public sealed record PluginCatalogItem(PluginItem Plugin) : CatalogItem
{
    public override string Id => Plugin.Id;
    public override CatalogItemKind Kind => CatalogItemKind.Plugin;
    public override AgentPlatform Platform => Plugin.Platform;
    public override string DisplayName => Plugin.DisplayName;
    public override string DescriptionText => Plugin.Description;
    public override string ListSubtitle => Plugin.ListSubtitle;
    public override DateTime? ModifiedAt => Plugin.ModifiedAt;
}
