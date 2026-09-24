# 06 — Updates & About

- **Status:** Not started
- **Depends on:** [04 Conversion flow](04-conversion-flow.md)  **Can run alongside:** [05 Saving](05-saving.md)
- **ADRs:** [0002](../adr/0002-pwa-updates-reload-prompt.md) §1–6; research: [.NET 10 PWA facts](../research/dotnet10-blazor-wasm-pwa.md)

## Goal

An installed PWA notices a new version, downloads it silently, and offers a Bulgarian reload
snackbar. The snackbar never reloads over an unsaved Order without asking. The About dialog shows
`vX.Y.Z (sha)` and can check for updates on demand.

## Steps

### 1. Version

- Add `<Version>0.1.0</Version>` to `Kroiko.Client.Blazor.csproj`. It is bumped by hand in the PR
  that goes to `release`, and becomes `1.0.0` at go-live ([07](07-hosting-and-go-live.md) 07b).
- Read `AssemblyInformationalVersionAttribute` (the SDK appends `+<sha>` through Source Link) and
  format it as `v0.1.0 (a1b2c3d)` (the short SHA). A unit test covers the formatting, including a
  version without a `+` suffix.
- Show it in the About dialog only (ADR-0002 §5).

### 2. Service worker and update detection

- `service-worker.published.js`: on a `message` of `SKIP_WAITING`, call `self.skipWaiting()`.
  Keep the template's install/activate/fetch behaviour otherwise.
- A JS module `wwwroot/js/updates.js`, started from `index.html`'s registration:
  - watch the registration for a `waiting` worker (on load and on `updatefound` → `installed`) and
    notify .NET through a `DotNetObjectReference`;
  - call `registration.update()` every **60 minutes**, on `visibilitychange` to visible, and on
    `online`. It swallows failures while offline (ADR-0002 §4);
  - `applyUpdate()`: post `SKIP_WAITING` to the waiting worker, then reload once on `controllerchange`;
  - `checkNow()`: returns *up to date* / *downloading* / *offline* for the About dialog.
- The dev-time `service-worker.js` stays a no-op. The flow only exists in a published build.

### 3. Snackbar and About

- A new waiting worker shows a persistent `MudSnackbar`: **"Нова версия е налична"** with
  **"Презареди"** and **"По-късно"** (ADR-0002 §1).
- "Презареди": if `ConverterState.HasUnsavedWork`, ask first (Bulgarian: the current order will be
  lost). Otherwise, or after "yes", call `applyUpdate()`.
- "По-късно": hide the snackbar for this session. The About dialog then shows that a new version is
  ready, with the same "Презареди" (and the same guard) (ADR-0002 §3).
- About: the version, and a **"Провери за обновления"** button that reports the `checkNow()` result
  in Bulgarian.

## Done criteria

- [ ] About shows `vX.Y.Z (sha)` from the assembly's informational version; the formatting is unit-tested.
- [ ] A published build shows "Нова версия е налична" after a newer build is served; "Презареди" applies it; "По-късно" moves the offer into About.
- [ ] "Презареди" asks first when `HasUnsavedWork` is true.
- [ ] Update checks run hourly, on visibility and on `online`, and are silent offline; the manual check reports its three outcomes.
- [ ] The update flow was verified manually by publishing two versions locally (noted in the PR), and appears in the release checklist's per-release items ([07](07-hosting-and-go-live.md) 07b step 1).

## Out of this phase

- Automating the update flow in Playwright (rejected, ADR-0007 alternatives).
- Stamping the version from CI (`-p:Version`) — CI is outside this plan.
