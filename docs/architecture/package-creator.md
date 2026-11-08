# Package creator

The package creator ("Kreator spolszczeń" in Menadżer Spolszczeń) turns an author's folders of changed files into
packages. It runs in the window's process, because the work is heavy and short.

## Projects

A project holds what its packages share: the game's versions (a copy of the game per store), its parts, the package's
description and the author's answers to the checks. A part is one package's work, the base or an add-on, with the
author's folder laid out like the game and its mode: simple, where the operations follow from the folder, or advanced,
where the author orders the steps by hand. Each part is compared and planned for each version on its own.

Projects stay in the app's data folder, in `creator\<project id>\`: `project.json` and `builds.json`, what each build
read and wrote, which tells what changed since it. Only the window's process reads them, and a project saves itself
after every change. An exported project file (`.plproj` in Menadżer Spolszczeń, `.moditproj` in JustModIt Manager) has
the same content as `project.json`, and importing one always makes a new project. Exports leave the output folder out.

A project starts from a folder. `GameRecognizer` tells the game without opening a file: it looks a sample of the
folder's paths up in every detected game and in the folder's name. The other stores' copies of that game are offered
as versions, and the site's project for the game names the add-ons and suggests the next version.

## From folders to packages

1. **Compare.** Each part's folder is compared with each version's game. Files are hashed only when their sizes match.
   A version with folders laid out differently is read through a rule detected against the first version
   (`FolderLayout`), so the comparison, the plan and the rules all work in the project's paths. A file that holds
   another version's own file is not the author's work.
2. **Plan.** The creator proposes an operation for each difference: copy a new file, patch a changed one, rename or
   delete. An add-on is planned on top of its base's plan on the same version. Planning reads no file and runs after
   every change.
3. **Check.** The plan is replayed the way the installer would replay it, and checked against the site's hosting
   rules: no store or DRM libraries, no cracks, no whole game files without a reason.
4. **Build.** `ProjectBuilder` builds every part and version, one package at a time. Versions of a part share a package
   when `PackageGroups` proves that their operations and the originals of their patches are the same; such a package
   names each of them as a target, at most 8 when its software changes game files. Only copies give a plain `.zip`;
   anything else gives a manifest package. Patches are encoded with xdelta3. A stopped run keeps the packages it
   finished.

When the site keeps a part's versions as one package, the built packages show about how much it will store
(`SiteStorage`). A test install of the result runs in the agent like any install, so it can be undone the same way; it
installs the base first, then the add-ons the author leaves on.

Code: `src/JustModIt.Manager.Creator/`, `Ui/Pages/Creator/`.
