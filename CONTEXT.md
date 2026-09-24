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
Browser ──SignalR circuit──► ATAFurniture.Server (Blazor Server, .NET 8)
                                 ├─ Razer UI (Radzen + Syncfusion components)
                                 ├─ DetailsExtractorService  (adapter over the domain's PolyboardParser)
                                 ├─ Kroiko.Domain            (map + group + template build + LargeXlsx, behind IOrderFormat)
                                 ├─ EF Core 9 ──► SQL Server  (users + credits)
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
app.kroiko.com (Azure Static Web Apps, static files only)  ── first load + updates ──►  Browser
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
([ADR-0007](docs/adr/0007-parity-and-test-strategy.md)). Decisions: [ADR-0001–0009](docs/adr/README.md).
Build order: [docs/implementation/00-overview.md](docs/implementation/00-overview.md).

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

## 8. Cross-cutting invariants (do not break)

- **Culture:** all numeric parse/format must use `CultureInfo.InvariantCulture`.
  Hosts and browsers may be `bg-BG` (comma decimal) — ambient culture corrupts both
  the Polyboard parse and the `.cut_mt` output.
- **`.cut_mt` line endings are CRLF** on every host ([ADR-0009](docs/adr/0009-cut-mt-line-endings-crlf.md)) —
  never `AppendLine` / `Environment.NewLine` in the `.cut_mt`.
- **`Kroiko.Domain` must stay browser-safe** ([ADR-0004](docs/adr/0004-shared-browser-safe-conversion-domain.md)) — no server-only APIs
  (no direct EF, no `System.Net` server calls, no file-system assumptions).
- **Secrets never ship to the browser.** The PWA has none; DB/email/blob credentials live only in the Server.
- **The PWA makes no cross-origin requests** and must start fully offline: every asset (incl. the
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
| A file with bad lines is rejected and the first 10 bad lines are listed (Server: generic alert, nothing loaded) | [0006](docs/adr/0006-known-conversion-bugs-in-pwa.md) §2 | `ConverterState` unit test + Playwright "bad lines" |
| MegaTrading orders with more than 6 materials cannot be generated (Server: `.cut_mt` header truncated) | [0006](docs/adr/0006-known-conversion-bugs-in-pwa.md) §3 | `Check` domain test + `ConverterState` unit test |
| Both contact fields must be filled before generating; last values and manufacturer remembered per device | [0005](docs/adr/0005-copy-conversion-ui-into-pwa.md) §6 | `ConverterState` unit test + Playwright "device storage" |
| Any input edit clears the generated files (Server: stale output kept) | [0005](docs/adr/0005-copy-conversion-ui-into-pwa.md) §7 | `ConverterState` unit test |
| Switching manufacturer or re-uploading asks before discarding files (Server: silent rebuild) | [0005](docs/adr/0005-copy-conversion-ui-into-pwa.md) §5 | `ConverterState` unit test |
| Order persists across in-app navigation (Server: lost when leaving the Converter page) | [0005](docs/adr/0005-copy-conversion-ui-into-pwa.md) §4 | `ConverterState` unit test |
| Files are saved to a picked folder or downloaded; clashes get ` (n)`; names pass through `FileNameSanitizer` (Server: Blob Storage links) | [0003](docs/adr/0003-save-order-files-to-picked-folder.md) | `FileNameSanitizer` domain test; picker manual |
| Busy spinners always clear; errors show a snackbar (Server: generate spinner can hang) | [0006](docs/adr/0006-known-conversion-bugs-in-pwa.md) §4 | `ConverterState` unit test |
