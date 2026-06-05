using System.Windows;
using System.Windows.Controls;

namespace SkillzWin.Views.Components;

/// <summary>A fixed-label / selectable-value row. Mirrors macOS <c>SkillzDetailRow</c>.</summary>
public partial class DetailRow : UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(DetailRow), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(DetailRow), new PropertyMetadata(string.Empty));

    public DetailRow() => InitializeComponent();

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }
}
