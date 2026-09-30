using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.DiscMenus.Model;
using MediaBrowser.Controller.Library;
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

    /// <summary>The local Special Feature item IDs a parent item's bound menu currently resolves to.</summary>
    [HttpGet("{parentItemId}/SpecialFeatures")]
    public ActionResult<IReadOnlyList<Guid>> GetSpecialFeatures([FromRoute] Guid parentItemId)
    {
        var items = _discMenuService.GetSpecialFeatures(parentItemId);
        return Ok(items.Select(i => i.Id).ToList());
    }

    /// <summary>
    /// Runs the duration auto-matcher for a parent item and persists the
    /// result. <paramref name="userId"/> scopes GetExtras to a specific user
    /// (e.g. the calling admin); omitted, falls back to the server's first user.
    /// </summary>
    [HttpPost("{parentItemId}/AutoMatch")]
    public ActionResult<AutoMatchResult> RunAutoMatch([FromRoute] Guid parentItemId, [FromQuery] Guid? userId = null)
    {
        var user = userId is { } id ? _userManager.GetUserById(id) : _userManager.GetFirstUser();
        if (user is null)
        {
            return NotFound("No Jellyfin user available to scope the match to.");
        }

        var result = _discMenuService.RunAutoMatch(parentItemId, user);
        return result is null ? NotFound($"No disc menu bound to item '{parentItemId}'.") : Ok(result);
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
