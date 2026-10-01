using Jellyfin.Plugin.DiscMenus.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.DiscMenus.Api;

/// <summary>
/// Player-facing API: unlike <see cref="DiscMenusController"/> (admin-only,
/// [Authorize(Policy = "RequiresElevation")]), this just requires any
/// authenticated Jellyfin user - any viewer watching a title should be able
/// to fetch its disc menu, not just admins.
/// </summary>
[ApiController]
[Authorize]
[Route("DiscMenus")]
public sealed class DiscMenusPlayerController : ControllerBase
{
    private readonly DiscMenuService _discMenuService;

    public DiscMenusPlayerController(DiscMenuService discMenuService)
    {
        _discMenuService = discMenuService;
    }

    /// <summary>
    /// A parent item's disc menu, with every playExtra/playSequence entry
    /// already resolved to local item IDs, ready for the web renderer to
    /// consume directly - no separate menu/binding join needed client-side.
    /// </summary>
    [HttpGet("{parentItemId}/Menu")]
    public ActionResult<RenderableMenuDocument> GetRenderableMenu([FromRoute] Guid parentItemId)
    {
        if (!_discMenuService.ScanBindings().TryGetValue(parentItemId, out var pair))
        {
            return NotFound($"No disc menu bound to item '{parentItemId}'.");
        }

        var (menu, binding, _) = pair;

        var menus = menu.Menus.ToDictionary(
            kv => kv.Key,
            kv => new RenderableMenu
            {
                Title = kv.Value.Title,
                Background = kv.Value.Background,
                Theme = kv.Value.Theme,
                Layout = kv.Value.Layout,
                Entries = kv.Value.Entries.Select(e => ToRenderableEntry(e, binding)).ToList(),
            });

        return Ok(new RenderableMenuDocument
        {
            MenuId = menu.MenuId,
            Root = menu.Root,
            Background = menu.Background,
            Theme = menu.Theme,
            Layout = menu.Layout,
            Trailers = UsesTrailer(menu) ? _discMenuService.GetTrailers(parentItemId).ToList() : null,
            Chapters = UsesChapters(menu) ? _discMenuService.GetChapters(parentItemId).ToList() : null,
            Menus = menus,
        });
    }

    private static bool UsesChapters(MenuDocument menu) =>
        menu.Menus.Values.SelectMany(m => m.Entries).Any(e => e is ChaptersEntry || e is PlayFeatureEntry { StartChapter: not null });

    private static bool UsesTrailer(MenuDocument menu) =>
        menu.Background?.Source == BackgroundSource.Trailer
        || menu.Menus.Values.Any(m => m.Background?.Source == BackgroundSource.Trailer);

    private static RenderableEntry ToRenderableEntry(MenuEntry entry, BindingDocument binding)
    {
        var result = ToActionEntry(entry, binding);
        result.Position = entry.Position;
        result.Style = entry.Style;
        result.Image = entry.Image;
        result.ImageFocus = entry.ImageFocus;
        return result;
    }

    private static RenderableEntry ToActionEntry(MenuEntry entry, BindingDocument binding) => entry switch
    {
        PlayFeatureEntry e => new RenderableEntry { Action = e.Action, Label = e.Label, StartChapter = e.StartChapter },
        PlayExtraEntry e => new RenderableEntry { Action = e.Action, Label = e.Label, ItemId = ResolvedItemId(binding, e.Extra) },
        PlaySequenceEntry e => new RenderableEntry
        {
            Action = e.Action,
            Label = e.Label,
            ItemIds = e.Extras.Select(k => ResolvedItemId(binding, k)).Where(id => id.HasValue).Select(id => id!.Value).ToList(),
        },
        SubmenuEntry e => new RenderableEntry { Action = e.Action, Label = e.Label, Menu = e.Menu },
        ChaptersEntry e => new RenderableEntry { Action = e.Action, Label = e.Label, PerPage = e.PerPage, Menu = e.Menu },
        BackEntry e => new RenderableEntry { Action = e.Action, Label = e.Label },
        HomeEntry e => new RenderableEntry { Action = e.Action, Label = e.Label },
        _ => throw new NotSupportedException($"Unhandled menu entry type '{entry.GetType()}'."),
    };

    private static Guid? ResolvedItemId(BindingDocument binding, string extraKey) =>
        binding.Bindings.TryGetValue(extraKey, out var b) && b.Status == BindingStatus.Matched ? b.ItemId : null;
}
