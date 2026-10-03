using Jellyfin.Plugin.DiscMenus.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.DiscMenus.Api;

/// <summary>
/// Backend of the live menu editor (a dashboard page). Admin-only. Menu text travels as the raw
/// request body rather than inside a JSON wrapper, so what the editor sends is exactly the file's
/// text and half-typed (invalid) JSON never trips the framework's own body parsing.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("DiscMenus/Editor")]
public sealed class DiscMenusEditorController : ControllerBase
{
    private readonly DiscMenuService _discMenuService;

    public DiscMenusEditorController(DiscMenuService discMenuService)
    {
        _discMenuService = discMenuService;
    }

    /// <summary>Every menu file with its version and where it stands in discovery.</summary>
    [HttpGet("Files")]
    public ActionResult GetFiles()
    {
        var statuses = _discMenuService.GetStatuses().ToDictionary(s => s.MenuFile.Replace('\\', '/'), StringComparer.Ordinal);
        var files = _discMenuService.CreateEditor().List().Select(f =>
        {
            statuses.TryGetValue(f.File, out var s);
            return new
            {
                f.File,
                f.Version,
                f.Size,
                f.ModifiedUtc,
                State = s?.State,
                s?.Title,
                s?.ItemName,
            };
        });
        return Ok(files);
    }

    /// <summary>One menu file's text and version.</summary>
    [HttpGet("File")]
    public ActionResult GetFile([FromQuery] string? name)
    {
        var read = _discMenuService.CreateEditor().Read(name);
        return read.Kind == EditorResultKind.Ok
            ? Ok(new { File = name, Json = read.Json, read.Version })
            : Failure(read.Kind, read.Error);
    }

    /// <summary>
    /// Saves the request body over an existing menu file. <paramref name="version"/> must be the
    /// version the editor read: if the file has changed since, nothing is written (409).
    /// </summary>
    [HttpPut("File")]
    public async Task<ActionResult> SaveFile([FromQuery] string? name, [FromQuery] string? version)
    {
        var json = await ReadBodyAsync();
        if (json is null)
        {
            return Failure(EditorResultKind.TooLarge, "This menu is too large to save here.");
        }

        var result = _discMenuService.CreateEditor().Save(name, json, version);
        if (result.Kind == EditorResultKind.Ok)
        {
            _discMenuService.NotifyFilesChanged();
            return Ok(new { Version = result.Version });
        }

        return result.Kind switch
        {
            EditorResultKind.Invalid => StatusCode(422, new { Errors = result.Errors }),
            EditorResultKind.Conflict => Conflict(new { Error = result.Error, CurrentVersion = result.Version }),
            _ => Failure(result.Kind, result.Error),
        };
    }

    /// <summary>
    /// What viewers would get for the menu text in the request body: validated by the real loader,
    /// then built by the same code as the player endpoint. If <paramref name="file"/> names a file
    /// that is bound to a title, the preview uses that title's real extras, chapters and trailers.
    /// </summary>
    [HttpPost("Preview")]
    public async Task<ActionResult> Preview([FromQuery] string? file)
    {
        var json = await ReadBodyAsync();
        if (json is null)
        {
            return StatusCode(422, new { Errors = new[] { new EditorError("This menu is too large to edit here.") } });
        }

        var errors = MenuFileEditor.Validate(json);
        if (errors.Count > 0)
        {
            return StatusCode(422, new { Errors = errors });
        }

        var menu = MenuFileLoader.ParseMenu(json, "preview");
        var (kind, fullPath, _) = _discMenuService.CreateEditor().Resolve(file);
        var bound = kind == EditorResultKind.Ok ? _discMenuService.FindBindingFor(fullPath!) : null;
        var parentId = bound?.ParentItemId ?? Guid.Empty;
        return Ok(new
        {
            Document = RenderableBuilder.Build(menu, bound?.Binding, parentId, _discMenuService),
            ParentItemId = bound is null ? (Guid?)null : parentId,
            Bound = bound is not null,
            ItemName = bound?.ItemName,
        });
    }

    /// <summary>Movies and series in the library matching a name, for choosing the title of a new menu.</summary>
    [HttpGet("Titles")]
    public ActionResult GetTitles([FromQuery] string? q) =>
        Ok(_discMenuService.SearchTitles(q).Select(t => new { t.Id, t.Name, t.Year, t.Type, t.HasIds }));

