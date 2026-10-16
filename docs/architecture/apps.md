# Apps

Each app is a thin executable project. Its `Product.props` sets its identity and its site (names, link scheme, API
and site addresses, update feed, language, accent colour), its `Branding/` folder holds its icon and logos, and its
`Resources/` its own words. The app then composes the shared pieces, replacing a service, a screen or a style where it
differs. Menadżer Spolszczeń, for example, allows one translation per game. Shared code never checks which app it runs
in.

A development build gets names of its own (data folder, mutexes, pipe, link scheme), so it runs beside an installed
copy without touching it.

## What the app sends where

The app sends no telemetry. It talks only to:

- the site's API and storage, for the catalogue and packages;
- GitHub, for its own updates;
- the hosts the site loads images from.

If the app crashes, it can send an error report to the app's GlitchTip project. It does so only with the player's
consent, and shows the report first. Logs stay on the computer, with the user's name hidden.

## Other platforms

Windows comes first. Everything platform-specific sits behind interfaces in `Core` (stores, processes, the tray,
autostart, the link scheme), so Linux and macOS can follow.

Code: `src/JustModIt.Manager/`, `src/MenadzerSpolszczen/`, `src/JustModIt.Manager.Host/`.
