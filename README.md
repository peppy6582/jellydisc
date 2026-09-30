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
  involved) are the callback and the injected script itself. See "Web menu renderer" below —
  confirmed working end to end, but the script is still a placeholder; the real on-screen menu
  UI isn't built yet.
- `Jellyfin.Plugin.DiscMenus.Tests/` – xUnit tests for `DurationMatcher` (the tolerance/
  ordinal/tiebreak edge cases). `docker compose exec dev dotnet test Jellyfin.Plugin.DiscMenus.Tests`
- `build.yaml` – jprm plugin manifest (name/guid/version/targetAbi/framework) for
  packaging a release zip.

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

**Still to do**: `discmenus.js` is a placeholder (`console.log(...)` only) — the actual on-screen
menu UI (grid layout, background art, theming via Jellyfin 12 CSS variables, wiring to
`GET DiscMenus/{id}` and `GET DiscMenus/{id}/SpecialFeatures`) doesn't exist yet. Its execution
in a real browser also hasn't been visually confirmed (only that the server serves the right
bytes) — should show up as a console log line on any page load once confirmed.

## Still to verify on 12.x
- Local extras discovery rules (folder names / suffixes) — how an admin would organize
  `*.menu.json`/`*.binding.json` per library item in the general case; sidestepped for the Thor
  test via an explicit `MenusPath`.
- `discmenus.js` actually executing in a real browser (server-side injection is confirmed; the
  browser-side console log hasn't been visually checked yet).

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
3. Web menu renderer (via File Transformation), themed with Jellyfin 12 CSS variables —
   **pipeline proven, real UI not started**. File Transformation is installed and our script
   injection into `index.html` is confirmed working end to end at the HTTP level (see "Web menu
   renderer via File Transformation" above). `discmenus.js` is still just a placeholder — the
   actual themed on-screen menu (grid layout, background art, wiring to the existing
   `GET DiscMenus/{id}`/`SpecialFeatures` API) is the remaining work.
4. Sharing via a GitHub-backed JSON repo keyed by TMDB ID + edition
