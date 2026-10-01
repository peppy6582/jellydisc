using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.DiscMenus.Model;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.DiscMenus.Api;

/// <summary>
/// Admin-only management API for disc menus: inspect the Special Features a
/// bound menu currently resolves to, and trigger the duration auto-matcher.
/// This is NOT the (separate, still-unverified) endpoint Jellyfin clients use
/// to play back Special Features - see README "Still to verify on 12.x".
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")] // convention seen in other Jellyfin plugins; not independently verified for 12.x
[Route("DiscMenus")]
public sealed class DiscMenusController : ControllerBase
{
    private readonly DiscMenuService _discMenuService;
    private readonly IUserManager _userManager;

    public DiscMenusController(DiscMenuService discMenuService, IUserManager userManager)
    {
        _discMenuService = discMenuService;
        _userManager = userManager;
    }

    private User? ResolveUser(Guid? userId) =>
        userId is { } id ? _userManager.GetUserById(id) : _userManager.GetFirstUser();

    /// <summary>Every bound menu found under the configured menus directory, with match counts.</summary>
    /// <summary>
    /// Where every menu file stands in automatic discovery: bound to which title, still waiting for
    /// one, ambiguous, in conflict with another menu, or invalid.
    /// </summary>
    [HttpGet("Status")]
    public ActionResult<IReadOnlyList<MenuStatus>> GetStatus() => Ok(_discMenuService.GetStatuses());

