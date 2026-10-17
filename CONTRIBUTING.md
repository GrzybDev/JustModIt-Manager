# Contributing

Thanks for helping. Please follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Reporting problems

Use the [issue forms](https://github.com/GrzybDev/JustModIt-Manager/issues/new/choose). Players of Menadżer
Spolszczeń can also report in [its repository](https://github.com/GrzybDev/MenadzerSpolszczen/issues/new/choose).
Include the app version (Settings, "About") and, if you can, the app's logs. Security problems go through
[SECURITY.md](SECURITY.md), not public issues.

For anything bigger than a small fix, open an issue before you start, so we can agree on the approach.

## Building and testing

You need Windows 10/11 x64, the .NET SDK version from `global.json`, and Visual Studio or Build Tools with the "Desktop
development with C++" workload (for the native xdelta3 library).

```bash
git clone --recurse-submodules https://github.com/GrzybDev/JustModIt-Manager.git
cd JustModIt-Manager
dotnet build JustModIt.Manager.slnx
dotnet test --solution JustModIt.Manager.slnx
dotnet run --project src/MenadzerSpolszczen
```

- A development build uses its own data folder and link scheme, so it does not interfere with an installed copy.
- UI tests run headless. Set `UI_RENDERS` to a folder to save screenshots of what they show.
- Live tests use your real Steam library and the real site. They are off unless you set `MANAGER_LIVE_TESTS=1`. The
  games they use are named in `tests/live-games.json`, which git ignores: copy `tests/live-games.example.json` there
  and fill in games installed on your machine.
- To build a release package locally: `build\package.ps1 -App MenadzerSpolszczen -Version 0.1.0 -NoDelta`.

[docs/architecture](docs/architecture/README.md) explains how the apps work.

## Code

- Code, comments and logs are in English and never name a specific site or brand. Brand names and site settings come
  from each app's `Product.props`.
- Shared code never checks which app it is running in. An app replaces a service, screen or style instead.
- Text the player sees goes in the resource files, never in code ([translating](docs/translating.md)).
- Never block the UI thread: no file, network or process calls there. The window talks to the engine only through the
  agent.
- Everything must work with NativeAOT and trimming. Warnings are errors.
- Document public and internal methods with a one-line `<summary>` and a line per parameter. Comment only what the code
  can't say itself: the reason behind it.
- Tests use invented data: no real people, teams, games or projects.

## Commits and pull requests

- One commit per change, with its tests. The message is a single short line in the imperative mood ("Fix …", "Add …").
- Sign your commits.
- Make sure the build and tests pass, then open a pull request against `main`. For a visible change, add a screenshot.

Contributions are licensed under the [GPL-3.0-or-later](LICENSE.md), like the rest of the code.
