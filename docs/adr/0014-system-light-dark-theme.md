# ADR-0014: A System / Light / Dark theme in the PWA, following the device by default

- Status: Accepted
- Date: 2026-09-25
- Deciders: Pavel Pavlov

## Context

The PWA shipped with MudBlazor's light palette only: a bare `<MudThemeProvider/>` in `MainLayout`, no dark palette
and no way to switch. Installed PWAs are expected to follow the device's light or dark setting, and some workshop
machines may run dark. Others may want the opposite of the OS setting for reading cut lists.

Some constraints shaped the decision:

- `ATAFurniture.Server` is maintained, not developed, and the two apps share no UI
  ([ADR-0005](0005-copy-conversion-ui-into-pwa.md)).
- The device settings in `localStorage` (`kroiko.deviceSettings`, [ADR-0002](0002-pwa-updates-reload-prompt.md) §7–8)
  are saved only by a successful generation, and they feed `ConverterState`. A theme must be saved the moment it is
  picked.
- `MudThemeProvider` can only apply a palette once Blazor has rendered. Before that, the browser shows
  `index.html`'s loading screen while the .NET runtime downloads. On a dark device, that would be a white screen
  and then a light layout for a frame before the app turned dark.
- `MudThemeProvider.ObserveSystemDarkModeChange` defaults to `true`. It then sets `IsDarkMode` whenever the device
  changes, which would override a manual pick.

## Decision

1. **Three modes, `System` by default.** We will give the PWA only (not the Server) a `ThemeMode` of
   **System**, **Light** or **Dark**. `System` follows `prefers-color-scheme` and follows device changes live. The
   app bar gets a „Тема“ icon button, beside „Относно“, whose icon shows the current mode. It opens a menu of
   „Системна“, „Светла“ and „Тъмна“ (`menuitemradio` items), and the current item is ticked.
2. **Its own storage key.** We will keep the mode in `localStorage` under **`kroiko.theme`**, as a plain string:
   `system`, `light` or `dark`. It is not part of the device-settings document, so that document keeps its schema
   and its save-on-generation rule. `LocalStorageThemeStore` saves a pick at once. Loading never throws: a missing
   or unknown value, or storage that throws, loads `System`. A save that fails is logged, and the pick still applies
   until the next reload.
3. **The theme is known before anything paints.** An inline script in `index.html`'s `<head>` reads `kroiko.theme`
   by the same rules and falls back to `matchMedia('(prefers-color-scheme: dark)')`. It then sets
   `<html data-theme="light|dark">`. `app.css` styles the page background, `color-scheme` and the loading screen's
   progress circle from that attribute. `MainLayout` loads the mode and resolves it before it renders
   `MudThemeProvider` and the layout, so the first render is already in the right palette. From then on it keeps
   `data-theme` in step with the palette on screen. `ObserveSystemDarkModeChange` is on only while the mode is
   `System`. The error bar (`#blazor-error-ui`) stays light-only.
4. **MudBlazor's default palettes.** One `MudTheme` (`Theme/AppTheme`) holds MudBlazor's default `PaletteLight` and
   `PaletteDark`. Light mode looks exactly as before. Rebranding the palettes is a separate decision.
5. **The manifest is unchanged.** `theme_color` stays `#03173d` and `background_color` stays `#ffffff`. A manifest
   cannot change them at runtime, and the navy title bar suits both palettes.

## Consequences

- ✅ A dark-device operator never sees a white loading screen or a light first frame, and a manual pick survives
  reloads and device changes.
- ✅ The device-settings document, its schema and its migrations are untouched.
- ⚠️ The storage rules are written twice: in `LocalStorageThemeStore` and in `index.html`'s script. A new mode or
  stored value must change both. `E2E/ThemeTests` checks that they agree on a reload.
- ⚠️ The loading screen's dark colours in `app.css` repeat MudBlazor's default dark background (`#32333d`) and
  primary (`#776be7`). A palette change must update them too.
- ⚠️ The installed app's title bar stays navy and the Android launch splash stays white in dark mode.
- This is a new difference from the Server, which stays light only (CONTEXT.md §9).

## Alternatives considered

- **Follow the OS only, or a manual Light/Dark toggle only:** the first gives no override, and the second loses
  "follow the device", which is the default an installed app is expected to have.
- **A `theme` field in `kroiko.deviceSettings`:** it needs a `schemaVersion` 1→2 migration and a save outside
  generation, and it mixes a UI preference into the conversion's settings.
- **A cycling button, or a setting on the Configuration page:** a cycling button hides what the next click does,
  and the Configuration page holds conversion setup.
- **Accepting the start-up flash, or a CSS-only `@media (prefers-color-scheme)` loading screen:** the flash happens
  on every launch, and CSS alone ignores a manual pick.
- **A custom Kroiko palette now:** it would also rebrand light mode. That is deferred to its own decision.
- **`<meta name="theme-color">` per scheme, or set from JS:** a media-query meta tag ignores a manual pick, and
  support in installed desktop PWAs is uneven.

## Related

- [ADR-0002](0002-pwa-updates-reload-prompt.md) §7–8: the device settings in `localStorage`, which this key stays
  out of.
- [ADR-0005](0005-copy-conversion-ui-into-pwa.md): no UI shared with the Server.
- Implemented by: [08 — Theme](../implementation/08-theme.md)
