# Roadmap

What exists, and what's open. If you want to help, this is the list: items marked **good first** are
self-contained and don't need deep knowledge of the codebase; items marked **big** deserve a short
conversation (open an issue) before you start.

## Where things stand

Working today, on Jellyfin 12.x with the web client:

- Menus as shareable JSON: placement and layout, a paging grid, layers, text styling, five background
  kinds (Jellyfin image, TMDB backdrop, your own image, colour, looping trailer), music and button sounds,
  transitions, scene selection from chapters.
- One-click playback, with the menu reappearing when playback ends; keyboard, remote and gamepad navigation.
- **Automatic discovery:** a menu dropped into the folder is bound to the right title by TMDB / IMDB / TVDB
  id and its extras are matched by duration.
- **Drafts:** a starter menu generated from a title's real extras and chapters.
- A **Menu Editor** on the dashboard with a live preview of the real renderer.

## Open work

### Menu Editor
- Forms for editing the selected menu, entry, theme and background, instead of raw JSON, with a TMDB
  backdrop picker and an asset picker. **big**
- Drag and resize buttons, layers and the paging grid directly on the preview (positions are percentages,
  so this maps straight onto the JSON). **big**
- Upload artwork and audio into the assets folder (type and size limits, path safety). **big**
- Create a menu from a title in the UI (the draft API exists). 
- A history view that lists and restores the automatic backups. 
- Undo and redo within an editing session. 

### Discovery and library
- Tell apart several copies of one title (for example a 4K and a 1080p file as separate items) by scoring
  release format and edition, instead of reporting them as ambiguous. **big**
- A "Change item" control on the dashboard for ambiguous menus.
- Items Jellyfin merges as multiple versions: decide which version a menu applies to.
- Drafts for series and seasons (movies only for now).

### Rendering
- Looping video from a file in the assets folder as a background.
- A trailer `poster` taken from a Jellyfin image (today only an `asset:` or `https` image).
- `fanart` backgrounds (the schema accepts them; the renderer shows a dark screen).
- An accessibility pass: screen-reader announcements, focus order, contrast checks. **good first**
- Clients other than the web client: the menu format is client-agnostic, but only jellyfin-web renders it.
  See "Other clients" below. **big**

### Sharing
- A catalogue of shared menus (a repository of JSON keyed by TMDB id and edition) and "install this menu" from
  a title's page, matched locally so a library is never sent anywhere. **big**
- An export that strips anything local before sharing, plus a checker for the rules in
  [Authoring menus](AUTHORING.md#rules-for-shareable-menus).
- An allowlist of image hosts for shared menus, and a menu contribution policy.

### Packaging
- A release pipeline: build with [jprm](https://github.com/oddstr13/jellyfin-plugin-repository-manager),
  publish a zip on GitHub Releases, and a plugin repository manifest so it can be installed from Jellyfin's
  catalogue. **good first** for someone who knows jprm.

### Other clients
- A written client implementer's guide: the response format of `GET /DiscMenus/{itemId}/Menu`, the layout,
  paging and navigation rules, and test vectors from the existing test suites, so a native client (Swiftfin,
  Neptune, Android TV, ...) can render the same menus. **big**

### Quality
- More example menus in different styles. **good first**
- Screenshots for the documentation (made from the shipped examples, never from studio artwork).
  **good first**
- Translating the editor and dashboard UI strings.
- More tests wherever you find a gap; every bug fix should come with one.

## Not planned

- Playing real disc menus (`VIDEO_TS` / BDMV navigation): this recreates the *experience* with new, light
  assets, and deliberately doesn't touch disc content or copy protection.
- Bundling any studio artwork, music or video.
