# The manifest package format

Manifest packages are the package format of JustModIt Manager and its apps. The format is the same in every app; only
the extension is the app's own (`ManagerPackageExtension` in its `Product.props`): `.modit` for JustModIt Manager and
`.plmod` for Menadżer Spolszczeń. A site marks release files that need the app by their extension: the backend
derives `requires_manager` from it and never offers such files as direct downloads, so a site must list its app's
extension (Baza Spolszczeń lists `.plmod`; a site for JustModIt Manager needs `.modit`). Sites accept two
kinds of release files:

| Kind | When | Installation |
|---|---|---|
| plain `.zip` | the release only **adds or replaces** files, packed relative to the game's root folder | by hand (unpack into the game folder) or by the app |
| manifest package (`.modit`, `.plmod`) | the release **patches** game files (xdelta3), renames or deletes files, or names files its software changes while the game runs | only by the app |

The package creator writes a plain `.zip` whenever the release only copies files and a manifest package with the app's
extension otherwise, also for copies that carry a third-party tool, whose licence and source only a manifest can name.
Packages can be made by hand, but the creator is the supported way.

The app tells the kind from the content, never from the extension: a manifest package unpacked as a plain archive
would spill its manifest and payload folders into the game folder. An archive is a manifest package when its root
holds `manifest.json` and either every other entry lies in `files/` or `patches/`, or the manifest declares a
`formatVersion`. Payload folders without a manifest are refused as a damaged package. Everything else is a plain
`.zip`, including one that ships a game's own `manifest.json`.

## Archive

A manifest package is a standard ZIP archive (`Deflate` or `Store` entries):

```
manifest.json                         the package and its operations (required)
files/<game path>                     whole files that operations copy
patches/<game path>.xdelta            xdelta3 patches (VCDIFF, RFC 3284)
```

The names `manifest.json`, `files/` and `patches/` are case-sensitive. The creator names payloads after the game path
they belong to; when one package writes the same path twice, the second payload gets `.2`, then `.3`, and so on. The
app only requires that `copy` sources lie in `files/` and `patch` payloads in `patches/`, and that the entries exist.
Payload entries no operation uses are allowed.

## Validation

The app checks the whole archive before it looks at the game. It refuses:

- an entry name or operation path that is absolute, contains `..` or `:` (a drive or an NTFS stream), starts with `/`
  or `\`, has an empty segment or a `.` segment;
- a name Windows cannot hold, on every OS: characters `< > : " / \ | ? *` or control characters, a trailing dot or
  space, a device name as the part before the first dot (`CON`, `PRN`, `AUX`, `NUL`, `COM0`-`COM9`, `LPT0`-`LPT9` and
  the superscript `COM¹`-`LPT³`), a name longer than 255 UTF-16 units or 255 UTF-8 bytes, a path longer than 1,024
  characters;
