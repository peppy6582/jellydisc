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
- `Jellyfin.Plugin.DiscMenus/` – the plugin itself. `Model/` mirrors the two schemas
  as C# POCOs (reusing Jellyfin's own `ExtraType`/`ImageType` enums); `MenuFileLoader`
  loads/saves + runs the same semantic checks as `validate.py`; `DiscMenuService` resolves
  a parent item's bound menu to local `BaseItem`s (its Special Features), runs
  `DurationMatcher` to auto-fill bindings, and exposes `SetManualBinding`/`IgnoreBinding`/
  `ResetBinding` for per-key manual overrides; `Api/DiscMenusController` exposes all of it
  over HTTP (`GET DiscMenus`, `GET DiscMenus/{id}`, `GET DiscMenus/{id}/SpecialFeatures`,
  `GET DiscMenus/{id}/Candidates`, `POST DiscMenus/{id}/AutoMatch`,
  `POST DiscMenus/{id}/Bindings/{key}/{Link|Ignore|Reset}`), admin-gated via
  `[Authorize(Policy = "RequiresElevation")]`. `Configuration/configPage.html` is a minimal
  admin page (registered via `IHasWebPages`) listing bound menus with match counts, an
  Auto-Match button per row, and a "Details" panel per menu for manually linking, ignoring,
  or resetting individual extra keys. Deployed and end-to-end tested against real library data
  on a real 12.1.0 server (see "End-to-end test against real data" below); only the page's
  in-browser JS itself (as opposed to the API it calls) hasn't run in an actual browser yet.
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

**Still not verified**: the config page's actual browser-side JS (`ApiClient`/`Dashboard`
globals, the `pageshow` lifecycle) — everything above was curl/API-key testing, not a logged-in
dashboard session in a real browser. The PascalCase-JSON assumption the JS depends on **is now
confirmed** (every response above came back PascalCase, e.g. `"ParentItemId"`, `"MenuTitle"`),
which was the main risk in that area.

## Still to verify on 12.x
- Local extras discovery rules (folder names / suffixes) — how an admin would organize
  `*.menu.json`/`*.binding.json` per library item in the general case; sidestepped for the Thor
  test via an explicit `MenusPath`.
- The config page's browser-side JS specifically (dashboard globals, click handlers) — the data
  it depends on (JSON casing, all API responses) is now confirmed; only the DOM/JS execution
  itself hasn't run in a real browser.

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
2. Duration auto-match + manual linking UI — **done, proven against real data**.
   `DurationMatcher` + `DiscMenuService.RunAutoMatch` match by type + duration (± tolerance),
   fall back to ordinal position on ambiguity, and persist results back to the binding file
   without touching bindings already Manual/Ignored (covered by unit tests, and now also a real
   5/5 correct auto-match against actual local extras). The config page's per-menu "Details"
   panel lists every extra key with its current status and a dropdown of local candidates, so
   an admin can Link, Ignore, or Reset any key by hand — the underlying API for all three was
   exercised directly and works correctly. Only the config page's in-browser JS itself hasn't
   been clicked through in a real dashboard session yet.
3. Web menu renderer (via File Transformation), themed with Jellyfin 12 CSS variables
4. Sharing via a GitHub-backed JSON repo keyed by TMDB ID + edition
