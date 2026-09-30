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
  loads + runs the same semantic checks as `validate.py`; `DiscMenuService` resolves a
  parent item's bound menu to local `BaseItem`s (its Special Features). No config page,
  matcher, or web renderer yet – see "Planned build order".
- `build.yaml` – jprm plugin manifest (name/guid/version/targetAbi/framework) for
  packaging a release zip.

## Matching
Extras are matched by `type` (Jellyfin ExtraType) + `durationSec` (± `toleranceSec`),
with `ordinal` as a tiebreaker, then manual linking. Never by filename.

## Verified against server source (master)
- ExtraType enum
- ImageType enum (allowed background image types)

## Still to verify on 12.x
- SpecialFeatures endpoint path
- Local extras discovery rules (folder names / suffixes)

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
schemas` is the default test task, `dotnet build` the default build task.
`.vscode/settings.json` binds `*.menu.json` and `*.binding.json` to their
schemas so you get validation while typing, not only when the validator runs.

Volumes: `jdm_nuget` (package cache), `jdm_artifacts` (bin/obj), `jdm_pip`.
All on the cache pool, none on the flash.

## Planned build order
1. Plugin: local JSON load/validate, expose entries as Special Features — **scaffolded**.
   Loading, semantic validation, and resolving a bound menu's entries to local `BaseItem`s
   work (see `Jellyfin.Plugin.DiscMenus/`). Still missing: a config page, a way to actually
   discover `*.menu.json`/`*.binding.json` pairs per-library-item (currently a flat
   configured directory scanned by `parentItemId`), and wiring `GetSpecialFeatures` into
   whatever the real SpecialFeatures endpoint/API surface turns out to be.
2. Duration auto-match + manual linking UI
3. Web menu renderer (via File Transformation), themed with Jellyfin 12 CSS variables
4. Sharing via a GitHub-backed JSON repo keyed by TMDB ID + edition
