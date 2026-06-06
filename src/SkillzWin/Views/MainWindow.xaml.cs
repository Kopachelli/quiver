using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SkillzWin.Services;
using SkillzWin.ViewModels;
using SkillzWin.Views.Dialogs;
using Wpf.Ui.Controls;

namespace SkillzWin.Views;

/// <summary>
/// The primary application window: WPF-UI <see cref="FluentWindow"/> hosting the 3-pane catalog
/// browser. Mirrors macOS <c>MainWindowView</c> + the app lifecycle (scan on load, refresh on
/// activate, stop watching on close).
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _vm;
    private bool _loaded;

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;

        _vm.RequestNewSkill = OpenNewSkill;
        _vm.RequestEditDetails = OpenSkillDetails;
        _vm.RequestRenameSkill = OpenRename;
        _vm.RequestDeleteSkill = OpenDelete;
        _vm.RequestSyncSkill = OpenSync;
        _vm.RequestSettings = OpenSettings;

        Loaded += OnLoaded;
        Activated += OnActivated;
        Closed += OnClosed;
    }

    private void OpenNewSkill()
    {
        var vm = new NewSkillViewModel(_vm.Catalog, App.Services.GetRequiredService<PlatformSkillPaths>());
        var dlg = new NewSkillDialog { Owner = this, DataContext = vm };
        vm.RequestClose = () => dlg.Close();
        dlg.ShowDialog();
    }

    private void OpenRename()
    {
        if (_vm.Catalog.SelectedItem?.AsSkill is not { } skill) return;
        var vm = new RenameSkillViewModel(skill, _vm.Catalog, App.Services.GetRequiredService<EditorDocument>());
        var dlg = new RenameSkillDialog { Owner = this, DataContext = vm };
        vm.RequestClose = () => dlg.Close();
        dlg.ShowDialog();
    }

    private void OpenSync()
    {
        if (_vm.Catalog.SelectedItem?.AsSkill is not { } skill) return;
        var vm = new SyncSkillViewModel(skill, App.Services.GetRequiredService<PlatformSkillPaths>(), _vm.Catalog);
        var dlg = new SyncSkillDialog { Owner = this, DataContext = vm };
        vm.RequestClose = () => dlg.Close();
        dlg.ShowDialog();
    }

    private void OpenSkillDetails()
    {
        if (_vm.Catalog.SelectedItem?.AsSkill is not { } skill) return;
        var vm = new SkillDetailsViewModel(skill, _vm.Catalog, App.Services.GetRequiredService<SkillFileService>());
        var dlg = new SkillDetailsDialog { Owner = this, DataContext = vm };
        vm.RequestClose = () => dlg.Close();
        dlg.ShowDialog();
    }

    private void OpenDelete()
    {
        if (_vm.Catalog.SelectedItem?.AsSkill is not { } skill) return;
        var result = System.Windows.MessageBox.Show(
            $"This permanently deletes the skill folder at:\n{skill.RootDirectory}",
            $"Delete \"{skill.DisplayName}\"?",
            System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Warning);
        if (result != System.Windows.MessageBoxResult.OK) return;

        var document = App.Services.GetRequiredService<EditorDocument>();
        document.PauseAutosave();
        try { _vm.Catalog.DeleteSelectedSkill(); }
        catch (Exception ex) { _vm.Catalog.LastOperationError = FileAccessError.UserMessage(ex); }
        finally { document.ResumeAutosave(); }
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _loaded = true;
        _vm.Catalog.RefreshOnBecomeActive();   // start watching + first scan
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        if (!_loaded) return;
        _vm.Catalog.Refresh(silent: true);
    }

    private void OnClosed(object? sender, EventArgs e) => _vm.Catalog.StopWatching();

    public void ShowOnboarding()
    {
        var vm = new OnboardingViewModel(App.Services.GetRequiredService<ISettingsService>(), _vm.Catalog);
        var dlg = new OnboardingWindow { Owner = this, DataContext = vm };
        vm.RequestClose = () => dlg.Close();
        dlg.ShowDialog();
    }

    private void OpenSettings()
    {
        var vm = new SettingsViewModel(App.Services.GetRequiredService<ISettingsService>(), _vm.Catalog);
        var dlg = new SettingsWindow { Owner = this, DataContext = vm };
        vm.RequestClose = () => dlg.Close();
        vm.RequestShowOnboarding = ShowOnboarding;
        dlg.ShowDialog();
    }
}
