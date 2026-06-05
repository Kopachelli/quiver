using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using SkillzWin.Models;

namespace SkillzWin.Themes;

/// <summary>True → a fixed <see cref="GridLength"/> (parameter or 220), False → 0. For a collapsible column.</summary>
public sealed class BoolToGridLengthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var width = double.TryParse(parameter as string, NumberStyles.Any, CultureInfo.InvariantCulture, out var w) ? w : 220;
        return value is true ? new GridLength(width) : new GridLength(0);
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>An <see cref="AgentPlatform"/> → its display name ("Cursor", "Claude Code", …).</summary>
public sealed class AgentPlatformDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is AgentPlatform p ? p.DisplayName() : string.Empty;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>True/False → "Enabled"/"Disabled" (plugin badges).</summary>
public sealed class BoolToEnabledTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "Enabled" : "Disabled";
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Joins a string sequence with a space (MCP arguments).</summary>
public sealed class ArgsJoinConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is IEnumerable<string> items ? string.Join(" ", items) : string.Empty;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Joins env key names with ", " and notes that values are hidden.</summary>
public sealed class EnvKeysDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not IEnumerable<string> keys) return string.Empty;
        var list = keys.ToList();
        return list.Count == 0 ? string.Empty : string.Join(", ", list) + " (values hidden)";
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Catalog row subtitle: prefer the trimmed description; else the list subtitle (if it isn't the
/// name); else "No description". Mirrors macOS <c>SkillzListRow.subtitleText</c>.
/// </summary>
public sealed class CatalogSubtitleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not CatalogItem item) return string.Empty;
        var desc = item.DescriptionText.Trim();
        if (desc.Length > 0) return desc;
        var sub = item.ListSubtitle.Trim();
        if (sub.Length > 0 && !string.Equals(sub, item.DisplayName, StringComparison.Ordinal)) return sub;
        return "No description";
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Uppercases a string. Used for section-header labels (macOS textCase(.uppercase)).</summary>
public sealed class ToUpperConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => (value as string)?.ToUpperInvariant() ?? string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>True/non-empty -> Visible, else Collapsed. Pass parameter "invert" to flip.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value switch
        {
            bool b => b,
            string s => !string.IsNullOrEmpty(s),
            int i => i != 0,
            null => false,
            _ => true,
        };
        if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase))
            flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}

/// <summary>Null or empty string/collection -> Collapsed, else Visible. Pass "invert" to flip.</summary>
public sealed class NullOrEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasValue = value switch
        {
            null => false,
            string s => !string.IsNullOrWhiteSpace(s),
            System.Collections.ICollection c => c.Count > 0,
            _ => true,
        };
        if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase))
            hasValue = !hasValue;
        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Collapses a full file path to a compact form with a middle ellipsis for narrow columns.</summary>
public sealed class CompactPathConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path)) return string.Empty;
        const int max = 64;
        if (path.Length <= max) return path;
        var name = Path.GetFileName(path);
        var head = path[..Math.Min(20, path.Length)];
        return $"{head}…{(name.Length > 0 ? name : path[^Math.Min(24, path.Length)..])}";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
