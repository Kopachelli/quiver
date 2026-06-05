using System.Windows;
using ICSharpCode.AvalonEdit;

namespace SkillzWin.Views;

/// <summary>
/// Attached two-way text binding for AvalonEdit (whose <c>Text</c> isn't a DependencyProperty).
/// Bind <c>BoundText</c> to a VM property that routes writes through the editor document so dirty
/// tracking / debounced autosave fire correctly.
/// </summary>
public static class AvalonEditBehavior
{
    private static readonly HashSet<TextEditor> Hooked = new();

    public static readonly DependencyProperty BoundTextProperty =
        DependencyProperty.RegisterAttached(
            "BoundText", typeof(string), typeof(AvalonEditBehavior),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBoundTextChanged));

    public static string GetBoundText(DependencyObject o) => (string)o.GetValue(BoundTextProperty);
    public static void SetBoundText(DependencyObject o, string value) => o.SetValue(BoundTextProperty, value);

    private static void OnBoundTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextEditor editor) return;
        EnsureHooked(editor);
        var text = e.NewValue as string ?? string.Empty;
        if (editor.Text != text) editor.Text = text;
    }

    private static void EnsureHooked(TextEditor editor)
    {
        if (!Hooked.Add(editor)) return;
        editor.TextChanged += (s, _) =>
        {
            var ed = (TextEditor)s!;
            if (GetBoundText(ed) != ed.Text) SetBoundText(ed, ed.Text);
        };
        editor.Unloaded += (s, _) => Hooked.Remove((TextEditor)s!);
    }
}
