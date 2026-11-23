# Apps

Each app is a thin executable project. Its `Product.props` sets its identity and its site (names, link scheme, Linux
desktop id, API and site addresses, update feed, language, accent colour), its `Branding/` folder holds its icon and
logos, and its `Resources/` its own words. The app then composes the shared pieces, replacing a service, a screen or a
style where it differs. Menadżer Spolszczeń, for example, allows one translation per game. Shared code never checks
which app it runs in.

A development build gets names of its own (data folder, instance locks, pipe, link scheme, desktop id), so it runs
beside an installed copy without touching it.

## What the app sends where

The app sends no telemetry. It talks only to:

- the site's API and storage, for the catalogue and packages;
- GitHub, for its own updates;
- the hosts the site loads images from.

If the app crashes, it can send an error report to the app's GlitchTip project. It does so only with the player's
consent, and shows the report first. Logs stay on the computer, with the user's name hidden.

## Other platforms

The apps run on Windows 10/11 x64 and on Linux x86-64 with systemd and glibc 2.35 or later, SteamOS on the Steam Deck
included. Everything platform-specific sits behind interfaces in `Core` (the stores' records, processes and open files,
the tray, notifications, autostart, the link scheme, window frames, image decoders); `Platform.Windows` and
`Platform.Linux` implement them, and `Host` composes those of the system it runs on. macOS has no implementations, so
the agent refuses to start there.

Code: `src/JustModIt.Manager/`, `src/MenadzerSpolszczen/`, `src/JustModIt.Manager.Host/`.
