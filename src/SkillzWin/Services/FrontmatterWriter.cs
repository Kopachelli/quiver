using System.Collections.Generic;
using SkillzWin.Models;

namespace SkillzWin.Services;

/// <summary>
/// Faithful port of macOS <c>FrontmatterWriter</c> — the intentionally non-standard mini-YAML
/// emitter that round-trips with <see cref="FrontmatterParser"/>. Fixed key order; multi-line
/// descriptions emitted as a <c>&gt;</c> block; <c>name: skill</c> fallback when empty.
/// Always emits <c>\n</c> line endings (callers write UTF-8 without BOM — R7).
/// </summary>
public static class FrontmatterWriter
{
    public readonly record struct Update(
        string? Name = null,
        string? Description = null,
        string? Version = null,
        bool? DisableModelInvocation = null);

    public static string Apply(string content, Update update)
    {
        var (existing, body) = FrontmatterParser.Parse(content);

        var name = existing.Name;
        var description = existing.Description;
        var version = existing.Version;
        var disable = existing.DisableModelInvocation;

        if (update.Name is not null) name = update.Name;
        if (update.Description is not null) description = update.Description;
        if (update.Version is not null) version = update.Version.Length == 0 ? null : update.Version;
        if (update.DisableModelInvocation is not null) disable = update.DisableModelInvocation;

        var merged = new SkillFrontmatter(name, description, version, disable);
        var yaml = Serialize(merged);
        var trimmedBody = body.Trim('\r', '\n');   // Swift trims .newlines from both ends

        return trimmedBody.Length == 0
            ? $"---\n{yaml}---\n"
            : $"---\n{yaml}---\n\n{trimmedBody}\n";
    }

    public static string Make(string name, string description, string body)
    {
        var trimmedBody = body.Trim();   // Swift .whitespacesAndNewlines
        var bodyText = trimmedBody.Length == 0
            ? $"# {name}\n\nDescribe when to use this skill."
            : trimmedBody;
        return Apply($"---\nname: skill\n---\n\n{bodyText}\n", new Update(Name: name, Description: description));
    }

    private static string Serialize(SkillFrontmatter fm)
    {
        var lines = new List<string>();

        if (!string.IsNullOrEmpty(fm.Name))
            lines.Add($"name: {QuoteIfNeeded(fm.Name!)}");

        if (!string.IsNullOrEmpty(fm.Description))
        {
            if (fm.Description!.Contains('\n'))
            {
                lines.Add("description: >");
                lines.AddRange(fm.Description.Split('\n'));
            }
            else
            {
                lines.Add($"description: {QuoteIfNeeded(fm.Description)}");
            }
        }

        if (!string.IsNullOrEmpty(fm.Version))
            lines.Add($"version: {QuoteIfNeeded(fm.Version!)}");

        if (fm.DisableModelInvocation is bool disable)
            lines.Add($"disable-model-invocation: {(disable ? "true" : "false")}");

        if (lines.Count == 0)
            lines.Add("name: skill");

        return string.Join("\n", lines) + "\n";
    }

    private static string QuoteIfNeeded(string value)
    {
        if (value.Contains(':') || value.Contains('#') || value.StartsWith(" ", StringComparison.Ordinal))
            return $"\"{value.Replace("\"", "\\\"")}\"";
        return value;
    }
}
