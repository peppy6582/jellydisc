# Disc Menus for Jellyfin

A Jellyfin plugin that recreates **DVD and Blu-ray disc menus**: shareable JSON menus with backgrounds,
music, transitions and scene selection, linked automatically to the movies in your library.

> **Status: early alpha.** It works end to end on Jellyfin 12.x in the web client, but it isn't packaged for
> Jellyfin's plugin catalogue yet, so for now you build it yourself (it takes a few minutes). It's built in
> the open and **[help is very welcome](CONTRIBUTING.md)**.

When you rip or buy a disc, you lose the menu: the "Play Movie / Scene Selection / Special Features" screen
that made it feel like a *release*. This plugin brings that back. A menu is a small JSON file that describes
the look and the structure; the plugin matches it to the right title in your library by its TMDB / IMDB id
and links its extras for you.

## What a menu can do

- **Look like a disc menu:** place buttons anywhere (as percentages of the screen), draw banner and panel
  layers, style the text, and use a paging grid that adds a **More** button when a submenu is too long.
- **Backgrounds:** a Jellyfin image, a TMDB backdrop, your own picture, a flat colour, or the title's own
  trailer looping behind the menu, and a different one for each page.
- **Sound and motion:** looping background music (or the title's Jellyfin theme song), button sounds, and
  fade / slide / zoom / wipe transitions between pages.
- **Scene selection** generated from the movie's chapters, with thumbnails when Jellyfin has them.
- **One-click playback** of the movie, any extra, a "Play All" sequence, or a chapter, and the menu comes
  back when playback ends. It works with a mouse, the keyboard, a TV remote and a gamepad.
- **Shareable:** a menu is layout and ids only: no file names, paths or server details, and never code, so
  menus can be exchanged safely. Your server's own links to its files are kept separately and never shared.

## Requirements

- **Jellyfin 12.x** server.
- The [**File Transformation**](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation)
  plugin, which lets this plugin add its menu to the Jellyfin web client. Without it everything except the
  on-screen **Disc Menu** button still works.
- The menus are shown in the **Jellyfin web client** for now. Other clients can't render them yet (see the
  [roadmap](docs/ROADMAP.md)).

## Getting started

1. **Install:** build the plugin and copy it into your Jellyfin plugins folder, as described in the
   [development guide](docs/DEVELOPMENT.md#trying-it-on-a-jellyfin-server), then restart Jellyfin. (A proper
   release and plugin repository are on the roadmap.)
2. **Choose a folder for your menus:** the plugin uses a `menus` folder in its own data folder by default, or
   set the **Menus directory** on the plugin's page in the Jellyfin dashboard (**Disc Menus** in the sidebar).
3. **Add a menu:** copy a `.menu.json` into that folder. Try one from [`examples/`](examples/README.md), or
   change its `match` ids to a title you own. The plugin finds the title in your library and links the
   extras by type and duration. The **Menu Files** table on the dashboard page shows how it went.
4. **Watch it:** open that title in the web client and click **Disc Menu**.

To start from your own library instead, have the plugin draft a menu for a movie (a starter menu built from
its real extras and chapters, see [Authoring menus](docs/AUTHORING.md#drafting-a-menu-from-your-library)),
then refine it in the **Menu Editor** on the dashboard, which shows a live preview as you edit.

## Documentation

- **[Authoring menus](docs/AUTHORING.md):** everything a menu can contain, how discovery works, the editor.
- **[Example menus](examples/README.md):** static, solid colour, trailer, local art, and a full-featured one.
- **[Development guide](docs/DEVELOPMENT.md):** building, testing, how the code fits together.
- **[Jellyfin 12 notes](docs/JELLYFIN_NOTES.md):** verified facts about writing a plugin for Jellyfin 12.
- **[Roadmap](docs/ROADMAP.md):** what's done and what's open.

## Contributing

Menus, bug reports, docs and code are all welcome. Start with **[CONTRIBUTING.md](CONTRIBUTING.md)**, and
see the [roadmap](docs/ROADMAP.md) for open work (look for **good first**). One firm rule: never add studio
artwork, music or video to the repository or to a shared menu.

To report a security problem, see [SECURITY.md](SECURITY.md).

## Credits and licence

Copyright (C) 2026 Phillip Berryman. Licensed under the [GNU General Public License v3.0](LICENSE)
(GPL-3.0-only), the same licence as the Jellyfin packages the plugin builds on.

- The code, schemas, validator, example menus, and the placeholder art and audio in `examples/assets/` (all
  generated for this project) are covered by that licence. **Studio artwork, music and video are not
  included and never will be**: the `thor-ragnarok` example refers to artwork it doesn't ship.
- Menus are shown through [Jellyfin](https://jellyfin.org). The web client integration depends on the
  [File Transformation](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation) plugin by
  IAmParadox27.
- Movie and series metadata and images come from TMDB through Jellyfin. This product uses the TMDB API but
  is not endorsed or certified by TMDB.
