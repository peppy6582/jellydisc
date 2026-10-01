# jellyfin-disc-menus

User-authored, shareable "disc menus" for Jellyfin special features.
Target: **Jellyfin 12.x** (net10.0, targetAbi 12.0.0.0, Jellyfin.Controller 12.0.0).

## Layout
- `schema/menu.schema.json` – shareable menu definition. Anchored to the parent item's
  provider IDs (Tmdb/Imdb/Tvdb) + release. No file names, paths, or local item IDs.
- `schema/binding.schema.json` – local-only map from menu extra keys to this server's
  Jellyfin item IDs. Never exported.
- `examples/` – one valid example of each.
- `tools/validate.py` – JSON Schema validation plus cross-reference checks the schema
  can't express (dangling extra/menu keys, menus unreachable from `root`), with
  negative test cases. `pip install jsonschema && python3 tools/validate.py`
- `Jellyfin.Plugin.DiscMenus/` – the plugin itself. `Plugin.cs` overrides `Id`/`Description`
  with fixed values matching `build.yaml`'s guid — **required**, not cosmetic: Jellyfin's
  `PluginManager.CreatePluginInstance` treats the *loaded assembly's* `IPlugin.Id`/`Description`
  as authoritative over `meta.json` and rewrites the manifest to match on every startup: without
  this override, `Id` defaults to `Guid.Empty` and permanently zeroes `meta.json`'s guid (see
  "Fixed: plugin Id resetting to Guid.Empty" below — this bit us for real). `Model/` mirrors the
  two schemas as C# POCOs (reusing Jellyfin's own `ExtraType`/`ImageType` enums); `MenuFileLoader`
  loads/saves + runs the same semantic checks as `validate.py`; `DiscMenuService` resolves
  a parent item's bound menu to local `BaseItem`s (its Special Features), runs
  `DurationMatcher` to auto-fill bindings, and exposes `SetManualBinding`/`IgnoreBinding`/
  `ResetBinding` for per-key manual overrides; `Api/DiscMenusController` exposes all of it
  over HTTP (`GET DiscMenus`, `GET DiscMenus/{id}`, `GET DiscMenus/{id}/SpecialFeatures`,
  `GET DiscMenus/{id}/Candidates`, `POST DiscMenus/{id}/AutoMatch`,
  `POST DiscMenus/{id}/Bindings/{key}/{Link|Ignore|Reset}`), admin-gated via
  `[Authorize(Policy = "RequiresElevation")]`. `Configuration/configPage.html` is a minimal
  admin page (registered via `IHasWebPages`, `EnableInMainMenu = true` under the `server` menu
  section so it shows in the dashboard sidebar) listing bound menus with match counts, an
  Auto-Match button per row, and a "Details" panel per menu for manually linking, ignoring,
  or resetting individual extra keys. Deployed and confirmed fully working end to end in a real
  logged-in browser session against real library data — sidebar entry, detail page, and every
  JS-driven feature on the config page itself (see "End-to-end test against real data" and the
  two "Fixed:" sections below for what broke along the way and why).
  `FileTransformationIntegration.cs` (an `IHostedService`) registers a web-renderer script
  injection with the separately-installed "File Transformation" plugin, if present; `Api/DiscMenusController`'s
  `web/Transform` and `web/discmenus.js` actions (both `[AllowAnonymous]` — no Jellyfin session
  involved) is the callback; `Web/discmenus.js` (embedded resource, served by
  `GetRendererScript()`) is the real renderer — a floating button + full-screen overlay menu,
  themed from the menu's own `theme.json`. `Api/DiscMenusPlayerController` (plain `[Authorize]` —
  any logged-in viewer, not admin-only) exposes `GET DiscMenus/{id}/Menu`, the fully-resolved
  navigable menu tree the renderer consumes. See "Web menu renderer" below — the injection
  pipeline and this endpoint are both confirmed end to end against real data; the renderer's
  actual in-browser behavior (as opposed to the server-side pieces it depends on) isn't yet, and
  it has real, documented v1 limitations (no one-click playback chief among them).
- `Jellyfin.Plugin.DiscMenus.Tests/` – xUnit tests for `DurationMatcher` (the tolerance/
  ordinal/tiebreak edge cases). `docker compose exec dev dotnet test Jellyfin.Plugin.DiscMenus.Tests`
- `build.yaml` – jprm plugin manifest (name/guid/version/targetAbi/framework) for
  packaging a release zip.

## Authoring a disc-style layout

By default a menu's entries flow in a centred-left column. To replicate a real DVD/Blu-ray menu,
add placement data. Everything is optional and additive (`schemaVersion` stays 1), so older menus
keep working unchanged.

- **Per entry:** `position` `{ x, y, w?, h?, anchor? }` — percentages (0-100) of the menu screen,
  so a layout scales to any display. `anchor` says which point of the button `x`/`y` refers to
  (`top-left` default, `top`, `center`, `bottom-right`, ...). `style` is how the highlight is drawn:
  `text`, `frame`, `glow` or `arrow`. `image` / `imageFocus` replace the text with artwork
  (the label then becomes the accessible name).
