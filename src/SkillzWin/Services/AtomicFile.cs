using System.IO;
using System.Text;

namespace SkillzWin.Services;

/// <summary>
/// Atomic, UTF-8 (no BOM) text writes that preserve <c>\n</c> line endings verbatim — the Windows
/// equivalent of Swift's <c>write(to:atomically:encoding:.utf8)</c> (R7). Writes to a temp file in
/// the same directory, then moves it into place so readers never see a partial file.
/// </summary>
public static class AtomicFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void WriteAllText(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(tmp, content, Utf8NoBom);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp))
            {
                try { File.Delete(tmp); } catch { /* ignore */ }
            }
        }
    }
}
