using Jellyfin.Plugin.DiscMenus.Model;

namespace Jellyfin.Plugin.DiscMenus.Api;

/// <summary>
/// A parent item's disc menu with every entry's extra key(s) already resolved
/// to local item IDs (or dropped, if unmatched) - everything the web menu
/// renderer needs in one response, without it having to separately fetch and
/// join the menu/binding data itself.
/// </summary>
public sealed class RenderableMenuDocument
{
    public required Guid MenuId { get; init; }

    public required string Root { get; init; }

    public BackgroundSpec? Background { get; init; }

    public ThemeSpec? Theme { get; init; }

    public required Dictionary<string, RenderableMenu> Menus { get; init; }
}

public sealed class RenderableMenu
{
    public required string Title { get; init; }

    public BackgroundSpec? Background { get; init; }

    public ThemeSpec? Theme { get; init; }

    public required List<RenderableEntry> Entries { get; init; }
}

/// <summary>
/// One menu entry. Only the fields relevant to <see cref="Action"/> are set -
/// mirrors how the entries are described in schema/menu.schema.json itself.
/// </summary>
public sealed class RenderableEntry
{
    public required string Action { get; init; }

    public required string Label { get; init; }

    /// <summary>playFeature only.</summary>
    public int? StartChapter { get; init; }

    /// <summary>playExtra only. Null if the extra has no matched local item.</summary>
    public Guid? ItemId { get; init; }

    /// <summary>playSequence only. Unmatched extras are dropped, not left as gaps.</summary>
    public List<Guid>? ItemIds { get; init; }

    /// <summary>submenu only: the key to look up in the parent document's Menus.</summary>
    public string? Menu { get; init; }

    /// <summary>chapters only.</summary>
    public int? PerPage { get; init; }
}