- **Per menu:** `layout` `{ titlePosition?, hideTitle?, buttonStyle? }` — where the title goes and
  the default `style` for entries that don't set one.
- **All or none:** within one menu either every entry has a `position` or none does (mixing makes
  overlap ambiguous). Enforced by both `tools/validate.py` and the plugin's loader.
- **Navigation just works:** focus moves to the nearest button in the pressed direction by on-screen
  position, so any layout is navigable with arrow keys, a remote or a gamepad.

- **Shared look:** a top-level `layout` is the default for every menu; a menu's own `layout` overrides
  it field by field. Put the banner `layers`, `buttonStyle` and `hideTitle` there once and every
  submenu inherits them.
- **Automatic grid + paging (`layout.flow`):** `{ region, columns, rows, moreLabel?, previousLabel? }`.
  Entries with no `position` fill the region's cells left-to-right, top-to-bottom, so a submenu can sit
  inside the same banner without hand-placing anything. If they don't all fit, the renderer adds a
  **More** button (and **Previous** on later pages) in the grid's last cells. The first `back` entry is
  pinned to every page, like a disc's Return button. Use `home` (jump to the root menu) instead of `back`
  (return to the parent) when you want a "Home"/"Main Menu" button that is never confused with Previous. The Back *key* steps back a page first; the
  on-screen Back button goes up to the parent menu. A flow menu's entries must not have positions, and
  the grid needs at least 4 cells so the paging buttons never crowd out every entry.

- **Trailer video background:** `background` `{ "source": "trailer", "trailerIndex"?, "muted"?, "poster"?, "dim"? }`
  plays one of the item's own trailers behind the menu. `trailerIndex` (default 0) counts local trailer
  files first, then the YouTube trailers Jellyfin has stored for the item (Thor: 0 = Official, 1 = Teaser).
  YouTube trailers play in a muted, looping, borderless privacy-enhanced embed (`youtube-nocookie.com`),
  faded in only once the player reports it is playing; local files play in a plain `<video>`. The
  `poster` image shows until then and stays if the item has no trailer or embedding is refused. The
  video sits in its own layer, so it keeps looping across submenus and pages and stops when the menu
  closes. Caveats: YouTube playback needs internet access and tells Google the viewer watched it; some
  trailers disallow embedding; local trailers only play if the browser can decode the file directly. Captions are always suppressed for background trailers (YouTube's captions are
  unloaded even if the viewer's YouTube account prefers them, and a local file's embedded subtitle tracks are disabled).

- **Scene selection:** a `chapters` entry (`{ "action": "chapters", "label": "Scene Selection", "perPage": 6 }`)
  opens a screen generated from the feature's chapters: one button per chapter, paged with More /
  Previous, with Back pinned. Each chapter starts the movie at that chapter. Chapters show Jellyfin's
  thumbnail when it has extracted chapter images (the "Extract chapter images" task), otherwise the
  chapter name and start time. With no further settings it uses a built-in 3x3 grid. Add
  `"menu": "<key>"` to style it with one of your own menus: that menu's title, background, theme and
  layout are used, the chapters are placed first (at most `perPage` per page), and its own entries
  (typically `home`/`back`) stay pinned. Use `"layers": []` in that menu's layout to switch off an
  inherited banner for the screen. A `playFeature` entry can also set `startChapter` (1-based).

- **Music and button sounds (`audio`, opt-in):** a top-level `audio` is the default for every menu; a menu's
  own `audio` replaces it per part (its `music` replaces the default music, its `sounds` the default
  sounds). `music` is `{ "source": "file", "file": "asset:folder/track.mp3", "volume": 0.5 }`, or
  `{ "source": "themeSong" }` for the item's own Jellyfin theme song (silent if it has none), or
  `{ "source": "none" }` to silence a menu. A file is an `https://` URL or an `asset:` reference ending in
  `.mp3 .ogg .opus .m4a .wav`. Music loops with fades and keeps playing across menus that name the same
  track; a different track crossfades. `sounds` is `{ "preset": "click|chime|beep|none", "volume": 0.5,
  "move"?, "select"?, "back"? }`: the presets are synthesised in the browser (no files needed) and any of
  move/select/back can be replaced by an audio file. Everything stops when the menu closes or playback
  starts. Browsers only allow audio after a click, which the viewer's click on "Disc Menu" provides.
- **Transitions (`layout.transition`):** `{ "style": "none|fade|slide|rise|zoom|wipe", "durationMs": 300 }`.
  Going into a submenu or the next page animates forward and Back/Previous animates the other way. Only
  the buttons and title animate: the background, a trailer video and any layers (e.g. a banner) shared by
  both menus stay put. Honours the viewer's reduced-motion setting.

