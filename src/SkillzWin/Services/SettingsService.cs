using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SkillzWin.Models;

namespace SkillzWin.Services;

/// <summary>App settings, observable + persisted. Windows port of macOS <c>AppSettings</c>/<c>@AppStorage</c>.</summary>
public interface ISettingsService : INotifyPropertyChanged
{
    bool HideBuiltInCursorSkills { get; set; }
    bool HideSystemCodexSkills { get; set; }
    double EditorFontSize { get; set; }
    bool ShowInspector { get; set; }
    SkillzAppearance Appearance { get; set; }
    bool EnableAgentNotch { get; set; }
    string? AgentNotchDisplayUuid { get; set; }
    bool HasCompletedOnboarding { get; set; }
    bool ShowAgentCountInMenuBar { get; set; }
    bool AutoInstallAgentHooks { get; set; }
    string SettingsFilePath { get; }
}

/// <summary>
/// JSON-backed settings at <c>%APPDATA%\SkillzWin\settings.json</c> with debounced write-through,
/// reproducing <c>@AppStorage</c>'s mutate-then-persist behavior. Defaults exactly match macOS.
/// </summary>
public sealed partial class SettingsService : ObservableObject, ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly DispatcherTimer _saveTimer;
    private bool _loading;

    [ObservableProperty] private bool _hideBuiltInCursorSkills;
    [ObservableProperty] private bool _hideSystemCodexSkills;
    [ObservableProperty] private double _editorFontSize;
    [ObservableProperty] private bool _showInspector;
    [ObservableProperty] private SkillzAppearance _appearance;
    [ObservableProperty] private bool _enableAgentNotch;
    [ObservableProperty] private string? _agentNotchDisplayUuid;
    [ObservableProperty] private bool _hasCompletedOnboarding;
    [ObservableProperty] private bool _showAgentCountInMenuBar;
    [ObservableProperty] private bool _autoInstallAgentHooks;

    public string SettingsFilePath => _path;

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _path = Path.Combine(appData, "SkillzWin", "settings.json");
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); Persist(); };

        Load();
        PropertyChanged += (_, _) => { if (!_loading) ScheduleSave(); };
    }

    private void Load()
    {
        _loading = true;
        var model = ReadModel();
        HideBuiltInCursorSkills = model.HideBuiltInCursorSkills;
        HideSystemCodexSkills = model.HideSystemCodexSkills;
        EditorFontSize = model.EditorFontSize;
        ShowInspector = model.ShowInspector;
        Appearance = SkillzAppearanceInfo.FromRaw(model.Appearance);
        EnableAgentNotch = model.EnableAgentNotch;
        AgentNotchDisplayUuid = model.AgentNotchDisplayUuid;
        HasCompletedOnboarding = model.HasCompletedOnboarding;
        ShowAgentCountInMenuBar = model.ShowAgentCountInMenuBar;
        AutoInstallAgentHooks = model.AutoInstallAgentHooks;
        _loading = false;
    }

    private SettingsModel ReadModel()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<SettingsModel>(File.ReadAllText(_path)) ?? new SettingsModel();
        }
        catch { /* fall through to defaults */ }
        return new SettingsModel();
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void Persist()
    {
        try
        {
            var model = new SettingsModel
            {
                HideBuiltInCursorSkills = HideBuiltInCursorSkills,
                HideSystemCodexSkills = HideSystemCodexSkills,
                EditorFontSize = EditorFontSize,
                ShowInspector = ShowInspector,
                Appearance = Appearance.RawValue(),
                EnableAgentNotch = EnableAgentNotch,
                AgentNotchDisplayUuid = AgentNotchDisplayUuid,
                HasCompletedOnboarding = HasCompletedOnboarding,
                ShowAgentCountInMenuBar = ShowAgentCountInMenuBar,
                AutoInstallAgentHooks = AutoInstallAgentHooks,
            };
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_path, JsonSerializer.Serialize(model, JsonOptions));
        }
        catch { /* best-effort persistence */ }
    }
}
