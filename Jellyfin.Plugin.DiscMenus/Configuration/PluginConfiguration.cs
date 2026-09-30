using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.DiscMenus.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Directory scanned for "*.menu.json" / "*.binding.json" pairs.
    /// Empty means "menus" under the plugin's data folder.
    /// </summary>
    public string MenusPath { get; set; } = string.Empty;
}
