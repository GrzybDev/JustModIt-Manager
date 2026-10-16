# Catalogue and downloads

The agent reads the site's public API, caches what it reads, and works offline from the cache. It refreshes at start,
every six hours and when the player asks.

## Matching games to projects

A project's store links carry the store's id of the game (a Steam app id, a GOG product id, an Epic namespace). A game
the player has and a project with a link to the same store and id match. A project shows in "Moje gry" when its public
release has a file that fits the player's copy: its store, its system and a format the app installs.

A release has base files and optional add-ons. The app picks one package for the base and one for each add-on that
fits, and installs them together. When several teams translate the same game, the player chooses, or a preferred team
in the settings decides.

## Downloads

The site counts downloads, so the app makes the counted request exactly once per real download and never retries it.
It keeps the packages of every install, so an undo or a retry never downloads (or counts) again. Downloads resume after
a break and are checked against the size and hash the site gives.

The counted request carries a random id of the installation, so the site counts one download of a release per
installation. No other request carries it.

[Site integration](../site-integration.md) lists the endpoints and fields involved.

Code: `Core/Catalog/`, `Core/Downloads/`.
