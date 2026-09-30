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
