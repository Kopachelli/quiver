using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Controls;

namespace SkillzWin.ViewModels;

/// <summary>One sidebar nav row (a library section or a platform filter). Icon + title + live count.</summary>
public sealed partial class SidebarRowViewModel : ObservableObject
{
    private readonly Func<int> _count;
    private readonly Func<bool> _isSelected;
    private readonly Action _activate;

    public SymbolRegular Icon { get; }
    public string Title { get; }

    public SidebarRowViewModel(SymbolRegular icon, string title, Func<int> count, Func<bool> isSelected, Action activate)
    {
        Icon = icon;
        Title = title;
        _count = count;
        _isSelected = isSelected;
        _activate = activate;
    }

    public int Count => _count();
    public bool IsSelected => _isSelected();

    [RelayCommand]
    private void Activate() => _activate();

    /// <summary>Re-reads the count and selection state (called when the catalog/filters change).</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(IsSelected));
    }
}
