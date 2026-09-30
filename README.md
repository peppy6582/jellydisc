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
  a parent item's bound menu to local `BaseItem`s (its Special Features) and runs
  `DurationMatcher` to auto-fill bindings, and `Api/DiscMenusController` exposes all three over
  HTTP (`GET DiscMenus`, `GET DiscMenus/{parentItemId}/SpecialFeatures`,
  `POST DiscMenus/{parentItemId}/AutoMatch`), admin-gated via
  `[Authorize(Policy = "RequiresElevation")]`. `Configuration/configPage.html` is a minimal
  admin page (registered via `IHasWebPages`) listing bound menus with match counts and an
  Auto-Match button per row; **untested against a live server** (see "Still to verify on
  12.x"). No manual-linking UI yet for whatever auto-match can't resolve – see
  "Planned build order".
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

## Still to verify on 12.x
- SpecialFeatures endpoint path (Jellyfin's own, for clients — distinct from this plugin's
  `Api/DiscMenusController`, which is our own management API, not a client playback path)
- Local extras discovery rules (folder names / suffixes)
- `"RequiresElevation"` as the admin-only authorization policy name — used by
  `Api/DiscMenusController`, follows a convention seen in other Jellyfin plugins' source,
  not confirmed against 12.x directly (no running server to test against yet)
- The config page's JS (`ApiClient.getPluginConfiguration`/`updatePluginConfiguration`/`ajax`,
  `Dashboard.showLoadingMsg`/`processPluginConfigurationUpdateResult`, the `pageshow` lifecycle
  event, `data-role`/`is="emby-*"` markup conventions) follows the long-standing Jellyfin plugin
  config-page pattern (part of jellyfin-web, largely independent of server-side API changes),
  but has never been loaded in an actual Jellyfin instance. Confirmed only: the embedded
  resource name (`Jellyfin.Plugin.DiscMenus.Configuration.configPage.html`) exactly matches
  what `Plugin.GetPages()` constructs, checked by inspecting the built DLL's manifest resources.

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

## Planned build order
1. Plugin: local JSON load/validate, expose entries as Special Features — **scaffolded**.
   Loading, semantic validation, and resolving a bound menu's entries to local `BaseItem`s
   work and are reachable via `GET DiscMenus/{parentItemId}/SpecialFeatures` and the config
   page (see `Jellyfin.Plugin.DiscMenus/`). Still missing: a way to actually discover
   `*.menu.json`/`*.binding.json` pairs per-library-item (currently a flat configured
   directory scanned by `parentItemId`), and confirming how this should relate to Jellyfin's
   own client-facing SpecialFeatures endpoint (see "Still to verify on 12.x").
2. Duration auto-match + manual linking UI — **auto-match + API + list page done, manual
   linking not started**. `DurationMatcher` + `DiscMenuService.RunAutoMatch` match by type +
   duration (± tolerance), fall back to ordinal position on ambiguity, and persist results
   back to the binding file without touching bindings already Manual/Ignored. Covered by unit
   tests. Reachable via `POST DiscMenus/{parentItemId}/AutoMatch` or the config page's
   per-row Auto-Match button. Still missing: a UI for manually linking whatever auto-match
   can't resolve (the config page only lists counts today, not individual unmatched keys).
3. Web menu renderer (via File Transformation), themed with Jellyfin 12 CSS variables
4. Sharing via a GitHub-backed JSON repo keyed by TMDB ID + edition
