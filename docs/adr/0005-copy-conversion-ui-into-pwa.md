# ADR-0005: Copy the conversion UI into the PWA, with an app-wide Order state

- Status: Accepted
- Date: 2026-09-24
- Deciders: Pavel Pavlov

## Context

`Kroiko.Client.Blazor` needs the Server's conversion flow: upload → pick manufacturer → review/edit
per manufacturer → generate → save. The Server's `.razor` components are already MudBlazor, but
most of them are tied to things the PWA drops:

| Server component | Tied to |
|---|---|
| `Converter.razor` page | `[Authorize]`, `UserContextService` |
| `FileUploadComponent` | credits (disabled at 0), "credits not consumed" error text |
| `TargetCompanySelectionComponent` | persists the choice on the `User` row |
| `FileDisplayComponent` | grouping into files, which [ADR-0004](0004-shared-browser-safe-conversion-domain.md) moves into `IOrderFormat.CreateFiles` |
| `OrderHandlingComponent` | Blob Storage download links, SendinBlue email, credit consumption |
| `Lonira/Suliver/MegaTradingTabItemContent` | only the file, `ContactInfo` and the different-edge-colour text |

Only the three tab contents (~180 lines) are clean enough to share. The Server is kept running but
no longer developed. Other forces found in the code:

- `ConverterContext` is created per page visit, so leaving the Converter page loses the order.
- Contact fields are repeated inside every tab, and the live flow has **no** "must be filled"
  guard (`MissingAccountInfo`, which had one, is referenced nowhere).
- The MegaTrading grid edits a `MegaTradingViewModel` copy and projects it back onto
  `File.Details` on every commit, although `MegaTradingDetail` is itself a mutable POCO.
- Switching manufacturer or re-uploading silently rebuilds the files, discarding every edit.
- Generated files are discarded only when the files are rebuilt; editing a cell or the contact
  info after generating leaves stale output that is then saved without the edit.

## Decision

1. **Copy and strip, never share.** The conversion UI is copied into `Kroiko.Client.Blazor` and
   stripped of auth, credits, Blob Storage and email. No Razor Class Library, now or later; the
   Server's UI stays untouched and the two copies diverge by design.
2. **Pages.** The Converter is `/`; the Polyboard configuration help is `/configuration`, copied
   as is (the upload error keeps linking to it). No Home or Signout page. About (version and
   "check for updates", [ADR-0002](0002-pwa-updates-reload-prompt.md)) is a dialog opened from a
   MudBlazor `MudAppBar` that carries the logo, the two links and the About icon — no drawer.
   Unknown routes redirect to `/`.
3. **Grids edit the domain details directly.** Each tab binds its `MudDataGrid` to the
   `LoniraDetail`/`SuliverDetail`/`MegaTradingDetail` instances returned by `CreateFiles`.
   `MegaTradingViewModel`, `MegaTradingExtensions` and the write-back are not carried over; the
   MegaTrading material rename is a tab-local row (old → new name) that rewrites `Material` on
   the matching details. Editable fields stay as on the Server: Lonira — note and file/material
   name; Suliver — note, plus "Кантиране с друг цвят" when present; MegaTrading — the four edges,
   edge-banding material, note and the material rename.
4. **One app-wide Order state.** A `ConverterState` service registered once for the app holds the
   Order: parsed details, manufacturer, `KroikoFile`s, contact info, different-edge-colour text and
   the generated files. Components read it directly and re-render on a single `Changed` event. It
   survives in-app navigation and is lost on reload; nothing but the contact info and the last
   manufacturer is persisted.
5. **Confirm before discarding edits.** When files exist, switching manufacturer or uploading
   another file first asks (Bulgarian confirm dialog); cancelling changes nothing and reverts the
   picker. With no files there is no prompt.
6. **One contact section with a guard.** A single "Контакти на клиента" section above the tabs,
   pre-filled from device storage. Both fields are required (non-whitespace only, no phone format
   check); "Генерирай бланки" is disabled until both are filled. The values are written back to
   device storage when a generation succeeds.
7. **Any input edit discards generated files.** Every committed grid cell, material rename, file
   name, contact field or different-edge-colour edit signals `ConverterState`, which clears the
   generated files; the operator generates again. This refines ADR-0003 §1's "changing the input
   discards the generated files (as today)": in the PWA the input is everything `Generate` reads.
   Clearing never prompts. For ADR-0002's unsaved-work guard, an Order with files is unsaved until
   it has been generated since its last edit and saved per ADR-0003 §8.

## Consequences

- ✅ The Server is not touched by UI work; the PWA's UI evolves freely.
- ✅ One source of truth for edits (the domain objects), so no lost-edit or stale-output class of
  bug; the MegaTrading write-back patch disappears.
- ✅ The help page no longer costs the operator their order, and a mis-click on the manufacturer
  picker no longer wipes their edits.
- ✅ Order files can no longer be generated with empty contacts.
- ⚠️ UI fixes made in one app must be made again in the other, if ever wanted there.
- ⚠️ Editing live domain objects means no undo within an Order (the Server has none either).
- ⚠️ Stricter than the Server on purpose (contact guard, stale-output clearing, discard prompt) —
  the parity checklist must treat these as intended differences, not regressions.
- Follow-up: the known-bugs ticket decides how parse errors and bad lines are shown on the upload
  panel; the parity ticket decides UI test coverage; the implementation guide lays out the
  components.

## Alternatives considered

- **Razor Class Library shared by both apps**: dedups ~180 lines at the cost of reworking the
  Server's UI and keeping MudBlazor versions in lock-step for an app that is no longer developed.
- **Copy now, extract an RCL later**: an open-ended promise that rarely happens; ruled out instead.
- **Keep `MegaTradingViewModel`**: a 13-field INPC copy protecting nothing, and the source of the
  lost-edit bug its write-back patched.
- **Page-scoped state (as today)**: loses the Order on every visit to the help page.
- **Persist the in-progress Order in browser storage**: new scope with schema/migration cost
  (ADR-0002) for a flow that takes minutes.
- **Silent rebuild on manufacturer switch (as today)**, or **lock the picker after upload**: the
  first loses edits without warning; the second blocks checking one cut list against two
  manufacturers.
- **Warn only when something was edited**: needs per-cell dirty tracking for little gain.
- **Contact fields per tab (as today)**: the same two values repeated on every tab.
- **Generate silently at save time**: removes ADR-0003's review-then-save step.

## Related

- Ticket: [Share or copy the Server's conversion UI into the PWA](https://github.com/PavelRPavlov/Kroiko/issues/22)
- Map: [Offline installable Blazor WASM PWA (no backend)](https://github.com/PavelRPavlov/Kroiko/issues/15)
- [ADR-0002](0002-pwa-updates-reload-prompt.md) — About dialog and the unsaved-work guard this state feeds.
- [ADR-0003](0003-save-order-files-to-picked-folder.md) — generate-then-save; §1 refined by decision 7.
- [ADR-0004](0004-shared-browser-safe-conversion-domain.md) — `Parse`, `CreateFiles`, `Generate` that this UI calls.
- Informs: [Fix or replicate the known conversion bugs in the PWA](https://github.com/PavelRPavlov/Kroiko/issues/23),
  [What proves parity, and how it's tested](https://github.com/PavelRPavlov/Kroiko/issues/24)
- Implemented by: [03 — Client shell](../implementation/03-client-shell.md), [04 — Conversion flow](../implementation/04-conversion-flow.md)
