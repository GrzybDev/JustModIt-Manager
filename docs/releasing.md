# Releasing

Each app is versioned and released on its own. Versions live only in tags such as `menadzer-spolszczen/v0.1.0`, and
releases are named `v<version>`.

## Making a release

Run Actions > Release > Run workflow, then pick the app and the version bump (`patch`, `minor`, `major`, `prerelease`,
`release`, with an optional `alpha`/`beta`/`rc` label). The workflow builds, tests, signs, publishes and tags the
release. A dry run does everything except publishing.

To preview the version (after `git fetch --tags`):

```powershell
build\version.ps1 -App menadzer-spolszczen -Bump prerelease -PreRelease beta
```

JustModIt Manager is published here. Menadżer Spolszczeń is published to
[GrzybDev/MenadzerSpolszczen](https://github.com/GrzybDev/MenadzerSpolszczen), which holds only releases. Its README
and licence are kept in `src/MenadzerSpolszczen/Release/`.

## What a release carries

Every release holds both systems, each packed on its own runner, since NativeAOT compiles only for the system it runs
on:

| File | What it is |
|---|---|
| `<AppId>-win-Setup.exe` | the Windows installer |
| `<AppId>.AppImage` | the Linux app |
| `install.sh` | installs or replaces the release's AppImage and starts the app |
| `<slug>-install.desktop` | runs `install.sh` in a terminal from a file manager |
| `<slug>-install.zip` | the same file packed for Firefox, which renames every downloaded `.desktop` file |
| `checksums.txt` | the SHA-256 of every file a player downloads, of both systems |
| full and delta packages, `releases.*.json` | Velopack's packages and feeds, which installed copies update from |

`install.sh` and the installer files point at their own release, so `releases/latest/download/install.sh` installs the
newest stable release. The script needs curl or wget and FUSE (`fusermount3` or `fusermount`), and refuses root and
anything but x86_64. It checks the AppImage against `checksums.txt`, puts it into `~/Applications/<AppId>.AppImage`,
restarts the agent's systemd unit if it runs and starts the app. Run again, it updates or repairs the install.
`build/package.ps1` writes all three from the app's project and its `Installer_*` texts.

The workflow uploads the Windows files as a draft, then attaches the installer files and `checksums.txt`, and publishes
the release with the Linux packages last, so players never see it without one system's files. When an upload fails in
between, the draft stays: delete it on GitHub before releasing that version again.

## Signing

Only the Windows files are signed: Linux has no code signing, and `install.sh` checks the AppImage against
`checksums.txt` instead. The certificate is on a smart card, so signing happens on the maintainer's machine. While a
release runs, keep this running there, from a clone of [GrzybDev/actions](https://github.com/GrzybDev/actions):

```powershell
codesign\sign-pending.ps1 -Watch
```

It asks for the PIN twice per release: once for the app's files, once for the installer. Check that the listed files
belong to the running release before entering it. Unsigned files fail the release after an hour.

## Secrets

| Secret | Use |
|---|---|
| `RELEASE_REPOSITORY_TOKEN` | publishing to the release repository |
| `MANAGER_API_KEY_MENADZER_SPOLSZCZEN`, `MANAGER_API_KEY_JUSTMODIT_MANAGER`, `MANAGER_API_KEY_CI` | each channel's API key (optional) |
| `MANAGER_ERROR_REPORTS_DSN_MENADZER_SPOLSZCZEN`, `MANAGER_ERROR_REPORTS_DSN_JUSTMODIT_MANAGER` | each app's GlitchTip project (optional) |
| `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, `AWS_DEFAULT_REGION`, `AWS_ENDPOINT_URL` | the signing and symbols storage |
