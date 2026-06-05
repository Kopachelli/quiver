namespace SkillzWin.Models;

/// <summary>MCP server transport kind. Mirrors macOS <c>MCPTransport</c>.</summary>
public enum McpTransport
{
    Stdio,
    Http,
    Unknown,
}

/// <summary>One MCP server entry parsed from a platform's MCP config. Mirrors macOS <c>MCPItem</c>.</summary>
public sealed record McpItem(
    string Id,
    AgentPlatform Platform,
    string Name,
    McpTransport Transport,
    string? Command,
    IReadOnlyList<string> Args,
    string? Url,
    IReadOnlyList<string> EnvKeys,
    string ConfigFileUrl,
    DateTime? ModifiedAt)
{
    public string TransportLabel => Transport switch
    {
        McpTransport.Stdio => "stdio",
        McpTransport.Http => "HTTP",
        _ => "Unknown",
    };

    /// <summary>One-line endpoint summary; em dash (U+2014) when neither url nor command is present.</summary>
    public string EndpointSummary
    {
        get
        {
            if (!string.IsNullOrEmpty(Url)) return Url!;
            if (Command is not null)
                return Args.Count == 0 ? Command : Command + " " + string.Join(" ", Args);
            return "—";
        }
    }

    public static string MakeId(AgentPlatform platform, string name)
        => $"mcp:{platform.RawValue()}:{name}";
}
