using System.Linq;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SkillzWin.Models;
using SkillzWin.Services;

namespace SkillzWin.ViewModels;

/// <summary>
/// The app-wide catalog store: discovery snapshot, filtering, selection, refresh (background),
/// source statuses, and skill CRUD. Mirrors macOS <c>CatalogStore</c>.
/// </summary>
public sealed partial class CatalogViewModel : ObservableObject
{
    private readonly CatalogDiscoveryService _discovery;
    private readonly PlatformSourceDetector _detector;
    private readonly ISettingsService _settings;
    private readonly SkillFileService _fileService;
    private readonly ShellService _shell;
    private readonly PlatformSkillPaths _paths;
    private readonly Dispatcher _dispatcher;

    private CatalogFileWatcher? _watcher;

    /// <summary>Raised after the snapshot or any filter changes — sidebar counts re-read on this.</summary>
    public event Action? CatalogChanged;

    [ObservableProperty] private CatalogSnapshot _snapshot = CatalogSnapshot.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private DateTime? _lastRefreshedAt;
    [ObservableProperty] private IReadOnlyList<PlatformSourceStatus> _sourceStatuses = Array.Empty<PlatformSourceStatus>();
    [ObservableProperty] private CatalogSection _selectedSection = CatalogSection.All;
    [ObservableProperty] private AgentPlatform? _selectedPlatformFilter;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string? _selectedItemId;
    [ObservableProperty] private bool _showInspector;
    [ObservableProperty] private string? _lastOperationError;

    public CatalogViewModel(
        CatalogDiscoveryService discovery,
        PlatformSourceDetector detector,
        ISettingsService settings,
        SkillFileService fileService,
        ShellService shell,
        PlatformSkillPaths paths)
    {
        _discovery = discovery;
        _detector = detector;
        _settings = settings;
        _fileService = fileService;
        _shell = shell;
        _paths = paths;
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        _showInspector = settings.ShowInspector;
        _sourceStatuses = _detector.Detect(CatalogSnapshot.Empty);
    }

    // --- Derived state ---

    public IReadOnlyList<CatalogItem> FilteredItems =>
        CatalogFilter.Sorted(CatalogFilter.Items(Snapshot, SelectedSection, SelectedPlatformFilter, SearchText));

    public CatalogItem? SelectedItem =>
        SelectedItemId is null ? null : Snapshot.AllItems.FirstOrDefault(i => i.Id == SelectedItemId);

    public bool HasAnyCatalogItems => Snapshot.AllItems.Count > 0;

    public IReadOnlyCollection<AgentPlatform> DetectedPlatforms => _detector.DetectedPlatforms(SourceStatuses);

    public IReadOnlyCollection<AgentPlatform> DefaultNewSkillPlatforms => _detector.DefaultNewSkillPlatforms(SourceStatuses);

    public int CountForSection(CatalogSection section)
        => CatalogFilter.Items(Snapshot, section, SelectedPlatformFilter, SearchText).Count;

    public int CountForPlatform(AgentPlatform platform)
        => CatalogFilter.Items(Snapshot, SelectedSection, platform, SearchText).Count;

    public int CountAllPlatforms()
        => CatalogFilter.Items(Snapshot, SelectedSection, null, SearchText).Count;

    public IReadOnlyList<AgentPlatform> RelatedPlatforms(string skillName, SkillItem item)
    {
        if (item.AlsoAvailableOn.Count > 0) return item.AlsoAvailableOn.ToList();
        return Snapshot.Skills
            .Where(s => s.DisplayName == skillName && s.Id != item.Id)
            .Select(s => s.Platform)
            .ToList();
    }

    // --- Refresh / reload ---

    public void Refresh(bool silent = false)
    {
        if (!silent) IsLoading = true;
        var hideBuiltIn = _settings.HideBuiltInCursorSkills;
        var hideSystem = _settings.HideSystemCodexSkills;
        var preserveId = SelectedItemId;

        Task.Run(() =>
        {
            var snap = _discovery.Discover(hideBuiltIn, hideSystem);
            var statuses = _detector.Detect(snap);
            _dispatcher.Invoke(() =>
            {
                Snapshot = snap;
                SourceStatuses = statuses;
                LastRefreshedAt = DateTime.Now;
                if (!silent) IsLoading = false;
                ApplySelection(preserveId);
            });
        });
    }

