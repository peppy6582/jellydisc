# Contributing

Thanks for wanting to help. This project is young, and there is a lot of room to contribute, from menus
and documentation to the renderer, the editor and the discovery logic. See the [roadmap](docs/ROADMAP.md)
for what's open (look for **good first**), and open an issue if you'd like to talk something through before
starting.

## Ways to help

- **Make menus.** The most useful contribution is a good menu for a real title, or a new example in a
  different style. Menus are plain JSON, and the dashboard's **Menu Editor** gives you a live preview.
  See [Authoring menus](docs/AUTHORING.md).
- **Report bugs.** Include your Jellyfin version, the plugin version, your browser, the menu file (or the
  relevant part), and what you expected versus what happened. For a display problem, a screenshot helps.
  Browser console messages starting with `[Disc Menus]` are often the quickest clue.
- **Improve the docs**, especially anything that confused you.
- **Fix something or build a feature.** Start from the roadmap.

## Ground rules

1. **No studio content.** Don't add studio artwork, music, video, logos or screenshots of copyrighted
   menus to the repository, an example or a menu. Use art you made or that is clearly licensed for this,
   and say where it came from in your pull request. This is the one rule that protects the whole project.
2. **Menus are untrusted input.** A menu can come from anyone, so anything read from one must be handled
   as hostile: render it as text (never HTML), build URLs only from validated pieces, and never let it
   name a file outside the assets folder. A rule belongs in **all three places** that check menus: the
   JSON Schema, `tools/validate.py`, and `MenuFileLoader` (the loader doesn't run JSON Schema). See
   [Development](docs/DEVELOPMENT.md#how-it-fits-together).
3. **Match titles by id, never by file name.** A menu author's file organisation won't match anyone
   else's.
4. **Every change comes with tests**, and every bug fix with a test that fails without it.
5. **Keep it dependency-light.** The renderer and the dashboard pages are plain JavaScript with no build
   step on purpose.

## Workflow

1. Fork the repository and create a branch from `main`.
2. Set up as described in the [development guide](docs/DEVELOPMENT.md#setting-up), then make your change.
3. Run the whole test suite:
   ```bash
   dotnet test Jellyfin.Plugin.DiscMenus.Tests
   python3 tools/validate.py
   (cd tests/js && npm test)
   ```
   The same checks run on every pull request.
4. If it changes how something looks, try it in a real browser too: the tests can't judge appearance. The
   Menu Editor's preview is the quickest way.
5. Open a pull request that says what changed and why. Small, focused pull requests are easiest to review.

Commit messages should say what changed and why, in the imperative ("Add scene selection paging"), with the
reason in the body when it isn't obvious.

## Licence

By contributing you agree that your contribution is licensed under the project's licence,
[GPL-3.0-only](LICENSE). Please don't contribute code you can't license that way.

## Behaviour

Be kind and be patient: people here are volunteers with different experience levels. Disagree with
ideas, not with people.
