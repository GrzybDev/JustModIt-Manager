<div align="center">

<img src="https://raw.githubusercontent.com/GrzybDev/JustModIt-Manager/main/src/MenadzerSpolszczen/Branding/logo-256.png" alt="" width="96" height="96">

# Menadżer Spolszczeń

The desktop app of [Baza Spolszczeń](https://bazaspolszczen.pl): it finds your games, installs Polish fan translations
for them, keeps them up to date and can undo every change.

[![Latest release](https://img.shields.io/github/v/release/GrzybDev/MenadzerSpolszczen?label=release)](https://github.com/GrzybDev/MenadzerSpolszczen/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/GrzybDev/MenadzerSpolszczen/total)](https://github.com/GrzybDev/MenadzerSpolszczen/releases)
![Windows 10/11 x64](https://img.shields.io/badge/Windows-10%20%7C%2011%20x64-0078D4)

**[Download](https://github.com/GrzybDev/MenadzerSpolszczen/releases/latest)** ·
[Baza Spolszczeń](https://bazaspolszczen.pl) ·
[Report a problem](https://github.com/GrzybDev/MenadzerSpolszczen/issues/new/choose) ·
[Source code](https://github.com/GrzybDev/JustModIt-Manager)

*Po polsku: [README.pl.md](README.pl.md)*

</div>

This repository only publishes the app's releases and collects players' reports. Its source code lives in
[JustModIt Manager](https://github.com/GrzybDev/JustModIt-Manager).

## Download

Download `MenadzerSpolszczen-win-Setup.exe` from the [latest
release](https://github.com/GrzybDev/MenadzerSpolszczen/releases/latest) and run it. The app installs for your Windows
user, without administrator rights, and then updates itself.

- Requires Windows 10 or 11, 64-bit.
- The app and its installer are code-signed by "Open Source Developer, Marek Grzyb".

## What it does

- **Finds your games** in Steam, GOG (GOG Galaxy, offline installers and Heroic) and the Epic Games Store (the Epic
  Games Launcher, Heroic and Legendary) and follows the stores' changes as they happen, without scanning your drives.
- **"Moje gry"** lists the games for which Baza Spolszczeń has a finished translation that fits your copy of the game;
  the **"Katalog"** lets you browse every translation on the site.
- **Installs translations** with one click or automatically, checks that the translation fits your version of the game
  first, and never touches a game that is running or that the store is updating.
- **Opens from the website**: "Pobierz" on a translation's page on Baza Spolszczeń can open it straight in the app, with
  the parts you chose there. Nothing installs until you click "Zainstaluj".
- **Lets you choose** when several teams translated the same game, and which optional parts of a translation to
  install; the teams you prefer in the settings decide what is installed automatically.
- **Keeps them up to date**: installs new releases of your translations and puts a translation back when a game update
  overwrote it. A translation is never installed automatically into a game that officially offers Polish.
- **Can undo everything**, even offline: the original files are kept (by default in a hidden `.menadzer-spolszczen`
  folder inside the game's folder), and an interrupted install is rolled back after a crash or power loss.
- **Runs quietly** in the system tray, starting with Windows unless you turn that off.
- **Kreator spolszczeń**, for translators in creator mode, builds translation packages from a folder of translated
  files.

## Questions

**My game is not in "Moje gry".** The list shows only games with a finished translation that fits your version of the
game. If the game is installed but the app did not find it, open "Nie widzisz swojej gry?" in "Moje gry": it shows what
each store reported and lets you point to a library folder the store uses.

**Windows warns me about the installer.** The installer is signed, but Windows SmartScreen may still warn about new
releases until they are downloaded often enough. Check that the warning names "Open Source Developer, Marek Grzyb" as
the publisher, then choose "More info" and "Run anyway".

**Do I need an account on Baza Spolszczeń?** No. The app reads the public catalogue and needs no sign-in.

**Does it work on Linux or macOS?** Not yet. The app is built so that other systems can follow.

## Privacy

The app collects no data about how you use it. It talks only to Baza Spolszczeń (the list of translations, packages and
images), to GitHub (updates of the app) and to the hosts of the images that the site shows. Downloading a package
counts on the translation's page, just like a download from the website. So that it counts once per installation, that
request alone carries a random installation id, which says nothing about you or your computer; reinstalling the app
gives a new one.

If the app closes unexpectedly, it can send a description of the error to its authors: only when you agree, or without
asking once you turn that on in the settings. You see what goes before it is sent; it holds no user name and no file
contents, though its last log lines may name the games and folders they were about. The
[privacy policy of Baza Spolszczeń](https://bazaspolszczen.pl/legal/privacy-policy) has the details.

## Reporting a problem

- **A problem with the app or an idea for it:** [open an issue](https://github.com/GrzybDev/MenadzerSpolszczen/issues/new/choose),
  in Polish or English. In the app, "O programie" in the settings opens one with your versions filled in.
- **A problem with a translation** (wrong files, a broken release): the translation's page on Baza Spolszczeń, or its
  [contact form](https://bazaspolszczen.pl/contact).
- **A security problem:** report it privately, as the
  [security policy](https://github.com/GrzybDev/JustModIt-Manager/security/policy) describes.

## Uninstalling

Uninstall the app in Windows Settings, under Apps. Translations you installed stay in your games: to get the original
files back, uninstall the translations in the app first. The app's settings and records stay in
`%LOCALAPPDATA%\BazaSpolszczen\MenadzerSpolszczen`, so reinstalling it picks up where you left off.

## Licence

The app's code is free software under the GNU General Public License, version 3 or later. The names and logos of Baza
Spolszczeń and Menadżer Spolszczeń are not covered by that licence. See [LICENSE.md](LICENSE.md) for the details and
for where to find the source code of each release.
