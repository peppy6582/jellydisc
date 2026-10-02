# Development guide

How to build, test, run and understand the project. For *using* it see the [README](../README.md) and
[Authoring menus](AUTHORING.md); for how to submit changes see [CONTRIBUTING](../CONTRIBUTING.md).

- [What's in the repository](#whats-in-the-repository)
- [How it fits together](#how-it-fits-together)
- [Setting up](#setting-up)
- [Running the tests](#running-the-tests)
- [Trying it on a Jellyfin server](#trying-it-on-a-jellyfin-server)
- [Working on the renderer](#working-on-the-renderer)
- [Gotchas worth knowing](#gotchas-worth-knowing)

## What's in the repository

| Path | What it is |
|---|---|
| `Jellyfin.Plugin.DiscMenus/` | The plugin (C#, .NET 10) |
| `Jellyfin.Plugin.DiscMenus.Tests/` | xUnit tests |
| `tests/js/` | Tests for the browser code (Node + jsdom) |
| `schema/` | JSON Schemas: `menu.schema.json` (shareable) and `binding.schema.json` (local only) |
| `tools/validate.py` | Validates the schemas, the examples, and a set of must-be-rejected cases, plus cross-reference rules JSON Schema can't express |
| `examples/` | Example menus (also exercised by the test suites), with bundled placeholder art and audio |
| `docs/` | Documentation |
| `build.yaml` | Plugin manifest for packaging with [jprm](https://github.com/oddstr13/jellyfin-plugin-repository-manager) |

## How it fits together

```
 menu file  ---->  loader/validator  ---->  discovery  ---->  index  ---->  player endpoint  ---->  renderer
 (shareable)       MenuFileLoader           (provider ids)    (cached)      GET DiscMenus/{id}/Menu   discmenus.js
                                                 |                                  ^
                                          binding (local)  <-- duration matcher     |
                                                                                    +-- editor preview
```

Inside `Jellyfin.Plugin.DiscMenus/`:

- **Plugin shell:** `Plugin.cs` (identity and the two dashboard pages), `ServiceRegistrator.cs`,
  `FileTransformationIntegration.cs` (registers the renderer script with the separately installed
  *File Transformation* plugin, which is how it gets into jellyfin-web).
- **Model and validation:** `Model/` mirrors the two schemas as C# types; `MenuFileLoader.cs` parses a
  menu and runs the semantic and presentation checks. The JSON Schema in `schema/`, `tools/validate.py`
  and `MenuFileLoader` are **three implementations of the same rules** (menus are untrusted input, and
  the loader does not run JSON Schema), so a rule change usually touches all three, plus tests.
- **Logic kept free of Jellyfin types, so it can be tested thoroughly:** `MenuDiscovery.cs` (which library
  item a menu is for), `MenuDraftBuilder.cs` (a starter menu from a title), `DurationMatcher.cs`
  (extras by type and duration), `MenuFileEditor.cs` (safe file listing, reading and saving for the
  editor), `YouTubeLink.cs`.
- **`DiscMenuService.cs`:** the cached index of menus and bindings (rebuilt when files or the library
  change), discovery and auto-binding, and the Jellyfin-facing lookups (extras, chapters, trailers).
- **`Api/`:** `DiscMenusController` (admin), `DiscMenusPlayerController` (viewers),
  `DiscMenusEditorController` (admin), and `RenderableBuilder`, which turns a menu plus a binding into
  exactly what the renderer consumes. The player endpoint and the editor preview share it.
- **`Web/discmenus.js`:** the renderer. A single self-contained script with no build step. It also has a
  *preview mode* used by the editor. **`Web/preview.html`** is the page the editor embeds.
- **`Configuration/`:** the two dashboard pages (`configPage.html`, `editorPage.html`), plain HTML and JS
  embedded in the DLL.

## Setting up

You need the **.NET 10 SDK**. For the schema validator, Python 3 with `jsonschema`; for the browser tests,
Node 20+.

**Native:**

```bash
dotnet build Jellyfin.Plugin.DiscMenus -c Release
dotnet test Jellyfin.Plugin.DiscMenus.Tests
pip install jsonschema && python3 tools/validate.py
(cd tests/js && npm install && npm test)
```

**With Docker** (nothing to install on the host; `docker-compose.yml` defines the tools):

```bash
docker compose up -d dev                                   # .NET 10 SDK container
docker compose exec dev dotnet build Jellyfin.Plugin.DiscMenus -c Release
docker compose exec dev dotnet test Jellyfin.Plugin.DiscMenus.Tests
docker compose run --rm validate                           # schemas + examples
docker compose run --rm jstest                             # browser-code tests
```

With VS Code, *Reopen in Container* uses `.devcontainer/`, and `.vscode/settings.json` binds
`*.menu.json` / `*.binding.json` to their schemas so you get validation while typing.

## Running the tests

| Suite | Command | Covers |
|---|---|---|
| C# | `dotnet test Jellyfin.Plugin.DiscMenus.Tests` | Discovery decisions, the draft builder, the editor's file handling (path traversal, symlinks, conflicts, backups), the loader's rules, and loading every shipped example through the real loader |
| Schemas | `python3 tools/validate.py` | The schemas against the examples, and dozens of must-be-rejected cases |
| Browser code | `cd tests/js && npm test` | The renderer (navigation, paging, layout, scenes, audio, transitions, backgrounds, trailers, preview mode) and the editor page, in a simulated browser with scripted server responses |

The browser tests stub the browser APIs the code uses (audio, animation, the preview iframe) and record
what the code asks for, so they run fast and need no browser. They can't judge how something *looks*;
for visual changes, check in a real browser too (see below).

## Trying it on a Jellyfin server

You need a **Jellyfin 12.x** server. To see menus in the web client you also need the
[File Transformation](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation) plugin, which
lets this plugin add its script to jellyfin-web. Without it the plugin's API and dashboard pages still
work; there is just no **Disc Menu** button.

A plugin lives in `<jellyfin config>/data/plugins/<Name>_<version>/` containing the DLL and a `meta.json`:

```bash
dotnet build Jellyfin.Plugin.DiscMenus -c Release
PLUGINS="<jellyfin config>/data/plugins"
mkdir -p "$PLUGINS/Disc Menus_0.1.0.0"
cp Jellyfin.Plugin.DiscMenus/bin/Release/net10.0/Jellyfin.Plugin.DiscMenus.dll "$PLUGINS/Disc Menus_0.1.0.0/"
```

`meta.json` (beside the DLL) follows the shape Jellyfin writes for installed plugins; its `guid` must match
`build.yaml` and `Plugin.cs`:

```json
{
  "category": "General",
  "changelog": "dev build",
  "description": "User-authored, shareable disc menus for Jellyfin special features.",
  "guid": "913b9d44-cafe-4fe7-ae0b-f1362dc12cc7",
  "name": "Disc Menus",
  "overview": "User-authored, shareable disc menus for Jellyfin special features.",
  "owner": "peppy6582",
  "targetAbi": "12.0.0.0",
  "timestamp": "2026-01-01T00:00:00.0000000Z",
  "version": "0.1.0.0",
  "status": "Active",
  "autoUpdate": false,
  "imagePath": "",
  "assemblies": []
}
```

Restart Jellyfin and look in its log for `Loaded plugin: "Disc Menus"`. The folder name carries the
version, so on a version bump remove the old folder; Jellyfin won't overwrite a differently-versioned
folder for the same plugin. Set the plugin's **Menus directory** on its dashboard page, drop a menu in it,
and open the title in the web client.

**Calling the API directly:** create an API key in the Jellyfin dashboard and send it as
`Authorization: MediaBrowser Token="<key>"`. Requests made with a key have no associated user, so
endpoints that need one take an explicit `userId`, or fall back to the server's first user.

## Working on the renderer

`Web/discmenus.js` is embedded in the DLL, so a change needs a rebuild, a copy of the DLL, and a Jellyfin
restart. Menu, asset and binding files need no restart. The injected script URL carries a per-build
version and is served with `no-cache`, so the browser picks up a new build; if a change "does nothing",
compare the line number in the browser console against the file first, to be sure the new script loaded.

The easiest loop for layout work is the dashboard's **Menu Editor**, which runs the renderer in preview
mode against a real server (see [Authoring menus](AUTHORING.md#the-menu-editor)).

## Gotchas worth knowing

- **Plugin identity.** `Plugin.cs` must override `Id` and `Description`. Jellyfin treats the loaded
  assembly's values as authoritative and rewrites `meta.json` to match on every startup; without the
  override `Id` is `Guid.Empty` and the plugin's sidebar entry and detail page break.
- **Config pages.** A dashboard page's `<script>` must be *inside* the `data-role="page"` element: only
  that element is inserted into the page, anything outside it silently never runs.
- **Cross-plugin types.** Each plugin loads in its own assembly context, so a type shared with another
  plugin (or a library like Newtonsoft.Json) can be a *different* type with the same name; an
  `Object of type X cannot be converted to type X` error means exactly that.
- **Threading.** The cached index is shared between requests and is never mutated: writers reload the
  binding file from disk, change that copy, save it, and invalidate the index.
- **Untrusted menus.** Anything read from a menu must be treated as hostile: render it as text (never
  HTML), build URLs only from validated pieces, and enforce a rule in the loader as well as the schema.
- **Verified Jellyfin 12 API facts** (namespaces, signatures, quirks) are collected in
  [JELLYFIN_NOTES.md](JELLYFIN_NOTES.md). Jellyfin 12 is new and its docs and plugin templates lag
  behind: check a signature against the real packages before relying on memory.
