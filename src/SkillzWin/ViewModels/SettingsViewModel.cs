using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkillzWin.Models;
using SkillzWin.Services;

namespace SkillzWin.ViewModels;

/// <summary>Settings window: General (appearance, library), Sources, Editor.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly CatalogViewModel _catalog;

    public ISettingsService Settings { get; }
    public IReadOnlyList<SkillzAppearance> Appearances => SkillzAppearanceInfo.All;

    public Action? RequestShowOnboarding;
    public Action? RequestClose;

    public SettingsViewModel(ISettingsService settings, CatalogViewModel catalog)
    {
        Settings = settings;
        _catalog = catalog;
        _catalog.PropertyChanged += OnCatalogChanged;
    }

    public string AppName => AppBrand.Name;
    public IReadOnlyList<PlatformSourceStatus> SourceStatuses => _catalog.SourceStatuses;
    public int DetectedCount => _catalog.DetectedPlatforms.Count;

    [RelayCommand]
    private void RefreshSources() => _catalog.Refresh();

    [RelayCommand]
    private void RevealPath(string? path)
    {
        if (!string.IsNullOrEmpty(path)) _catalog.RevealInExplorer(path);
    }

    [RelayCommand]
    private void ShowOnboardingAgain()
    {
        Settings.HasCompletedOnboarding = false;
        RequestClose?.Invoke();
        RequestShowOnboarding?.Invoke();
    }

    private void OnCatalogChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CatalogViewModel.SourceStatuses))
        {
            OnPropertyChanged(nameof(SourceStatuses));
            OnPropertyChanged(nameof(DetectedCount));
        }
    }
}
