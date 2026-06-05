using System.IO;

namespace SkillzWin.Services;

/// <summary>
/// Recursive directory/file enumeration that mirrors macOS <c>FileManager</c>
/// <c>.skipsHiddenFiles</c>: skip dot-prefixed and Hidden-attribute entries and do not descend
/// into them. When <c>allowSystem</c> is set, the <c>.system</c> directory is still traversed so
/// Codex's <c>hideSystemCodex</c> toggle can govern its skills (R5).
/// </summary>
public static class HiddenAwareWalk
{
    public static IEnumerable<string> Files(string root, bool allowSystem = false)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            string[] files, subdirs;
            try { files = Directory.GetFiles(dir); subdirs = Directory.GetDirectories(dir); }
            catch { continue; }

            foreach (var f in files)
                if (!IsHiddenFile(f)) yield return f;
            foreach (var sd in subdirs)
                if (ShouldDescend(sd, allowSystem)) stack.Push(sd);
        }
    }

    public static IEnumerable<string> Directories(string root, bool allowSystem = false)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            string[] subdirs;
            try { subdirs = Directory.GetDirectories(dir); }
            catch { continue; }

            foreach (var sd in subdirs)
            {
                if (!ShouldDescend(sd, allowSystem)) continue;
                yield return sd;
                stack.Push(sd);
            }
        }
    }

    private static bool ShouldDescend(string dir, bool allowSystem)
    {
        var name = Path.GetFileName(dir);
        if (allowSystem && name == ".system") return true;
        if (name.StartsWith(".", StringComparison.Ordinal)) return false;
        try { if ((File.GetAttributes(dir) & FileAttributes.Hidden) != 0) return false; }
        catch { /* ignore */ }
        return true;
    }

    private static bool IsHiddenFile(string file)
    {
        if (Path.GetFileName(file).StartsWith(".", StringComparison.Ordinal)) return true;
        try { return (File.GetAttributes(file) & FileAttributes.Hidden) != 0; }
        catch { return false; }
    }
}
