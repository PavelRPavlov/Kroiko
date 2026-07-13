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
| **KroikoFile** | A logical output unit — a filename plus its details. Lonira produces one KroikoFile per material; Suliver/MegaTrading produce one. | `Kroiko.Domain/TemplateBuilding/KroikoFile.cs` |
| **SupportedCompany** | A target manufacturer (name, translated label, order email). | `Kroiko.Domain/CellsExtracting/SupportedCompanies.cs` |
| **Sheet / Cell** | In-memory spreadsheet model. A Cell has an Excel-style name ("A1"), a value, and alignment. | `Kroiko.Domain/TemplateBuilding/` |
| **TemplateBuilder** | Per-company: fills a JSON template sheet with static info + detail rows. | `*/TemplateBuilding/*TemplateBuilder.cs` |
| **TableRowProvider** | Per-company: maps a detail into a row of Cells (currently via reflection). | `Kroiko.Domain/TemplateBuilding/*/` |
| **FileNameProvider** | Per-company: names the produced file. | `Kroiko.Domain/TemplateBuilding/*/` |
| **Credit** | Unit of metered usage. Consumed on download / successful email. Stored on the `User` row. | `UserContextService`, `KroikoDataRepository` |
| **FALC** | A special panel operation that adjusts a detail's height/width. | `Models/SuliverExtensions.cs` |

## 3. The three target manufacturers

| Company | Output | Notable rules |
|---|---|---|
| **Lonira** | One `.xlsx` **per material** (details grouped by material). | Reads `{MaterialName}` template flag; per-file sheet. |
| **Suliver** (two branches: main + Kuklensko Shosе) | One `.xlsx`. | Both branches currently share `Name = "Suliver"` — they differ only by order email. ⚠️ see Known issues. |
| **MegaTrading** | One `.xlsx` **and** one `.cut_mt` text file. | `.cut_mt` uses a special separator (`╪`) and always emits **exactly 6 material rows**; supports bulk material rename. |

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

- **Projects:** `ATAFurniture.Server` (web), `Kroiko.Domain` (class lib), `ATAFurniture.Server.Tests` (xUnit — currently **empty**), `UWPTextConverter` (**dead legacy**, to be deleted).
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
Summary:

| Decision | ADR |
|---|---|
| Upgrade the solution to .NET 10 (first, on the current architecture) | [ADR-0001](docs/adr/0001-upgrade-to-dotnet-10.md) |
| Migrate the UI from Blazor Server to Blazor WebAssembly (UI only) | [ADR-0002](docs/adr/0002-blazor-server-to-webassembly.md) |
| Dedicated ASP.NET Core Minimal API backend (all DB/secret/integration logic) | [ADR-0003](docs/adr/0003-dedicated-minimal-api-backend.md) |
| Parse + generate order files in the browser (client-side) | [ADR-0004](docs/adr/0004-client-side-file-generation.md) |
| The API serves the WASM client's static files (one deployable) | [ADR-0005](docs/adr/0005-api-serves-static-client.md) |
| Replace Syncfusion + Radzen with MudBlazor | [ADR-0006](docs/adr/0006-replace-syncfusion-radzen-with-mudblazor.md) |

## 7. Known issues / tech debt (fix opportunistically during migration)

These were found in a code review; several are naturally fixed by the migration.

- 🔴 **Credit double-decrement / overcharge.** `ConsumeSingleCredit` decrements the
  `User` reference after `RemoveCredits` already decremented+saved it; the extra
  decrement persists on the next save. `UserContextService.cs` + `KroikoDataRepository.cs`.
  → Fixed by the atomic server-side credit endpoint (see [docs/implementation/04-backend-api.md](docs/implementation/04-backend-api.md)).
- 🔴 **Null-deref crash.** Missing template builder / file-name provider is logged as
  a warning then dereferenced. `OrderHandlingComponent.razor.cs` → `FileGeneratorService.cs`.
- 🔴 **Free-credits abuse hole.** "Add credits" button grants 10 credits with no
  payment. `UserCreditsComponent.razor`.
- 🟠 **No optimistic concurrency** on `User` → lost updates under concurrent use.
- 🟠 **Silent whole-file discard** when one Polyboard line has a bad field count.
  `DetailsExtractorService.cs`.
- 🟠 **MegaTrading `.cut_mt`:** materials beyond 6 are silently dropped; doubles are
  formatted with ambient culture (comma decimal on `bg-BG` corrupts the file — pin to
  `InvariantCulture`). `MegaTradingFileGenerator.cs`.
- 🟠 **Fire-and-forget** credit consume; **spinners hang** on error paths;
  **`ConverterContext` never disposed** (event-handler leak).
- 🟡 **Dead code:** `INotifyPropertyChanged` plumbing on immutable records/entities;
  empty test project; dead UWP project; stale `<Compile Remove Cosmos…>` entries.
- 🟡 **`SupportedCompanies` key collision:** Suliver / SuliverKuklensko share `Name`.
- 🟠 **Browser-safety gaps for [ADR-0004](docs/adr/0004-client-side-file-generation.md) (found in Spike 0.5).**
  `TemplateBuilderBase.ReadTemplateAsync` uses `File.ReadAllTextAsync` (no filesystem in WASM →
  fetch `template.json` via `HttpClient`); its `JsonSerializer.Deserialize` and the
  `MegaTrading`/`Suliver` `TableRowProvider` `Type.GetProperty` reflection are trim-fragile
  (IL2026/IL2070). LargeXlsx generation itself is WASM/trim-clean. → Phase 3 P3-T3/P3-T8.

## 8. Cross-cutting invariants (do not break)

- **Culture:** all numeric parse/format must use `CultureInfo.InvariantCulture`.
  Hosts and browsers may be `bg-BG` (comma decimal) — ambient culture corrupts both
  the Polyboard parse and the `.cut_mt` output.
- **`Kroiko.Domain` must stay browser-safe** once [ADR-0004](docs/adr/0004-client-side-file-generation.md) lands — no server-only APIs
  (no direct EF, no `System.Net` server calls, no file-system assumptions).
- **Secrets never ship to the browser.** DB/email/blob credentials live only in the API.
- **Two Polyboard formats** (11 and 23 fields) must both keep parsing.
