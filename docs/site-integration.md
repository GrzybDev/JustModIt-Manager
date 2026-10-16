# Site integration

What a JustModIt site needs for the apps to work with it.

## API

The apps use the site's public API, without cookies and without a player's account. They read `system/v1/config`, the
`games/v1/` listing and its filters, and each project's page at `games/v1/<game>/projects/<team>/`. Fields other than
ids and names are optional, so older backends keep working.

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

## Links

The website can open the app with `<scheme>://install/<game>/<team>/<project>?features=<key>,…`, where the scheme is
the app's (`menadzer-spolszczen`). The app opens the game with that project and add-ons selected; the player still has
to click "Zainstaluj". Leaving out `features` keeps the author's defaults; `features=` with no value means none.
