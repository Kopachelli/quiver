using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkillzWin.Models;
using SkillzWin.Services;
using Wpf.Ui.Controls;

namespace SkillzWin.ViewModels;

/// <summary>
/// Shell view-model: owns the catalog store, builds the sidebar, switches the detail pane, and
/// hosts the top-bar commands. Mirrors macOS <c>MainWindowView</c> + the app command set.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly EditorDocument _document;
    private readonly SkillScanner _scanner;

    public CatalogViewModel Catalog { get; }

    public ObservableCollection<SidebarRowViewModel> SectionRows { get; } = new();
    public ObservableCollection<SidebarRowViewModel> PlatformRows { get; } = new();

    [ObservableProperty] private bool _isSidebarVisible = true;
    [ObservableProperty] private object? _currentDetail;

    public MainViewModel(CatalogViewModel catalog, ISettingsService settings, EditorDocument document, SkillScanner scanner)
    {
        Catalog = catalog;
        _settings = settings;
        _document = document;
        _scanner = scanner;

        BuildSidebar();

        Catalog.CatalogChanged += OnCatalogChanged;
        Catalog.PropertyChanged += OnCatalogPropertyChanged;
        _document.PropertyChanged += OnDocumentPropertyChanged;
        _settings.PropertyChanged += OnSettingsPropertyChanged;
    }

    // --- Header / state ---

    public string AppName => AppBrand.Name;
    public int TotalItemCount => Catalog.Snapshot.AllItems.Count;
    public bool ShowInspector => Catalog.ShowInspector;

    public string ListTitle
    {
        get
        {
            var parts = new List<string>();
            if (Catalog.SelectedSection != CatalogSection.All) parts.Add(Catalog.SelectedSection.DisplayName());
            if (Catalog.SelectedPlatformFilter is AgentPlatform p) parts.Add(p.DisplayName());
            return parts.Count == 0 ? "All Items" : string.Join(" · ", parts);
        }
    }

    public bool IsSkillSelected => Catalog.SelectedItem is SkillCatalogItem;
    public bool CanModifySelectedSkill => Catalog.CanModifySelectedSkill();
    public bool CanSaveCurrentSkill => IsSkillSelected && _document.FileUrl is not null && _document.IsDirty;

    public string? ErrorMessage =>
        Catalog.LastOperationError
        ?? (_document.SaveStatus.Kind == SaveStatusKind.Failed ? _document.SaveStatus.Error : null);

    public bool HasError => ErrorMessage is not null;

    // --- Commands ---

    [RelayCommand]
    private void Refresh() => Catalog.Refresh();

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarVisible = !IsSidebarVisible;

    [RelayCommand]
    private void ToggleInspector() => Catalog.ShowInspector = !Catalog.ShowInspector;

    [RelayCommand(CanExecute = nameof(CanSaveCurrentSkill))]
    private void Save() => _document.SaveImmediately();

    [RelayCommand] private void RevealPath(string? path) { if (!string.IsNullOrEmpty(path)) Catalog.RevealInExplorer(path); }
    [RelayCommand] private void OpenPath(string? path) { if (!string.IsNullOrEmpty(path)) Catalog.OpenInDefaultApp(path); }
    [RelayCommand] private void CopyPath(string? path) { if (!string.IsNullOrEmpty(path)) Catalog.CopyPath(path); }
    [RelayCommand] private void OpenInCursor(string? path) { if (!string.IsNullOrEmpty(path)) Catalog.OpenInCursor(path); }

    [RelayCommand] private void DismissError() => Catalog.ClearLastOperationError();

    // Context-menu helpers (operate on a specific catalog item)
    [RelayCommand] private void RevealItem(CatalogItem? item) { var p = PathFor(item); if (p is not null) Catalog.RevealInExplorer(p); }
    [RelayCommand] private void CopyItemPath(CatalogItem? item) { var p = PathFor(item); if (p is not null) Catalog.CopyPath(p); }
    [RelayCommand] private void OpenItemInCursor(CatalogItem? item) { if (item?.AsSkill is { } s) Catalog.OpenInCursor(s.SkillPath); }

    private static string? PathFor(CatalogItem? item) => item switch
    {
        SkillCatalogItem s => s.Skill.SkillPath,
        McpCatalogItem m => m.Mcp.ConfigFileUrl,
        PluginCatalogItem p => p.Plugin.InstallPath ?? p.Plugin.MetadataPath,
        _ => null,
    };

    // Dialog commands — implemented in M9.
    [RelayCommand] private void NewSkill() => RequestNewSkill?.Invoke();
    [RelayCommand] private void EditDetails() => RequestEditDetails?.Invoke();
    [RelayCommand(CanExecute = nameof(CanModifySelectedSkill))] private void RenameSkill() => RequestRenameSkill?.Invoke();
    [RelayCommand(CanExecute = nameof(CanModifySelectedSkill))] private void DeleteSkill() => RequestDeleteSkill?.Invoke();
    [RelayCommand(CanExecute = nameof(IsSkillSelected))] private void SyncSkill() => RequestSyncSkill?.Invoke();

    [RelayCommand] private void OpenSettings() => RequestSettings?.Invoke();

    /// <summary>Hooks the view wires to open dialogs / windows.</summary>
    public Action? RequestNewSkill;
    public Action? RequestEditDetails;
    public Action? RequestRenameSkill;
    public Action? RequestDeleteSkill;
    public Action? RequestSyncSkill;
    public Action? RequestSettings;

    // --- Wiring ---

    private void BuildSidebar()
    {
        foreach (var section in CatalogSectionInfo.All)
        {
            var s = section;
            SectionRows.Add(new SidebarRowViewModel(
                SectionIcon(s), s.DisplayName(),
                () => Catalog.CountForSection(s),
                () => Catalog.SelectedSection == s,
                () => Catalog.SelectedSection = s));
        }

        PlatformRows.Add(new SidebarRowViewModel(
            SymbolRegular.StackStar20, "All Platforms",
            () => Catalog.CountAllPlatforms(),
            () => Catalog.SelectedPlatformFilter is null,
            () => Catalog.SelectedPlatformFilter = null));

        foreach (var platform in AgentPlatformInfo.All)
        {
            var p = platform;
            PlatformRows.Add(new SidebarRowViewModel(
                PlatformIcon(p), p.DisplayName(),
                () => Catalog.CountForPlatform(p),
                () => Catalog.SelectedPlatformFilter == p,
                () => Catalog.SelectedPlatformFilter = p));
        }
    }

    private void OnCatalogChanged()
    {
        foreach (var row in SectionRows) row.Refresh();
        foreach (var row in PlatformRows) row.Refresh();
        OnPropertyChanged(nameof(TotalItemCount));
        OnPropertyChanged(nameof(ListTitle));
    }

    private void OnCatalogPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(CatalogViewModel.SelectedItem):
            case nameof(CatalogViewModel.SelectedItemId):
                RebuildDetail();
                break;
            case nameof(CatalogViewModel.ShowInspector):
                OnPropertyChanged(nameof(ShowInspector));
                break;
            case nameof(CatalogViewModel.LastOperationError):
                OnPropertyChanged(nameof(ErrorMessage));
                OnPropertyChanged(nameof(HasError));
                break;
        }
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorDocument.IsDirty) or nameof(EditorDocument.FileUrl))
        {
            OnPropertyChanged(nameof(CanSaveCurrentSkill));
            SaveCommand.NotifyCanExecuteChanged();
        }
        if (e.PropertyName is nameof(EditorDocument.SaveStatus))
        {
            OnPropertyChanged(nameof(ErrorMessage));
            OnPropertyChanged(nameof(HasError));
        }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ISettingsService.HideBuiltInCursorSkills) or nameof(ISettingsService.HideSystemCodexSkills))
            Catalog.Refresh();
    }

    private void RebuildDetail()
    {
        (CurrentDetail as IDisposable)?.Dispose();
        CurrentDetail = Catalog.SelectedItem switch
        {
            SkillCatalogItem s => new SkillDetailViewModel(s.Skill, _document, _scanner, Catalog, _settings),
            McpCatalogItem m => m.Mcp,
            PluginCatalogItem p => p.Plugin,
            _ => null,
        };

        OnPropertyChanged(nameof(IsSkillSelected));
        OnPropertyChanged(nameof(CanModifySelectedSkill));
        OnPropertyChanged(nameof(CanSaveCurrentSkill));
        SaveCommand.NotifyCanExecuteChanged();
        RenameSkillCommand.NotifyCanExecuteChanged();
        DeleteSkillCommand.NotifyCanExecuteChanged();
        SyncSkillCommand.NotifyCanExecuteChanged();
    }

    private static SymbolRegular SectionIcon(CatalogSection s) => s switch
    {
        CatalogSection.All => SymbolRegular.AppsListDetail24,
        CatalogSection.Skills => SymbolRegular.Sparkle24,
        CatalogSection.McpServers => SymbolRegular.Server16,
        CatalogSection.Plugins => SymbolRegular.PuzzleCube24,
        _ => SymbolRegular.AppsListDetail24,
    };

    private static SymbolRegular PlatformIcon(AgentPlatform p) => p switch
    {
        AgentPlatform.Cursor => SymbolRegular.Cursor24,
        AgentPlatform.ClaudeCode => SymbolRegular.Chat16,
        AgentPlatform.Codex => SymbolRegular.Window24,
        AgentPlatform.Hermes => SymbolRegular.Flash24,
        AgentPlatform.Pi => SymbolRegular.Laptop24,
        AgentPlatform.OpenClaw => SymbolRegular.Rss24,
        _ => SymbolRegular.AppsListDetail24,
    };
}