**Shareable means no binaries and no code.** Images are never embedded files or paths: an `image` is
an `https://` URL or a small `data:image/(png|jpeg|webp);base64,` URI (SVG is refused). URLs may not
contain whitespace, quotes, parentheses or angle brackets. The server and the browser both re-check this,
since a downloaded menu is untrusted input. Note an https image is fetched by the viewer's browser
from that host, so the host sees the viewer's IP; an author who cares can use `data:` icons or the
parent item's own Jellyfin images (`background.source: "jellyfin"`).

Not yet implemented: free-floating decorative images/text layers, background video, menu music,
transition animations, and scene-selection chapter grids (see the roadmap in the project notes).

## Automatic discovery

Drop a `*.menu.json` into the menus folder and the plugin does the rest:

- **Finds the title** by the menu's `match` provider ids (TMDB / IMDB / TVDB), through Jellyfin's own
  provider-id lookup. Never by file name or path. An item counts if at least one id matches and none
  contradicts (an item with a different TMDB id is a different title even if an IMDB id coincides);
  matching more ids wins; two equally good items are **ambiguous** and nothing is chosen. A season menu
  matches its series by id and then the season number.
- **Links the extras** by type and duration (the existing matcher), without needing a signed-in user.
  Only extras that aren't already matched, manual or ignored are touched, and files are only written
  when something changes.
- **Keeps its own bindings** in the plugin's data folder (`bindings/<menuId>.binding.json`), so the
  shareable menu files stay untouched. A `<name>.binding.json` written by hand next to a menu always
  takes priority and is never modified automatically.
- **Stays current:** the index is cached and rebuilt when a menu/binding file changes, or (throttled to
  once per five seconds) after the library changes, so a title or extra added later gets linked on its
  own, and a corrected ID re-binds. If two menus claim one title, a hand-written binding wins, then the
  newer revision, then file order; the other is reported as "not used".
- **Shows its work:** `GET /DiscMenus/Status` (admin) and the **Menu Files** table on the plugin's
  dashboard page list every menu file as bound, waiting for its title, needs review, not used, or can't
  be loaded; `POST /DiscMenus/Scan` and the **Scan now** button force a re-check.

**Drafting a menu for a title** (admin API): `GET /DiscMenus/Draft/{itemId}` previews a starter menu built from
what the library has for a movie: its TMDB/IMDB ids, its real local extras with their types and
durations, and a Scene Selection entry if it has chapters. Extras are listed flat when there are up to
eight, otherwise grouped by type (Featurettes, Shorts, ...) with a Play All; a type with one extra is a
direct entry. Labels come from the files' own names with rip clutter removed ("Featurette - title 083"
becomes "Featurette 1 (5:10)" so the files can be told apart). `POST /DiscMenus/Draft/{itemId}` writes it
into the menus folder as `<title>-<year>.menu.json`, where discovery binds it and matches the extras
straight away. It never overwrites a file and refuses a title that already has a menu unless
`force=true`; every draft passes the same validation as a hand-written menu before it is returned. A
draft is a starting point: edit the labels and design, and don't share it as-is (its labels are your
rip's, not the disc's).

Not yet: telling apart several copies of the same title (e.g. a 4K and a 1080p file as separate items)
beyond reporting them as ambiguous; scoring by edition/format is the planned next step.

## Menu Editor

A **Menu Editor** page sits under the plugin's entry in the Jellyfin dashboard sidebar. Pick a menu file,
edit its JSON, and watch the live preview update beside it:

