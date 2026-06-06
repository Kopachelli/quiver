using System.IO;
using System.Linq;
using System.Text;
using SkillzWin.Models;

namespace SkillzWin.Services;

public enum SkillFileErrorKind
{
    Blocked,
    DuplicateName,
    Validation,
}

/// <summary>Raised by <see cref="SkillFileService"/> for blocked/duplicate/validation failures.</summary>
public sealed class SkillFileException : Exception
{
    public SkillFileErrorKind Kind { get; }
    public SkillFileException(SkillFileErrorKind kind, string message) : base(message) => Kind = kind;
}

/// <summary>
/// Create / rename / delete / metadata-edit of skill folders with capability gates and atomic,
/// UTF-8 no-BOM writes. Mirrors macOS <c>SkillFileService</c>. Adds Windows case-only-rename
/// handling and best-effort writability checks (R8).
/// </summary>
public sealed class SkillFileService
{
    private readonly PlatformSkillPaths _paths;

    public SkillFileService(PlatformSkillPaths paths) => _paths = paths;

    public bool CanModify(SkillItem skill)
    {
        if (skill.IsBuiltIn || !IsFolderBackedSkill(skill)) return false;
        var parent = Path.GetDirectoryName(skill.RootDirectory);
        return IsWritable(skill.RootDirectory) && parent is not null && IsWritable(parent);
    }

    public bool CanEditMetadata(SkillItem skill) => IsWritable(skill.SkillPath);

    public string ModificationBlockedReason(SkillItem skill)
    {
        if (skill.IsBuiltIn)
            return $"Built-in Cursor skills cannot be renamed or deleted from {AppBrand.Name}.";
        if (!IsFolderBackedSkill(skill))
            return $"This skill is stored as a single SKILL.md file, so only its metadata can be edited from {AppBrand.Name}.";
        return $"This skill folder is not writable by {AppBrand.Name}. Check file permissions or edit it in its install folder.";
    }

    public string MetadataBlockedReason(SkillItem skill)
        => CanEditMetadata(skill)
            ? string.Empty
            : $"This SKILL.md file is not writable by {AppBrand.Name}. Check file permissions or edit it in its install folder.";

    public string RenameSkill(SkillItem skill, string newFolderName)
    {
        if (!CanModify(skill))
            throw new SkillFileException(SkillFileErrorKind.Blocked, ModificationBlockedReason(skill));

        var validation = SkillNameValidator.Validate(newFolderName);
        if (!validation.IsValid)
            throw new SkillFileException(SkillFileErrorKind.Validation, validation.Error!);
        var validated = validation.Value!;

        var parent = Path.GetDirectoryName(skill.RootDirectory)!;
        var newRoot = Path.Combine(parent, validated);
        var oldRoot = skill.RootDirectory;

        var sameCaseInsensitive = PathEqual(newRoot, oldRoot);
        var caseOnlyRename = sameCaseInsensitive && !string.Equals(newRoot, oldRoot, StringComparison.Ordinal);

        if (!sameCaseInsensitive && Directory.Exists(newRoot))
            throw new SkillFileException(SkillFileErrorKind.DuplicateName,
                $"A skill named \"{validated}\" already exists in this location.");

        if (caseOnlyRename)
        {
            var tmp = oldRoot + ".__rename__" + Guid.NewGuid().ToString("N");
            Directory.Move(oldRoot, tmp);
            Directory.Move(tmp, newRoot);
        }
        else if (!string.Equals(newRoot, oldRoot, StringComparison.Ordinal))
        {
            Directory.Move(oldRoot, newRoot);
        }

        var newSkillPath = Path.Combine(newRoot, "SKILL.md");
        if (File.Exists(newSkillPath))
        {
            var content = File.ReadAllText(newSkillPath, Encoding.UTF8);
            var updated = FrontmatterWriter.Apply(content, new FrontmatterWriter.Update(Name: validated));
            AtomicFile.WriteAllText(newSkillPath, updated);
        }

        return newRoot;
    }

    public void DeleteSkill(SkillItem skill)
    {
        if (!CanModify(skill))
            throw new SkillFileException(SkillFileErrorKind.Blocked, ModificationBlockedReason(skill));
        Directory.Delete(skill.RootDirectory, recursive: true);
    }

