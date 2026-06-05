using System.Windows;
using System.Windows.Controls;

namespace SkillzWin.Views.Components;

public enum TagVariant
{
    Outline,
    Filled,
    Muted,
    Subtle,
}

/// <summary>A capsule badge. Mirrors macOS <c>SkillzTag</c> (outline / filled / muted / subtle).</summary>
public partial class Tag : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(Tag), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty VariantProperty =
        DependencyProperty.Register(nameof(Variant), typeof(TagVariant), typeof(Tag), new PropertyMetadata(TagVariant.Subtle));

    public Tag() => InitializeComponent();

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public TagVariant Variant
    {
        get => (TagVariant)GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }
}
