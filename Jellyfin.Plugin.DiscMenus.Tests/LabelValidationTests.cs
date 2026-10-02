using System.Text.Json.Nodes;
using Xunit;

namespace Jellyfin.Plugin.DiscMenus.Tests;

/// <summary>
/// Entry labels and menu titles are plain text of 1-80 characters (schema $defs/label). The plugin's loader
/// doesn't run JSON Schema, so it must enforce the rule itself or a menu that the schema rejects would load
/// (and the Menu Editor, which validates with the loader, would stay silent about it).
/// </summary>
public class LabelValidationTests
{
    private static string ExamplePath([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "examples", "example.menu.json"));

    private static void Load(Action<JsonObject> mutate)
    {
        var doc = JsonNode.Parse(File.ReadAllText(ExamplePath()))!.AsObject();
        mutate(doc);
        MenuFileLoader.ParseMenu(doc.ToJsonString(), "test");
    }

    private static void AssertRejected(Action<JsonObject> mutate, string fragment)
    {
        var ex = Assert.Throws<MenuValidationException>(() => Load(mutate));
        Assert.Contains(ex.Errors, e => e.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static JsonNode FirstEntry(JsonObject d) => d["menus"]!["main"]!["entries"]![0]!;

    [Theory]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("a<b")]
    [InlineData("a>b")]
    [InlineData("")]
    public void RejectsBadEntryLabels(string label) =>
        AssertRejected(d => FirstEntry(d)["label"] = label, "main[0] label");

    [Fact]
    public void RejectsOverLongEntryLabels() =>
        AssertRejected(d => FirstEntry(d)["label"] = new string('x', 81), "main[0] label");

    [Theory]
    [InlineData("<b>Main</b>")]
    [InlineData("")]
    public void RejectsBadMenuTitles(string title) =>
        AssertRejected(d => d["menus"]!["main"]!["title"] = title, "menu 'main' title");

    [Fact]
    public void RejectsOverLongMenuTitles() =>
        AssertRejected(d => d["menus"]!["features"]!["title"] = new string('t', 81), "menu 'features' title");

    [Fact]
    public void AcceptsLabelsAtTheLimitsAndInAnyScript()
    {
        Load(d => FirstEntry(d)["label"] = "x");
        Load(d => FirstEntry(d)["label"] = new string('x', 80));
        Load(d => FirstEntry(d)["label"] = "日本語のタイトル");
        Load(d => FirstEntry(d)["label"] = "Café & Crème \"Brûlée\" (2008) — 50%");
        Load(d => d["menus"]!["main"]!["title"] = new string('t', 80));
    }

    [Fact]
    public void CountsCharactersLikeTheSchemaDoesNotUtf16Units()
    {
        // 80 emoji is 80 characters (160 UTF-16 units): the schema accepts it, so the loader must too.
        Load(d => FirstEntry(d)["label"] = string.Concat(Enumerable.Repeat("😀", 80)));
        // 81 is one over, in either counting.
        AssertRejected(d => FirstEntry(d)["label"] = string.Concat(Enumerable.Repeat("😀", 81)), "main[0] label");
    }

    [Fact]
    public void FlowLabelsFollowTheSameRule()
    {
        AssertRejected(d => d["menus"]!["features"]!["layout"] = new JsonObject
        {
            ["flow"] = new JsonObject
            {
                ["region"] = new JsonObject { ["x"] = 50, ["y"] = 50, ["w"] = 50, ["h"] = 20 },
                ["columns"] = 4,
                ["rows"] = 1,
                ["moreLabel"] = "<More>",
            },
        }, "labels must be");
    }

    [Fact]
    public void EveryShippedExampleStillLoads()
    {
        var dir = Path.GetDirectoryName(ExamplePath())!;
        foreach (var file in Directory.GetFiles(dir, "*.menu.json"))
        {
            MenuFileLoader.LoadMenu(file);
        }
    }
}