    /// <summary>Synchronous re-scan used after CRUD so the selection updates immediately.</summary>
    public void ReloadCatalog(string? preferredId)
    {
        Snapshot = _discovery.Discover(_settings.HideBuiltInCursorSkills, _settings.HideSystemCodexSkills);
        SourceStatuses = _detector.Detect(Snapshot);
        LastRefreshedAt = DateTime.Now;
        ApplySelection(preferredId);
    }

    public void RefreshOnBecomeActive()
    {
        StartWatching();
        Refresh(silent: true);
    }

    public void StartWatching()
    {
        var roots = _paths.WatchDirectories().Where(System.IO.Directory.Exists).ToList();
        _watcher?.Stop();
        _watcher = new CatalogFileWatcher(() => _dispatcher.Invoke(() => Refresh(silent: true)));
        _watcher.Start(roots);
    }

    public void StopWatching()
    {
        _watcher?.Stop();
        _watcher = null;
    }

    public void ClearLastOperationError() => LastOperationError = null;

    private void ApplySelection(string? preferredId)
    {
        if (preferredId is not null && Snapshot.AllItems.Any(i => i.Id == preferredId))
            SelectedItemId = preferredId;
        else
            SelectedItemId = FilteredItems.FirstOrDefault()?.Id;
    }

    // --- Shell helpers ---

    public void RevealInExplorer(string path) => _shell.RevealInExplorer(path);
    public void OpenInDefaultApp(string path) => _shell.OpenInDefaultApp(path);
    public void CopyPath(string path) => _shell.CopyPath(path);
    public void OpenInCursor(string path) => _shell.OpenInCursor(path);

    // --- Skill CRUD (delegates to SkillFileService, then reloads) ---

    public bool CanModifySelectedSkill()
        => SelectedItem?.AsSkill is { } skill && _fileService.CanModify(skill);

    public void RenameSelectedSkill(string newFolderName)
    {
        if (SelectedItem?.AsSkill is not { } skill) return;
        var newRoot = _fileService.RenameSkill(skill, newFolderName);
        var newPath = System.IO.Path.Combine(newRoot, "SKILL.md");
        ReloadCatalog(SkillItem.MakeId(skill.Platform, newPath));
        LastOperationError = null;
    }

    public void DeleteSelectedSkill()
    {
        if (SelectedItem?.AsSkill is not { } skill) return;
        _fileService.DeleteSkill(skill);
        ReloadCatalog(null);
        LastOperationError = null;
    }

    public void UpdateSelectedSkillMetadata(string name, string description, string? version)
    {
        if (SelectedItem?.AsSkill is not { } skill) return;
        _fileService.UpdateMetadata(skill, name, description, version);
        ReloadCatalog(skill.Id);
        LastOperationError = null;
    }

    public string CreateSkill(string name, string description, string body, IReadOnlyCollection<AgentPlatform> platforms)
    {
        var paths = _fileService.CreateSkill(name, description, body, platforms);
        var first = paths[0];
        SelectedSection = CatalogSection.Skills;
        ReloadCatalog(SkillItem.MakeId(PlatformSkillPaths.PlatformFor(first), first));
        LastOperationError = null;
        return first;
    }

    // --- Change notifications for derived members ---

    partial void OnSnapshotChanged(CatalogSnapshot value) => RaiseDerived();
    partial void OnSelectedSectionChanged(CatalogSection value) => RaiseDerived();
    partial void OnSelectedPlatformFilterChanged(AgentPlatform? value) => RaiseDerived();
    partial void OnSearchTextChanged(string value) => RaiseDerived();
    partial void OnSelectedItemIdChanged(string? value) => OnPropertyChanged(nameof(SelectedItem));
    partial void OnShowInspectorChanged(bool value) => _settings.ShowInspector = value;

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(FilteredItems));
        OnPropertyChanged(nameof(SelectedItem));
        OnPropertyChanged(nameof(HasAnyCatalogItems));
        OnPropertyChanged(nameof(DetectedPlatforms));
        OnPropertyChanged(nameof(DefaultNewSkillPlatforms));
        CatalogChanged?.Invoke();
    }
}
