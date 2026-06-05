namespace SkillzWin.Services;

/// <summary>Result of validating a proposed skill folder name.</summary>
public readonly record struct SkillNameValidation(bool IsValid, string? Value, string? Error)
{
    public static SkillNameValidation Ok(string value) => new(true, value, null);
    public static SkillNameValidation Fail(string error) => new(false, null, error);
}

/// <summary>
/// Faithful port of macOS <c>SkillNameValidator</c> plus a Windows addition (R9): rejection of
/// Win32 device-reserved names (CON, NUL, COM1…) that NTFS cannot create.
/// </summary>
public static class SkillNameValidator
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static SkillNameValidation Validate(string name)
    {
        var trimmed = name.Trim();

        if (trimmed.Length == 0)
            return SkillNameValidation.Fail("Name cannot be empty.");

        if (trimmed is "." or "..")
            return SkillNameValidation.Fail("Name is not valid.");

        if (trimmed.StartsWith(".", StringComparison.Ordinal))
            return SkillNameValidation.Fail("Name cannot start with a dot.");

        foreach (var ch in trimmed)
        {
            var allowed = (ch >= 'a' && ch <= 'z')
                || (ch >= 'A' && ch <= 'Z')
                || (ch >= '0' && ch <= '9')
                || ch == '-' || ch == '_';
            if (!allowed)
                return SkillNameValidation.Fail("Use only letters, numbers, hyphens, and underscores.");
        }

        if (ReservedNames.Contains(trimmed))
            return SkillNameValidation.Fail("Name is reserved by Windows.");

        return SkillNameValidation.Ok(trimmed);
    }
}
