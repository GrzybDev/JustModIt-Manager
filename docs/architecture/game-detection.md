# Game detection

The app reads the records that stores and launchers already keep, and never asks the player to point at a game folder.

| Store | Sources |
|---|---|
| Steam | its library folders and app manifests |
| GOG | the registry (Galaxy and offline installers), Galaxy's databases, Heroic |
| Epic Games Store | the Epic Games Launcher's manifests, Legendary (also inside Heroic) |

Each store's provider reads its records and returns the games it found. The library keeps the last good result and
watches the records for changes; there is no polling beyond a full scan every six hours.

A few rules keep games safe:

- A game counts as installed and idle only when its store says so clearly. Anything else (updating, running, unknown)
  blocks writing.
- A drive that does not answer holds up only the games on it, and an incomplete scan never takes a missing game for an
  uninstalled one.
- Right before any write, a job reads the game's records again and checks for processes running from its folder.

When a game is not found, the "Nie widzisz swojej gry?" panel shows what each store reported and lets the player add a
folder that the store itself recognises.

Code: `Core/Library/`, `Core/Games/`, `Core/Safety/`, `Platform.Windows/`.
