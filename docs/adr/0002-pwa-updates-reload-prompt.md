# ADR-0002: Deliver PWA updates silently, with an opt-in reload prompt

- Status: Accepted
- Date: 2026-09-23
- Deciders: Pavel Pavlov

## Context

`Kroiko.Client.Blazor` is installed on operators' machines and runs offline. With no backend,
the only way a fix reaches them is through the service worker. The .NET 10 `--pwa` template's
default behaviour ([.NET 10 PWA facts](https://github.com/PavelRPavlov/Kroiko/issues/17),
[`docs/research/dotnet10-blazor-wasm-pwa.md`](../research/dotnet10-blazor-wasm-pwa.md)) is:

- On each **navigation** (in practice: each launch) the browser byte-compares `service-worker.js`
  and `service-worker-assets.js`. A new version is downloaded in the background into a new,
  SHA-256-pinned cache — all or nothing; a partial deploy fails integrity and users keep the old one.
- The new version **activates only after every app window is closed**; a reload is not enough.
  Installed users therefore get it one launch late, and a window left open all day never even
  checks.
- Offline users stay on their last good version. There is no API to stay compatible with.

Operators may keep the app open for a whole working day and may be mid-way through an order
(uploaded Polyboard file, edits on the review screen) when an update lands. The only data that
outlives a reload is per-device settings in `localStorage` (customer contact info, last-selected
manufacturer), which every new version inherits from the previous one.

## Decision

We will keep the template's silent, atomic update and add a user-controlled way to apply it
early, plus rules for version display and stored-data compatibility.

1. **Reload prompt.** When a new version has fully downloaded (the service worker is `waiting`),
   show a MudBlazor snackbar in Bulgarian — "Нова версия е налична" with **Презареди** (Reload)
   and **По-късно** (Later). Reload does `postMessage` → `skipWaiting()` → `controllerchange` →
   page reload. We never force a reload.
2. **Unsaved work guard.** If a Polyboard file is loaded and its generated output has not been
   saved yet, Reload first asks for confirmation ("the current order will be lost"). Otherwise it
   reloads immediately. What counts as "saved" follows the saving decision
   ([How generated order files are saved to the user's machine](https://github.com/PavelRPavlov/Kroiko/issues/20)).
3. **Dismissal.** "По-късно" hides the snackbar for the rest of the session. The About dialog then
   shows "a new version is ready" with a Reload button. If ignored, the next launch applies it anyway.
4. **Update checks.** In addition to the browser's launch-time check, call `registration.update()`
   every **60 minutes**, when the window becomes visible again (`visibilitychange`) and when the
   connection returns (`online`). Failures while offline are silent. The About dialog has a manual
   **"Check for updates"** button that reports: up to date / downloading an update / offline.
5. **Version display.** The version is shown **only in an About dialog** reachable from the app
   bar, formatted `v1.3.0 (a1b2c3d)`.
6. **Version source.** A hand-maintained SemVer `<Version>` in `Kroiko.Client.Blazor.csproj`,
   bumped in the PR that goes to `release`. The short commit SHA comes from the SDK's automatic
   `InformationalVersion` suffix (`+<sha>`, built-in Source Link). CI may later override it with
   `-p:Version=`.
7. **Stored-data compatibility.** Each stored setting carries a `schemaVersion`. On startup,
   forward migrations upgrade older data to the current schema before it is read.
8. **Roll forward, never back.** A bad release is fixed by reverting the change, bumping the
   version and deploying — never by redeploying an older build. The version shown in About only
   ever increases, and the fix arrives through the normal reload prompt. Defensive fallback: if
   stored data has a *newer* `schemaVersion` than the running app knows, use defaults in memory
   and leave storage untouched until the user edits that setting.

## Consequences

- ✅ A shipped fix reaches an all-day operator within about an hour, at a moment they choose, and
  "please reload" becomes a meaningful instruction.
- ✅ Update consistency is preserved: the prompt appears only after the complete new version is
  cached, so reloading never mixes old and new files.
- ✅ No work is lost silently; offline users are unaffected.
- ✅ The version in About identifies the exact build for bug reports, with no CI dependency.
- ⚠️ Custom JS in `index.html` (or a small module) plus a JS-interop callback into Blazor — about
  25–30 lines, outside the template, to be kept in sync with future template changes.
- ⚠️ The version must be bumped by hand; forgetting it leaves two builds with the same SemVer
  (still told apart by the SHA).
- ⚠️ Migration code must be written and tested for every breaking change to a stored setting.
- Follow-up: the conversion flow must expose an "unsaved work" flag (see the saving decision);
  the About dialog and snackbar need Bulgarian texts; migrations and the newer-schema fallback
  need unit tests.

## Alternatives considered

- **Silent only (template default)** — no code, but a window left open never sees a fix, and
  telling a user to reload does not help because a reload does not switch versions.
- **Forced reload when an update is ready** — fastest delivery, but can drop an in-progress order.
- **Persisting the in-progress order across the reload** — would survive updates, but means
  serialising parse/review state across versions whose shape may change; too heavy for a rare case.
- **Version always visible in the app bar/footer** — rejected in favour of a cleaner layout;
  the About dialog also hosts the update controls.
- **CI-stamped or SHA-only versions** — CI-stamped depends on deferred CI; SHA-only is unreadable
  and unordered for operators.
- **Tolerant reads without migrations** — simpler, but may make users re-enter settings after a
  breaking change; migrations were preferred so no stored data is ever lost on upgrade.
- **Rollback by redeploying an older build** — would make migrations meet newer data and the
  version go backwards; replaced by roll-forward.

## Related

- Ticket: [How installed users receive new versions](https://github.com/PavelRPavlov/Kroiko/issues/18)
- Map: [Offline installable Blazor WASM PWA (no backend)](https://github.com/PavelRPavlov/Kroiko/issues/15)
- [ADR-0001](0001-host-pwa-on-azure-static-web-apps.md) — hosting; its `no-cache` headers on the
  service-worker files keep the periodic update checks cheap and accurate.
- Research: [`docs/research/dotnet10-blazor-wasm-pwa.md`](../research/dotnet10-blazor-wasm-pwa.md)
- Sources: [Blazor PWA — background updates](https://learn.microsoft.com/en-us/aspnet/core/blazor/progressive-web-app/?view=aspnetcore-10.0#background-updates),
  [service worker lifecycle](https://web.dev/articles/service-worker-lifecycle)
