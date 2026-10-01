using System.Text.Json;
using Jellyfin.Plugin.DiscMenus.Model;

namespace Jellyfin.Plugin.DiscMenus;

/// <summary>
/// Thrown when a menu document is well-formed JSON matching the schema's
/// shape, but fails a cross-reference check the JSON Schema can't express.
/// </summary>
public sealed class MenuValidationException : Exception
{
    public MenuValidationException(IReadOnlyList<string> errors)
        : base("Menu document failed validation: " + string.Join("; ", errors))
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}

/// <summary>
/// Loads menu/binding JSON and runs the same cross-reference checks as
/// tools/validate.py's semantic(), since those can't be expressed in the
/// JSON Schema itself.
/// </summary>
public static class MenuFileLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static MenuDocument LoadMenu(string path)
    {
        var json = File.ReadAllText(path);
        var doc = JsonSerializer.Deserialize<MenuDocument>(json, Options)
            ?? throw new JsonException($"'{path}' deserialized to null.");

        var errors = Validate(doc);
        if (errors.Count > 0)
        {
            throw new MenuValidationException(errors);
        }

        return doc;
    }

    public static BindingDocument LoadBinding(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<BindingDocument>(json, Options)
            ?? throw new JsonException($"'{path}' deserialized to null.");
    }

    public static void SaveBinding(string path, BindingDocument binding)
        => File.WriteAllText(path, JsonSerializer.Serialize(binding, Options));

    private static readonly HashSet<string> ButtonStyles = new() { "text", "frame", "glow", "arrow" };

    private static readonly HashSet<string> Anchors = new()
    {
        "top-left", "top", "top-right", "left", "center", "right", "bottom-left", "bottom", "bottom-right",
    };

    // https URL (no whitespace, quotes, parens, angle brackets, backslashes) or a small
    // raster data: URI. Mirrors schema $defs/imageRef; enforced here too because the loader
    // does not run JSON Schema and these strings end up in the browser.
    private static readonly System.Text.RegularExpressions.Regex ImageRef = new(
        "^(https://[^\\s\"'()<>\\\\]+|data:image/(png|jpeg|webp);base64,[A-Za-z0-9+/=]+|asset:[A-Za-z0-9][A-Za-z0-9._-]{0,63}(/[A-Za-z0-9][A-Za-z0-9._-]{0,63}){0,3})$",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly HashSet<string> Fonts = new() { "sans", "serif", "condensed", "wide", "mono" };

    private static readonly System.Text.RegularExpressions.Regex HexColor = new(
        "^#[0-9a-fA-F]{6}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static void ValidateTheme(string where, ThemeSpec? t, List<string> errors)
    {
        if (t is null)
        {
            return;
        }

        foreach (var c in new[] { t.Accent, t.TextColor })
        {
            if (c is not null && !HexColor.IsMatch(c))
            {
                errors.Add($"{where} theme colour '{c}' must be #rrggbb");
            }
        }

        if (t.Font is not null && !Fonts.Contains(t.Font))
        {
            errors.Add($"{where} unknown font '{t.Font}'");
        }

        if (t.FontSize is < 1 or > 10 || (t.FontSize is double fs && double.IsNaN(fs)))
        {
            errors.Add($"{where} fontSize must be 1-10");
        }

        if (t.LetterSpacing is < 0 or > 1)
        {
            errors.Add($"{where} letterSpacing must be 0-1");
        }
    }

    private static void ValidateBackground(string where, BackgroundSpec? b, List<string> errors)
    {
        if (b is { Source: BackgroundSource.Image } && (b.Image is null || !ImageRef.IsMatch(b.Image)))
        {
            errors.Add($"{where} background image must be an https URL, small data: URI or asset: reference");
        }

        if (b?.Poster is not null && !ImageRef.IsMatch(b.Poster))
        {
            errors.Add($"{where} background poster must be an https URL, small data: URI or asset: reference");
        }

        if (b?.TrailerIndex is < 0 or > 9)
        {
            errors.Add($"{where} background trailerIndex must be 0-9");
        }
    }

    private static void CheckPosition(string where, PositionSpec? p, List<string> errors)
    {
        if (p is null)
        {
            return;
        }

        foreach (var v in new[] { p.X, p.Y, p.W ?? 0, p.H ?? 0 })
        {
            if (v < 0 || v > 100 || double.IsNaN(v))
            {
                errors.Add($"{where} position values must be within 0-100");
                break;
            }
        }

        if (p.Anchor is not null && !Anchors.Contains(p.Anchor))
        {
            errors.Add($"{where} unknown anchor '{p.Anchor}'");
        }
    }

    private static void CheckStyle(string where, string? style, List<string> errors)
    {
        if (style is not null && !ButtonStyles.Contains(style))
        {
            errors.Add($"{where} unknown button style '{style}'");
        }
    }

    private static readonly System.Text.RegularExpressions.Regex PlainLabel = new(
        "^[^<>]{1,80}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Checks one layout block (a menu's own, or the document-wide default).</summary>
    private static void ValidateLayout(string where, MenuLayout? layout, List<string> errors)
    {
        if (layout is null)
        {
            return;
        }

        CheckPosition($"{where} title", layout.TitlePosition, errors);
        CheckStyle(where, layout.ButtonStyle, errors);

        if (layout.Flow is { } flow)
        {
            var fwhere = $"{where} flow";
            CheckPosition(fwhere + " region", flow.Region, errors);
            if (flow.Region.W is null || flow.Region.H is null)
            {
                errors.Add($"{fwhere} region needs w and h");
            }

            if (flow.Columns is < 1 or > 8 || flow.Rows is < 1 or > 12)
            {
                errors.Add($"{fwhere} columns must be 1-8 and rows 1-12");
            }

            // Room for the page's own navigation (Back, Previous, More) plus at least one entry.
            if (flow.Columns * flow.Rows < 4)
            {
                errors.Add($"{fwhere} needs at least 4 cells (columns x rows) to fit paging buttons");
            }

            foreach (var l in new[] { flow.MoreLabel, flow.PreviousLabel })
            {
                if (l is not null && !PlainLabel.IsMatch(l))
                {
                    errors.Add($"{fwhere} labels must be 1-80 characters with no < or >");
                }
            }
        }

        for (var li = 0; li < (layout.Layers?.Count ?? 0); li++)
        {
            var layer = layout.Layers![li];
            var lwhere = $"{where} layer {li}";
            if (layer.Type is not ("panel" or "image"))
            {
                errors.Add($"{lwhere} unknown type '{layer.Type}'");
            }

            if (layer.Fit is not null && layer.Fit is not ("contain" or "fill" or "cover"))
            {
                errors.Add($"{lwhere} unknown fit '{layer.Fit}'");
            }

            if (layer.Type == "image" && (layer.Image is null || !ImageRef.IsMatch(layer.Image)))
            {
                errors.Add($"{lwhere} image must be an https URL, small data: URI or asset: reference");
            }

            foreach (var c in new[] { layer.Fill, layer.BorderColor })
            {
                if (c is not null && !HexColor.IsMatch(c))
                {
                    errors.Add($"{lwhere} colour '{c}' must be #rrggbb");
                }
            }

            if (layer.Opacity is < 0 or > 1 || layer.BorderWidth is < 0 or > 10 || layer.Radius is < 0 or > 50)
            {
                errors.Add($"{lwhere} opacity/borderWidth/radius out of range");
            }

            CheckPosition(lwhere, layer.Position, errors);
        }
    }

    private static void ValidatePresentation(string menuKey, MenuDef menu, MenuLayout? documentLayout, List<string> errors)
    {
        ValidateBackground(menuKey, menu.Background, errors);
        ValidateTheme(menuKey, menu.Theme, errors);
        ValidateLayout(menuKey, menu.Layout, errors);

        var positioned = menu.Entries.Count(e => e.Position is not null);
        var flowSpec = menu.Layout?.Flow ?? documentLayout?.Flow;
        var flows = flowSpec is not null;
        if (flowSpec is not null)
        {
            // Back and Home are pinned to every page; with Previous, More and one entry
            // that is pinned + 3 cells (never fewer than 4).
            var pinned = (menu.Entries.Any(e => e is BackEntry) ? 1 : 0) + (menu.Entries.Any(e => e is HomeEntry) ? 1 : 0);
            var needed = Math.Max(4, pinned + 3);
            if (flowSpec.Columns * flowSpec.Rows < needed)
            {
                errors.Add($"menu '{menuKey}' needs a flow grid of at least {needed} cells for its pinned Back/Home plus paging buttons");
            }
        }

        if (flows && positioned != 0)
        {
            errors.Add($"menu '{menuKey}' uses flow layout, so its entries must not have positions");
        }
        else if (positioned != 0 && positioned != menu.Entries.Count)
        {
            errors.Add($"menu '{menuKey}' mixes positioned and unpositioned entries (position all or none)");
        }

        for (var i = 0; i < menu.Entries.Count; i++)
        {
            var e = menu.Entries[i];
            var where = $"{menuKey}[{i}]";
            CheckPosition(where, e.Position, errors);
            CheckStyle(where, e.Style, errors);
            foreach (var img in new[] { e.Image, e.ImageFocus })
            {
                if (img is not null && !ImageRef.IsMatch(img))
                {
                    errors.Add($"{where} image must be an https URL, small data: URI or asset: reference");
                }
            }
        }
    }

    private static List<string> Validate(MenuDocument m)
    {
        var errors = new List<string>();

        if (!m.Menus.ContainsKey(m.Root))
        {
            errors.Add($"root '{m.Root}' not in menus");
        }

        foreach (var (menuKey, menu) in m.Menus)
        {
            for (var i = 0; i < menu.Entries.Count; i++)
            {
                switch (menu.Entries[i])
                {
                    case PlayExtraEntry e when !m.Extras.ContainsKey(e.Extra):
                        errors.Add($"{menuKey}[{i}] unknown extra {e.Extra}");
                        break;
                    case PlaySequenceEntry e:
                        errors.AddRange(
                            e.Extras.Where(x => !m.Extras.ContainsKey(x))
                                .Select(x => $"{menuKey}[{i}] unknown extra {x}"));
                        break;
                    case SubmenuEntry e when !m.Menus.ContainsKey(e.Menu):
                        errors.Add($"{menuKey}[{i}] unknown menu {e.Menu}");
                        break;
                }
            }
        }

        ValidateTheme("document", m.Theme, errors);
        ValidateBackground("document", m.Background, errors);
        ValidateLayout("document", m.Layout, errors);
        foreach (var (menuKey, menu) in m.Menus)
        {
            ValidatePresentation(menuKey, menu, m.Layout, errors);
        }

        var seen = new HashSet<string>();
        var stack = new Stack<string>();
        stack.Push(m.Root);
        while (stack.Count > 0)
        {
            var k = stack.Pop();
            if (!seen.Add(k) || !m.Menus.TryGetValue(k, out var menu))
            {
                continue;
            }

            foreach (var sub in menu.Entries.OfType<SubmenuEntry>())
            {
                stack.Push(sub.Menu);
            }
        }

        errors.AddRange(m.Menus.Keys.Where(k => !seen.Contains(k)).Select(k => $"menu '{k}' unreachable from root"));

        return errors;
    }
}
