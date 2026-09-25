# 04 — Conversion flow

- **Status:** Done
- **Depends on:** [02 Shared domain](02-shared-domain.md), [03 Client shell](03-client-shell.md)  **Can run alongside:** —
- **ADRs:** [0005](../adr/0005-copy-conversion-ui-into-pwa.md), [0006](../adr/0006-known-conversion-bugs-in-pwa.md) §2–4, [0003](../adr/0003-save-order-files-to-picked-folder.md) §1, §5, §7–8, [0002](../adr/0002-pwa-updates-reload-prompt.md) §7–8, [0007](../adr/0007-parity-and-test-strategy.md) §4–5

## Goal

An operator can do a whole Order in the PWA: upload, pick a manufacturer, edit, fill the contacts,
"Генерирай бланки за поръчка", and get the files with **"Изтегли всички"**. The Playwright suite
proves that the **trimmed** build produces the golden files under `bg-BG` and `en-US`, and offline.
This is the first point where the PWA is usable end to end. Phase 05 adds the folder picker and
per-file links; phase 06 adds updates.

## Steps

### 1. `ConverterState` and its unit tests

A plain C# service in `Kroiko.Client.Blazor` (no Razor), registered once for the app (ADR-0005 §4) by
`AddConverterState()` — scoped, which is once per app in WebAssembly and lets it use MudBlazor's scoped dialogs.
`Program.cs` calls it in the step that registers the last of its two interfaces (steps 2–3): the WASM host
validates the container in Development, so it cannot be registered before them.
It holds the Order: parse result, manufacturer, `KroikoFile`s, contact info, different-edge-colour
text, the current `Check` problems, the generated files and a **saved** flag. It raises one
`Changed` event.

- It depends on two small interfaces that the tests fake (ADR-0007 §4): a **confirmation** (asks
  a yes/no question, returns `Task<bool>`) and **device settings** (loads/saves contact info and the
  last manufacturer).
- Rules, each with an xUnit test in `Kroiko.Client.Tests` (no browser, no `Category=E2E`):
  - Upload: parse errors → nothing is loaded, and the Order keeps what it had. The message lists the
    first 10 errors as `ред N: …` (Bulgarian, per `ParseErrorKind`), then `…и още N` (ADR-0006 §2).
    A file with no Details and no errors is rejected as empty.
  - Switching manufacturer or uploading again **when files exist** asks first. "No" changes nothing
    (the UI reverts the picker); with no files there is no question (ADR-0005 §5).
  - **Any** input edit (a cell, a material rename, a file name, a contact field, the
    different-edge-colour text) clears the generated files and the saved flag, without asking (ADR-0005 §7).
  - `CanGenerate` is false while either contact field is empty/whitespace or `Check` reports any
    problem. `Check` runs on every change (ADR-0005 §6, ADR-0006 §3).
  - Successful generation stores the contact info and the manufacturer through device settings.
  - `HasUnsavedWork` is true when a file is loaded and its current input has not been generated and
    saved since — so also before the first generation and after an edit (ADR-0002 §2, ADR-0005 §7).
    The **saved** flag is set by "at least one download triggered" (ADR-0003 §8; phase 05 adds
    "folder save succeeded").
  - Busy flags clear in `finally`. An exception in parsing or generation surfaces as an error
    message for a snackbar and leaves the Order as it was (ADR-0006 §4).

### 2. Device settings in browser storage

- An implementation of the device-settings interface over `localStorage` (JS interop), holding
  contact info and the last manufacturer **with a `schemaVersion`** (ADR-0002 §7). Start at `1`.
- Forward migrations run on load before anything is read. A **newer** `schemaVersion` than the app
  knows → use defaults in memory and leave storage untouched until the operator edits a setting
  (ADR-0002 §8). Storage that is missing or throws → defaults, never a crash.
- Unit tests for the migration and newer-schema rules (the JSON handling is pure C#; only the
  `localStorage` call is interop).

### 3. Upload panel and manufacturer picker

Copy `FileUploadComponent` and `TargetCompanySelectionComponent` from the Server and strip the
credits, user and error text about credits (ADR-0005 §1). Both components bind to `ConverterState`.
The upload alert shows `ConverterState`'s bad-line message and keeps the link to `/configuration`.
Confirmations use a `MudDialog` implementation of the confirmation interface, in Bulgarian.

### 4. The three tabs

- Copy `LoniraTabItemContent`, `SuliverTabItemContent` and `MegaTradingTabItemContent`. Each
  `MudDataGrid` binds **directly** to the `LoniraDetail` / `SuliverDetail` / `MegaTradingDetail`
  instances from `CreateFiles`. There is no `MegaTradingViewModel` (ADR-0005 §3).
- Editable fields exactly as on the Server (ADR-0005 §3). The MegaTrading material rename is a
  tab-local "old → new name" row that rewrites `Material` on the matching details. Every committed
  edit notifies `ConverterState`.
- The per-tab contact fields are **not** copied (see step 5). A MegaTrading `TooManyMaterials`
  problem shows the offending materials and points to the rename row.

### 5. Contacts, generation and "Изтегли всички"

- One **"Контакти на клиента"** section above the tabs, pre-filled from device settings, with both
  fields required (ADR-0005 §6).
- **"Генерирай бланки за поръчка"** is disabled while `CanGenerate` is false. It calls `Generate`
  and lists the generated files.
- **"Изтегли всички"** downloads every file one after another: `Blob` + `<a download>`, streamed
  through `DotNetStreamReference`. Each name first passes through `FileNameSanitizer` (ADR-0003
  §5, §7). Triggering it sets the saved flag. Keep the download JS in one module, `wwwroot/js/files.js`;
  phase 05 extends it.
- Errors show a Bulgarian `MudSnackbar`; spinners always clear.

### 6. E2E parity scenarios

Add to `Kroiko.Client.Tests` (ADR-0007 §5):

- **Lonira**, **Suliver**, **MegaTrading**: upload a fixture → fill the contacts with the same values
  as the golden helper → generate → "Изтегли всички" → collect the downloads →
  `OrderFilesAssert.MatchGolden`. Each runs with the browser locale **`bg-BG` and `en-US`**. Between
  them they cover both field formats; MegaTrading uses a fixture with ≤ 6 materials.
- **Bad lines:** the >10-bad-lines fixture shows the `ред N: …` alert with `…и още N` and loads nothing.
- **Device storage:** generate once, reload; the contacts and the manufacturer are pre-filled.
- **Offline** (extends phase 03's scenario): after the offline reload, a Lonira conversion matches the golden files.

## Done criteria

- [x] `ConverterState` holds the Order app-wide; every rule in step 1 has a unit test.
- [x] Device settings carry `schemaVersion`, with forward migrations and newer-schema fallback, and are unit-tested.
- [x] Upload, picker, tabs and the contact section are copied and stripped from the Server; no auth, credits, Blob Storage or email code remains.
- [x] Grids edit the domain details directly; `MegaTradingViewModel` is not in the client.
- [x] "Генерирай бланки за поръчка" is disabled on empty contacts or `Check` problems.
- [x] "Изтегли всички" downloads sanitised file names and sets the saved flag.
- [x] The three manufacturer E2E scenarios match the golden files under both `bg-BG` and `en-US`.
- [x] The bad-lines, device-storage and offline-conversion E2E scenarios pass.
- [x] Every row of [CONTEXT.md §9](../../CONTEXT.md) that this phase implements names its actual test.

## Out of this phase

- "Запази в папка…", per-file download links, ` (n)` clash naming → [05](05-saving.md).
- The reload snackbar that reads `HasUnsavedWork` → [06](06-updates-and-about.md).
