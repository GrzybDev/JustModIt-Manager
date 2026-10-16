# Package creator

The package creator ("Kreator spolszczeń" in Menadżer Spolszczeń) turns an author's folder of changed files into a
package. It runs in the window's process, because the work is heavy and short.

1. **Compare.** The author's folder, laid out like the game, is compared with the game folder. Files are hashed only
   when their sizes match.
2. **Plan.** The creator proposes an operation for each difference: copy a new file, patch a changed one, rename or
   delete. In advanced mode the author orders the steps by hand.
3. **Check.** The plan is replayed the way the installer would replay it, and checked against the site's hosting
   rules: no store or DRM libraries, no cracks, no whole game files without a reason.
4. **Build.** Only copies give a plain `.zip`; anything else gives a manifest package. Patches are encoded with
   xdelta3.

A test install of the result runs in the agent like any install, so it can be undone the same way.

Code: `src/JustModIt.Manager.Creator/`, `Ui/Pages/Creator/`.
