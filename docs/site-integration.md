# Site integration

What a JustModIt site needs for the apps to work with it.

## API

The apps use the site's public API, without cookies and without a player's account. They read `system/v1/config`, the
`games/v1/` listing, the lists behind its filters (teams, statuses, project types, genres, platforms and store
platforms) and each project's page at `games/v1/<game>/projects/<team>/`. A list that the backend does not offer leaves
its filter empty, and without the configuration the apps use their defaults. Fields other than ids, slugs, names and
add-on keys, and a project's game and team, are optional, so older backends keep working.

## Matching games

A project appears for a player when one of its store links has the store's id of a game they own. The backend stores
the id in the link's `additional_data`:

| Store | Provider | Key |
|---|---|---|
| Steam | `steam` | `appid` |
| GOG | `gog` | `gog_id` |
| Epic Games Store | `epic` | `namespace` |

The app installs a project's public release when it has a file that fits the player's copy (store and system) and is
a `.zip` or a [manifest package](package-format.md).

## Downloads

Downloads go through `projects/v1/teams/<team>/<project>/releases/<release>/download/?files=…`, which counts once per
install of a release. The app makes that request once and never retries it. It sends:

- `Authorization: Bearer <key>`, the app's application key, if the build has one. The key needs the scopes
  `catalog.read`, `site.read` and `releases.download`;
- `X-JustModIt-Installation`, a random id of the installation, so the site can count one download per installation.

If the site refuses the key, the app drops it and carries on without one.

## Packages for several versions

The apps read these optional fields, which let a site keep one package for versions of a game that are laid out
differently. Without them each file is downloaded and installed as it is.

- `limits.release_versions_merged` in `system/v1/config`: true when the site keeps the packages of a release part's game
  versions as one. The package creator then tells authors about how much the site will store. Absent reads as false.
- `replaces` on a release file: the uuids of other files of the release that this ready file stands in for. An install
  that holds one of them gets no update for it, and a repair or a change of add-ons uses the download it keeps. It is
  only for files proved to install alike, never for a team's own upload, which has to reach players as an update.
- `layout` on a plain `.zip` release file: an object with `folders`, `object_size` and `object_sha256`. The app installs
  from the shared object it describes and counts and checks that size and hash. `folders` lists the same `package` and
  `game` rules as a [manifest target](package-format.md#several-versions-of-a-game). The file's own `size`, `md5` and
  `sha256` describe what a browser gets.
- `layouts=1` on a download request: the app sends it on the release's route, and on
  `projects/v1/teams/<team>/<project>/releases/<release>/files/<uuid>/download/` only for a file with a `layout`. For
  such a file the answer must then be the address of its shared object.

## Links

The website can open the app with `<scheme>://install/<game>/<team>/<project>?features=<key>,…`, where the scheme is
the app's (`menadzer-spolszczen`). The app opens the game with that project and add-ons selected; the player still has
to click "Zainstaluj". Leaving out `features` keeps the author's defaults; `features=` with no value means none.