    /// <summary>
    /// Creates a menu for a library title: <c>kind=blank</c> (just its ids and a Play button) or <c>kind=draft</c> (a starter menu from its real
    /// extras and chapters; movies only). Never overwrites a file. Returns the new file's name and version.
    /// </summary>
    [HttpPost("New")]
    public ActionResult NewMenu([FromQuery] Guid item, [FromQuery] string? kind)
    {
        if (kind is not ("blank" or "draft"))
        {
            return BadRequest(new { Error = "kind must be blank or draft." });
        }

        var outcome = kind == "draft" ? _discMenuService.WriteDraft(item, force: true) : _discMenuService.WriteBlank(item);
        if (outcome.StatusCode != 200 || outcome.FileName is null)
        {
            return StatusCode(outcome.StatusCode, new { Error = outcome.Error });
        }

        var read = _discMenuService.CreateEditor().Read(outcome.FileName);
        return Ok(new { File = outcome.FileName, read.Version });
    }

    /// <summary>A copy of a menu with a fresh id and revision 1, beside the original. Never overwrites.</summary>
    [HttpPost("Duplicate")]
    public ActionResult Duplicate([FromQuery] string? name)
    {
        var result = _discMenuService.CreateEditor().Duplicate(name);
        if (result.Kind == EditorResultKind.Ok)
        {
            _discMenuService.NotifyFilesChanged();
            return Ok(new { result.File, result.Version });
        }

        return result.Kind == EditorResultKind.Invalid
            ? StatusCode(422, new { Errors = result.Errors })
            : Failure(result.Kind, result.Error);
    }

    /// <summary>Deletes a menu (a backup is kept). Menus installed from the catalogue are removed on the catalogue page instead.</summary>
    [HttpDelete("File")]
    public ActionResult DeleteFile([FromQuery] string? name)
    {
        var result = _discMenuService.CreateEditor().DeleteUserMenu(name);
        if (result.Kind == EditorResultKind.Ok)
        {
            _discMenuService.NotifyFilesChanged();
            return Ok(new { Deleted = true });
        }

        return Failure(result.Kind, result.Error);
    }

    /// <summary>The saved earlier versions of a menu, newest first.</summary>
    [HttpGet("Backups")]
    public ActionResult GetBackups([FromQuery] string? name) =>
        Ok(_discMenusBackups(name));

    /// <summary>Puts an earlier version back as a new revision (the current file is backed up first). <c>version</c> is the version the editor last read.</summary>
    [HttpPost("Restore")]
    public ActionResult Restore([FromQuery] string? name, [FromQuery] string? backup, [FromQuery] string? version)
    {
        var result = _discMenuService.CreateEditor().Restore(name, backup, version);
        if (result.Kind == EditorResultKind.Ok)
        {
            _discMenuService.NotifyFilesChanged();
            return Ok(new { Version = result.Version });
        }

        return result.Kind switch
        {
            EditorResultKind.Invalid => StatusCode(422, new { Errors = result.Errors }),
            EditorResultKind.Conflict => Conflict(new { Error = result.Error, CurrentVersion = result.Version }),
            _ => Failure(result.Kind, result.Error),
        };
    }

    private IEnumerable<object> _discMenusBackups(string? name) =>
        _discMenuService.CreateEditor().ListBackups(name).Select(b => new { b.Stamp, b.Size, b.Revision, b.ModifiedUtc });

    // The body as text, or null if it exceeds the editor's size limit.
    private async Task<string?> ReadBodyAsync()
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await Request.Body.ReadAsync(chunk)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MenuFileEditor.MaxBytes)
            {
                return null;
            }
        }

        return new System.Text.UTF8Encoding(false).GetString(buffer.ToArray());
    }

    private ActionResult Failure(EditorResultKind kind, string? error) => kind switch
    {
        EditorResultKind.NotFound => NotFound(new { Error = error }),
        EditorResultKind.TooLarge => StatusCode(413, new { Error = error }),
        EditorResultKind.Conflict => Conflict(new { Error = error }),
        _ => BadRequest(new { Error = error }),
    };
}
