# 08 — Theme

- **Status:** Done
- **Depends on:** [03 Client shell](03-client-shell.md)  **Can run alongside:** any other phase
- **ADRs:** [0014](../adr/0014-system-light-dark-theme.md)

## Goal

The operator picks „Системна“, „Светла“ or „Тъмна“ from the app bar. The app follows the device by default, applies
a pick at once, and starts the next visit in it, from the loading screen on.

This phase was delivered in **one PR** for all three steps, as a deliberate exception to "one step = one PR"
([00](00-overview.md)): the feature is small, and its steps are only useful together.

## Steps

### 1. Mode and storage

- `Theme/ThemeMode`: `System`, `Light`, `Dark`.
- `Theme/IThemeStore` and `Theme/LocalStorageThemeStore`, over the existing `IBrowserStorage`: `kroiko.theme` holds
  `system`, `light` or `dark` (ADR-0014 §2). Loading never throws, and anything unreadable loads `System`. A save
  that throws is logged.
- `AddTheme()` registers the store and `Theme/BrowserTheme`. `IBrowserStorage` is `TryAdd`-ed here and in
  `AddDeviceSettingsStore()`, so the app has one.
- Unit tests: `Theme/LocalStorageThemeStoreTests` (no value, round-trip, the stored strings, unknown values, storage
  throwing on load and on save) and `Theme/ThemeRegistrationTests`.

### 2. Palettes, picker and start-up

- `Theme/AppTheme`: one `MudTheme` with MudBlazor's default light and dark palettes (ADR-0014 §4).
- `Layout/ThemeMenu`: a `MudMenu` with an icon activator (`BrightnessAuto` / `LightMode` / `DarkMode`) and the
  tooltip „Тема“ on its left. Its items are `menuitemradio` with `aria-checked`, and the current one is ticked.
- `MainLayout`: loads the mode and resolves it (`BrowserTheme.PrefersDarkAsync` for `System`) before it renders
  `MudThemeProvider` and the layout. The popover, dialog and snackbar providers render from the start.
  `ObserveSystemDarkModeChange` is on only for `System`, and its `IsDarkModeChanged` updates the palette. Every
  change sets `<html data-theme>` through `BrowserTheme.ShowAsync`.
- `index.html`: an inline `<head>` script sets `data-theme` before anything paints (ADR-0014 §3). `app.css` gives
  `:root[data-theme]` its background and `color-scheme`, and gives the dark loading screen its circle colours.
- The manifest is unchanged (ADR-0014 §5).

### 3. End-to-end test

- `E2E/ThemeTests`: one test on the published app, with an emulated device scheme. „Системна“ follows a dark then a
  light device, „Тъмна“ overrides a light device, and „Светла“ overrides a dark one. A reload on the dark device
  starts in „Светла“, with `data-theme="light"` already set at `DOMContentLoaded`, before Blazor boots. After each
  step the test checks the body background, `data-theme` and the ticked menu item.

## Done criteria

- [x] The three modes switch the palette at once, and „Системна“ follows device changes live.
- [x] The pick is kept under `kroiko.theme`, and anything unreadable follows the system; unit-tested.
- [x] The loading screen and the first render are already in the stored or system theme.
- [x] `E2E/ThemeTests` passes on the published app.
- [x] CONTEXT.md describes the theme (§5) and lists it as a difference from the Server (§9).

## Out of this phase

- A Kroiko brand palette (ADR-0014 §4), and a runtime `theme-color` (§5).
- Theming `ATAFurniture.Server`.
