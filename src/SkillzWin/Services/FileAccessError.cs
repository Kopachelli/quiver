using System.IO;

namespace SkillzWin.Services;

/// <summary>
/// Maps filesystem exceptions to user-facing messages. Windows port of macOS <c>FileAccessError</c>
/// (NSError domains → Win32 exception types; "Finder" → "File Explorer").
/// </summary>
public static class FileAccessError
{
    // ERROR_WRITE_PROTECT = 19 (0x13) surfaced in HResult as 0x80070013.
    private const int HResultWriteProtect = unchecked((int)0x80070013);

    public static string UserMessage(Exception error) => error switch
    {
        UnauthorizedAccessException =>
            $"{AppBrand.Name} doesn't have permission to access this file. Check folder permissions in File Explorer.",
        FileNotFoundException =>
            "The file no longer exists. Try refreshing the catalog.",
        DirectoryNotFoundException =>
            "The file no longer exists. Try refreshing the catalog.",
        IOException io when io.HResult == HResultWriteProtect =>
            $"This volume is read-only. {AppBrand.Name} can't save changes here.",
        _ => error.Message,
    };
}
