# CONTEXT

> Domain model, architecture, and decision log for the **ATATextConverter** solution.
> Read this before making changes. For *how to work* in this repo (build, test,
> conventions, guardrails) see [AGENTS.md](AGENTS.md). For the in-flight migration
> see [docs/implementation/00-overview.md](docs/implementation/00-overview.md), and the
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
| **Old vs latest format** | Polyboard lines come in two shapes: **11 fields** (legacy) or **23 fields** (current). Field count selects the parser. | `DetailsExtractorService.cs` |
| **Parse result** | What parsing a Polyboard file yields: the Details plus a list of **Parse errors** (line number + reason). The parser reports bad lines; the caller decides what to do with them (the PWA rejects the file and lists them; the Server discards it). | [ADR-0004](docs/adr/0004-shared-browser-safe-conversion-domain.md), [ADR-0006](docs/adr/0006-known-conversion-bugs-in-pwa.md) |
| **KroikoFile** | A logical output unit — a filename plus its details. Lonira produces one KroikoFile per material; Suliver/MegaTrading produce one. | `Kroiko.Domain/TemplateBuilding/KroikoFile.cs` |
| **SupportedCompany** | A target manufacturer (name, translated label). The domain knows three: Lonira, Suliver, MegaTrading. Order emails and Suliver's second branch are Server-only. | `Kroiko.Domain/CellsExtracting/SupportedCompanies.cs` |
| **Order format** | Everything one manufacturer needs to turn Details into order files: map + group Details into KroikoFiles, then generate the files. One per SupportedCompany; the only way the apps reach TemplateBuilders, TableRowProviders and FileNameProviders. | `IOrderFormat` — [ADR-0004](docs/adr/0004-shared-browser-safe-conversion-domain.md) |
| **Order problem** | A reason an Order format refuses to generate, found by `IOrderFormat.Check` before generating — today only MegaTrading's "more than 6 materials". The PWA blocks generation while any exist; the Server does not check. | [ADR-0006](docs/adr/0006-known-conversion-bugs-in-pwa.md) |
| **Order** | One conversion in progress: a parsed Polyboard file, the chosen SupportedCompany, its editable KroikoFiles, the Contact info and, once generated, the order files. Changing any of these inputs discards the generated files. Not the same as the Server's `User`/credit records. | [ADR-0005](docs/adr/0005-copy-conversion-ui-into-pwa.md) |
| **Contact info** | The end customer's company name and phone number, written into the order files. Both must be filled before generating; the PWA remembers the last values used per device. _Avoid_: account info, profile. | [ADR-0005](docs/adr/0005-copy-conversion-ui-into-pwa.md) |
| **Sheet / Cell** | In-memory spreadsheet model. A Cell has an Excel-style name ("A1"), a value, and alignment. | `Kroiko.Domain/TemplateBuilding/` |
| **TemplateBuilder** | Per-company: fills a JSON template sheet with static info + detail rows. | `*/TemplateBuilding/*TemplateBuilder.cs` |
| **TableRowProvider** | Per-company: maps a detail into a row of Cells (explicit column list; reflection is being removed). | `Kroiko.Domain/TemplateBuilding/*/` |
| **FileNameProvider** | Per-company: names the produced file. | `Kroiko.Domain/TemplateBuilding/*/` |
| **Credit** | Unit of metered usage. Consumed on download / successful email. Stored on the `User` row. | `UserContextService`, `KroikoDataRepository` |
| **FALC** | A special panel operation that adjusts a detail's height/width. | `Models/SuliverExtensions.cs` |

## 3. The three target manufacturers

| Company | Output | Notable rules |
|---|---|---|
| **Lonira** | One `.xlsx` **per material** (details grouped by material). | Reads `{MaterialName}` template flag; per-file sheet. |
| **Suliver** (two branches: main + Kuklensko Shosе) | One `.xlsx`. | Both branches currently share `Name = "Suliver"` — they differ only by order email. ⚠️ see Known issues. |
| **MegaTrading** | One `.xlsx` **and** one `.cut_mt` text file. | `.cut_mt` uses a special separator (`╪`) and always emits **exactly 6 material rows**; supports bulk material rename. The PWA refuses orders with more than 6 materials ([ADR-0006](docs/adr/0006-known-conversion-bugs-in-pwa.md)). |

## 4. Current architecture (as of migration start)

**Blazor Server** monolith (`ATAFurniture.Server`) that does everything in-process:

```
Browser ──SignalR circuit──► ATAFurniture.Server (Blazor Server, .NET 8)
                                 ├─ Razer UI (Radzen + Syncfusion components)
                                 ├─ DetailsExtractorService  (parse Polyboard text)
                                 ├─ Kroiko.Domain            (template build + LargeXlsx)
                                 ├─ EF Core 9 ──► SQL Server  (users + credits)
                                 ├─ Azure AD B2C auth (Microsoft.Identity.Web, cookie/OIDC)
                                 ├─ Azure Blob Storage (download links)
                                 └─ SendinBlue/Brevo (email with attachments)
```

