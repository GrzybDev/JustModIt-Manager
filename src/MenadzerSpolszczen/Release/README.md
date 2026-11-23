<div align="center">

<img src="https://raw.githubusercontent.com/GrzybDev/JustModIt-Manager/main/src/MenadzerSpolszczen/Branding/logo-256.png" alt="" width="96" height="96">

# Menadżer Spolszczeń

The desktop app of [Baza Spolszczeń](https://bazaspolszczen.pl): it finds your games, installs Polish fan translations
for them, keeps them up to date and can undo every change.

[![Latest release](https://img.shields.io/github/v/release/GrzybDev/MenadzerSpolszczen?label=release)](https://github.com/GrzybDev/MenadzerSpolszczen/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/GrzybDev/MenadzerSpolszczen/total)](https://github.com/GrzybDev/MenadzerSpolszczen/releases)
![Windows 10/11 and Linux, x86-64](https://img.shields.io/badge/Windows%2010%2F11%20%7C%20Linux-x86--64-0078D4)

**[Download](https://github.com/GrzybDev/MenadzerSpolszczen/releases/latest)** ·
[Baza Spolszczeń](https://bazaspolszczen.pl) ·
[Report a problem](https://github.com/GrzybDev/MenadzerSpolszczen/issues/new/choose) ·
[Source code](https://github.com/GrzybDev/JustModIt-Manager)

*Po polsku: [README.pl.md](README.pl.md)*

</div>

This repository only publishes the app's releases and collects players' reports. Its source code lives in
[JustModIt Manager](https://github.com/GrzybDev/JustModIt-Manager).

## Download

### Windows

Download `MenadzerSpolszczen-win-Setup.exe` from the [latest
release](https://github.com/GrzybDev/MenadzerSpolszczen/releases/latest) and run it. The app installs for your Windows
user, without administrator rights, and then updates itself.

- Requires Windows 10 or 11, 64-bit.
- The app and its installer are code-signed by "Open Source Developer, Marek Grzyb".

### Linux and Steam Deck

The app needs 64-bit Linux on an Intel or AMD processor, with systemd and glibc 2.35 or later, such as SteamOS 3.7 or
later on the Steam Deck, Bazzite, Fedora, Ubuntu 22.04 or later, Arch Linux or Linux Mint. It installs into your home
folder, without an administrator password, and then updates itself.

1. On a Steam Deck, switch to Desktop Mode first: the STEAM button › Power › Switch to Desktop.
2. Download `menadzer-spolszczen-install.desktop` from the
   [latest release](https://github.com/GrzybDev/MenadzerSpolszczen/releases/latest). Firefox renames downloaded
   `.desktop` files, so in Firefox download `menadzer-spolszczen-install.zip` instead and extract it in the Dolphin file
   manager (right mouse button, then Extract › Extract here).
3. Double-click the file in Dolphin. KDE asks what to do with it, where you choose to run it, and for a file downloaded
   directly also shows a warning, which you confirm.

The installer downloads the app into `~/Applications/MenadzerSpolszczen.AppImage`, checks it against the release's
checksums and starts it, showing its progress in a Konsole window. If something fails, the window says what, and the
installer can run again.

GNOME does not run downloaded `.desktop` files. There, or instead of the file anywhere, open a terminal, paste this
command and press Enter:

```sh
curl -fsSL https://bazaspolszczen.pl/manager/linux | sh
```

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
- **Translates Steam achievements** where a team translated them: Steam shows the Polish texts in its library, the
  in-game overlay and unlock notifications while its interface is in Polish, and you turn them on or off on the game's
  page. Games that show achievements in their own menus, and Steam's web pages, keep their texts.
- **Runs quietly** in the background, with an icon in the system tray where the desktop has one, starting with the
  system unless you turn that off.
- **Kreator spolszczeń**, for translators in creator mode, keeps saved projects, recognises the game by the translated
  files in a folder, and builds packages for several versions of a game and its add-ons from one project.

## Questions

**My game is not in "Moje gry".** The list shows only games with a finished translation that fits your version of the
game. If the game is installed but the app did not find it, open "Nie widzisz swojej gry?" in "Moje gry": it shows what
each store reported and lets you point to a library folder the store uses.

**Windows warns me about the installer.** The installer is signed, but Windows SmartScreen may still warn about new
releases until they are downloaded often enough. Check that the warning names "Open Source Developer, Marek Grzyb" as
the publisher, then choose "More info" and "Run anyway".

**Do I need an account on Baza Spolszczeń?** No. The app reads the public catalogue and needs no sign-in.

**How does it work on the Steam Deck?** Install it in Desktop Mode, as [above](#linux-and-steam-deck). Game Mode shows
only what Steam starts, so add the app to Steam's library: the app offers it when it first starts, and "Skrót w
bibliotece Steama" in the settings does it later. In Game Mode the app then opens from the library like a game, and its
background part installs and updates in both modes. Game Mode has no tray or notifications, so what waits for you is
under "Do zrobienia" in "Moje gry". Translated achievements reach Steam through the classic mode ("Tryb klasyczny bez
Millennium" in the settings), since Millennium cannot be installed on SteamOS.

**Does it work on macOS?** No. It runs on Windows and on Linux with an Intel or AMD processor, not on ARM.

## Privacy

The app collects no data about how you use it. It talks only to Baza Spolszczeń (the list of translations, packages and
images), to GitHub (updates of the app) and to the hosts of the images that the site shows. Downloading a package
counts on the translation's page, just like a download from the website. So that it counts once per installation, that
request alone carries a random installation id, which says nothing about you or your computer; reinstalling the app
gives a new one.

If the app closes unexpectedly, it can send a description of the error to its authors: only when you agree, or without
asking once you turn that on in the settings. You see what goes before it is sent; it holds only the error itself, with
no user name, no file contents, no paths and no games. The [privacy policy of Baza Spolszczeń](https://bazaspolszczen.pl/legal/privacy-policy) has the details.

## Reporting a problem

- **A problem with the app or an idea for it:** [open an issue](https://github.com/GrzybDev/MenadzerSpolszczen/issues/new/choose),
  in Polish or English. In the app, "O programie" in the settings opens one with your versions filled in.
- **A problem with a translation** (wrong files, a broken release): the translation's page on Baza Spolszczeń, or its
  [contact form](https://bazaspolszczen.pl/contact).
- **A security problem:** report it privately, as the
  [security policy](https://github.com/GrzybDev/JustModIt-Manager/security/policy) describes.

## Uninstalling

Translations you installed stay in your games: to get the original files back, uninstall the translations in the app
first. The app's settings and records stay too, so reinstalling it picks up where you left off.

- **Windows:** uninstall the app in Windows Settings, under Apps. Its data stays in
  `%LOCALAPPDATA%\BazaSpolszczen\MenadzerSpolszczen`.
- **Linux:** uninstall it in the app, with "Odinstaluj aplikację" under "O programie" in the settings, or with the
  "Odinstaluj" action of its menu entry. Its data stays in `~/.local/share/BazaSpolszczen/MenadzerSpolszczen`, and its
  shortcut in Steam's library stays until you remove it in Steam.

## Licence

The app's code is free software under the GNU General Public License, version 3 or later. The names and logos of Baza
Spolszczeń and Menadżer Spolszczeń are not covered by that licence. See [LICENSE.md](LICENSE.md) for the details and
for where to find the source code of each release.
