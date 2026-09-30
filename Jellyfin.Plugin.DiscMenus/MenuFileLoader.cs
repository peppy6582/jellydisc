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
