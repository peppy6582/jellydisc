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
  or resetting individual extra keys. Deployed and loads cleanly on a real 12.1.0 server (see
  "Verified against a live server" below); the page's in-browser JS specifically is still
  unverified.
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

**Not yet verified even after this**: the config page's actual JS behavior inside the real
dashboard iframe (`ApiClient`/`Dashboard` globals, the `pageshow` lifecycle, and the assumption
that JSON responses are PascalCase, e.g. `item.MenuTitle`) — that needs a logged-in browser
session, which this check didn't have. Same for exercising `AutoMatch`/`Link`/`Ignore`/`Reset`
against real local extras — no `*.menu.json`/`*.binding.json` pair has been placed on this
server yet to test against.

## Still to verify on 12.x
- SpecialFeatures endpoint path (Jellyfin's own, for clients — distinct from this plugin's
  `Api/DiscMenusController`, which is our own management API, not a client playback path)
- Local extras discovery rules (folder names / suffixes)
- The config page's browser-side JS and the PascalCase-JSON assumption — see the live-server
  section above for exactly what is and isn't covered by non-browser testing.

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

## Planned build order
1. Plugin: local JSON load/validate, expose entries as Special Features — **scaffolded**.
   Loading, semantic validation, and resolving a bound menu's entries to local `BaseItem`s
   work and are reachable via `GET DiscMenus/{parentItemId}/SpecialFeatures` and the config
   page (see `Jellyfin.Plugin.DiscMenus/`). Still missing: a way to actually discover
   `*.menu.json`/`*.binding.json` pairs per-library-item (currently a flat configured
   directory scanned by `parentItemId`), and confirming how this should relate to Jellyfin's
   own client-facing SpecialFeatures endpoint (see "Still to verify on 12.x").
2. Duration auto-match + manual linking UI — **done, loads cleanly on a real server, behavior
   under real data not yet exercised**. `DurationMatcher` + `DiscMenuService.RunAutoMatch` match
   by type + duration (± tolerance), fall back to ordinal position on ambiguity, and persist
   results back to the binding file without touching bindings already Manual/Ignored (covered
   by unit tests). The config page's per-menu "Details" panel lists every extra key with its
   current status and a dropdown of local candidates, so an admin can Link, Ignore, or Reset
   any key by hand. Deployed to this box's real Jellyfin 12.1.0 (see "Verified against a live
   server"); still needs an actual `*.menu.json`/`*.binding.json` pair placed and a logged-in
   browser session to exercise the full flow end to end.
3. Web menu renderer (via File Transformation), themed with Jellyfin 12 CSS variables
4. Sharing via a GitHub-backed JSON repo keyed by TMDB ID + edition
