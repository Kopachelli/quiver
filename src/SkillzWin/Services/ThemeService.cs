using System.Windows;
using Microsoft.Win32;
using SkillzWin.Models;
using Wpf.Ui.Appearance;

namespace SkillzWin.Services;

/// <summary>
/// Applies the chosen appearance (System / Light / Dark) by switching both the WPF-UI theme and
/// the app's own color dictionary at runtime.
/// </summary>
public static class ThemeService
{
    public static void Apply(SkillzAppearance appearance)
    {
        var dark = ResolveDark(appearance);
        ApplicationThemeManager.Apply(dark ? ApplicationTheme.Dark : ApplicationTheme.Light);
        SwapColors(dark);
    }

    private static bool ResolveDark(SkillzAppearance appearance) => appearance switch
    {
        SkillzAppearance.Dark => true,
        SkillzAppearance.Light => false,
        _ => IsSystemDark(),
    };

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    private static void SwapColors(bool dark)
    {
        var app = Application.Current;
        if (app is null) return;
        var dicts = app.Resources.MergedDictionaries;
        var target = dark ? "Themes/Colors.Dark.xaml" : "Themes/Colors.Light.xaml";

        for (var i = 0; i < dicts.Count; i++)
        {
            var src = dicts[i].Source?.OriginalString ?? string.Empty;
            if (src.Contains("Colors.Light") || src.Contains("Colors.Dark"))
            {
                dicts[i] = new ResourceDictionary { Source = new Uri(target, UriKind.Relative) };
                return;
            }
        }
    }
}
