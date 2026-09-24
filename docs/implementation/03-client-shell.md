# 03 — Client shell

- **Status:** In progress
- **Depends on:** —  **Can run alongside:** [01](01-golden-baseline.md), [02](02-shared-domain.md)
- **ADRs:** [0005](../adr/0005-copy-conversion-ui-into-pwa.md) §2, [0007](../adr/0007-parity-and-test-strategy.md) §1, §5–6; research: [.NET 10 PWA facts](../research/dotnet10-blazor-wasm-pwa.md)

## Goal

`Kroiko.Client.Blazor` is a MudBlazor app with the final navigation and no conversion yet. It
installs, starts **fully offline** (no failed requests, Roboto included) and is exercised by a
Playwright harness that publishes the trimmed Release build. Phases 04–06 add scenarios to this
harness. This phase does not touch `Kroiko.Domain`.

## Steps

### 1. Strip the template, add MudBlazor and the navigation

- Delete the template's Bootstrap (`wwwroot/lib/`), `Counter`, `Weather`, `Home`, `NavMenu`,
  `sample-data/` and their CSS.
- Add `MudBlazor`, pinned to the version `ATAFurniture.Server` uses (9.7.0 today); register
  `AddMudServices()`; add the MudBlazor providers (theme, popover, dialog, snackbar) to the layout.
- `MainLayout`: a `MudAppBar` with the Kroiko logo (copy the Server's `wwwroot/Assets` images that
  are used), links to the Converter (`/`) and to the Polyboard configuration help (`/configuration`),
  and an About icon button. No drawer (ADR-0005 §2).
- Pages: `/` is an empty Converter placeholder; `/configuration` is copied **as is** from the
  Server's `Pages/Configuration.razor`. Unknown routes redirect to `/`.
- `index.html`: `lang="bg"`, title `Kroiko`. `manifest.webmanifest`: name/short name `Kroiko`,
  Kroiko icons at 192 and 512.
- About: a `MudDialog` opened from the app bar, with static content for now (phase 06 adds the
  version and update controls).

### 2. Offline asset completeness

- **Self-host Roboto** (the weights MudBlazor uses, `.woff2`, Latin + Cyrillic subsets) under
  `wwwroot/fonts/`, with an `@font-face` stylesheet. Don't add the Google Fonts link (there must be
  no cross-origin requests at all). Roboto is SIL OFL 1.1 licensed (Roboto 3; older releases were
  Apache-2.0); commit its licence file next to the fonts. Done with weights 300/400/500/700 from the
  `@fontsource/roboto` npm package, in `fonts/roboto.css` + `fonts/OFL.txt`.
- In `service-worker.published.js`, add `/\.woff2$/` to `offlineAssetsInclude`.
- Keep ICU globalization: **no `InvariantGlobalization`**. MudBlazor's number and date display
  follows the operator's culture (ADR-0004 alternatives).
- Check `service-worker-assets.js` in a Release publish: the fonts are listed, and nothing from
  another origin is referenced.

### 3. `Kroiko.Client.Tests` and the Playwright harness

- Add **`Kroiko.Client.Tests`** (xUnit 2.9.3, FluentAssertions `[7.1.0]`, `Microsoft.Playwright`),
  referencing `Kroiko.Client.Blazor` and `Kroiko.Testing`. If phase 01 has not yet merged,
  add the `Kroiko.Testing` reference in phase 04 instead.
- **Publish fixture** (a collection fixture shared by every E2E test, ADR-0007 §5):
  `dotnet publish Kroiko.Client.Blazor -c Release` **once per test run** into a temp folder. It
  serves that `wwwroot` from an in-process Kestrel static-file host on a free `localhost` port, with
  MIME types for `.wasm`, `.dat`, `.webmanifest` and `.woff2`, precompressed `.br`/`.gz` negotiation,
  and the SPA fallback to `index.html`.
- **Browser:** the Chromium that ships with the Playwright package, headless; `HEADED=1` shows it.
  If the browser is missing, the tests **fail** with a message containing the exact
  `pwsh bin/Debug/net10.0/playwright.ps1 install chromium` command.
- Tag every browser test `[Trait("Category", "E2E")]`.
- **Scenario "offline shell":** load `/`; wait until the service worker is `activated` and the
  cache is filled; set the context offline; reload. Assert that the app bar renders,
  `/configuration` opens, **no request failed** after going offline, and
  `document.fonts.check("16px Roboto")` is `true`.

## Done criteria

- [x] No Bootstrap, template pages or `sample-data` remain; MudBlazor is the only UI library.
- [x] `/` and `/configuration` render inside a `MudAppBar` layout; unknown routes redirect to `/`; the About dialog opens.
- [x] Roboto is self-hosted, precached (`.woff2` in `offlineAssetsInclude`), and the app makes no cross-origin requests.
- [x] `InvariantGlobalization` is not set.
- [ ] `Kroiko.Client.Tests` publishes the Release build once per run and serves it from Kestrel; missing browsers fail with the install command.
- [ ] The "offline shell" E2E scenario passes: offline reload, no failed requests, Roboto available.
- [ ] `dotnet test` and `dotnet test --filter Category!=E2E` both work from the repo root.
- [ ] The app installs from Edge on `localhost` (manual, noted in the PR).

## Out of this phase

- Anything that calls `Kroiko.Domain`, and `ConverterState` → [04](04-conversion-flow.md).
- Version display and update handling → [06](06-updates-and-about.md).
- `staticwebapp.config.json` and deployment → [07](07-hosting-and-go-live.md) (07a may start once this phase is Done).
