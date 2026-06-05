using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkillzWin.Models;
using SkillzWin.Services;

namespace SkillzWin.ViewModels;

/// <summary>One platform row in the New Skill dialog (toggle + skills path + detection hint).</summary>
public sealed partial class PlatformToggle : ObservableObject
{
    public AgentPlatform Platform { get; }
    public string DisplayName => Platform.DisplayName();
    public string SkillsPath { get; }
    public bool IsDetected { get; }
    public string NotDetectedHint => IsDetected ? string.Empty : "Not detected on this PC";

    [ObservableProperty] private bool _isSelected;

    public PlatformToggle(AgentPlatform platform, string skillsPath, bool isDetected, bool isSelected)
    {
        Platform = platform;
        SkillsPath = skillsPath;
        IsDetected = isDetected;
        _isSelected = isSelected;
    }
}

/// <summary>New Skill dialog. Mirrors macOS <c>NewSkillSheet</c>.</summary>
public sealed partial class NewSkillViewModel : ObservableObject
{
    private readonly CatalogViewModel _catalog;

    public ObservableCollection<PlatformToggle> Platforms { get; } = new();

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _body = "# Skill Name\n\nDescribe when to use this skill.";
    [ObservableProperty] private string? _errorMessage;

    public bool Succeeded { get; private set; }
    public Action? RequestClose;

    public NewSkillViewModel(CatalogViewModel catalog, PlatformSkillPaths paths)
    {
        _catalog = catalog;
        var detected = catalog.DetectedPlatforms;
        var defaults = catalog.DefaultNewSkillPlatforms;

        foreach (var p in AgentPlatformInfo.All)
        {
            var toggle = new PlatformToggle(p, paths.UserSkillsDirectory(p), detected.Contains(p), defaults.Contains(p));
            toggle.PropertyChanged += OnToggleChanged;
            Platforms.Add(toggle);
        }
    }

    public bool CanCreate => !string.IsNullOrWhiteSpace(Name) && Platforms.Any(t => t.IsSelected);

    partial void OnNameChanged(string value) => CreateCommand.NotifyCanExecuteChanged();

    private void OnToggleChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlatformToggle.IsSelected)) CreateCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void Create()
    {
        try
        {
            var selected = Platforms.Where(t => t.IsSelected).Select(t => t.Platform).ToList();
            _catalog.CreateSkill(Name.Trim(), Description, Body, selected);
            Succeeded = true;
            RequestClose?.Invoke();
        }
        catch (SkillFileException ex) { ErrorMessage = ex.Message; }
        catch (Exception ex) { ErrorMessage = FileAccessError.UserMessage(ex); }
    }
}

/// <summary>Rename Skill dialog. Mirrors macOS <c>RenameSkillSheet</c>.</summary>
public sealed partial class RenameSkillViewModel : ObservableObject
{
    private readonly CatalogViewModel _catalog;
    private readonly EditorDocument _document;

    public SkillItem Skill { get; }
    public string PlatformName => Skill.Platform.DisplayName();
    public string Location => Path.GetDirectoryName(Skill.RootDirectory) ?? Skill.RootDirectory;

    [ObservableProperty] private string _folderName;
    [ObservableProperty] private string? _errorMessage;

    public bool Succeeded { get; private set; }
    public Action? RequestClose;

    public RenameSkillViewModel(SkillItem skill, CatalogViewModel catalog, EditorDocument document)
    {
        Skill = skill;
        _catalog = catalog;
        _document = document;
        _folderName = Path.GetFileName(skill.RootDirectory);
    }

    public bool CanRename => !string.IsNullOrWhiteSpace(FolderName);

    partial void OnFolderNameChanged(string value) => RenameCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanRename))]
    private void Rename()
    {
        _document.PauseAutosave();
        try
        {
            _catalog.RenameSelectedSkill(FolderName.Trim());
            Succeeded = true;
            RequestClose?.Invoke();
        }
        catch (SkillFileException ex) { ErrorMessage = ex.Message; }
        catch (Exception ex) { ErrorMessage = FileAccessError.UserMessage(ex); }
        finally { _document.ResumeAutosave(); }
    }
}

/// <summary>Skill Details (metadata) dialog. Mirrors macOS <c>SkillDetailsSheet</c>.</summary>
public sealed partial class SkillDetailsViewModel : ObservableObject
{
    private readonly CatalogViewModel _catalog;

    public SkillItem Skill { get; }
    public bool CanModify { get; }
    public string BlockedReason { get; }
    public string PlatformName => Skill.Platform.DisplayName();
    public string RootPath => Skill.RootDirectory;

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _description;
    [ObservableProperty] private string _version;
    [ObservableProperty] private string? _errorMessage;

    public bool Succeeded { get; private set; }
    public Action? RequestClose;

    public SkillDetailsViewModel(SkillItem skill, CatalogViewModel catalog, SkillFileService fileService)
    {
        Skill = skill;
        _catalog = catalog;
        CanModify = fileService.CanEditMetadata(skill);
        BlockedReason = CanModify ? string.Empty : fileService.MetadataBlockedReason(skill);

        _name = skill.Frontmatter.Name ?? Path.GetFileName(skill.RootDirectory);
        _description = skill.Description == "No description" ? string.Empty : skill.Description;
        _version = skill.Version ?? string.Empty;
    }

    [RelayCommand]
    private void Save()
    {
        try
        {
            _catalog.UpdateSelectedSkillMetadata(
                Name.Trim(), Description,
                string.IsNullOrWhiteSpace(Version) ? null : Version.Trim());
            Succeeded = true;
            RequestClose?.Invoke();
        }
        catch (SkillFileException ex) { ErrorMessage = ex.Message; }
        catch (Exception ex) { ErrorMessage = FileAccessError.UserMessage(ex); }
    }
}
