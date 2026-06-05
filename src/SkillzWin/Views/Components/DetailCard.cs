using System.Windows;
using System.Windows.Controls;

namespace SkillzWin.Views.Components;

/// <summary>
/// A bordered card with an uppercase section header and arbitrary content. Mirrors macOS
/// <c>SkillzDetailCard</c>. Templated in <c>Themes/Controls.xaml</c>.
/// </summary>
public class DetailCard : ContentControl
{
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(DetailCard), new PropertyMetadata(string.Empty));

    static DetailCard()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(DetailCard), new FrameworkPropertyMetadata(typeof(DetailCard)));
    }

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }
}