- **Projects:** `ATAFurniture.Server` (web), `Kroiko.Domain` (class lib), `ATAFurniture.Server.Tests` (xUnit — generation smoke tests over checked-in fixtures; golden tests move to a new `Kroiko.Domain.Tests` per [ADR-0004](docs/adr/0004-shared-browser-safe-conversion-domain.md), with shared test data in `Kroiko.Testing` and PWA tests in `Kroiko.Client.Tests` per [ADR-0007](docs/adr/0007-parity-and-test-strategy.md)), `UWPTextConverter` (**dead legacy**, to be deleted).
- **Auth:** Azure AD B2C; per-page `[Authorize]` (global filter is commented out). Claims read in `UserContextService`.
- **Secrets:** SQL conn string, Azure Storage conn string, SendinBlue API key — all from user-secrets/env (not committed). Sentry DSN **is** committed (should be rotated/moved). *(The Syncfusion license key is gone — [ADR-0006](docs/adr/0006-replace-syncfusion-radzen-with-mudblazor.md) replaced Syncfusion + Radzen with MudBlazor, one fewer secret.)*
- **Observability:** Serilog (console + rolling file) + Sentry.

## 5. Target architecture (what we are migrating to)

A **UI-only Blazor WebAssembly client** + an **ASP.NET Core Minimal API** that owns
all backend logic *and* serves the client's static files. File parsing and Excel
generation run **in the browser**.

```
Browser ── static files ──►  ATAFurniture.Api (Minimal API, .NET 10)  ◄── the only hosted server
   │  (Blazor WASM client)      ├─ serves the WASM client (UseBlazorFrameworkFiles + MapFallbackToFile)
   │   • MudBlazor UI           ├─ EF Core 10 ──► SQL Server (users + credits, atomic)
   │   • Kroiko.Domain in-browser│─ Azure AD token validation (AddMicrosoftIdentityWebApi)
   │     (parse + LargeXlsx)     ├─ Email endpoint (SendinBlue)  ◄── holds ALL secrets
   │   • MSAL auth (bearer)      └─ (optional) Blob endpoint
   └── HTTPS /api ──────────────►
```

**Target projects:** `ATAFurniture.Client` (WASM, UI only), `ATAFurniture.Api`
(minimal API + host), `Kroiko.Domain` (shared, **must stay browser-safe**),
`ATAFurniture.Contracts` (shared request/response DTOs), `ATAFurniture.Tests`.

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
  → Fixed by the atomic server-side credit endpoint (see [docs/implementation/04-backend-api.md](docs/implementation/04-backend-api.md)).
- 🔴 **Null-deref crash.** Missing template builder / file-name provider is logged as
  a warning then dereferenced. `OrderHandlingComponent.razor.cs` → `FileGeneratorService.cs`.
  → Removed by `IOrderFormat` ([ADR-0004](docs/adr/0004-shared-browser-safe-conversion-domain.md)).
- 🔴 **Free-credits abuse hole.** "Add credits" button grants 10 credits with no
  payment. `UserCreditsComponent.razor`.
- 🟠 **No optimistic concurrency** on `User` → lost updates under concurrent use.
- 🟠 **Silent whole-file discard** when one Polyboard line has a bad field count.
  `DetailsExtractorService.cs`. Also splits on CRLF only (LF-only files fail) and
  chokes on a UTF-8 BOM.
  → [ADR-0006](docs/adr/0006-known-conversion-bugs-in-pwa.md): the domain parser accepts any line ending
  and a BOM (both apps); the PWA rejects bad files listing the bad lines; the Server keeps the discard.
- 🟠 **MegaTrading `.cut_mt`:** materials beyond 6 are silently dropped; doubles are
  formatted with ambient culture (comma decimal on `bg-BG` corrupts the file — pin to
  `InvariantCulture`). `MegaTradingFileGenerator.cs`.
  → Culture fixed by ADR-0004; the PWA blocks MegaTrading orders with more than 6 materials
  via `IOrderFormat.Check` ([ADR-0006](docs/adr/0006-known-conversion-bugs-in-pwa.md)); the Server still truncates.
- 🟠 **Fire-and-forget** credit consume; **spinners hang** on error paths;
  **`ConverterContext` never disposed** (event-handler leak).
  → PWA rule: busy flags clear in `finally`, errors show a snackbar ([ADR-0006](docs/adr/0006-known-conversion-bugs-in-pwa.md)).
- 🟡 **Dead code:** `INotifyPropertyChanged` plumbing on immutable records/entities;
  empty test project; dead UWP project; stale `<Compile Remove Cosmos…>` entries.
- 🟡 **`SupportedCompanies` key collision:** Suliver / SuliverKuklensko share `Name`.
  → Gone from the domain (three manufacturers, ADR-0004); Kuklensko stays Server-only.
- 🟠 **Browser-safety gaps in `Kroiko.Domain`.**
  `TemplateBuilderBase.ReadTemplateAsync` uses `File.ReadAllTextAsync` (no filesystem in WASM);
  its `JsonSerializer.Deserialize` and all three `TableRowProvider`s' `Type.GetProperty`
  reflection are trim-fragile (IL2026/IL2070). LargeXlsx generation itself is WASM/trim-clean.
  → Decided in [ADR-0004](docs/adr/0004-shared-browser-safe-conversion-domain.md): embedded
  templates + source-generated JSON, explicit column mappings, trim analyzers as errors (not yet implemented).

## 8. Cross-cutting invariants (do not break)

- **Culture:** all numeric parse/format must use `CultureInfo.InvariantCulture`.
  Hosts and browsers may be `bg-BG` (comma decimal) — ambient culture corrupts both
  the Polyboard parse and the `.cut_mt` output.
- **`Kroiko.Domain` must stay browser-safe** ([ADR-0004](docs/adr/0004-shared-browser-safe-conversion-domain.md)) — no server-only APIs
  (no direct EF, no `System.Net` server calls, no file-system assumptions).
- **Secrets never ship to the browser.** DB/email/blob credentials live only in the API.
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
