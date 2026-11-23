# Processes

One executable runs in one of three roles:

| Role | Started by | Does |
|---|---|---|
| launcher | Start menu or the desktop's list of apps, shortcut, app link | hands its request to the agent and exits; becomes the agent if none runs, or on Linux starts the agent's unit and becomes the window |
| agent (`--agent`, `--background`) | Windows at sign-in, on Linux its systemd user unit; the launcher | the tray, detection, the catalogue, downloads, installs, updates |
| window (`--ui`) | the agent; on Linux also the launcher | the Avalonia window; exits when closed |

Keeping the window in its own process keeps the tray small: the idle agent holds about 9 MB, while an Avalonia process
keeps well over 100 MB even after its window closes. It also means a frozen or crashed window can never stop an install.

## Talking to each other

The agent holds a per-user lock and serves a pipe that only the same user can open: a named mutex and a named pipe on
Windows, a locked file and a Unix socket in `$XDG_RUNTIME_DIR` on Linux. Messages are length-prefixed JSON. The agent
pushes its whole state whenever it changes (a slow window simply skips states), plus job progress and notices; the
window sends commands and queries. A launcher only sends `Activate` with its command line or link.

App links such as `menadzer-spolszczen://install/<game>/<team>/<project>` only navigate. Nothing installs without a
click in the window.

## Lifetime

- Closing the window ends its process. The agent keeps running in the background, exits, or asks, as the player chose.
- Exiting while jobs run either waits for them or cancels them, and a cancelled job undoes what it changed first.
- The app updates itself with Velopack, in the background, when no window is open and no job runs.
- After a quiet period the agent runs a compacting garbage collection, so the memory its first scan used goes back to
  the system.

## On Linux

- The agent runs as the systemd user unit `<DesktopId>.service` (`dev.grzyb.MenadzerSpolszczen.service`), which the app
  writes into `~/.config/systemd/user` and enables while "start with the system" is on. A launch starts the unit and
  becomes the window, so the agent lives outside Steam's game sessions and the window is the process that the desktop
  or Steam started.
- Stopping the unit stops every process left in it, so the agent starts everything else (the window, the updater, games,
  links) in a systemd scope of its own, with the user manager's current environment.
- At every start the agent keeps its desktop entry and icons in `~/.local/share` up to date: the entry registers the
  link scheme and offers an exit action and, for an AppImage, an uninstall action. The tray is a StatusNotifierItem and
  notifications go to `org.freedesktop.Notifications`, both over D-Bus. On a desktop without a tray the window comes
  back from the app's menu entry.
- In Game Mode (gamescope) Steam shows only the programs it starts, so the window opens from the app's shortcut in
  Steam's library, which the agent asks Steam to add. A launch that Steam did not start, such as a link opened in
  Steam's browser, hands its request to the agent, whose page waits for the window. Game Mode shows no system
  notifications, so the app sends none there.
- An installed AppImage's window runs from a copy of the program in the cache (`<cache>/program/<version>`), since
  Steam's "Exit game" and a logout stop the AppImage's mount together with the window. When an update replaces the
  AppImage, an open window starts anew in the agent's version in the same process, which Steam keeps as its game.
- The window closes as asked on SIGTERM (a logout) and SIGINT (Steam's "Exit game"). When its X display goes away, as
  when the desktop's session ends for Game Mode, it ends its session and leaves at once (`Platform.Linux/XDisplay.cs`),
  since Xlib's own exit hangs in the runtime's shutdown; a window whose display does not answer does not start.
- An AppImage uninstalls itself, from the settings or its menu entry's uninstall action: it removes the unit, the
  desktop entry, the icons and the file, and keeps the app's data and the projects in games.

Code: `Host/Roles.cs`, `Host/AgentPlatform.cs`, `Agent/`, `Protocol/`, `Ui/Connection/`, `Platform.Linux/`.
