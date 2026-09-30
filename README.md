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

## Matching
Extras are matched by `type` (Jellyfin ExtraType) + `durationSec` (± `toleranceSec`),
with `ordinal` as a tiebreaker, then manual linking. Never by filename.

## Verified against server source (master)
- ExtraType enum
- ImageType enum (allowed background image types)

## Still to verify on 12.x
- SpecialFeatures endpoint path
- Local extras discovery rules (folder names / suffixes)

## Planned build order
1. Plugin: local JSON load/validate, expose entries as Special Features
2. Duration auto-match + manual linking UI
3. Web menu renderer (via File Transformation), themed with Jellyfin 12 CSS variables
4. Sharing via a GitHub-backed JSON repo keyed by TMDB ID + edition
