# Example menus

Each `*.menu.json` here is a complete, shareable menu. They are validated against
`schema/menu.schema.json` and loaded through the plugin's own loader by the test suite, so what
you see here is known to work. The extras listed in them are illustrative, not real disc
listings.

| File | Background | Shows off |
|---|---|---|
| [`example.menu.json`](example.menu.json) | the title's Jellyfin backdrop | the original minimal example, paired with [`example.binding.json`](example.binding.json) |
| [`static-backdrop.menu.json`](static-backdrop.menu.json) | **static**: Jellyfin backdrop, dimmed | the simplest menu: a plain list of buttons, no layout, no art files |
| [`solid-color-glow.menu.json`](solid-color-glow.menu.json) | **solid colour** | hand-placed centred buttons, serif/uppercase text with a glow highlight, three menu levels with both `back` and `home` |
| [`trailer-background.menu.json`](trailer-background.menu.json) | **trailer video** (looping, muted) | a translucent bar layer, buttons inside it, an automatic paged grid (More / Previous) with Home pinned |
| [`local-art.menu.json`](local-art.menu.json) | **your own image** via `asset:` | background + banner images served from the menus `assets` folder (bundled in [`assets/local-art/`](assets/local-art/)) |
| [`thor-ragnarok.menu.json`](thor-ragnarok.menu.json) | trailer + poster still | a real menu using everything at once, with a banner declared once and inherited by every menu |

## Using one

1. Copy the `.menu.json` into your server's menus folder (the plugin's `MenusPath`).
2. Create a `<same name>.binding.json` beside it that ties the menu to a library item. The minimum
   is:

   ```json
   {
     "schemaVersion": 1,
     "menuId": "<the menuId from the menu file>",
     "menuRevision": 1,
     "parentItemId": "<your library item's Jellyfin id>",
     "bindings": {}
   }
   ```

   Then run **Auto-Match** from the plugin's page in the Jellyfin dashboard to link the menu's
   extras to the item's local Special Features by type and duration. (Binding files are local to
   your server and are never shared; automatic discovery of items is not built yet, so this step
   is manual for now.)
3. Change `match` in the menu to your own title's TMDB/IMDB ids if you are adapting an example, and
   give it a fresh `menuId` (any UUID) so it doesn't collide with the example.
4. For menus that use `asset:` images, copy their art into `<MenusPath>/assets/<folder>/`. For
   `local-art.menu.json` that means copying `assets/local-art/` from this folder.

## Notes

- **Artwork you can't redistribute isn't included.** `thor-ragnarok.menu.json` refers to
  `asset:thor-ragnarok/background.webp` and `menu-banner.webp`; supply your own images with those
  names, or edit the references. Without them the menu still works, just without the pictures.
- **Trailer backgrounds** use the item's own trailers: local trailer files first, then the YouTube
  trailers Jellyfin has stored for the item. `trailerIndex` picks which. YouTube playback needs
  internet access, and trailers that disallow embedding fall back to the `poster` (or a dark screen
  if none is set). Captions are always switched off for background trailers.
- Images are never embedded in menu files: an `image` is an `https://` URL, a small inline
  `data:` image, or an `asset:` reference. See the main README's "Authoring a disc-style layout".