    /// <summary>
    /// A starter menu for a movie, built from what the library has for it (ids, extras with types and
    /// durations, chapters). Preview only: nothing is written.
    /// </summary>
    [HttpGet("Draft/{itemId}")]
    public ActionResult GetDraft([FromRoute] Guid itemId)
    {
        var outcome = _discMenuService.BuildDraft(itemId);
        return outcome.Draft is null
            ? StatusCode(outcome.StatusCode, outcome.Error)
            : Content(outcome.Draft.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), "application/json");
    }

    /// <summary>
    /// Builds that draft and writes it into the menus folder, where automatic discovery binds it to
    /// the title. Never overwrites a file; refuses a title that already has a menu unless force=true.
    /// </summary>
    [HttpPost("Draft/{itemId}")]
    public ActionResult CreateDraft([FromRoute] Guid itemId, [FromQuery] bool force = false)
    {
        var outcome = _discMenuService.WriteDraft(itemId, force);
        return outcome.Draft is null
            ? StatusCode(outcome.StatusCode, outcome.Error)
            : Ok(new { File = outcome.FileName, Statuses = _discMenuService.Rescan() });
    }

    /// <summary>Re-reads the menus folder and the library right now, then returns the statuses.</summary>
    [HttpPost("Scan")]
    public ActionResult<IReadOnlyList<MenuStatus>> Scan() => Ok(_discMenuService.Rescan());

    [HttpGet]
    public ActionResult<IReadOnlyList<MenuBindingSummary>> ListBindings()
    {
        var summaries = _discMenuService.ScanBindings()
            .Select(kv =>
            {
                var (menu, binding, _) = kv.Value;
                return new MenuBindingSummary
                {
                    ParentItemId = kv.Key,
                    MenuId = menu.MenuId,
                    MenuTitle = menu.Menus.TryGetValue(menu.Root, out var rootMenu) ? rootMenu.Title : menu.Root,
                    Matched = binding.Bindings.Values.Count(b => b.Status == BindingStatus.Matched),
                    Unmatched = binding.Bindings.Values.Count(b => b.Status == BindingStatus.Unmatched),
                    Ignored = binding.Bindings.Values.Count(b => b.Status == BindingStatus.Ignored),
                };
            })
            .ToList();
        return Ok(summaries);
    }

    /// <summary>Every extra key in a parent item's bound menu, with its current binding status.</summary>
    [HttpGet("{parentItemId}")]
    public ActionResult<MenuBindingDetail> GetBindingDetail([FromRoute] Guid parentItemId)
    {
        if (!_discMenuService.ScanBindings().TryGetValue(parentItemId, out var pair))
        {
            return NotFound($"No disc menu bound to item '{parentItemId}'.");
        }

        var (menu, binding, _) = pair;
        var extras = menu.Extras.Select(kv =>
        {
            binding.Bindings.TryGetValue(kv.Key, out var b);
            return new ExtraBindingDetail
            {
                Key = kv.Key,
                Type = kv.Value.Type,
                DurationSec = kv.Value.DurationSec,
                Status = b?.Status ?? BindingStatus.Unmatched,
                ItemId = b?.ItemId,
                Method = b?.Method,
                Confidence = b?.Confidence,
            };
        }).ToList();

        return Ok(new MenuBindingDetail
        {
            ParentItemId = parentItemId,
            MenuId = menu.MenuId,
            MenuTitle = menu.Menus.TryGetValue(menu.Root, out var rootMenu) ? rootMenu.Title : menu.Root,
            Extras = extras,
        });
    }

    /// <summary>The local Special Feature item IDs a parent item's bound menu currently resolves to.</summary>
    [HttpGet("{parentItemId}/SpecialFeatures")]
    public ActionResult<IReadOnlyList<Guid>> GetSpecialFeatures([FromRoute] Guid parentItemId)
    {
        var items = _discMenuService.GetSpecialFeatures(parentItemId);
        return Ok(items.Select(i => i.Id).ToList());
    }

    /// <summary>
    /// Local extras under a parent item available to manually link, regardless
    /// of whether auto-match already claimed them.
    /// </summary>
    [HttpGet("{parentItemId}/Candidates")]
    public ActionResult<IReadOnlyList<LocalExtraOption>> GetCandidates([FromRoute] Guid parentItemId, [FromQuery] Guid? userId = null)
    {
        var user = ResolveUser(userId);
        if (user is null)
        {
            return NotFound("No Jellyfin user available to scope the lookup to.");
        }

        var options = _discMenuService.GetLocalExtrasWithDuration(parentItemId, user)
            .Select(x => new LocalExtraOption
            {
                ItemId = x.Item.Id,
                Name = x.Item.Name,
                Type = x.Type,
                DurationSec = x.DurationSec,
            })
            .ToList();
        return Ok(options);
    }

    /// <summary>
    /// Runs the duration auto-matcher for a parent item and persists the
    /// result. <paramref name="userId"/> scopes GetExtras to a specific user
    /// (e.g. the calling admin); omitted, falls back to the server's first user.
    /// </summary>
    [HttpPost("{parentItemId}/AutoMatch")]
    public ActionResult<AutoMatchResult> RunAutoMatch([FromRoute] Guid parentItemId, [FromQuery] Guid? userId = null)
    {
        var user = ResolveUser(userId);
        if (user is null)
        {
            return NotFound("No Jellyfin user available to scope the match to.");
        }

        var result = _discMenuService.RunAutoMatch(parentItemId, user);
        return result is null ? NotFound($"No disc menu bound to item '{parentItemId}'.") : Ok(result);
    }

    /// <summary>Manually links one extra key to a specific local item, overriding any prior auto-match.</summary>
    [HttpPost("{parentItemId}/Bindings/{extraKey}/Link")]
    public ActionResult Link([FromRoute] Guid parentItemId, [FromRoute] string extraKey, [FromQuery] Guid itemId) =>
        _discMenuService.SetManualBinding(parentItemId, extraKey, itemId)
            ? NoContent()
            : NotFound($"No extra '{extraKey}' on the menu bound to '{parentItemId}'.");

    /// <summary>Marks one extra key as intentionally unresolved; auto-match will never touch it again.</summary>
    [HttpPost("{parentItemId}/Bindings/{extraKey}/Ignore")]
    public ActionResult Ignore([FromRoute] Guid parentItemId, [FromRoute] string extraKey) =>
        _discMenuService.IgnoreBinding(parentItemId, extraKey)
            ? NoContent()
            : NotFound($"No extra '{extraKey}' on the menu bound to '{parentItemId}'.");

    /// <summary>Clears a manual link or ignore flag back to Unmatched, so auto-match will consider it again.</summary>
    [HttpPost("{parentItemId}/Bindings/{extraKey}/Reset")]
    public ActionResult Reset([FromRoute] Guid parentItemId, [FromRoute] string extraKey) =>
        _discMenuService.ResetBinding(parentItemId, extraKey)
            ? NoContent()
            : NotFound($"No extra '{extraKey}' on the menu bound to '{parentItemId}'.");

    /// <summary>
    /// Changes on every build, so the injected script URL changes with it and a
    /// browser can never keep running a stale cached renderer after an update.
    /// </summary>
    private static readonly string ScriptVersion =
        typeof(DiscMenusController).Assembly.ManifestModule.ModuleVersionId.ToString("N");

    /// <summary>
    /// Called by the "File Transformation" plugin (server-to-server, no Jellyfin
    /// session) to inject the web menu renderer's script tag into jellyfin-web's
    /// index.html. See FileTransformationIntegration.cs for the registration side.
    /// Body is a plain JSON object with a single "contents" field (the full file
    /// text); response body is the transformed text, read back as a raw string -
    /// see File Transformation's TransformationHelper.ApplyTransformation.
    /// </summary>
    [HttpPost("web/Transform")]
    [AllowAnonymous]
    public async Task<ContentResult> TransformIndexHtml()
    {
        using var reader = new StreamReader(Request.Body);
        var raw = await reader.ReadToEndAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(raw);
        var contents = doc.RootElement.TryGetProperty("contents", out var contentsProp) ? contentsProp.GetString() ?? string.Empty : string.Empty;

        const string BodyCloseTag = "</body>";
        var insertAt = contents.LastIndexOf(BodyCloseTag, StringComparison.OrdinalIgnoreCase);
        var result = insertAt < 0
            ? contents
            : contents[..insertAt] + $"<script src=\"/DiscMenus/web/discmenus.js?v={ScriptVersion}\"></script>\n" + contents[insertAt..];

        return Content(result, "text/html");
    }

    private static readonly Dictionary<string, string> AssetTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".webp"] = "image/webp",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".mp3"] = "audio/mpeg",
        [".ogg"] = "audio/ogg",
        [".opus"] = "audio/ogg",
        [".m4a"] = "audio/mp4",
        [".wav"] = "audio/wav",
    };

    /// <summary>
    /// Serves a menu's own art and audio (an "asset:" reference) from the assets folder.
    /// Anonymous because an img/CSS fetch cannot carry Jellyfin's auth header. Only raster
    /// image and audio files inside the assets folder are ever served; any path that escapes it, or
    /// isn't an existing image file, is a 404.
    /// </summary>
    [HttpGet("Assets/{**path}")]
    [AllowAnonymous]
    public ActionResult GetAsset([FromRoute] string path)
    {
        var root = Path.GetFullPath(DiscMenuService.AssetsPath) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, path));
        if (!full.StartsWith(root, StringComparison.Ordinal)
            || !AssetTypes.TryGetValue(Path.GetExtension(full), out var contentType)
            || !System.IO.File.Exists(full))
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "no-cache";
        // Range support: browsers need it to seek and loop audio/video.
        return PhysicalFile(full, contentType, new FileInfo(full).LastWriteTimeUtc, null, enableRangeProcessing: true);
    }

    /// <summary>The web menu renderer script injected into jellyfin-web by <see cref="TransformIndexHtml"/>.</summary>
    [HttpGet("web/discmenus.js")]
    [AllowAnonymous]
    public ContentResult GetRendererScript()
    {
        var assembly = GetType().Assembly;
        var resourceName = $"{assembly.GetName().Name}.Web.discmenus.js";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        Response.Headers.CacheControl = "no-cache";
        return Content(reader.ReadToEnd(), "application/javascript");
    }
}

public sealed class MenuBindingSummary
{
    public required Guid ParentItemId { get; init; }

    public required Guid MenuId { get; init; }

    public required string MenuTitle { get; init; }

    public required int Matched { get; init; }

    public required int Unmatched { get; init; }

    public required int Ignored { get; init; }
}

public sealed class MenuBindingDetail
{
    public required Guid ParentItemId { get; init; }

    public required Guid MenuId { get; init; }

    public required string MenuTitle { get; init; }

    public required IReadOnlyList<ExtraBindingDetail> Extras { get; init; }
}

public sealed class ExtraBindingDetail
{
    public required string Key { get; init; }

    public required ExtraType Type { get; init; }

    public required double DurationSec { get; init; }

    public required BindingStatus Status { get; init; }

    public Guid? ItemId { get; init; }

    public BindingMethod? Method { get; init; }

    public double? Confidence { get; init; }
}

public sealed class LocalExtraOption
{
    public required Guid ItemId { get; init; }

    public required string Name { get; init; }

    public required ExtraType Type { get; init; }

    public required double DurationSec { get; init; }
}
