using System.Text.Json.Serialization;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.DiscMenus.Model;

/// <summary>
/// Mirrors schema/menu.schema.json: the shareable menu definition.
/// </summary>
public sealed class MenuDocument
{
    public int SchemaVersion { get; init; }

    public Guid MenuId { get; init; }

    public int Revision { get; init; }

    public MenuMeta? Meta { get; init; }

    public required MatchInfo Match { get; init; }

    public BackgroundSpec? Background { get; init; }

    public ThemeSpec? Theme { get; init; }

    public Dictionary<string, ExtraSpec> Extras { get; init; } = new();

    public required string Root { get; init; }

    public required Dictionary<string, MenuDef> Menus { get; init; }
}

public sealed class MenuMeta
{
    public string? Author { get; init; }

    public DateTimeOffset? Created { get; init; }

    public DateTimeOffset? Updated { get; init; }

    public string? Language { get; init; }

    public string? Notes { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MatchItemType
{
    Movie,
    Series,
    Season,
}

public sealed class MatchInfo
{
    public MatchItemType ItemType { get; init; }

    public Dictionary<string, string> ProviderIds { get; init; } = new();

    public int? SeasonNumber { get; init; }

    public ReleaseInfo? Release { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReleaseFormat
{
    DVD,
    BluRay,
    UHD,
    Digital,
    Other,
}

public sealed class ReleaseInfo
{
    public string? Edition { get; init; }

    public ReleaseFormat? Format { get; init; }

    public string? Region { get; init; }

    public int? Year { get; init; }

    public string? Publisher { get; init; }

    public string? Upc { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BackgroundSource
{
    [JsonStringEnumMemberName("jellyfin")]
    Jellyfin,

    [JsonStringEnumMemberName("tmdb")]
    Tmdb,

    [JsonStringEnumMemberName("fanart")]
    Fanart,

    [JsonStringEnumMemberName("color")]
    Color,
}

/// <summary>
/// Image references only; the reference image is fetched/rendered locally,
/// never stored in the shared menu file.
/// </summary>
public sealed class BackgroundSpec
{
    public BackgroundSource Source { get; init; }

    /// <summary>
    /// Jellyfin's own ImageType enum, reused directly (README: "Verified
    /// against server source"). It isn't decorated with a string-enum
    /// converter in MediaBrowser.Model, so it must be applied here.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ImageType? ImageType { get; init; }

    public int? Index { get; init; }

    public string? TmdbFilePath { get; init; }

    public string? FanartId { get; init; }

    public string? Color { get; init; }

    public double? Dim { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ThemeAlign
{
    [JsonStringEnumMemberName("left")]
    Left,

    [JsonStringEnumMemberName("center")]
    Center,

    [JsonStringEnumMemberName("right")]
    Right,
}

public sealed class ThemeSpec
{
    public required string Id { get; init; }

    public string? Accent { get; init; }

    public ThemeAlign? Align { get; init; }
}

/// <summary>
/// An extra this release is expected to contain. Deliberately excludes file
/// names/paths; duration is the primary local-matching signal.
/// </summary>
public sealed class ExtraSpec
{
    /// <summary>
    /// Jellyfin's own ExtraType enum, reused directly (README: "Verified
    /// against server source"). See the JsonConverter note on
    /// <see cref="BackgroundSpec.ImageType"/>.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ExtraType Type { get; init; }

    public double DurationSec { get; init; }

    public double? ToleranceSec { get; init; }

    public int? Ordinal { get; init; }

    public bool? Optional { get; init; }
}

public sealed class MenuDef
{
    public required string Title { get; init; }

    public BackgroundSpec? Background { get; init; }

    public ThemeSpec? Theme { get; init; }

    public required List<MenuEntry> Entries { get; init; }
}