- **The preview is the real viewer.** It runs the same renderer in a 1920x1080 frame scaled to fit, so
  layouts, text sizes, clicks, remote keys, transitions, backgrounds, trailers and scene selection behave
  exactly as for viewers. A **Show menu** picker jumps to any menu in the file, and follows you as you click
  through the preview. If the file is bound to a library title the preview uses that title's real extras,
  chapters and trailers; otherwise it says what it lacks. Anything the menu "plays" is reported ("in a real
  menu this would start playing...") and never reaches your own session. It starts with sound off.
- **Errors as you type.** Problems come from the same loader the server uses, with line and column for syntax
  errors (click to jump there). While the text is invalid the preview keeps the last good version.
- **Saving is careful.** It writes only existing `*.menu.json` files inside the menus folder; refuses text
  that wouldn't load, a changed `menuId` (bindings are tied to it) or a lower `revision`; bumps the revision
  for you when you changed something; keeps the previous version as a timestamped backup (newest 25 per file,
  in the plugin's data folder); and checks the file hasn't changed on disk since you opened it, offering to
  load the server's version or overwrite if it has. Ctrl+S saves.
- **Admin API** (all admin-only): `GET /DiscMenus/Editor/Files`, `GET /DiscMenus/Editor/File?name=`,
  `PUT /DiscMenus/Editor/File?name=&version=` (body = the menu text), `POST /DiscMenus/Editor/Preview?file=`
  (body = the menu text; returns exactly what the player endpoint would serve for it).

Not in the editor yet: forms for editing properties, dragging buttons into place, uploading art or audio, and
creating a menu from a title (use `POST /DiscMenus/Draft/{itemId}`).

## Matching
Extras are matched by `type` (Jellyfin ExtraType) + `durationSec` (± `toleranceSec`),
with `ordinal` as a tiebreaker, then manual linking. Never by filename.

## Verified against server source (master)
- ExtraType enum
- ImageType enum (allowed background image types)

## Verified against a live server (2026-09-30)
Deployed to the `Jellyfin` container on this box (12.1.0, `lscr.io/linuxserver/jellyfin`) and
restarted. Confirmed by server log + direct HTTP requests, not just compiling:
- The plugin loads cleanly on a 12.1.0 server with `targetAbi: "12.0.0.0"` — no ABI error, no
  exceptions anywhere in startup (`Loaded plugin: "Disc Menus" "0.1.0.0"`).
- `Api/DiscMenusController` registered correctly via plain assembly scanning, no
  `FrameworkReference` needed — confirmed by `GET /DiscMenus` reaching the controller at all
  (not a 404).
- `[Authorize(Policy = "RequiresElevation")]` is a real, registered policy: the same
  unauthenticated `GET /DiscMenus` request returned a clean `401 Unauthorized`, not a `500`
  (an unregistered policy name throws on evaluation, which would show as 500). This was
  previously an unverified guess borrowed from other plugins' source.
- The config page is served and byte-for-byte correct: `GET /web/configurationpage?name=DiscMenus`
  returns `200` with a body identical to `Configuration/configPage.html` (verified both file
  size and a content grep for the embedded plugin GUID). Confirms `IHasWebPages.GetPages()`,
  the embedded resource wiring, and the real Jellyfin config-page URL convention all work
  end to end.
- Real plugin install layout, confirmed from another installed plugin's actual files rather
  than guessed: plugins live at `/config/data/plugins/<Name>_<version>/` (not `/config/plugins/`),
  each folder holding the DLL(s) plus a `meta.json` (`category`, `changelog`, `description`,
  `guid`, `name`, `overview`, `owner`, `targetAbi`, `timestamp`, `version`, `status`,
  `autoUpdate`, `imagePath`, `assemblies: []` — `assemblies` empty even for real working
  plugins, so it isn't what drives DLL discovery). See "Deploying" below.

## End-to-end test against real data: Thor: Ragnarok (2026-09-30)
Set `MenusPath` explicitly (hand-wrote
`/mnt/cache/appdata/Jellyfin/data/plugins/configurations/Jellyfin.Plugin.DiscMenus.xml`,
matching the plain `<PluginConfiguration><MenusPath>...` shape of other installed plugins'
config XML) to `/config/data/discmenus/menus`, then built a real menu/binding pair from the
actual "Thor: Ragnarok" (2017) entry in the 4K library — found via direct read-only queries
against `jellyfin.db` (provider IDs Tmdb 284053 / Imdb tt3501632, and its 5 real local extras
with their real durations), schema-validated with `tools/validate.py`'s underlying jsonschema
check, then exercised every endpoint using the pre-existing "Claude" API key found in the
`ApiKeys` table:

- `GET /DiscMenus` and `GET /DiscMenus/{id}` — correctly list the menu and all 5 extras as
  Unmatched before any match has run.
- `GET /DiscMenus/{id}/Candidates` — returned exactly the 5 real local extras (name, type,
  duration) straight from `BaseItem.GetExtras`.
- `POST /DiscMenus/{id}/AutoMatch` — matched **5/5 correctly** on the first run (all unique
  by duration alone, confidence 1.0 since the menu's `durationSec` came directly from the same
  `RunTimeTicks`), and the binding file on disk updated correctly (N-format GUIDs, schema-valid).
- `GET /DiscMenus/{id}/SpecialFeatures` — returned exactly those 5 resolved item IDs.
- `POST .../Bindings/{key}/Ignore` then `AutoMatch` again — correctly reported
  `{Matched:0, Skipped:5}`, leaving the ignored key alone.
- `POST .../Bindings/{key}/Reset` then `AutoMatch` again — correctly re-matched just that one
  key (`{Matched:1, Skipped:4}`).
- `POST .../Bindings/{key}/Link?itemId=...` — correctly set status Matched / method Manual /
  confidence 1.

Zero exceptions in the server log across the whole sequence. This is the strongest evidence
so far that `DurationMatcher`, `DiscMenuService`, and the controller all work correctly against
real data, not just unit tests and synthetic examples. The test menu/binding pair is still in
place on this server (`thor-ragnarok.menu.json`/`.binding.json` under the path above) as a
working example — Thor: Ragnarok's Special Features are, as a side effect, now genuinely
resolvable through this plugin.

Also resolved by this test: hitting Jellyfin's own native endpoint (unrelated to this plugin)
at `GET /Items/{itemId}/SpecialFeatures` confirmed that path is real — it reached a controller
action and failed only because API-key auth has no associated user
(`UserManager.GetUserById(Guid.Empty)` threw), which is exactly the scenario our own endpoints'
explicit `userId` parameter (with a `GetFirstUser()` fallback) was designed to avoid.

At this point everything above was curl/API-key testing, not a logged-in dashboard session in a
real browser — the config page's actual JS execution was confirmed separately, and needed two
more fixes first; see the two "Fixed:" sections below.

## Fixed: plugin Id resetting to Guid.Empty on every startup (2026-09-30)
After the Thor test above, the dashboard showed almost nothing for this plugin (no working
settings link, just bare manifest fields) and threw "An error occurred while getting the plugin
details from the repository". `GET /Plugins` confirmed the actual cause: this plugin's `Id` was
genuinely `Guid.Empty` at runtime, and `meta.json`'s `guid`/`description` fields were being
silently reset to `""`/all-zeros within ~6 seconds of every single restart — before any
scheduled task runs, confirmed by second-by-second polling.

Root cause (confirmed by reading the actual `jellyfin/jellyfin` v12.1 source, not guessed):
`PluginManager.CreatePluginInstance` (`Emby.Server.Implementations/Plugins/PluginManager.cs`,
~line 629) compares the loaded assembly's `instance.Id`/`instance.Description` against the
manifest read from `meta.json`, and **unconditionally overwrites the manifest with the
assembly's values** whenever they differ — treating the compiled plugin as authoritative over
the manifest file, by design (the comment in that code explains it's meant to self-heal a
manifest for a plugin that failed to load). `BasePlugin.Id` (`MediaBrowser.Common/Plugins/BasePlugin.cs`)
is a `private set` property that only gets populated from an assembly-level `[Guid(...)]`
attribute if one exists (`BasePluginOfT.cs` constructor) — we had none, so `Id` defaulted to
`Guid.Empty`, which then got written back into `meta.json` on every load, permanently
clobbering whatever guid we'd hand-written there.

**Fix**: override `Id` (and `Description`) directly in `Plugin.cs` with fixed values matching
`build.yaml`'s guid — confirmed to be exactly the pattern used by the official
`jellyfin/jellyfin-plugin-template` repo's own `Plugin.cs`. Once deployed, `meta.json` self-healed
immediately (guid and description both came back correct on the very next restart), `GET /Plugins`
reports the right `Id`, and the dashboard's plugin-details flow should now work. **If you ever
change the guid in `build.yaml`/`meta.json`, `Plugin.cs`'s `Id` override must be updated to
match — they are two independent hardcoded copies of the same value with nothing enforcing they
stay in sync.**

Also added in this pass: `GetPages()` now sets `EnableInMainMenu = true`, `MenuSection =
"server"`, `MenuIcon = "extension"` so the config page shows in the dashboard's left sidebar.
Confirmed working after the `Id` fix above — the sidebar entry (built from `GET
/web/ConfigurationPages`, filtered by `EnableInMainMenu`) and the plugin detail page's Settings
link (which matches `configurationPage` to the plugin by `PluginId`) both depend on a real,
non-empty plugin `Id`, so this had been silently broken by the exact same root cause.
`MenuSection` itself turned out to be vestigial in current jellyfin-web (grepped the actual
source — it's read nowhere; only `EnableInMainMenu` and `MenuIcon` matter), but it's harmless to
keep set.

## Fixed: config page JS never ran — script was a sibling of the page `<div>`, not inside it (2026-09-30)
Once the sidebar/detail-page fix above landed, the config page itself loaded (not just the bare
manifest), but **nothing JS-driven populated** — neither the "Menus directory" field nor the
"Bound Menus" table, even though the underlying API calls both depend on were independently
confirmed working via curl. Both symptoms from one cause: `viewContainer.js` (jellyfin-web)
extracts `div[data-role="page"]` from the fetched HTML and inserts *that* into the live DOM —
our `<script>` tag was a sibling of that div (both direct children of `<body>`), not nested
inside it, so it was never part of what got extracted and simply never reached the DOM at all.
(A real, if ultimately irrelevant, side-finding from chasing this down first: jQuery genuinely
is exposed as `window.jQuery` in the current client — confirmed directly in-browser — so the
theory that script execution requires jQuery's `.appendTo()` doesn't explain this; the simpler
structural bug does.) **Fix**: moved the `<script>` block to be the last child inside
`<div id="DiscMenusConfigPage">`, before its closing tag, instead of after it. Confirmed working
end to end in a real logged-in browser session — this was the last unverified piece of the
whole plugin.

## Web menu renderer via File Transformation (2026-09-30)
Item 3 of the planned build order needs a way to show an actual on-screen "disc menu" in the
player, not just the admin dashboard — a completely different problem from the config page.
Jellyfin core has no supported way for a plugin to inject content into jellyfin-web (confirmed:
a Jellyfin forum moderator explicitly states this isn't supported, since it'd give one client
special treatment). The community answer is a separate, admin-installed plugin, **File
Transformation** (https://github.com/IAmParadox27/jellyfin-plugin-file-transformation, by
IAmParadox27) — it lets other plugins rewrite jellyfin-web's served files (here, `index.html`)
via a middleware hook. Verified by cloning and reading its actual source directly, not secondary
docs, since a first research pass could only get this secondhand:

- **Installed on this server**: repository `https://www.iamparadox.dev/jellyfin/plugins/manifest.json`
  added, plugin installed via `POST /Repositories` + `POST /Packages/Installed/{name}` (both real,
  confirmed-via-OpenAPI-spec endpoints), version 3.0.1.0, `targetAbi: "12.1.0.0"` — a build whose
  changelog literally says "Add support for 12.1," matching this server exactly.
- **Real API** (`PluginInterface.cs`): `RegisterTransformation(JObject payload)` /
  `RemoveTransformation(Guid)`, both static. Dependents can't reference File Transformation's
  types directly — Jellyfin loads every plugin in its own `AssemblyLoadContext` — so this has to
  go through reflection. The payload (`TransformationRegistrationPayload.cs`) supports three
  callback mechanisms (assembly reflection, a named pipe, or an HTTP endpoint); we use the HTTP
  one (`transformationEndpoint`) since it needs no shared types at all — File Transformation POSTs
  `{"contents": "<full file text>"}` to our own endpoint and reads the response body back as the
  transformed text (confirmed by reading `TransformationHelper.ApplyTransformation` directly).
- **A real bug hit and fixed along the way**: `RegisterTransformation` takes a
  `Newtonsoft.Json.Linq.JObject`. Building that payload from our *own* `Newtonsoft.Json`
  reference failed at runtime — `Object of type 'JObject' cannot be converted to type 'JObject'`
  — because each plugin's isolated `AssemblyLoadContext` gives even identically-versioned copies
  of the same type distinct runtime identities. Neither we nor File Transformation itself ship a
  private copy of `Newtonsoft.Json` (confirmed: absent from both plugins' actual installed
  folders) — Jellyfin's own server process already references it, so there's one shared instance
  for the whole process. Fix: don't reference `Newtonsoft.Json` from our own csproj at all;
  find the shared assembly via `AssemblyLoadContext.All` and build the payload with *its*
  `JObject.Parse`, obtained via reflection, so the type genuinely matches. See
  `FileTransformationIntegration.cs`.
- **Confirmed end to end**: `GET /web/index.html` now really contains
  `<script src="/DiscMenus/web/discmenus.js"></script>` injected right before `</body>`, and that
  script serves correctly. Since `index.html` is the browser's own top-level page load (not a
  dynamically-inserted SPA fragment like the config page was), normal HTML parsing applies and
  the injected script executes exactly like any other `<script>` tag — none of the config page's
  `viewContainer.js` insertion quirks apply here.

Confirmed fully end to end, including in a real browser: reloading any page logs the script's
own load message to the console. That was the last unverified piece of the injection pipeline
itself — script serving, jellyfin-web delivery, and actual execution.

### The renderer's data source: `GET DiscMenus/{parentItemId}/Menu`
The admin controller's endpoints are all `[Authorize(Policy = "RequiresElevation")]` (admin-only)
and expose flat, per-key binding *status* — not what a renderer needs. `Api/DiscMenusPlayerController`
is a separate controller (same `DiscMenus` route prefix — multiple controllers can share a prefix
as long as the specific routes don't collide) gated with a plain `[Authorize]` — any authenticated
viewer, not just admins, confirmed by ASP.NET Core's standard `[Authorize]` semantics (no policy
means "must be authenticated," nothing more; a class-level `[Authorize(Policy=...)]` can't be
*downgraded* by an action-level attribute, only added to, which is why this needed its own
controller rather than an action on the admin one). `GET {parentItemId}/Menu` joins `menu.json` +
`binding.json` server-side into one `RenderableMenuDocument` — the full navigable tree
(`root`/`menus`/`entries`/`background`/`theme`) with every `playExtra`/`playSequence` already
resolved to real local item IDs (unmatched ones dropped, not left as gaps) — so the renderer
never needs to do that join itself. Confirmed against the live Thor: Ragnarok data: returns the
correct nested `main`→`features` tree with all 5 extras' real item IDs, and correctly 401s
unauthenticated. Tested with the admin API key (which naturally satisfies a lower bar than
`RequiresElevation`) — a genuinely non-admin user session hasn't been separately tested, though
nothing in a bare `[Authorize]` should distinguish them.

### The renderer itself: `Web/discmenus.js` (2026-09-30)
A real, working overlay renderer, not just the placeholder — built after researching jellyfin-web's
actual client-side mechanics directly (cloned the repo again, plus fetched the real `jellyfin-apiclient`
npm package, v1.11.0, that `window.ApiClient` actually is), since guessing at browser-side APIs is
exactly the kind of thing that's bitten this project before:

- **Detecting the current item**: jellyfin-web is a hash-routed SPA (`components/router/appRouter.js`
  builds URLs like `#/details?id=<guid>`) — confirmed by reading `getRouteUrl()` directly. Since our
  script loads once with `index.html` and never reloads on in-app navigation, it watches the native
  `hashchange` event and extracts `id=` from the hash on every change, calling
  `GET DiscMenus/{id}/Menu` each time it changes. A 404 (no menu bound) means no button shows —
  silent, no error state needed.
- **Showing the menu**: a small floating "Disc Menu" button (bottom-right, doesn't need to know
  anything about Jellyfin's own details-page DOM layout, which was never reverse-engineered) opens a
  full-screen overlay rendering the current menu's entries, themed with the menu's own `accent`/`align`
  from `theme.json`, backed by `ApiClient.getImageUrl(itemId, { type, index })` for `background.source:
  "jellyfin"` (signature confirmed directly from the real `jellyfin-apiclient` package source — the
  one new API call this needed that the config page hadn't already exercised) or a solid color for
  `"color"`. Submenu/back navigation is a simple in-memory stack; arrow keys + Enter + Escape work via
  a `keydown` listener, mouse click always works.
- **Real, deliberate v1 limitations** (all called out in the script's own file header too):
  - **No one-click playback.** `playbackManager` (the thing that actually starts video playback) is
    an ES module internal to jellyfin-web's own bundle — confirmed by reading
    `apps/legacy/controllers/itemDetails/index.js`, which imports it directly
    (`import { playbackManager } from 'components/playback/playbackmanager'`) — not a global, and
    there's no `window.playbackManager` anywhere in the source (grepped for it directly), no
    AMD-style `require()` bridge for legacy scripts to pull internal modules by name either (that
    mechanism is gone from the current, React/Vite-based client), and no `autoplay`-style query
    param on the details route either. So `playFeature`/`playExtra` navigate to the target item's own
    details page (`location.hash = '#/details?id=...'`) instead of starting playback directly — the
    user clicks Jellyfin's own native Play button there. A real gap from the DVD-menu ideal, not a
    corner deliberately cut for no reason: closing it would mean either patching one of jellyfin-web's
    own bundle files via a second File Transformation rule to expose the module (fragile — breaks on
    any jellyfin-web rebuild that changes minified internals) or simulating a click on Jellyfin's own
    Play button (needs the real details-page DOM, unverified).
  - `playSequence` plays only the first resolved item, not the whole queue as a playlist (queueing
    multiple items also needs `playbackManager`).
  - `chapters` (scene selection) shows a "not implemented" message — this plugin doesn't fetch
    chapter data at all yet.
  - `background.source: "tmdb"`/`"fanart"` fall back to a plain dark background — not implemented.
  - **Jellyfin 12's actual theme CSS variables are much thinner than the original plan assumed**:
    checked `src/themes/_base/_theme.scss` directly — only four runtime custom properties exist
    (`--jf-palette-background-defaultImage`, `--jf-palette-AppBar-transparentBg`,
    `--jf-palette-AppBar-gradient`, `--jf-card-borderRadius`); the actual color palette (primary,
    background, text) is baked into MUI's JS theme object and compiled CSS at build time, not exposed
    as readable runtime variables. So "themed with Jellyfin 12 CSS variables" isn't really achievable
    as originally phrased — this version themes purely from the menu's own `theme.json`
    (`accent`/`align`) plus fixed dark colors, not by reading Jellyfin's current skin.
- **Not yet verified**: none of the above has been clicked through in a real browser session yet —
  only that the server serves the right script and the right menu data. The config page needed a
  real-browser pass to catch a real bug (the script-nesting issue) before it actually worked; this
  renderer hasn't had that pass yet.

## Still to verify on 12.x
- Local extras discovery rules (folder names / suffixes) — how an admin would organize
  `*.menu.json`/`*.binding.json` per library item in the general case; sidestepped for the Thor
  test via an explicit `MenusPath`.
- `Web/discmenus.js`'s actual behavior in a real browser — the button appearing, the overlay
  rendering correctly, keyboard nav, and the details-page navigation actually working. Only the
  underlying API calls and jellyfin-apiclient method signatures have been verified so far, not a
  live click-through.

## Development

Lives at `/boot/config/plugins/compose.manager/projects/jellyfin-disc-menus`
(the compose.manager project tree, alongside every other project on this box).
That path is the **boot flash** — source is fine there and restic backs this
tree up, but build output must not be. `Directory.Build.props` redirects `bin/`
and `obj/` into the `jdm_artifacts` Docker volume whenever `$JDM_ARTIFACTS` is
set, which `docker-compose.yml` does. Build outside the container and it falls
back to the usual `./bin` and `./obj`.

Nothing here autostarts (`autostart` is `false`). The `dev` service exists only
so VS Code has somewhere to attach.

```bash
docker compose up -d dev                 # .NET 10 SDK, idles on sleep infinity
docker compose exec dev dotnet --version # 10.0.401
docker compose run --rm validate         # JSON Schema + cross-reference checks
docker compose down                      # stops dev; volumes persist
```

**VS Code:** open the folder and *Reopen in Container* (`.devcontainer/`
attaches to the `dev` service), or just open it and use the tasks — `validate
schemas` is the default test task, `dotnet build` the default build task, and
`dotnet test` runs the C# unit tests. `.vscode/settings.json` binds
`*.menu.json` and `*.binding.json` to their schemas so you get validation
while typing, not only when the validator runs.

Volumes: `jdm_nuget` (package cache), `jdm_artifacts` (bin/obj), `jdm_pip`.
All on the cache pool, none on the flash.

**Deploying to the real server on this box** (the `Jellyfin` container, appdata at
`/mnt/cache/appdata/Jellyfin`) — manual, since there's no jprm packaging/release pipeline yet:

```bash
docker compose exec dev dotnet build Jellyfin.Plugin.DiscMenus -c Release
docker compose cp dev:/artifacts/Jellyfin.Plugin.DiscMenus/bin/Release/net10.0/Jellyfin.Plugin.DiscMenus.dll .
# meta.json by hand, matching build.yaml — see an existing plugin's meta.json for the exact
# shape, e.g. /mnt/cache/appdata/Jellyfin/data/plugins/Fanart_*/meta.json
mkdir -p "/mnt/cache/appdata/Jellyfin/data/plugins/Disc Menus_<version>"
cp Jellyfin.Plugin.DiscMenus.dll meta.json "/mnt/cache/appdata/Jellyfin/data/plugins/Disc Menus_<version>/"
chown -R 99:100 "/mnt/cache/appdata/Jellyfin/data/plugins/Disc Menus_<version>"  # matches container PUID/PGID
docker restart Jellyfin
```

Check `/mnt/cache/appdata/Jellyfin/log/log_<date>.log` for `Loaded plugin: "Disc Menus"` and no
exceptions. The folder must be renamed (old one removed) on every version bump — Jellyfin
doesn't overwrite a differently-versioned folder for the same plugin guid.

**Testing the API directly:** there's a standing Jellyfin API key named "Claude" in the
`ApiKeys` table of `/mnt/cache/appdata/Jellyfin/data/data/jellyfin.db`, meant for exactly this
kind of automated testing. Use it as `Authorization: MediaBrowser Token="<token>"`. API-key
requests have no associated user, so any endpoint needing one (ours accept an explicit
`userId` query param) needs it passed explicitly or falls back to the server's first user.

## Planned build order
1. Plugin: local JSON load/validate, expose entries as Special Features — **done, proven
   against real data**. Loading, semantic validation, and resolving a bound menu's entries to
   local `BaseItem`s work — confirmed end to end against the real "Thor: Ragnarok" 4K library
   entry (see "End-to-end test against real data"), reachable via
   `GET DiscMenus/{parentItemId}/SpecialFeatures` and the config page. Still missing: a way to
   actually discover `*.menu.json`/`*.binding.json` pairs per-library-item in general (the Thor
   test used an explicit `MenusPath`, not per-item discovery — see "Still to verify on 12.x").
2. Duration auto-match + manual linking UI — **done, fully confirmed in a real browser**.
   `DurationMatcher` + `DiscMenuService.RunAutoMatch` match by type + duration (± tolerance),
   fall back to ordinal position on ambiguity, and persist results back to the binding file
   without touching bindings already Manual/Ignored (covered by unit tests, and now also a real
   5/5 correct auto-match against actual local extras). The config page's per-menu "Details"
   panel lists every extra key with its current status and a dropdown of local candidates, so
   an admin can Link, Ignore, or Reset any key by hand — confirmed working end to end in a real
   logged-in dashboard session, after fixing the plugin-Id and config-page-script bugs described
   below.
3. Web menu renderer — **built, not yet browser-verified, real playback integration missing**.
   File Transformation injection, the `GET DiscMenus/{id}/Menu` data source, and a real
   `Web/discmenus.js` overlay renderer (floating button, full-screen menu, submenu/back nav,
   keyboard support, themed from the menu's own `theme.json`) all exist and are individually
   confirmed against the live server (see "Web menu renderer via File Transformation" and "The
   renderer itself" above). What's not done: a real in-browser click-through of the renderer
   itself, and true one-click playback — `playbackManager` isn't reachable from an externally
   injected script, so entries currently navigate to the target item's details page instead of
   playing it directly. "Themed with Jellyfin 12 CSS variables" turned out not to be achievable
   as originally phrased — confirmed directly that Jellyfin 12's actual runtime theme variables
   are far thinner than assumed (4 variables, no color palette) — so theming comes from the
   menu's own `theme.json` instead.
4. Sharing via a GitHub-backed JSON repo keyed by TMDB ID + edition
