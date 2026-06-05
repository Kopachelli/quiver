namespace SkillzWin.Models;

public enum SaveStatusKind
{
    Saved,
    Saving,
    Failed,
}

/// <summary>Editor save state. Mirrors macOS <c>SaveStatus</c>.</summary>
public sealed record SaveStatus(SaveStatusKind Kind, string? Error)
{
    public static readonly SaveStatus Saved = new(SaveStatusKind.Saved, null);
    public static readonly SaveStatus Saving = new(SaveStatusKind.Saving, null);
    public static SaveStatus Failed(string error) => new(SaveStatusKind.Failed, error);
}
