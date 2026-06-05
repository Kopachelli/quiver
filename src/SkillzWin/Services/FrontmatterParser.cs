using System.Linq;
using SkillzWin.Models;

namespace SkillzWin.Services;

/// <summary>
/// Faithful port of macOS <c>FrontmatterParser</c> — a deliberately minimal mini-YAML reader
/// (NOT real YAML). Recognizes only <c>name</c>, <c>description</c>, <c>version</c>, and
/// <c>disable-model-invocation</c>; collapses block scalars (<c>&gt;</c>/<c>|</c>) to empty.
/// <para>Windows divergence (R2): trailing <c>\r</c> is stripped before delimiter/line compares
/// so CRLF files parse, while the body is reconstructed verbatim.</para>
/// </summary>
public static class FrontmatterParser
{
    public static (SkillFrontmatter Frontmatter, string Body) Parse(string content)
    {
        if (!content.StartsWith("---", StringComparison.Ordinal))
            return (SkillFrontmatter.Empty, content);

        // Split on '\n' keeping empties; each line may retain a trailing '\r' on CRLF files.
        var lines = content.Split('\n');
        if (lines.Length < 2)
            return (SkillFrontmatter.Empty, content);

        int? endIndex = null;
        for (int i = 1; i < lines.Length; i++)
        {
            if (StripCr(lines[i]) == "---") { endIndex = i; break; }
        }
        if (endIndex is null)
            return (SkillFrontmatter.Empty, content);

        string? name = null, description = null, version = null;
        bool? disable = null;

        for (int i = 1; i < endIndex.Value; i++)
        {
            var trimmed = lines[i].Trim();   // .Trim() also removes the trailing '\r'
            if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
                continue;

            var colon = trimmed.IndexOf(':');
            if (colon < 0) continue;

            var key = trimmed[..colon].Trim();
            var value = trimmed[(colon + 1)..].Trim();
            if (value.StartsWith(">", StringComparison.Ordinal) || value.StartsWith("|", StringComparison.Ordinal))
                value = string.Empty;
            value = value.Trim('"', '\'');

            switch (key)
            {
                case "name":
                    name = value.Length == 0 ? null : value;
                    break;
                case "description":
                    description = value.Length == 0 ? null : value;
                    break;
                case "version":
                    version = value.Length == 0 ? null : value;
                    break;
                case "disable-model-invocation":
                    var lower = value.ToLowerInvariant();
                    disable = lower is "true" or "yes" or "1";
                    break;
            }
        }

        var bodyStart = endIndex.Value + 1;
        var body = bodyStart < lines.Length ? string.Join("\n", lines[bodyStart..]) : string.Empty;

        return (new SkillFrontmatter(name, description, version, disable), body);
    }

    /// <summary>
    /// First non-heading paragraph of the body collapsed to a single line, capped at 280 chars.
    /// Used as a description fallback when frontmatter has none.
    /// </summary>
    public static string FirstParagraph(string body)
    {
        var trimmed = body.Replace("\r", string.Empty).Trim();
        if (trimmed.Length == 0) return string.Empty;

        var paragraphs = trimmed.Split("\n\n");
        var first = (paragraphs.Length > 0 ? paragraphs[0] : string.Empty).Trim();
        var singleLine = string.Join(" ", first
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("#", StringComparison.Ordinal)));

        return singleLine.Length > 280 ? singleLine[..280] : singleLine;
    }

    private static string StripCr(string s) => s.EndsWith("\r", StringComparison.Ordinal) ? s[..^1] : s;
}
