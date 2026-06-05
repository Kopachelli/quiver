using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkillzWin.Models;
using SkillzWin.Services;

namespace SkillzWin.ViewModels;

/// <summary>First-run onboarding: detected tools + a couple of setup toggles + Get Started.</summary>
public sealed partial class OnboardingViewModel : ObservableObject
{
    private readonly CatalogViewModel _catalog;

    public ISettingsService Settings { get; }
    public Action? RequestClose;

    public OnboardingViewModel(ISettingsService settings, CatalogViewModel catalog)
    {
        Settings = settings;
        _catalog = catalog;
        _catalog.PropertyChanged += OnCatalogChanged;
    }

    public string AppName => AppBrand.Name;
    public string Tagline => "Your agent toolkit, in one quiver.";
    public IReadOnlyList<PlatformSourceStatus> SourceStatuses => _catalog.SourceStatuses;
    public int DetectedCount => _catalog.DetectedPlatforms.Count;
    public bool HasDetected => DetectedCount > 0;

    [RelayCommand]
    private void GetStarted()
    {
        Settings.HasCompletedOnboarding = true;
        RequestClose?.Invoke();
    }

    private void OnCatalogChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CatalogViewModel.SourceStatuses))
        {
            OnPropertyChanged(nameof(SourceStatuses));
            OnPropertyChanged(nameof(DetectedCount));
            OnPropertyChanged(nameof(HasDetected));
        }
    }
}
