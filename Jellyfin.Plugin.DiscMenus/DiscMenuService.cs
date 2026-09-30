using System.Text.Json;
using Jellyfin.Plugin.DiscMenus.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
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

    public DiscMenuService(ILibraryManager libraryManager, ILogger<DiscMenuService> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    private static string MenusPath =>
        string.IsNullOrEmpty(Plugin.Instance?.Configuration.MenusPath)
            ? Path.Combine(Plugin.Instance!.DataFolderPath, "menus")
            : Plugin.Instance.Configuration.MenusPath;

    /// <summary>
    /// Finds every "*.menu.json" / "*.binding.json" pair under the configured
    /// menus directory, keyed by the binding's parentItemId.
    /// </summary>
    public IReadOnlyDictionary<Guid, (MenuDocument Menu, BindingDocument Binding)> ScanBindings()
    {
        var result = new Dictionary<Guid, (MenuDocument, BindingDocument)>();
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

                result[binding.ParentItemId] = (menu, binding);
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

        var (menu, binding) = pair;
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

    private static IEnumerable<string> ExtraKeysOf(MenuEntry entry) => entry switch
    {
        PlayExtraEntry e => new[] { e.Extra },
        PlaySequenceEntry e => e.Extras,
        _ => Array.Empty<string>(),
    };
}