    public void UpdateMetadata(SkillItem skill, string name, string description, string? version)
    {
        if (!CanEditMetadata(skill))
            throw new SkillFileException(SkillFileErrorKind.Blocked, MetadataBlockedReason(skill));

        var validation = SkillNameValidator.Validate(name);
        if (!validation.IsValid)
            throw new SkillFileException(SkillFileErrorKind.Validation, validation.Error!);

        var content = File.ReadAllText(skill.SkillPath, Encoding.UTF8);
        var updated = FrontmatterWriter.Apply(content, new FrontmatterWriter.Update(
            Name: validation.Value,
            Description: description.Trim(),
            Version: version?.Trim()));
        AtomicFile.WriteAllText(skill.SkillPath, updated);
    }

    public IReadOnlyList<string> CreateSkill(string name, string description, string body, IReadOnlyCollection<AgentPlatform> platforms)
    {
        if (platforms.Count == 0)
            throw new SkillFileException(SkillFileErrorKind.Validation, "Select at least one platform.");

        var validation = SkillNameValidator.Validate(name);
        if (!validation.IsValid)
            throw new SkillFileException(SkillFileErrorKind.Validation, validation.Error!);
        var validated = validation.Value!;

        var fileContent = FrontmatterWriter.Make(validated, description.Trim(), body);

        var createdPaths = new List<string>();
        var duplicatePlatforms = new List<string>();

        foreach (var platform in platforms.OrderBy(p => p.DisplayName(), StringComparer.Ordinal))
        {
            var skillsRoot = _paths.UserSkillsDirectory(platform);
            Directory.CreateDirectory(skillsRoot);
            var skillDir = Path.Combine(skillsRoot, validated);

            if (Directory.Exists(skillDir))
            {
                duplicatePlatforms.Add(platform.DisplayName());
                continue;
            }

            Directory.CreateDirectory(skillDir);
            var skillPath = Path.Combine(skillDir, "SKILL.md");
            AtomicFile.WriteAllText(skillPath, fileContent);
            createdPaths.Add(skillPath);
        }

        if (createdPaths.Count == 0)
        {
            var names = string.Join(", ", duplicatePlatforms);
            throw new SkillFileException(SkillFileErrorKind.DuplicateName,
                $"A skill named \"{validated}\" already exists on: {names}.");
        }

        return createdPaths;
    }

    /// <summary>True if a skill with this folder name already lives in the platform's user skills dir.</summary>
    public bool SkillExistsOnPlatform(string folderName, AgentPlatform platform)
    {
        var dir = Path.Combine(_paths.UserSkillsDirectory(platform), folderName);
        return Directory.Exists(dir);
    }

    /// <summary>
    /// Copies a skill's entire folder (SKILL.md + any reference files) into another tool's skills
    /// directory. Quiver's cross-tool sync — the macOS app has no equivalent. Returns the new SKILL.md path.
    /// </summary>
    public string CopySkillToPlatform(SkillItem skill, AgentPlatform target)
    {
        var folderName = Path.GetFileName(skill.RootDirectory);
        var skillsRoot = _paths.UserSkillsDirectory(target);
        var destRoot = Path.Combine(skillsRoot, folderName);

        if (Directory.Exists(destRoot))
            throw new SkillFileException(SkillFileErrorKind.DuplicateName,
                $"A skill named \"{folderName}\" already exists on {target.DisplayName()}.");

        Directory.CreateDirectory(skillsRoot);
        CopyDirectory(skill.RootDirectory, destRoot);
        return Path.Combine(destRoot, "SKILL.md");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
        foreach (var dir in Directory.GetDirectories(source))
        {
            var name = Path.GetFileName(dir);
            if (name == ".git") continue;   // never propagate VCS metadata
            CopyDirectory(dir, Path.Combine(destination, name));
        }
    }

    private static bool IsFolderBackedSkill(SkillItem skill)
    {
        var skillMd = Path.Combine(skill.RootDirectory, "SKILL.md");
        var folderName = Path.GetFileName(skill.RootDirectory);
        return PathEqual(skillMd, skill.SkillPath)
            && folderName != "skills"
            && !folderName.EndsWith("skills", StringComparison.Ordinal);
    }

    private static bool IsWritable(string path)
    {
        try
        {
            if (Directory.Exists(path)) return true;   // dirs ignore ReadOnly; actual op is try/caught
            if (File.Exists(path)) return (File.GetAttributes(path) & FileAttributes.ReadOnly) == 0;
            return false;
        }
        catch { return false; }
    }

    private static bool PathEqual(string a, string b)
        => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