- symbolic links (Unix mode `0xA000` in the entry's external attributes);
- in a manifest package, any entry outside `manifest.json`, `files/` and `patches/`;
- two entries whose names differ only in letter case (Windows would give them one file);
- more than 100,000 entries, more than 64 GiB unpacked in total, or a manifest over 16 MiB;
- an entry that cannot be opened (an unsupported compression method, encryption or a broken local header);
- an entry that inflates past the size it declares, or ends before it (a zip bomb, or damage), which is checked while
  it is read;
- in a manifest package, a `copy` whose entry size differs from its `size`, a hash that is not 64 hex digits, a
  negative size, a missing payload, a target without a game id, or a manifest without operations; a plain `.zip`
  without files;
- a third-party notice without a name, a licence or an http(s) source address, with a name or licence over 200
  characters or an address over 2,048, with a path the rules above refuse, or more than 32 notices;
- `runtimeFiles` or `runtimeFolders` that are not a list, or a list that is not empty while `requires` lacks its
  capability (an app that ignored it would install the package without ever undoing what its software changes);
- a declared file whose path the rules above refuse, or with a segment that is an 8.3 short name (`PROGRA~1`,
  `LIST~1.DAT`), since a file the software has not created yet keeps the spelling it is given; a null entry; more than
  64 declared files; `originals` that are empty, hold more than 8 entries, a null entry, a negative size or a hash that
  is not 64 hex digits; a file declared twice in any letter case; a declared file that an operation patches, since a
  patch needs the store's content, which the software replaces.

Paths inside the game are resolved once more at install time: a path that a junction or symbolic link inside the game
folder would lead outside it, or into the app's own hidden folder at the game root, is refused before any change.

## `manifest.json`

UTF-8 JSON, optionally with a byte order mark. Properties are camelCase; unknown properties are ignored, and an
operation's `type` may appear anywhere in its object.

```json
{
  "formatVersion": 1,
  "id": "6f0c9f8e-3b1a-4a55-9d0e-2f5b1c7a9e10",
  "title": "Example project",
  "version": "1.2",
  "authors": ["Example Team"],
  "createdAt": "2026-09-22T18:00:00+00:00",
  "generator": "JustModIt Manager 0.1.0",
  "targets": [
    { "store": "steam", "id": "1000", "buildId": "1234567" },
    { "store": "gog", "id": "1000000001" }
  ],
  "requires": ["op:copy", "op:patch", "op:rename", "op:delete"],
  "thirdPartyNotices": [
    { "name": "Example Loader", "license": "MIT", "sourceUrl": "https://example.com/loader", "paths": ["dinput8.dll"] }
  ],
  "runtimeFiles": [
    { "path": "data/list.idx", "originals": [ { "size": 18230, "sha256": "…" } ] },
    { "path": "data/list_original.idx" }
  ],
  "operations": [
    { "type": "copy", "path": "data/lang/text.txt", "source": "files/data/lang/text.txt",
      "size": 18230, "sha256": "…" },
    { "type": "patch", "path": "data/strings.pak", "patch": "patches/data/strings.pak.xdelta",
      "sourceSize": 734003200, "sourceSha256": "…", "targetSize": 734105600, "targetSha256": "…" },
    { "type": "rename", "from": "bin/library.dll", "to": "bin/library_original.dll" },
    { "type": "delete", "path": "movies/intro.bik" }
  ]
}
```

| Property | Required | Meaning |
|---|---|---|
| `formatVersion` | yes | `1`. Checked before anything else: another version means the package was made for another version of the app, and the app says so instead of calling it damaged. |
| `id` | yes | A GUID, new for every build. Kept in the install record. |
| `title`, `version` | yes | Shown only when the site does not know the package. |
| `authors` | no | Strings. |
| `createdAt`, `generator` | no | When and with what the package was built. |
| `targets` | no | The games the package was made for: `store` (a store id such as `steam` or `gog`), `id` (the store's game id, always a string, since GOG ids exceed 2^53) and optionally `buildId`, the build the package was tested with. A package that names games in the game's own store, but not this game's id, is refused, since it was made for another game. A package that names the game only in other stores, or names another build of it, installs after a warning: it was made with another version of the game, and patches check every file they change. A package without targets installs on any game. |
| `requires` | no | Capabilities the package needs, see below. |
| `thirdPartyNotices` | no | Third-party components the package carries, see below. |
| `runtimeFiles` | no; needs `runtime:files@1` when not empty | Game files that software the package carries rewrites or creates while the game runs, at most 64, see below. Each has a `path` and optionally `originals`. |
| `runtimeFiles[].originals` | no | 1 to 8 entries of `{size, sha256}`: the content the store ships at the path, in any build or store version the package supports. The creator writes the one in the author's game. |
| `operations` | yes | Run in order; each one sees the result of the ones before it. |

All paths are relative to the game's root folder and use `/`. On Windows letter case does not matter.

### Capabilities

`requires` lists what the app must support to install the package: an operation (`op:copy`, `op:patch`, `op:rename`,
`op:delete`) and, in later versions, archive handlers such as `handler:unity-bundle@1` for repacking game archives.
The app checks the list, and the operation types, before it reads the rest of the manifest. A capability it lacks,
listed or implied by an operation type it does not know, means the package needs a newer version of the app: the
player is told to update rather than that the package is damaged. A manifest without `requires` needs the
capabilities of the operations it uses; the creator fills the list with those.

`runtime:files@1` stands for `runtimeFiles`. Unlike notices, declarations need a capability: an older app that ignored
them would install the package and never undo what its software changes, so it must say "update the app" instead
(0.1.0-alpha.1 does). A list that is not empty without its capability in `requires` is a damaged package.
`runtime:folders@1` is reserved for `runtimeFolders`, folders the software fills with files whose names cannot be known
in advance; no version of the app supports it yet, so a package that needs it asks for a newer app.

### Third-party notices

`thirdPartyNotices` names what the package carries from others, such as a mod loader, so players can see it: each
entry has the component's `name`, its `license` (a name or an SPDX expression), `sourceUrl` (an http or https address
of its source code) and optionally `paths`, the game paths of its files. The app keeps the notices with the install.
They need no capability: an app that does not know the property ignores it and installs the package as usual. The
creator writes a notice for every tool its hosting rules found, with the licence and source the author gave and without
paths, since a tool's folder also holds the author's own plugins.

### Files the package's software changes

Software a package carries, such as a proxy library or a loader, may rewrite game files or create new ones while the
game runs. The app's checks would take that for damage, and an uninstall would leave it behind, so the package names
those files in `runtimeFiles`. A declared file may also be the path of a `copy`, either side of a `rename` or a
`delete`, but never of a `patch`.

- **Install.** Each declared file the game has is copied, never moved, into the install's backups before any change,
  read so that the software can still write it; a file that changes while it is read stops the install before any
  change. A declared file the game lacks is recorded as absent, and the folders above it are created by the install.
- **Checks.** Verification never opens, hashes or even looks at a declared file, so the software's work never asks for
  a repair.
- **Uninstall.** Each declared path goes back to its state before the install. A file the game did not have is
  removed, always. Files the game had come back from their copies, all together, when the game is still the version
  their originals were taken from (its build, its languages and, for Steam, the manifests of its installed depots; a
  version unknown on both sides counts as the same) and every copy is sound. Otherwise they stay as they are and their
  copies are kept for the player, who is told why; a link or a folder that appeared at a declared path is never
  written through.
- **Uncertain originals.** An original that matches one of the declaration's `originals` is proven. Otherwise one
  taken over from an earlier install keeps that install's certainty, and on the very build a target names, an
  original unlike `originals` was changed already, by the software or a hand install. An uncertain original is put
  back like any other, and the player is advised to have the store check the files.
- **Replacements.** An update, a repair or another project taking an install's place hands every file the new package
  declares over as it is, with its state from before the old install, so the software keeps its state across the
  replacement and the original still comes back in the end. A declared file the old install never listed arrives
  uncertain. A replacement whose new package writes a file the old uninstall would keep is refused before anything is
  removed, and the release put back after a failed replacement takes over what its uninstall kept, as if it had never
  gone. An original taken before the app recorded the game's version never counts as fitting the game.
- **Kept files.** While a kept file still holds what the software left, an install that declares or writes it is
  refused, until the store's check of the files or the removal of the kept copies resolves it; content that matches
  the package's `originals` resolves it as well.

### Operations

| `type` | Fields | What it does |
|---|---|---|
| `copy` | `path`, `source`, `size`, `sha256` | Writes the payload to `path`, creating folders. An existing file is replaced. The written bytes must hash to `sha256`. |
| `patch` | `path`, `patch`, `sourceSize`, `sourceSha256`, `targetSize`, `targetSha256` | Applies an xdelta3 patch to the file at `path`, which may be the result of an earlier operation. The file must be exactly (`sourceSize`, `sourceSha256`), or the install is refused before any change, noting when the file already has the target content. The result must be exactly (`targetSize`, `targetSha256`). |
| `rename` | `from`, `to` | Moves the file `from` to `to`, replacing `to` and creating its folders. A rename that only changes letter case does nothing on Windows. Only files can be renamed. |
| `delete` | `path` | Deletes the file if it exists. A folder at `path` stops the install. |

A plain `.zip` installs as one `copy` per file entry, in archive order, without hashes; folder entries are checked but
never create empty folders.

## Patches

The app reads patches of the official `xdelta3` in every variant: without secondary compression, DJW, FGK and LZMA
(the 3.2.0 default), with or without an application header. The creator writes DJW, which old `xdelta3.exe` builds
bundled with patching tools also read, keeps Adler-32 window checksums on and writes no application header, so no file
paths of the author leak into the package. Patches are stored in the archive without compression, since DJW already
compresses them.

For patches made by hand:

- **Set the source window to at least the file size** (`-B`). With the default 64 MiB window xdelta3 does not find data
  that moved more than about 32 MiB, and a patch of a repacked archive grows to the size of the file.
- **On Windows, `xdelta3.exe` with `-B` above 2 GiB silently makes a patch without a source.** It works, but it is as
  large as the file. Use `-B 2147483648`.
- **`xdelta3` 3.2.0 in armor mode writes full file paths into the application header.** Pass `-a` to leave it out.

Decoding reads the source in blocks rather than mapping it, stops before the output grows past `targetSize`, and
checks the final size and SHA-256, since a patch truncated exactly at a window boundary decodes without an error.

## Releases of several packages

A release on the site is a base, which every player gets, and optional add-ons with packages of their own. The app
installs the base's package and the add-ons' packages as one install:

- Each package is validated on its own, as above; its kind comes from its content and its `requires` is checked for
  it alone.
- The packages install in order, the base first, then the add-ons in the order the release lists them, as one
  journaled operation with one record and one uninstall. A later package sees the changes of the earlier ones and wins
  on the same path; an original is backed up once, before the first package touches it.
- An add-on that does not fit the game (a patch or rename source that differs or is missing, a file another project's
  install owns, a target naming another game, or a `requires` this app lacks) is left out, and the base and the other
  add-ons install without it. A problem of the base, or of the game, refuses the whole install.
- Files an add-on's software changes while the game runs are declared in that add-on's manifest.

The authoring rule: the base is what every player gets. An add-on adds files or replaces whole files; it renames,
patches or deletes only game files the base leaves alone, since the package creator compares with the untouched game.
Two versions where neither contains the other become a base of their common files plus a group of add-ons. The
package creator tries an add-on together with the base it goes with, the base installing first, so a clash between the
two shows before players meet it.

## What the app guarantees

- **Check first, change later.** Before the first change the app replays every operation over a model of the game
  folder and reports every problem at once: missing or different patch sources, path conflicts, links leading out of
  the game, locked files with the programs holding them, folders it may not write to, free space on each drive, the
  game running or its store busy, files another installed project owns, a package made for another game, and
  interrupted operations waiting for recovery.
- **Everything can be undone.** Before a file is first changed, its original goes to the backup folder: moved there
  when it is on the same drive (the default is a hidden folder at the game root), otherwise copied, flushed and read
  back before the game file is replaced. A file the package's software changes is copied there before the install.
  Uninstalling puts the originals back, checked against their hashes, and removes the files and folders the package
  and its software added.
- **A crash changes nothing for good.** A journal describes each batch of changes on disk before the changes happen.
  After a crash or a power cut, the app undoes an unfinished install at its next start. Undoing never overwrites a
  file someone else wrote since.
- **Game updates win.** A file the store replaced after the install is left as it is when the project is uninstalled.
  After a version change, the files the package's software changes stay as they are too, their originals' copies are
  kept, and the player is told to have the store check the files.

Plain `.zip` packages install with the same guarantees.
