# Translating the apps

All text the apps show is in resource files, so translating them needs no code.

- `src/JustModIt.Manager.Localization/Resources/Strings.resx`: the English texts shared by both apps.
- `Strings.<language>.resx` next to it: a translation, for example `Strings.pl.resx`.
- `src/<App>/Resources/`: an app's own words, such as Menadżer Spolszczeń's "spolszczenie".

## Adding a language

1. Copy `Strings.resx` to `Strings.<language>.resx`, using a tag such as `de` or `pt-BR`.
2. Translate the values. Leave the keys and `{placeholders}` as they are. The first entries set number, size and date
   formats, so translate those too.
3. Set `ManagerLanguage` in the app's `Product.props` (or pass `-p:ManagerLanguage=de`) and build.
4. Run the tests. They check every message in every language.

Untranslated texts fall back to English. Don't name a site or an app in the shared texts: each app adds its own.

## Plurals and choices

Messages that depend on a number use [ICU MessageFormat](https://unicode-org.github.io/icu/userguide/format_parse/messages/):

```text
{count, plural, one {# game} other {# games}}
{count, plural, one {# gra} few {# gry} many {# gier} other {# gry}}
```

Use the plural categories of your language. `other` is always required, and `#` is the number. In a `select`, keep the
branch names and translate only the text inside the braces.

To check the result, run the UI tests with `UI_RENDERS` set to a folder and look at the screenshots for text that
doesn't fit.
