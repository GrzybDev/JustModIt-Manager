# Releasing

Each app is versioned and released on its own. Versions live only in tags such as `menadzer-spolszczen/v0.1.0`.

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

## Signing

The certificate is on a smart card, so signing happens on the maintainer's machine. While a release runs, keep this
running there, from a clone of [GrzybDev/actions](https://github.com/GrzybDev/actions):

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
