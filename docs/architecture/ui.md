# UI

The window is an Avalonia 12 app with compiled bindings, restyled to the site's dark design. It shows only what the
agent's state carries and never touches the file system, processes or the network on its UI thread.

- **Screens** are view models registered explicitly (`PageRegistry`, `ViewRegistry`), so an app can replace any of
  them. Navigation uses typed routes with a back stack.
- **Images** come through one cache that loads only from the site's image hosts and decodes off the UI thread.
- **Animations** run on the compositor, loop only while the window is visible, and follow the system's reduced-motion
  setting.
- **Dialogs and notifications** are drawn inside the window.
- **Accessibility**: every control has a screen-reader name, everything works from the keyboard, and the minimum
  window (960x600) keeps every main action visible.

## Package creator screens

The [package creator](package-creator.md) has a library of projects and the page of one open project (`Pages/Creator`).

- The library page (`CreatorPageViewModel`) shows a card per project and owns the open project, so a build goes on while
  another page shows, and only this page answers the window's close.
- `NewProjectDialogViewModel` starts a project from a folder or adds a version to one, recognising the game off the UI
  thread.
- The project's page (`ProjectPageViewModel`) shows one card for one version and part, or a table with a column per
  version and a row per part, whose cells open a panel. The bar under the tabs says what holds the build back.
- `CreatorSession` holds a cell per part and version. Cells compare one at a time, an add-on after its base, and every
  cell is planned again on the thread pool after each change.
- There is no file watcher: a project is compared again when its page shows or the window comes to the front and the
  files differ from what the comparison saw.

## Texts

Every text lives in resources: English base texts and translations in `Localization`, and an app's own words in the
app. The build merges the app's language into one resource set. Plurals use ICU MessageFormat; numbers, sizes and dates
follow the chosen language. [Translating the apps](../translating.md) explains how to add a language.

Code: `src/JustModIt.Manager.Ui/`, `src/JustModIt.Manager.Localization/`.
