using System.Text.Json;
using Jellyfin.Plugin.DiscMenus.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Jellyfin.Database.Implementations.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DiscMenus;

/// <summary>
/// Loads disc-menu definitions from disk and resolves their playExtra/
/// playSequence entries to this server's local Special Features via the
/// paired binding file.
/// </summary>
public sealed class DiscMenuService
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<DiscMenuService> _logger;

    private readonly IServiceProvider _services;

    public DiscMenuService(ILibraryManager libraryManager, ILogger<DiscMenuService> logger, IServiceProvider services)
    {
        _libraryManager = libraryManager;
        _logger = logger;
        _services = services;
    }

    /// <summary>Menu art lives in an "assets" folder beside the menu/binding files (never scanned as menus).</summary>
    public static string AssetsPath => Path.Combine(MenusPath, "assets");

    private static string MenusPath =>
        string.IsNullOrEmpty(Plugin.Instance?.Configuration.MenusPath)
            ? Path.Combine(Plugin.Instance!.DataFolderPath, "menus")
            : Plugin.Instance.Configuration.MenusPath;

    /// <summary>
    /// Finds every "*.menu.json" / "*.binding.json" pair under the configured
    /// menus directory, keyed by the binding's parentItemId.
    /// </summary>
    public IReadOnlyDictionary<Guid, (MenuDocument Menu, BindingDocument Binding, string BindingPath)> ScanBindings()
    {
        var result = new Dictionary<Guid, (MenuDocument, BindingDocument, string)>();
        if (!Directory.Exists(MenusPath))
        {
            return result;
        }

        foreach (var bindingPath in Directory.EnumerateFiles(MenusPath, "*.binding.json", SearchOption.AllDirectories))
        {
            var menuPath = bindingPath[..^".binding.json".Length] + ".menu.json";
            if (!File.Exists(menuPath))
            {
                _logger.LogWarning("No matching menu file for binding '{BindingPath}'", bindingPath);
                continue;
            }

            try
            {
                var binding = MenuFileLoader.LoadBinding(bindingPath);
                var menu = MenuFileLoader.LoadMenu(menuPath);
                if (binding.MenuId != menu.MenuId)
                {
                    _logger.LogWarning(
                        "Binding '{BindingPath}' menuId does not match '{MenuPath}'",
                        bindingPath,
                        menuPath);
                    continue;
                }

                result[binding.ParentItemId] = (menu, binding, bindingPath);
            }
            catch (Exception ex) when (ex is JsonException or MenuValidationException or IOException)
            {
                _logger.LogError(
                    ex,
                    "Failed to load disc menu pair '{MenuPath}' / '{BindingPath}'",
                    menuPath,
                    bindingPath);
            }
        }

        return result;
    }

    /// <summary>
    /// Runs the duration matcher against a parent item's local extras and
    /// writes any newly-matched (or newly-unmatched) bindings back to disk.
    /// Bindings already Manual or Ignored are left untouched. There is no
    /// linking UI yet; this only produces auto-match suggestions.
    /// </summary>
    /// <param name="user">
    /// BaseItem.GetExtras requires a user for permission-scoping. Pass the
    /// requesting admin (from a future API endpoint) or a designated
    /// system/admin account for unattended runs.
    /// </param>
    public AutoMatchResult? RunAutoMatch(Guid parentItemId, User user)
    {
        if (!ScanBindings().TryGetValue(parentItemId, out var pair))
        {
            return null;
        }

        var (menu, binding, bindingPath) = pair;
        if (_libraryManager.GetItemById(parentItemId) is not { } parentItem)
        {
            return null;
        }

        var candidates = WithDuration(parentItem.GetExtras(user))
            .Select(x => new LocalExtraCandidate(x.Item.Id, x.Type, x.DurationSec))
            .ToList();

        var pending = menu.Extras
            .Where(kv => !binding.Bindings.TryGetValue(kv.Key, out var existing)
                || existing.Status is not (BindingStatus.Matched or BindingStatus.Ignored))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        var suggestions = DurationMatcher.Match(pending, candidates);
        foreach (var (key, suggestion) in suggestions)
        {
            binding.Bindings[key] = suggestion;
        }

        binding.ResolvedAt = DateTimeOffset.UtcNow;
        MenuFileLoader.SaveBinding(bindingPath, binding);

        return new AutoMatchResult
        {
            Matched = suggestions.Values.Count(b => b.Status == BindingStatus.Matched),
            Unmatched = suggestions.Values.Count(b => b.Status == BindingStatus.Unmatched),
            Skipped = binding.Bindings.Count - suggestions.Count,
        };
    }

    /// <summary>
    /// Resolves every playExtra/playSequence entry reachable from a parent
    /// item's bound menu to a local BaseItem: the item's Special Features as
    /// defined by its disc menu.
    /// </summary>
    public IReadOnlyList<BaseItem> GetSpecialFeatures(Guid parentItemId)
    {
        if (!ScanBindings().TryGetValue(parentItemId, out var pair))
        {
            return Array.Empty<BaseItem>();
        }

        var (menu, binding, _) = pair;
        var extraKeys = menu.Menus.Values
            .SelectMany(m => m.Entries)
            .SelectMany(ExtraKeysOf)
            .Distinct();

        var items = new List<BaseItem>();
        foreach (var key in extraKeys)
        {
            if (!binding.Bindings.TryGetValue(key, out var b)
                || b.Status != BindingStatus.Matched
                || b.ItemId is not { } itemId)
            {
                continue;
            }

            var item = _libraryManager.GetItemById(itemId);
            if (item is not null)
            {
                items.Add(item);
            }
        }

        return items;
    }

    /// <summary>
    /// The feature's chapters, in order. Resolved lazily from the server's chapter manager so that
    /// if it were ever unavailable the menu still works, just without scene selection data.
    /// </summary>
    public IReadOnlyList<Api.RenderableChapter> GetChapters(Guid parentItemId)
    {
        var manager = _services.GetService(typeof(MediaBrowser.Controller.Chapters.IChapterManager))
            as MediaBrowser.Controller.Chapters.IChapterManager;
        if (manager is null)
        {
            _logger.LogWarning("Chapter manager unavailable; scene selection will have no chapters");
            return Array.Empty<Api.RenderableChapter>();
        }

        return manager.GetChapters(parentItemId)
            .Select((c, i) => new Api.RenderableChapter
            {
                Index = i,
                Name = c.Name,
                StartTicks = c.StartPositionTicks,
                HasImage = !string.IsNullOrEmpty(c.ImagePath),
                ImageStamp = string.IsNullOrEmpty(c.ImagePath) ? null : c.ImageDateModified.Ticks,
            })
            .ToList();
    }

    /// <summary>
    /// Trailers for a parent item, in the order a menu's trailerIndex counts them: local
    /// trailer files first, then the YouTube trailers Jellyfin has stored in its metadata.
    /// Non-YouTube remote trailers are skipped (they cannot be embedded safely).
    /// </summary>
    public IReadOnlyList<Api.RenderableTrailer> GetTrailers(Guid parentItemId)
    {
        var result = new List<Api.RenderableTrailer>();
        if (_libraryManager.GetItemById(parentItemId) is not { } parent)
        {
            return result;
        }

        // Filtered again in code: don't rely on the query's owner/extra-type semantics alone.
        var locals = _libraryManager.GetItemList(new InternalItemsQuery
        {
            OwnerIds = new[] { parentItemId },
            ExtraTypes = new[] { ExtraType.Trailer },
            IncludeExtras = true,
        });
        foreach (var item in locals.Where(i => i.ExtraType == ExtraType.Trailer && i.OwnerId == parentItemId))
        {
            result.Add(new Api.RenderableTrailer { Kind = "local", ItemId = item.Id, Name = item.Name });
        }

        foreach (var remote in parent.RemoteTrailers ?? Array.Empty<MediaUrl>())
        {
            if (YouTubeLink.TryGetVideoId(remote.Url) is { } videoId)
            {
                result.Add(new Api.RenderableTrailer { Kind = "youtube", VideoId = videoId, Name = remote.Name });
            }
        }

        return result;
    }

    /// <summary>
    /// Every local extra under a parent item that has a type and a runtime,
    /// regardless of whether it's already claimed by a binding - the pool an
    /// admin picks from when manually linking an unmatched key.
    /// </summary>
    public IReadOnlyList<(BaseItem Item, ExtraType Type, double DurationSec)> GetLocalExtrasWithDuration(Guid parentItemId, User user)
    {
        if (_libraryManager.GetItemById(parentItemId) is not { } parentItem)
        {
            return Array.Empty<(BaseItem, ExtraType, double)>();
        }

        return WithDuration(parentItem.GetExtras(user)).ToList();
    }

    /// <summary>Manually links an extra key to a specific local item, overriding any prior auto-match.</summary>
    public bool SetManualBinding(Guid parentItemId, string extraKey, Guid itemId) => TryUpdateBinding(
        parentItemId,
        extraKey,
        new ExtraBinding
        {
            Status = BindingStatus.Matched,
            ItemId = itemId,
            Method = BindingMethod.Manual,
            Confidence = 1.0,
        });

    /// <summary>Marks an extra key as intentionally unresolved; RunAutoMatch will never touch it again.</summary>
    public bool IgnoreBinding(Guid parentItemId, string extraKey) =>
        TryUpdateBinding(parentItemId, extraKey, new ExtraBinding { Status = BindingStatus.Ignored });

    /// <summary>Clears a manual link or an ignore flag back to Unmatched, so RunAutoMatch will consider it again.</summary>
    public bool ResetBinding(Guid parentItemId, string extraKey) =>
        TryUpdateBinding(parentItemId, extraKey, new ExtraBinding { Status = BindingStatus.Unmatched });

    private bool TryUpdateBinding(Guid parentItemId, string extraKey, ExtraBinding newBinding)
    {
        if (!ScanBindings().TryGetValue(parentItemId, out var pair))
        {
            return false;
        }

        var (menu, binding, bindingPath) = pair;
        if (!menu.Extras.ContainsKey(extraKey))
        {
            return false;
        }

        binding.Bindings[extraKey] = newBinding;
        binding.ResolvedAt = DateTimeOffset.UtcNow;
        MenuFileLoader.SaveBinding(bindingPath, binding);
        return true;
    }

    private static IEnumerable<(BaseItem Item, ExtraType Type, double DurationSec)> WithDuration(IEnumerable<BaseItem> items) =>
        items.Where(e => e.ExtraType is not null && e.RunTimeTicks is not null)
            .Select(e => (e, e.ExtraType!.Value, TimeSpan.FromTicks(e.RunTimeTicks!.Value).TotalSeconds));

    private static IEnumerable<string> ExtraKeysOf(MenuEntry entry) => entry switch
    {
        PlayExtraEntry e => new[] { e.Extra },
        PlaySequenceEntry e => e.Extras,
        _ => Array.Empty<string>(),
    };
}

public sealed class AutoMatchResult
{
    public required int Matched { get; init; }

    public required int Unmatched { get; init; }

    /// <summary>Bindings already Manual or Ignored, left untouched by this run.</summary>
    public required int Skipped { get; init; }
}
