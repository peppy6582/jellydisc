using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.DiscMenus;

/// <summary>One problem found in a menu being edited. Line/column are set for JSON syntax errors.</summary>
public sealed record EditorError(string Message, int? Line = null, int? Column = null);

/// <summary>A menu file in the menus folder.</summary>
/// <param name="File">Path relative to the menus folder, with forward slashes.</param>
/// <param name="Version">Fingerprint of the file's current content, used to detect that it changed under an editor.</param>
public sealed record EditorFile(string File, string Version, long Size, DateTime ModifiedUtc);

public enum EditorResultKind
{
    Ok,
    BadName,
    NotFound,
    TooLarge,
    Invalid,
    Conflict,
}

public sealed record EditorReadResult(EditorResultKind Kind, string? Json = null, string? Version = null, string? Error = null);

public sealed record EditorSaveResult(
    EditorResultKind Kind,
    string? Version = null,
    IReadOnlyList<EditorError>? Errors = null,
    string? Error = null);

/// <summary>
/// The file side of the live menu editor: lists, reads and saves menu files, and checks what is
/// being saved. Takes its folders as parameters (rather than reading plugin settings) so it can be
/// tested against temporary directories. Everything an admin can type reaches the disk only through
/// <see cref="Resolve"/>, which confines names to "*.menu.json" files inside the menus folder.
/// </summary>
public sealed class MenuFileEditor
{
    /// <summary>Largest menu the editor will read, validate or write (real menus are a few KB).</summary>
    public const int MaxBytes = 512 * 1024;

    private const int KeepBackups = 25;
    private const string MenuSuffix = ".menu.json";

    private static readonly Regex Segment = new(@"^[\p{L}\p{N} ._()\-]+$", RegexOptions.Compiled);

    private readonly string _root;
    private readonly string _assets;
    private readonly string _backups;

    public MenuFileEditor(string menusRoot, string assetsDir, string backupsDir)
    {
        _root = Path.GetFullPath(menusRoot).TrimEnd(Path.DirectorySeparatorChar);
        _assets = Path.GetFullPath(assetsDir).TrimEnd(Path.DirectorySeparatorChar);
        _backups = Path.GetFullPath(backupsDir);
    }

