using System.Diagnostics;
using System.IO;

namespace SkillzWin.Services;

/// <summary>
/// OS shell integrations. Windows port of the macOS <c>NSWorkspace</c>/<c>NSPasteboard</c> helpers
/// ("Reveal in Finder" → "Show in File Explorer", etc.).
/// </summary>
public sealed class ShellService
{
    /// <summary>Open File Explorer with the item selected (file or folder).</summary>
    public void RevealInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch { /* ignore */ }
    }

    public void OpenInDefaultApp(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { /* ignore */ }
    }

    public void CopyPath(string path)
    {
        try { System.Windows.Clipboard.SetText(path); }
        catch { /* clipboard can transiently fail */ }
    }

    public void OpenInCursor(string path)
    {
        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var cursorExe = Path.Combine(local, "Programs", "cursor", "Cursor.exe");
            if (File.Exists(cursorExe))
            {
                Process.Start(new ProcessStartInfo(cursorExe, $"\"{path}\"") { UseShellExecute = false });
                return;
            }
        }
        catch { /* fall through */ }
        OpenInDefaultApp(path);
    }
}
