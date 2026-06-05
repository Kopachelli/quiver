using System.IO;
using System.Text;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SkillzWin.Models;

namespace SkillzWin.Services;

/// <summary>
/// The markdown editor buffer with a 1.2 s debounced autosave, dirty tracking, and pause/resume.
/// Mirrors macOS <c>EditorDocument</c>. Writes are atomic, UTF-8 no-BOM, <c>\n</c>-preserving.
/// </summary>
public sealed partial class EditorDocument : ObservableObject
{
    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private string? _fileUrl;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private SaveStatus _saveStatus = SaveStatus.Saved;

    private string _savedText = string.Empty;
    private bool _autosavePaused;
    private readonly DispatcherTimer _autosaveTimer;

    public EditorDocument()
    {
        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
        _autosaveTimer.Tick += (_, _) => { _autosaveTimer.Stop(); PerformAutosave(); };
    }

    public void Load(string url)
    {
        CancelAutosave();
        FileUrl = url;
        string content;
        try { content = File.ReadAllText(url, Encoding.UTF8); }
        catch { content = string.Empty; }
        Text = content;
        _savedText = content;
        IsDirty = false;
        SaveStatus = SaveStatus.Saved;
    }

    public void UpdateText(string newValue)
    {
        Text = newValue;
        IsDirty = Text != _savedText;
        if (!IsDirty)
        {
            SaveStatus = SaveStatus.Saved;
            return;
        }
        if (_autosavePaused) return;
        ScheduleAutosave();
    }

    public void PauseAutosave()
    {
        _autosavePaused = true;
        CancelAutosave();
    }

    public void ResumeAutosave()
    {
        _autosavePaused = false;
        if (IsDirty) ScheduleAutosave();
    }

    public void Save()
    {
        if (FileUrl is null) return;
        SaveStatus = SaveStatus.Saving;
        AtomicFile.WriteAllText(FileUrl, Text);
        _savedText = Text;
        IsDirty = false;
        SaveStatus = SaveStatus.Saved;
    }

    public bool SaveImmediately()
    {
        CancelAutosave();
        if (!IsDirty)
        {
            SaveStatus = SaveStatus.Saved;
            return true;
        }
        try { Save(); return true; }
        catch (Exception ex) { SaveStatus = SaveStatus.Failed(FileAccessError.UserMessage(ex)); return false; }
    }

    public void DiscardChanges()
    {
        CancelAutosave();
        Text = _savedText;
        IsDirty = false;
        SaveStatus = SaveStatus.Saved;
    }

    private void ScheduleAutosave()
    {
        CancelAutosave();
        SaveStatus = SaveStatus.Saving;
        _autosaveTimer.Start();
    }

    private void PerformAutosave()
    {
        if (_autosavePaused || !IsDirty) return;
        try { Save(); }
        catch (Exception ex) { SaveStatus = SaveStatus.Failed(FileAccessError.UserMessage(ex)); }
    }

    private void CancelAutosave() => _autosaveTimer.Stop();
}