    /// <summary>Short fingerprint of file content: changes whenever the bytes do.</summary>
    public static string VersionOf(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant();

    private bool UnderAssets(string fullPath) =>
        fullPath.StartsWith(_assets + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    /// <summary>Every menu file, newest information first-class: path, version, size and modified time.</summary>
    public IReadOnlyList<EditorFile> List()
    {
        if (!Directory.Exists(_root))
        {
            return Array.Empty<EditorFile>();
        }

        var files = new List<EditorFile>();
        foreach (var path in Directory.EnumerateFiles(_root, "*" + MenuSuffix, SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            var full = Path.GetFullPath(path);
            if (UnderAssets(full))
            {
                continue;
            }

            var info = new FileInfo(full);
            if (info.Length > MaxBytes)
            {
                continue;
            }

            files.Add(new EditorFile(
                Path.GetRelativePath(_root, full).Replace(Path.DirectorySeparatorChar, '/'),
                VersionOf(File.ReadAllBytes(full)),
                info.Length,
                info.LastWriteTimeUtc));
        }

        return files;
    }

    /// <summary>
    /// Turns a name the client sent into a path inside the menus folder, or says why not. Only
    /// "*.menu.json" files, only below the menus folder, never in the assets folder, no dot-segments,
    /// no absolute paths, and symlinks must not lead out of the folder.
    /// </summary>
    public (EditorResultKind Kind, string? FullPath, string? Error) Resolve(string? name)
    {
        const string Bad = "That isn't a valid menu file name.";
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || !name.EndsWith(MenuSuffix, StringComparison.Ordinal)
            || name.Length == MenuSuffix.Length)
        {
            return (EditorResultKind.BadName, null, Bad);
        }

        foreach (var segment in name.Split('/'))
        {
            if (segment.Length == 0 || segment.StartsWith('.') || !Segment.IsMatch(segment))
            {
                return (EditorResultKind.BadName, null, Bad);
            }
        }

        var full = Path.GetFullPath(Path.Combine(_root, name));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || UnderAssets(full))
        {
            return (EditorResultKind.BadName, null, Bad);
        }

        // A symlink inside the folder must not be a way out of it.
        try
        {
            var info = new FileInfo(full);
            if (info.Exists && info.ResolveLinkTarget(returnFinalTarget: true) is { } target)
            {
                var targetFull = Path.GetFullPath(target.FullName);
                if (!targetFull.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || UnderAssets(targetFull))
                {
                    return (EditorResultKind.BadName, null, Bad);
                }
            }
        }
        catch (IOException)
        {
            return (EditorResultKind.BadName, null, Bad);
        }

        return (EditorResultKind.Ok, full, null);
    }

    public EditorReadResult Read(string? name)
    {
        var (kind, full, error) = Resolve(name);
        if (kind != EditorResultKind.Ok)
        {
            return new EditorReadResult(kind, Error: error);
        }

        var info = new FileInfo(full!);
        if (!info.Exists)
        {
            return new EditorReadResult(EditorResultKind.NotFound, Error: "No such menu file.");
        }

        if (info.Length > MaxBytes)
        {
            return new EditorReadResult(EditorResultKind.TooLarge, Error: "This file is too large to edit here.");
        }

        var bytes = File.ReadAllBytes(full!);
        return new EditorReadResult(EditorResultKind.Ok, Decode(bytes), VersionOf(bytes));
    }

    private static string Decode(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Everything wrong with a menu's JSON, as the plugin's real loader would see it, plus rules for
    /// editing an existing file: its menuId can't change (bindings are tied to it) and its revision
    /// can't go down. Empty means it would load.
    /// </summary>
    public static IReadOnlyList<EditorError> Validate(string json, string? originalJson = null)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaxBytes)
        {
            return new[] { new EditorError("This menu is too large to edit here.") };
        }

        var errors = new List<EditorError>();
        try
        {
            MenuFileLoader.ParseMenu(json, "menu");
        }
        catch (JsonException ex)
        {
            var text = ex.Message;
            var at = text.IndexOf(" Path:", StringComparison.Ordinal);
            if (at > 0)
            {
                text = text[..at].Trim();
            }

            errors.Add(new EditorError(
                text + (string.IsNullOrEmpty(ex.Path) || ex.Path == "$" ? string.Empty : $" (at {ex.Path})"),
                ex.LineNumber is { } line ? (int)line + 1 : null,
                ex.BytePositionInLine is { } col ? (int)col + 1 : null));
        }
        catch (MenuValidationException ex)
        {
            errors.AddRange(ex.Errors.Select(e => new EditorError(e)));
        }

        if (errors.Count == 0 && originalJson is not null)
        {
            try
            {
                using var before = JsonDocument.Parse(originalJson);
                using var after = JsonDocument.Parse(json);
                var oldId = before.RootElement.TryGetProperty("menuId", out var oi) ? oi.GetString() : null;
                var newId = after.RootElement.TryGetProperty("menuId", out var ni) ? ni.GetString() : null;
                if (oldId is not null && !string.Equals(oldId, newId, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(new EditorError("menuId can't be changed here: this server's bindings are tied to it."));
                }

                if (before.RootElement.TryGetProperty("revision", out var oldRev) && oldRev.TryGetInt32(out var oldRevision)
                    && after.RootElement.TryGetProperty("revision", out var newRev) && newRev.TryGetInt32(out var newRevision)
                    && newRevision < oldRevision)
                {
                    errors.Add(new EditorError($"revision can't go down (it was {oldRevision})."));
                }
            }
            catch (JsonException)
            {
                // The original on disk wasn't parseable: nothing to protect, let the save fix it.
            }
        }

        return errors;
    }

    /// <summary>
    /// Saves an edited menu over an existing file. Refuses (without touching the file) if the JSON
    /// would not load, if the file changed since <paramref name="expectedVersion"/> was read, or if it
    /// is too large. The previous content is kept as a timestamped backup, and the new content is
    /// written to a temporary file and moved into place so a crash can't leave half a menu.
    /// </summary>
    public EditorSaveResult Save(string? name, string json, string? expectedVersion)
    {
        var (kind, full, error) = Resolve(name);
        if (kind != EditorResultKind.Ok)
        {
            return new EditorSaveResult(kind, Error: error);
        }

        var info = new FileInfo(full!);
        if (!info.Exists)
        {
            return new EditorSaveResult(EditorResultKind.NotFound, Error: "No such menu file.");
        }

        if (Encoding.UTF8.GetByteCount(json) > MaxBytes)
        {
            return new EditorSaveResult(EditorResultKind.TooLarge, Error: "This menu is too large to save here.");
        }

        var currentBytes = File.ReadAllBytes(full!);
        var currentVersion = VersionOf(currentBytes);
        if (string.IsNullOrEmpty(expectedVersion) || !string.Equals(expectedVersion, currentVersion, StringComparison.Ordinal))
        {
            return new EditorSaveResult(
                EditorResultKind.Conflict,
                currentVersion,
                Error: "This file changed on the server since you opened it.");
        }

        var errors = Validate(json, Decode(currentBytes));
        if (errors.Count > 0)
        {
            return new EditorSaveResult(EditorResultKind.Invalid, Errors: errors);
        }

        var newBytes = new UTF8Encoding(false).GetBytes(json);
        if (newBytes.AsSpan().SequenceEqual(currentBytes))
        {
            return new EditorSaveResult(EditorResultKind.Ok, currentVersion); // nothing to do
        }

        Backup(name!, currentBytes);
        var temp = full + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(temp, newBytes);
            File.Move(temp, full!, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }

        return new EditorSaveResult(EditorResultKind.Ok, VersionOf(newBytes));
    }

    private void Backup(string name, byte[] content)
    {
        Directory.CreateDirectory(_backups);
        var stem = name.Replace('/', '~'); // '~' can't appear in a name, so stems never collide
        File.WriteAllBytes(Path.Combine(_backups, $"{stem}.{DateTime.UtcNow:yyyyMMddHHmmssfff}.bak"), content);

        // Keep the newest few per file; the timestamp in the name sorts chronologically.
        foreach (var old in Directory.EnumerateFiles(_backups, stem + ".*.bak")
                     .Where(p => Regex.IsMatch(Path.GetFileName(p), "^" + Regex.Escape(stem) + @"\.\d{17}\.bak$"))
                     .OrderByDescending(p => p, StringComparer.Ordinal)
                     .Skip(KeepBackups))
        {
            File.Delete(old);
        }
    }
}
