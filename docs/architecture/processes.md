# Processes

One executable runs in one of three roles:

| Role | Started by | Does |
|---|---|---|
| launcher | Start menu, shortcut, app link | hands its request to the agent and exits; becomes the agent if none runs |
| agent (`--agent`, `--background`) | Windows at sign-in, the launcher | the tray, detection, the catalogue, downloads, installs, updates |
| window (`--ui`) | the agent | the Avalonia window; exits when closed |

Keeping the window in its own process keeps the tray small: the idle agent holds about 9 MB, while an Avalonia process
keeps well over 100 MB even after its window closes. It also means a frozen or crashed window can never stop an install.

## Talking to each other

The agent holds a per-user mutex and serves a named pipe that only the same user can open. Messages are
length-prefixed JSON. The agent pushes its whole state whenever it changes (a slow window simply skips states), plus job
progress and notices; the window sends commands and queries. A launcher only sends `Activate` with its command line or
link.

App links such as `menadzer-spolszczen://install/<game>/<team>/<project>` only navigate. Nothing installs without a
click in the window.

## Lifetime

- Closing the window ends its process. The agent stays in the tray, exits, or asks, as the player chose.
- Exiting while jobs run either waits for them or cancels them, and a cancelled job undoes what it changed first.
- The app updates itself with Velopack, in the background, when no window is open and no job runs.
- After a quiet period the agent runs a compacting garbage collection, so the memory its first scan used goes back to
  the system.

Code: `Host/Roles.cs`, `Agent/`, `Protocol/`, `Ui/Connection/`.
