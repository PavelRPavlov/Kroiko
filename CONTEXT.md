# CONTEXT

> Domain model, architecture, and decision log for the **ATATextConverter** solution.
> Read this before making changes. For *how to work* in this repo (build, test,
> conventions, guardrails) see [AGENTS.md](AGENTS.md). For the in-flight migration
> see [docs/implementation/00-overview.md](docs/implementation/00-overview.md) (the PWA build plan), and the
> decisions behind it as ADRs in [docs/adr/](docs/adr/).

---

## 1. What this product is

A web application used by **ATA Furniture** to turn a **Polyboard** cut-list export
(a `;`-separated text file) into the order files that downstream furniture
manufacturers require. The operator uploads one text file, reviews/edits the parsed
detail rows, picks a target manufacturer, and gets back manufacturer-specific
Excel (`.xlsx`) and, for one manufacturer, a special text (`.cut_mt`) file — either
downloaded or emailed. Usage is metered with a per-user **credit** system.

## 2. Ubiquitous language (glossary)

| Term | Meaning | Where |
|---|---|---|
| **Detail** | One cut part (panel): height, width, quantity, material, edges, thickness, etc. Parsed from one line of the Polyboard file. | `Kroiko.Domain/CellsExtracting/Detail.cs` |
| **Old vs latest format** | Polyboard lines come in two shapes: **11 fields** (legacy) or **23 fields** (current). Field count selects the parser. | `Kroiko.Domain/CellsExtracting/PolyboardParser.cs` |
| **Parse result** | What parsing a Polyboard file yields: the Details of every good line plus one **Parse error** per bad line (line number, a `ParseErrorKind` — `FieldCount` or `InvalidNumber` — and the field count or the bad field's name). The parser reports bad lines; the caller decides what to do with them (the PWA rejects the file and lists them; the Server logs them and discards the file). | `Kroiko.Domain/CellsExtracting/ParseResult.cs`, [ADR-0004](docs/adr/0004-shared-browser-safe-conversion-domain.md), [ADR-0006](docs/adr/0006-known-conversion-bugs-in-pwa.md) |
| **KroikoFile** | A logical output unit — a filename plus its details, made by `IOrderFormat.CreateFiles`. Lonira produces one KroikoFile per material; Suliver/MegaTrading produce one. | `Kroiko.Domain/TemplateBuilding/KroikoFile.cs` |
| **SupportedCompany** | A target manufacturer (name, translated label). The domain knows three: Lonira, Suliver, MegaTrading. Order emails and Suliver's second branch are Server-only: the Server's **ManufacturerBranch** (name, label, order email) has four, Kuklensko being a branch named `Suliver`. | `Kroiko.Domain/CellsExtracting/SupportedCompanies.cs`, `ATAFurniture.Server/Models/ManufacturerBranch.cs` |
| **Order format** | Everything one manufacturer needs to turn Details into order files: map + group Details into KroikoFiles, then generate the files. One per SupportedCompany, from `OrderFormats.All` / `OrderFormats.For(company)` (the Server registers each with keyed DI); the only way the apps reach the `internal` TemplateBuilders, TableRowProviders, FileNameProviders and generators. `Generate` is synchronous. | `Kroiko.Domain/IOrderFormat.cs`, `OrderFormats.cs`, `TemplateBuilding/*/*OrderFormat.cs` — [ADR-0004](docs/adr/0004-shared-browser-safe-conversion-domain.md) |
| **Order problem** | A reason an Order format refuses to generate, found by `IOrderFormat.Check` before generating — today only MegaTrading's "more than 6 materials". The PWA blocks generation while any exist; the Server does not check. | [ADR-0006](docs/adr/0006-known-conversion-bugs-in-pwa.md) |
| **Order** | One conversion in progress: a parsed Polyboard file, the chosen SupportedCompany, its editable KroikoFiles, the Contact info and, once generated, the order files. Changing any of these inputs discards the generated files. Not the same as the Server's `User`/credit records. | [ADR-0005](docs/adr/0005-copy-conversion-ui-into-pwa.md) |
| **Contact info** | The end customer's company name and phone number, written into the order files. Both must be filled before generating; the PWA remembers the last values used per device. The Server edits a `ContactInfoModel` that also carries the user's email. _Avoid_: account info, profile. | `Kroiko.Domain/ContactInfo.cs`, [ADR-0005](docs/adr/0005-copy-conversion-ui-into-pwa.md) |
| **Sheet / Cell** | In-memory spreadsheet model. A Cell has an Excel-style name ("A1"), a value, and alignment. | `Kroiko.Domain/TemplateBuilding/` |
| **TemplateBuilder** | Per-company: fills a fresh copy of its `template.json` (embedded in `Kroiko.Domain`) with static info + detail rows. | `Kroiko.Domain/TemplateBuilding/*/*TemplateBuilder.cs` |
| **TableRowProvider** | Per-company: maps a detail into a row of Cells through an explicit column list (value selector + alignment, `TableColumn<TDetail>`), numbers in the invariant culture. | `Kroiko.Domain/TemplateBuilding/*/` |
| **FileNameProvider** | Per-company: names the produced file. | `Kroiko.Domain/TemplateBuilding/*/` |
| **Credit** | Unit of metered usage. Consumed on download / successful email. Stored on the Server's `User` row. | `UserContextService`, `KroikoDataRepository` |
| **FALC** | A special panel operation that adjusts a detail's height/width. | `Kroiko.Domain/TemplateBuilding/Suliver/SuliverOrderFormat.cs` |

## 3. The three target manufacturers

| Company | Output | Notable rules |
|---|---|---|
| **Lonira** | One `.xlsx` **per material** (details grouped by material). | Reads `{MaterialName}` template flag; per-file sheet. |
| **Suliver** (two branches: main + Kuklensko Shosе) | One `.xlsx`. | Both branches currently share `Name = "Suliver"` — they differ only by order email. ⚠️ see Known issues. |
| **MegaTrading** | One `.xlsx` **and** one `.cut_mt` text file. | `.cut_mt` uses a special separator (`╪`), CRLF line endings ([ADR-0009](docs/adr/0009-cut-mt-line-endings-crlf.md)) and always emits **exactly 6 material rows**; supports bulk material rename. The PWA refuses orders with more than 6 materials ([ADR-0006](docs/adr/0006-known-conversion-bugs-in-pwa.md)). |

## 4. Current architecture (as of migration start)

**Blazor Server** monolith (`ATAFurniture.Server`) that does everything in-process:

```
Browser ──SignalR circuit──► ATAFurniture.Server (Blazor Server, .NET 10)
                                 ├─ Razor UI (MudBlazor components)
                                 ├─ DetailsExtractorService  (adapter over the domain's PolyboardParser)
                                 ├─ Kroiko.Domain            (map + group + template build + LargeXlsx 2, behind IOrderFormat)
                                 ├─ EF Core 10 ─► SQL Server  (users + credits)
                                 ├─ Azure AD B2C auth (Microsoft.Identity.Web, cookie/OIDC)
                                 ├─ Azure Blob Storage (download links)
                                 └─ SendinBlue/Brevo (email with attachments)
```

- **Projects:** `ATAFurniture.Server` (web), `Kroiko.Domain` (class lib), `Kroiko.Testing` (class lib — shared test data: the synthetic Polyboard fixtures in `TestData/polyboard/`, the golden files in `TestData/golden/`, and `OrderFilesAssert.MatchGolden`, which compares generated order files with them or re-records them under `UPDATE_GOLDEN=1`), `ATAFurniture.Server.Tests` (xUnit — only the Server-only tests, [ADR-0007](docs/adr/0007-parity-and-test-strategy.md) §8: the keyed `IOrderFormat` registration for every manufacturer key and dropdown branch, Kuklensko → the Suliver format with its own email, a bad-line file → no Details and a log entry, and an LF-only file → its Details), `Kroiko.Domain.Tests` (xUnit — the domain's own tests: parser, `OrderFormats`, `Check`, `FileNameSanitizer`, template builders, row providers, generator culture, `OrderFilesAssert`, and `GoldenTests`, which runs every valid fixture × manufacturer through one `RunPipelineAsync` helper — `PolyboardParser.Parse` → `OrderFormats.For(m).CreateFiles` → `Generate` — plus the same theory under `bg-BG`). PWA tests go in `Kroiko.Client.Tests` per [ADR-0007](docs/adr/0007-parity-and-test-strategy.md).
- **Auth:** Azure AD B2C; per-page `[Authorize]` (global filter is commented out). Claims read in `UserContextService`.
- **Secrets:** SQL conn string, Azure Storage conn string, SendinBlue API key — all from user-secrets/env (not committed). Sentry DSN **is** committed (should be rotated/moved). *(The Syncfusion license key is gone — Syncfusion + Radzen were replaced with MudBlazor, one fewer secret.)*
- **Observability:** Serilog (console + rolling file) + Sentry.

## 5. Target architecture (the offline PWA)

A second, standalone app: **`Kroiko.Client.Blazor`**, a Blazor WebAssembly PWA with a
MudBlazor UI that runs the whole conversion **in the browser** and works offline after the
first visit. It has no backend, login, credits or email. `ATAFurniture.Server` stays
deployed and shares the conversion domain with it.

```
kroiko.com (CloudFront + S3, static files only)  ─────── first load + updates ───────►  Browser
                                                                                         │
  Kroiko.Client.Blazor (WASM, installable, service worker)                               │
   ├─ ConverterState (one app-wide Order)                                                │
   ├─ Kroiko.Domain: PolyboardParser.Parse → IOrderFormat.CreateFiles / Check / Generate │
   ├─ device storage: contact info + last manufacturer (schemaVersion)                   │
   └─ save: picked folder (showDirectoryPicker) or downloads                             │

ATAFurniture.Server (unchanged behaviour) ──► the same Kroiko.Domain
```

**Projects:** `Kroiko.Client.Blazor` (PWA), `Kroiko.Domain` (shared, **must stay
browser-safe**), `ATAFurniture.Server` (maintained), and the test projects `Kroiko.Testing`,
`Kroiko.Domain.Tests`, `Kroiko.Client.Tests`, `ATAFurniture.Server.Tests`
([ADR-0007](docs/adr/0007-parity-and-test-strategy.md)). Decisions: [ADR-0001–0013](docs/adr/README.md).
Build order: [docs/implementation/00-overview.md](docs/implementation/00-overview.md).

**`Kroiko.Client.Tests`** (xUnit, [ADR-0007](docs/adr/0007-parity-and-test-strategy.md) §5) tests the shipped
artifact: its `PublishedApp` collection fixture runs `dotnet publish Kroiko.Client.Blazor -c Release` once per
test run into a temp folder and serves that `wwwroot` from an in-process Kestrel (`StaticSiteHost`: loopback
port, Blazor MIME types, no rewriting, and every request through the committed CloudFront Function, as deployed
(ADR-0010): its SPA fallback, and the `.br` sibling with `Content-Encoding: br` only when `br` is accepted); the tests drive
the Chromium pinned by `Microsoft.Playwright`, headless unless `HEADED=1`, one browser context per test. Browser
tests are tagged `[Trait("Category", "E2E")]` and join `[Collection(E2ECollection.Name)]`; `OfflineShellTests`
proves the offline start (service worker activated → offline reload: app bar, `/configuration`, Roboto, no failed
and no cross-origin requests) and a Lonira conversion after it that matches the golden files. `ParityTests` proves parity of
the trimmed build: for each manufacturer (Lonira `wardrobes-4-materials` and MegaTrading `bathroom-4-materials`, 11 fields;
Suliver `cabinet-23-field`, 23 fields) upload → the golden helper's contacts → generate → "Изтегли всички" → the downloads match
the golden files via `OrderFilesAssert.MatchGolden`, under the browser locales `bg-BG` and `en-US`, plus Suliver with a
different edge colour; it also checks that ICU data loaded, without which .NET ignores the browser's locale. The E2E
tests compare with the golden files through `ConverterPage.MatchGolden`, which never records, even under `UPDATE_GOLDEN=1`:
only the domain tests record the Server's output. The host and the missing-browser message have their own browser-free tests.
`Hosting/` runs the CloudFront Function, `hosting/cloudfront/viewer-request.js`, in Jint (`ViewerRequestFunction`):
app routes → `/index.html`, files keep their path (so a missing one is a 404), `br/` only for a viewer that accepts
`br`, nothing but the URI changed, and `www.<domain>` → a `301` to `https://<domain><path>` (ADR-0011); and, against the
publish output, that every published and precached file keeps its path through it.
`PublishScript/` runs the real `scripts/publish-pwa.ps1` (the deploy script, phase 07) in `pwsh` against a
throwaway git repository with a bare `origin`, with `dotnet` and `aws` replaced by logging stubs on `PATH`: the
branch-model refusals, the `raw/` and `br/` trees with each object's metadata, the function update, a switch that
changes only the origin path, failures before and after the switch, the dry run, and `--profile` on every `aws` call.
`Conversion/ConverterStateTests` unit-tests the Order rules through `ConverterState`'s public API with the
confirmation and the device settings faked (`Conversion/Fakes.cs`, [ADR-0007](docs/adr/0007-parity-and-test-strategy.md) §4), on the
real domain and the shared fixtures; no browser, no `Category=E2E`.

**Deploys** all run `scripts/publish-pwa.ps1 -Environment main|production [-DryRun]`, which refuses a dirty tree, a
`HEAD` that is not `origin/main` (staging) or `origin/release` checked out as `release` with the pushed tag
`v<Version>`, no older than any `vX.Y.Z` tag on `origin` (production), and failing tests; then publishes in
Release, uploads `wwwroot` to a new S3 release folder `<environment>/<release>/` (a `raw/` and a `br/` tree, with
`Content-Type` and `Cache-Control` on every object), brings the environment's CloudFront Function up to the committed
code, switches the distribution's origin path to the folder and invalidates `/*` (ADR-0010). The IDs are in
`hosting/aws/hosting.json`; the credentials only in the AWS CLI profile `kroiko-pwa`. Production deploys itself on a
pushed `vX.Y.Z` tag: `.github/workflows/deploy-pwa.yml` checks the tag's form, checks out `release` and runs the script
on a Windows runner, writing that profile from the GitHub environment `pwa-production`'s secrets (ADR-0013); by hand,
the script deploys staging and is the fallback. [`docs/release-checklist.md`](docs/release-checklist.md)
has the release procedure (bump, merge `main` into `release`, push the tag, watch the run), the manual checks for every
production release on the installed Edge PWA, and the go-live checks (ADR-0007 §9). Each release's run of them is
recorded in a "Release vX.Y.Z sign-off" issue.

**`ConverterState`** (`Kroiko.Client.Blazor/Conversion/`) is the app's one Order ([ADR-0005](docs/adr/0005-copy-conversion-ui-into-pwa.md) §4),
registered scoped (once for a WASM app) by `AddConverterState()` in `Program.cs`. It depends on `IConfirmation` (a yes/no
question before files are discarded; `MudDialogConfirmation`, a Bulgarian "Да"/"Не" MudBlazor message box, registered by `AddConfirmationDialog()`)
and `IDeviceSettingsStore` (the last `ContactInfo` and manufacturer: loaded once per app start into whatever the
operator has not chosen yet — a device that remembers no manufacturer starts on Lonira, as the Server does — saved
after each successful generation), `IFileDownloader` (hands one file to the browser as a download: `BrowserFileDownloader`
streams it through a `DotNetStreamReference` to `wwwroot/js/files.js`, which saves it with a `Blob` and an `<a download>`, registered
by `AddFileDownloader()`) and `IFolderPicker` (`showDirectoryPicker`: `IsAvailableAsync`, and `PickAsync` → picked / cancelled (`AbortError`) /
blocked (`SecurityError`, `NotAllowedError`), the picked `IPickedFolder` listing its entry names and writing one file; `BrowserFolderPicker` over
`files.js`, which keeps the last picked folder's handle per device in IndexedDB (database `kroiko`, store `folders`, key `last`) and opens the
picker there, registered by `AddFolderPicker()`; both interop classes share one `FilesModule` import). Components read it and re-render on `Changed`;
grids edit the domain details in `Files` in place and call `NotifyInputEdited()`; an edit made while generating
drops that generation's output. `DownloadAllAsync` ("Изтегли всички") triggers the downloads one after another, each under its
`FileNameSanitizer` name ([ADR-0003](docs/adr/0003-save-order-files-to-picked-folder.md) §5, §7); the first triggered download sets the saved flag, and an
edit meanwhile stops the downloads of the files it discarded. `DownloadAsync(file)` (a file's link) triggers that one file's download under the
same name and sets the saved flag unless an edit came meanwhile; a file no longer generated is ignored. `SaveToFolderAsync` ("Запази в папка…", ADR-0003 §2–4, §6) opens the picker before
it awaits anything (the click's user activation), lists the picked folder, writes every generated file under its `ClashNaming.FinalNames` name
and then sets the saved flag and `FolderSave` (the folder's name and the final names, for the confirmation; cleared with the generated files); a
cancel does nothing, a blocked picker or a failed list/write raises `Error` pointing to "Изтегли всички", saves nothing and clears an earlier
save's confirmation, and an edit meanwhile stops the writes. Downloading (all or one file) and saving to a folder never run together (`IsSaving`). Failures of reading, the dialog, making files, generating, device storage,
downloads and folder saves are logged and raise `Error` with a Bulgarian message instead of throwing. `HasUnsavedWork` = a file is loaded and its current input
has not been generated and saved (at least one download triggered, or a folder save succeeded).
`ClashNaming.FinalNames` (pure) gives each sanitised name ` (2)`, ` (3)`, … before its extension when the folder
(ignoring case) or an earlier file of the same save already has it ([ADR-0003](docs/adr/0003-save-order-files-to-picked-folder.md) §4).

**Conversion UI** (`Kroiko.Client.Blazor/Components/`, copied from the Server and stripped, [ADR-0005](docs/adr/0005-copy-conversion-ui-into-pwa.md) §1): components
inherit `ConverterStateComponentBase` (injects `ConverterState`, re-renders on `Changed`). The Converter page (`/`) loads the device
settings on init and shows `Error` as a snackbar. `TargetCompanySelectionComponent` offers the three manufacturers and, after a "Не",
re-creates its `MudSelect` (a new `@key`) so it shows `ConverterState`'s manufacturer again. `FileUploadComponent` hands the picked file
(≤ 10 MB, as on the Server) to `UploadAsync` and lists `UploadErrors` (the last upload's only: cleared when the next one starts) in its alert, with the link to `/configuration`.
Under them, once there are files, `FileDisplayComponent` shows one tab per `KroikoFile` (a new `@key` per list of files, so new files start
on the first tab) with the manufacturer's `Lonira`/`Suliver`/`MegaTradingTabItemContent`: each `MudDataGrid` edits the domain details in place
(no `MegaTradingViewModel`), in the invariant culture whatever the browser's, and calls `NotifyInputEdited()` on each committed cell; editable as on
the Server ([ADR-0005](docs/adr/0005-copy-conversion-ui-into-pwa.md) §3) — Lonira the note and the file (material) name, Suliver the note and
"Кантиране с друг цвят" (`DifferentEdgeColor`) when a part has one, MegaTrading the edges, edge-banding material, note and the material rename,
whose tab-local "old → new name" rows go to `ConverterState.RenameMaterials` for that tab's file (each detail matched by its material before
the rename, so renames never chain and two materials can swap). The MegaTrading tab shows a `TooManyMaterials` problem with its materials and points to the rename rows. The per-tab
contact fields are not copied: once there are files, `ContactInfoComponent` ("Контакти на клиента") above the tabs edits
`CompanyName`/`MobileNumber`, both required, each keystroke an input edit; below them `OrderHandlingComponent` (the Server's, without email,
credits and Blob Storage) has "Генерирай бланки за поръчка", enabled by `CanGenerate`, a spinner while generating, and then the generated
files listed under their sanitised names, each a `MudLink` that downloads it (`DownloadAsync`; disabled while `IsSaving`), with "Изтегли всички" and, where `IFolderPicker.IsAvailableAsync` (asked on init, which also loads the
last folder ahead of the click), "Запази в папка…" as the primary (filled) action and "Изтегли всички" as the secondary (outlined) one; elsewhere
"Изтегли всички" is the primary one. A folder save's success shows a `MudAlert` listing the final names. `E2E/UploadPanelTests` covers the alert, the discard confirmation, the Lonira default and the remembered manufacturer;
`E2E/ConversionTabsTests` the three tabs, the rename and invariant numbers under `bg-BG`; `E2E/GenerationTests` the contact guard,
the `Check` guard, an edit discarding the generated files, sanitised download names, a file link downloading only its file, a Lonira order downloaded under `bg-BG` that
matches the golden files, and the contacts and manufacturer pre-filled after a reload (device storage). `Conversion/FileDownloaderRegistrationTests` and `Conversion/FolderPickerRegistrationTests` pin the `files.js` interop with a faked JS runtime.
`E2E/FolderSaveTests` replaces `showDirectoryPicker` with a stub returning an origin-private (OPFS) folder, so the rest runs for real: the
saved files match the golden files, a second save after a reload gets ` (2)` names and opens at the last folder, the pick had the click's user
activation, a cancel does nothing, a blocked picker shows the message, "Запази в папка…" is the primary action where the picker exists, and a browser without
the picker offers only the downloads, "Изтегли всички" as the primary action. Its main test
uses `PublishedApp.NewPersistentContextAsync` (a profile on disk): in Playwright's off-the-record contexts, reading a file-system handle back
from IndexedDB crashes the page (Chromium 153; [reported upstream](https://github.com/andeplane/fem-lab/issues/247) with native handles
too, so an Edge InPrivate window is a manual check). The real picker is a manual release check.

**Device settings** (`LocalStorageDeviceSettingsStore`, registered by `AddDeviceSettingsStore()`) are one JSON document
in `localStorage` under `kroiko.deviceSettings`: `{"schemaVersion":1,"companyName":…,"mobileNumber":…,"manufacturer":"Lonira"}`,
the manufacturer by `SupportedCompany.Name` (an unknown name loads as none). Loading runs the forward migrations
(oldest first, one per `schemaVersion` step; none yet) before anything is read, and never throws: a newer
`schemaVersion`, an unreadable document, or storage that is missing or throws load the defaults and leave storage
untouched until the operator edits the settings (committed by the next successful generation, which saves them) ([ADR-0002](docs/adr/0002-pwa-updates-reload-prompt.md) §7–8). Only the two
`localStorage` calls are interop (`IBrowserStorage`); `LocalStorageDeviceSettingsStoreTests` covers the rest.

**Theme** ([ADR-0014](docs/adr/0014-system-light-dark-theme.md)): a `ThemeMode` of „Системна“ (the default, following
`prefers-color-scheme` live), „Светла“ or „Тъмна“, picked from the app bar's „Тема“ menu (`Layout/ThemeMenu`).
`Theme/LocalStorageThemeStore` (registered by `AddTheme()`) keeps it in `localStorage` under `kroiko.theme` as `system`,
`light` or `dark`, apart from the device settings, and saves a pick at once. Loading never throws (anything unreadable
is „Системна“), and a failed save is logged. An inline script in `index.html`'s `<head>` reads the same key by the same
rules and sets `<html data-theme>` before anything paints, so `app.css` shows the loading screen in the right theme.
`MainLayout` resolves the mode before it renders `MudThemeProvider` and the layout, observes the device only while the
mode is „Системна“, and keeps `data-theme` in step. The palettes are MudBlazor's defaults (`Theme/AppTheme`).
`Theme/LocalStorageThemeStoreTests` covers the store; `E2E/ThemeTests` the picker, the live follow and the start-up in
the published app.

**Version** ([ADR-0002](docs/adr/0002-pwa-updates-reload-prompt.md) §5–6): the hand-maintained SemVer `<Version>` in
`Kroiko.Client.Blazor.csproj` (`0.1.0` until go-live, then `1.0.0`; bumped in the PR that goes to `release`, and read as
`X.Y.Z` by `scripts/publish-pwa.ps1`). The SDK's Source Link appends `+<commit sha>` to the assembly's informational version;
`Updates/AppVersion` formats it as `v0.1.0 (a1b2c3d)` (the first 7 characters of the sha, or just `v0.1.0` without one), and
only the About dialog shows it. `Updates/AppVersionTests` covers the formatting; `E2E/AboutDialogTests` the published app's About.

**Updates** ([ADR-0002](docs/adr/0002-pwa-updates-reload-prompt.md) §1, §3–4): `index.html` registers the service worker as the template
does and starts `wwwroot/js/updates.js` with that registration. The module reports a new version once it **waits** behind the
worker that controls the page (on load, or on `updatefound` → `installed`; a first install is not an update), and calls
`registration.update()` every 60 minutes, when the window becomes visible and on `online` — never while `navigator.onLine` is
false, and failures are swallowed. `applyUpdate()` posts `SKIP_WAITING` to the waiting worker, whose only change to the template's
`service-worker.published.js` is to answer it with `skipWaiting()`, and reloads once on `controllerchange` (with nothing waiting,
because another window applied it, it just reloads; a waiting worker replaced by a newer one hands over to that one). If the page
has not started to reload within 10 seconds, `applyUpdate()` fails, so a worker that never takes over (e.g. the development
no-op worker waiting behind a published one on the same origin) leaves the offer usable instead of its buttons disabled for good. Other open
windows are not reloaded (ADR-0002 §1: never forced): they keep their offer, and their "Презареди" reloads into the new version.
`checkNow()` answers `upToDate`, `downloading` (a newer worker is installing or already waits — About shows a waiting
one as ready) or `offline` (offline, or the check failed); it reads the registration directly, because `register()` and
`update()` queue behind an install in progress. The development `service-worker.js` stays a no-op, so the flow only
exists in a published build. `Updates/AppUpdates` (registered by `AddAppUpdates()`, started by `MainLayout`'s first render) is the
app's side: it subscribes a `DotNetObjectReference` (the module remembers a version that waited before), sets `IsUpdateReady` and
raises `UpdateReady` once, and wraps `ApplyUpdateAsync`/`CheckNowAsync` (`UpdateCheck`), logging failures instead of throwing.
`Updates/AppUpdatesTests` covers it with the JS runtime faked; `E2E/UpdatesModuleTests` the real module in the published app
(up to date; offline: silent checks and `offline`). A new version needs a second build, so the update itself is checked by hand
(ADR-0007 §5).

**Update offer** ([ADR-0002](docs/adr/0002-pwa-updates-reload-prompt.md) §1–4): `Updates/UpdateOffer` (also registered by
`AddAppUpdates()`, one for the app) is what the operator sees of a waiting version. `MainLayout` shows a persistent snackbar
(`Layout/UpdateSnackbar`: "Нова версия е налична", "Презареди", "По-късно"; no timeout, no close icon) while `ShowsSnackbar`.
"Презареди" (`ReloadAsync`) asks through `IConfirmation` first when `ConverterState.HasUnsavedWork` ("Текущата поръчка ще бъде
изгубена. Да презаредя ли с новата версия?"); "Не" keeps the Order and the offer, otherwise it calls `ApplyUpdateAsync`, and a
failure shows a Bulgarian error snackbar. "По-късно" (`Postpone`) hides the snackbar for the session; About then offers the same
"Презареди" (same guard) while `IsReady`, and otherwise has "Провери за обновления", which reports `CheckNowAsync`'s three
outcomes in Bulgarian. There is still no `beforeunload` handler: the guard is the only question. `Updates/UpdateOfferTests` covers
it on the faked module; `E2E/UpdateOfferTests` drives the snackbar and About in the published app with a stand-in `updates.js`
that announces a waiting version and records `applyUpdate`; `E2E/AboutDialogTests` checks the real module's "up to date".

## 6. Decision log

Architectural decisions are recorded as **ADRs** in [docs/adr/](docs/adr/) — that folder
is the canonical source (each ADR has full context, consequences, and alternatives).
The index lives in [docs/adr/README.md](docs/adr/README.md). The earlier ADR set (0001–0006
for the Server → WASM + API migration) was deliberately removed; numbers are reused by the
new set, so ignore old-ADR links elsewhere in this file.

## 7. Known issues / tech debt (fix opportunistically during migration)

These were found in a code review; several are naturally fixed by the migration.

- 🔴 **Credit double-decrement / overcharge.** `ConsumeSingleCredit` decrements the
  `User` reference after `RemoveCredits` already decremented+saved it; the extra
  decrement persists on the next save. `UserContextService.cs` + `KroikoDataRepository.cs`.
  → Server-only; the PWA has no credits. Not addressed by the PWA plan (the Server is maintained, not developed).
- 🔴 **Null-deref crash.** Missing template builder / file-name provider is logged as
  a warning then dereferenced. `OrderHandlingComponent.razor.cs` → `FileGeneratorService.cs`.
  → ✅ Removed by `IOrderFormat` ([ADR-0004](docs/adr/0004-shared-browser-safe-conversion-domain.md), phase 02 step 5): each format
  owns its builder and file-name provider, `FileGeneratorService` and its nullable arguments are gone, and the
  Server resolves the format by keyed DI.
- 🔴 **Free-credits abuse hole.** "Add credits" button grants 10 credits with no
  payment. `UserCreditsComponent.razor`.
- 🟠 **No optimistic concurrency** on `User` → lost updates under concurrent use.
- 🟠 **Silent whole-file discard** when one Polyboard line has a bad field count.
  `DetailsExtractorService.cs`, now an adapter that keeps the discard on purpose; the domain's `PolyboardParser`
  reports every bad line instead (phase 02 step 2). Also split on CRLF only (LF-only files failed) and
  choked on a UTF-8 BOM.
  → [ADR-0006](docs/adr/0006-known-conversion-bugs-in-pwa.md): ✅ the domain parser accepts any line ending,
  a BOM and whitespace-only lines, in both apps (phase 02 step 2); the PWA rejects bad files listing the bad
  lines (phase 04); the Server keeps the discard.
- 🟠 **MegaTrading `.cut_mt`:** materials beyond 6 are silently dropped; doubles were
  formatted with ambient culture (comma decimal on `bg-BG` corrupted the file). `MegaTradingFileGenerator.cs`.
  → ✅ Culture fixed (ADR-0004 §4, phase 02 step 4): the `.cut_mt` rows, the row providers' values and
  `ExcelFileGenerator`'s `double.TryParse` are all invariant, as are the "СДВ" notes and the file-name
  dates; the `bg-BG` golden theory runs and matches the invariant golden files.
  The PWA blocks MegaTrading orders with more than 6 materials
  via `IOrderFormat.Check` ([ADR-0006](docs/adr/0006-known-conversion-bugs-in-pwa.md)); the Server still truncates.
  ✅ `Check` exists (phase 02 step 6): MegaTrading returns `TooManyMaterials` above
  `MegaTradingFileGenerator.MaxMaterials`, the one constant that also sizes the header; the PWA's blocking is phase 04.
  Line endings were `Environment.NewLine` (LF on Linux and in WASM, CRLF on Windows, where the golden files
  were recorded). → ✅ Pinned to CRLF on every host ([ADR-0009](docs/adr/0009-cut-mt-line-endings-crlf.md));
  a Linux-hosted Server's `.cut_mt` changes from LF to CRLF.
- 🟠 **Fire-and-forget** credit consume; **spinners hang** on error paths;
  **`ConverterContext` never disposed** (event-handler leak).
  → PWA rule: busy flags clear in `finally`, errors show a snackbar ([ADR-0006](docs/adr/0006-known-conversion-bugs-in-pwa.md)).
  ✅ `ConverterState` (phase 04 step 1) clears `IsUploading`/`IsGenerating` in `finally` and raises its `Error` event
  with a Bulgarian message instead of throwing, which the Converter page shows as a snackbar; "Изтегли всички" clears `IsDownloading` the same way (phase 04 step 5).
- 🟡 **Dead code:** `INotifyPropertyChanged` plumbing on immutable records/entities;
  empty test project; dead UWP project; stale `<Compile Remove Cosmos…>` entries.
  → ✅ The domain has no INPC left (phase 02 step 1); the Server keeps it only where its UI listens.
- 🟡 **`SupportedCompanies` key collision:** Suliver / SuliverKuklensko share `Name`.
  → ✅ Gone from the domain (three manufacturers, ADR-0004; phase 02 step 1); Kuklensko is the Server's
  `ManufacturerBranches.SuliverKuklensko`, which shares the Suliver format on purpose.
- 🟠 **Browser-safety gaps in `Kroiko.Domain`.**
  ✅ Templates: the three `template.json` files are embedded resources of `Kroiko.Domain`, read with
  `GetManifestResourceStream` through the source-generated `TemplateJsonContext` (phase 02 step 3).
  ✅ Row providers: explicit per-manufacturer column lists replace `Type.GetProperty` (phase 02 step 4); a trial
  build with `IsTrimmable`/`IsAotCompatible` now reports no IL warnings. LargeXlsx generation itself is WASM/trim-clean.
  ✅ The compiler guards it (phase 02 step 7): `Kroiko.Domain` is `IsTrimmable`/`IsAotCompatible` with
  `IL2026;IL2067;IL2070;IL2075;IL3050` as errors, and `BannedSymbols.txt` (BannedApiAnalyzers, `RS0030` as an error) bans
  `System.IO.File`, `System.IO.Directory`, `Assembly.Location` and `Environment.CurrentDirectory`; no suppressions.
  → Decided in [ADR-0004](docs/adr/0004-shared-browser-safe-conversion-domain.md): embedded
  templates + source-generated JSON (done), explicit column mappings (done), trim analyzers as errors (done).
- 🟠 **Vulnerable transitive dependency:** LargeXlsx 1.12.0 pulled in SharpCompress 0.39.0, which has a moderate
  advisory (GHSA-6c8g-7p36-r338, NuGet NU1902).
  → ✅ Fixed by LargeXlsx 2.0.2 ([ADR-0008](docs/adr/0008-upgrade-largexlsx-to-2.md), phase 02 step 9): it writes the
  zip with `System.IO.Compression`, so SharpCompress is gone from both apps; the golden files did not change, and a
  trimmed publish of the domain reports no IL warnings.

## 8. Cross-cutting invariants (do not break)

- **Culture:** all numeric parse/format must use `CultureInfo.InvariantCulture`.
  Hosts and browsers may be `bg-BG` (comma decimal) — ambient culture corrupts both
  the Polyboard parse and the `.cut_mt` output.
- **`.cut_mt` line endings are CRLF** on every host ([ADR-0009](docs/adr/0009-cut-mt-line-endings-crlf.md)) —
  never `AppendLine` / `Environment.NewLine` in the `.cut_mt`.
- **`Kroiko.Domain` must stay browser-safe** ([ADR-0004](docs/adr/0004-shared-browser-safe-conversion-domain.md)) — no server-only APIs
  (no direct EF, no `System.Net` server calls, no file-system assumptions).
- **Secrets never ship to the browser.** The PWA has none; DB/email/blob credentials live only in the Server.
- **The PWA makes no cross-origin requests** and must start fully offline ([ADR-0007](docs/adr/0007-parity-and-test-strategy.md) §5–6): every asset (incl. the
  self-hosted Roboto in `wwwroot/fonts/`) is same-origin and matched by the service worker's
  `offlineAssetsInclude`. Adding an asset of a new file type means extending that list.
- **Two Polyboard formats** (11 and 23 fields) must both keep parsing.

## 9. Intended differences from the Server (PWA)

Where the PWA deliberately behaves differently from `ATAFurniture.Server`. These are **not**
regressions. Each row is pinned by a test of the PWA behaviour ([ADR-0007](docs/adr/0007-parity-and-test-strategy.md));
a new difference needs an ADR, a row here and a test in the same PR. Golden files stay the
Server's output. The manual acceptance check (`docs/release-checklist.md`) walks this table.

| Difference (PWA vs Server) | ADR | Pinned by |
|---|---|---|
| No login, user accounts, credits or email; nothing leaves the device | Map scope | — (absent features) |
| A file with bad lines is rejected and the first 10 bad lines are listed (Server: generic alert, nothing loaded) | [0006](docs/adr/0006-known-conversion-bugs-in-pwa.md) §2 | `ConverterStateTests.A_file_with_bad_lines_loads_nothing_and_lists_the_first_ten` + Playwright `UploadPanelTests.A_file_with_bad_lines_shows_the_first_ten_and_links_to_the_configuration` |
| MegaTrading orders with more than 6 materials cannot be generated (Server: `.cut_mt` header truncated) | [0006](docs/adr/0006-known-conversion-bugs-in-pwa.md) §3 | `OrderFormatCheckTests.MegaTrading_refuses_seven_materials` + `ConverterStateTests.More_than_six_MegaTrading_materials_is_a_problem_that_cannot_generate` + Playwright `ConversionTabsTests.MegaTrading_names_too_many_materials_and_the_rename_can_merge_them` |
| Both contact fields must be filled before generating; last values and manufacturer remembered per device | [0005](docs/adr/0005-copy-conversion-ui-into-pwa.md) §6 | `ConverterStateTests.An_empty_contact_field_cannot_generate`, `…Generating_remembers_the_contacts_and_the_manufacturer_on_the_device` + Playwright `GenerationTests.Generating_needs_both_contacts_and_downloading_all_saves_the_order_files`, `…After_generating_a_reload_pre_fills_the_contacts_and_the_manufacturer` |
| The MegaTrading material rename matches each part by its material before the rename, so renames never chain and two materials can swap (Server: checks each later row against the already-renamed material, so A→B then B→C renames A to C, and a swap merges both into one) | [0005](docs/adr/0005-copy-conversion-ui-into-pwa.md) §3 ("rewrites `Material` on the matching details") | `ConverterStateTests.A_rename_matches_the_names_before_it_so_two_materials_can_swap` |
| Any input edit clears the generated files (Server: stale output kept) | [0005](docs/adr/0005-copy-conversion-ui-into-pwa.md) §7 | `ConverterStateTests.Any_input_edit_clears_the_generated_files_and_the_saved_flag_without_asking` + Playwright `GenerationTests.An_edit_after_generating_discards_the_generated_files` |
| Switching manufacturer or re-uploading asks before discarding files (Server: silent rebuild) | [0005](docs/adr/0005-copy-conversion-ui-into-pwa.md) §5 | `ConverterStateTests.Switching_manufacturer_when_files_exist_asks_and_no_changes_nothing`, `…Uploading_again_when_files_exist_asks_and_no_changes_nothing` + Playwright `UploadPanelTests.Switching_manufacturer_or_uploading_again_asks_first_and_no_keeps_the_Order` |
| Order persists across in-app navigation (Server: lost when leaving the Converter page) | [0005](docs/adr/0005-copy-conversion-ui-into-pwa.md) §4 | `ConverterStateRegistrationTests.Every_page_of_the_app_gets_the_same_Order`, `ConverterStateTests.The_device_settings_are_loaded_once_so_returning_to_the_page_keeps_the_Order` |
| Files are saved to a picked folder or downloaded; clashes get ` (n)`; names pass through `FileNameSanitizer` (Server: Blob Storage links) | [0003](docs/adr/0003-save-order-files-to-picked-folder.md) | `FileNameSanitizerTests` (every case), `ClashNamingTests` (every case), `ConverterStateTests.Downloading_all_triggers_every_generated_file_in_order_under_its_sanitised_name`, `…Saving_to_a_folder_never_overwrites_and_confirms_the_numbered_names` + Playwright `GenerationTests.The_files_are_listed_and_downloaded_under_their_sanitised_names`, `FolderSaveTests` (picker stubbed); the real picker manual |
| Busy spinners always clear; errors show a snackbar (Server: generate spinner can hang) | [0006](docs/adr/0006-known-conversion-bugs-in-pwa.md) §4 | `ConverterStateTests.A_failed_generation_raises_an_error_and_leaves_the_Order_as_it_was`, `…A_file_that_cannot_be_read_raises_an_error_and_leaves_the_Order_as_it_was`, `…A_download_that_cannot_start_raises_an_error_and_is_not_saved` |
| A „Тема“ menu switches between the device's theme (the default), light and dark, remembered per device (Server: light only) | [0014](docs/adr/0014-system-light-dark-theme.md) | `LocalStorageThemeStoreTests` (every case) + Playwright `ThemeTests.Each_pick_applies_at_once_and_the_last_one_starts_the_next_visit` |
