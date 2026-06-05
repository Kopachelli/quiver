using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkillzWin.Models;
using SkillzWin.Services;

namespace SkillzWin.ViewModels;

/// <summary>One markdown file in a skill's file tree (relative display name).</summary>
public sealed record SkillFileItem(string Url, string RelativeName, bool IsPrimary);

/// <summary>
/// The skill editor screen: header, markdown-file tree, editor document binding, and save-status.
/// Mirrors macOS <c>SkillDetailView</c> + <c>MarkdownEditorView</c>.
/// </summary>
public sealed partial class SkillDetailViewModel : ObservableObject, IDisposable
{
    private readonly CatalogViewModel _catalog;

    public SkillItem Skill { get; }
    public EditorDocument Document { get; }

    public ObservableCollection<SkillFileItem> Files { get; } = new();

    [ObservableProperty] private SkillFileItem? _selectedFile;

    public SkillDetailViewModel(SkillItem skill, EditorDocument document, SkillScanner scanner, CatalogViewModel catalog)
    {
        Skill = skill;
        Document = document;
        _catalog = catalog;

        foreach (var f in scanner.MarkdownFiles(skill.RootDirectory))
            Files.Add(new SkillFileItem(f.Url, MakeRelative(skill.RootDirectory, f.Url), f.IsPrimary));

        Document.PropertyChanged += OnDocumentChanged;
        SelectInitialFile();
    }

    public string DisplayName => Skill.DisplayName;
    public string Description => Skill.Description;
    public string RootDirectory => Skill.RootDirectory;
    public AgentPlatform Platform => Skill.Platform;
    public bool IsBuiltIn => Skill.IsBuiltIn;
    public bool HasMultipleFiles => Files.Count > 1;

    public string SaveStatusText => Document.SaveStatus.Kind switch
    {
        SaveStatusKind.Saving => "Saving…",
        SaveStatusKind.Failed => "Save failed",
        _ => Document.IsDirty ? "Unsaved" : "Saved",
    };

    public bool SaveFailed => Document.SaveStatus.Kind == SaveStatusKind.Failed;

    /// <summary>Editor text, routed through the document so dirty tracking + autosave fire.</summary>
    public string EditorText
    {
        get => Document.Text;
        set => Document.UpdateText(value);
    }

    [RelayCommand]
    private void RevealFolder() => _catalog.RevealInExplorer(Skill.RootDirectory);

    [RelayCommand]
    private void Save() => Document.SaveImmediately();

    /// <summary>Push editor text into the document (called by the view's editor on TextChanged).</summary>
    public void OnEditorTextChanged(string text) => Document.UpdateText(text);

    private void SelectInitialFile()
    {
        var primary = Files.FirstOrDefault(f => f.IsPrimary) ?? Files.FirstOrDefault();
        if (Document.FileUrl is not null && !Document.IsDirty &&
            Files.FirstOrDefault(f => PathEqual(f.Url, Document.FileUrl)) is { } current)
        {
            SelectedFile = current;
        }
        else
        {
            SelectedFile = primary;
            if (primary is not null) Document.Load(primary.Url);
        }
    }

    partial void OnSelectedFileChanged(SkillFileItem? value)
    {
        if (value is null) return;
        if (Document.FileUrl is not null && PathEqual(Document.FileUrl, value.Url)) return;

        if (Document.IsDirty && !Document.SaveImmediately())
            return;   // save failed → keep the document on the old file; view surfaces the error

        Document.Load(value.Url);
    }

    private void OnDocumentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorDocument.SaveStatus) or nameof(EditorDocument.IsDirty))
        {
            OnPropertyChanged(nameof(SaveStatusText));
            OnPropertyChanged(nameof(SaveFailed));
        }
        if (e.PropertyName is nameof(EditorDocument.Text))
            OnPropertyChanged(nameof(EditorText));
    }

    public void Dispose() => Document.PropertyChanged -= OnDocumentChanged;

    private static string MakeRelative(string root, string path)
    {
        try { return Path.GetRelativePath(root, path); }
        catch { return Path.GetFileName(path); }
    }

    private static bool PathEqual(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
