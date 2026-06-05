using System.Windows;
using System.Windows.Controls;
using SkillzWin.Models;
using SkillzWin.ViewModels;

namespace SkillzWin.Views;

/// <summary>Picks the detail template by the selected detail VM/model type (skill / mcp / plugin).</summary>
public sealed class DetailTemplateSelector : DataTemplateSelector
{
    public DataTemplate? SkillTemplate { get; set; }
    public DataTemplate? McpTemplate { get; set; }
    public DataTemplate? PluginTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container) => item switch
    {
        SkillDetailViewModel => SkillTemplate,
        McpItem => McpTemplate,
        PluginItem => PluginTemplate,
        _ => null,
    };
}
