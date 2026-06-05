using System.ComponentModel;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SkillzWin.Services;
using SkillzWin.ViewModels;
using SkillzWin.Views;

namespace SkillzWin;

/// <summary>
/// Application bootstrap: builds the DI container, applies the theme, and shows the
/// main window. Mirrors the macOS <c>skillzApp.swift</c> scene/lifecycle setup.
/// </summary>
public partial class App : Application
{
    /// <summary>The app-wide service provider. Available after <see cref="OnStartup"/>.</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var collection = new ServiceCollection();
        ConfigureServices(collection);
        Services = collection.BuildServiceProvider();

        var settings = Services.GetRequiredService<ISettingsService>();
        ThemeService.Apply(settings.Appearance);
        settings.PropertyChanged += OnSettingsChanged;

        var window = Services.GetRequiredService<MainWindow>();
        window.Show();

        if (!settings.HasCompletedOnboarding)
            window.ShowOnboarding();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ISettingsService.Appearance) && sender is ISettingsService s)
            ThemeService.Apply(s.Appearance);
    }

    /// <summary>Registers services, view-models, and windows with the DI container.</summary>
    private static void ConfigureServices(IServiceCollection services)
    {
        // Environment & path layer (M2)
        services.AddSingleton<IAgentEnvironment>(_ => AgentEnvironment.Live);
        services.AddSingleton<AgentPaths>();
        services.AddSingleton<OpenClawConfig>();
        services.AddSingleton<PlatformSkillPaths>();

        // Scanners (M3/M4)
        services.AddSingleton<SkillScanner>();
        services.AddSingleton<TomlConfigReader>();
        services.AddSingleton<McpScanner>();
        services.AddSingleton<PluginScanner>();
        services.AddSingleton<PlatformSourceDetector>();
        services.AddSingleton<CatalogDiscoveryService>();

        // Editing, settings, shell (M5/M8/M9)
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<SkillFileService>();
        services.AddSingleton<EditorDocument>();
        services.AddSingleton<ShellService>();

        // Stores / view-models (M5/M6)
        services.AddSingleton<CatalogViewModel>();
        services.AddSingleton<MainViewModel>();

        // Windows
        services.AddSingleton<MainWindow>();
    }
}
