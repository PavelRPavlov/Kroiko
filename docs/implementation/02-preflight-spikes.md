# Phase 0.5 — Implementation plan: Pre-flight spikes

> **Status:** Done (2026-07-13). **Spike A ✅** (LargeXlsx runs in WASM, incl. trimmed) — ADR-0004
> confirmed. **Spike B ✅** (MudDataGrid cell-edit write-back + MudFileUpload full read) — ADR-0006
> confirmed. **Spike C deferred** — needs a live Azure AD B2C tenant (secrets lost per Phase 0); the
> net10 MSAL/Identity.Web packages exist, so nothing is package-blocked. Scratch app discarded.
> See **Spike results** and **Findings for later phases** below.
> **Prerequisite:** Phase 0 (.NET 10) · **Est. effort:** S (throwaway/scratch branch)
> Index: [00-overview.md](00-overview.md) · Context: [../../CONTEXT.md](../../CONTEXT.md)

## Objective
Cheaply prove the assumptions the later phases depend on — above all that `LargeXlsx`
runs in WebAssembly ([ADR-0004](../adr/0004-client-side-file-generation.md)) — before
writing any migration code. All work here is throwaway (scratch branch, not merged).

## Scope
- **In:** three time-boxed spikes with explicit decision gates.
- **Out:** production code, real UI, real auth wiring.

## Work breakdown

### P05-T1 — Spike A: LargeXlsx in WebAssembly *(critical — gates ADR-0004)*
- **Do:**
  1. `dotnet new blazorwasm -o spike-wasm` (.NET 10); add `LargeXlsx`.
  2. On a button click, generate a small `.xlsx` in a `MemoryStream`; download it via JS
     interop (`URL.createObjectURL`).
  3. Add `Kroiko.Domain` as a reference; run `FileGeneratorService` with a sample detail
     set end-to-end in the browser.
  4. Publish with `<PublishTrimmed>true</PublishTrimmed>`; confirm the file still opens.
- **Decision gate:**
  - ✅ Works (incl. trimmed) → keep **ADR-0004** (client-side generation).
  - ❌ Fails → open a superseding ADR flipping to **server-side generation** (`POST
    /api/generate` in Phase 2); update [05-blazor-wasm-client.md](05-blazor-wasm-client.md)
    and [../../CONTEXT.md](../../CONTEXT.md).
- **Verify:** a generated `.xlsx` opens correctly in Excel from a trimmed WASM publish.
- **Depends on:** —

### P05-T2 — Spike B: MudBlazor grid + upload parity
- **Do:** in the same scratch app, add `MudBlazor` (net10 build) + `AddMudServices()`:
  - `MudDataGrid<T>` with `EditMode="DataGridEditMode.Cell"`, some columns
    `Editable="false"` and one `Editable="true"`, `MudDataGridPager`, ~25 rows; confirm
    edits mutate the bound items in place.
  - `MudFileUpload<IBrowserFile>` reading a `.txt` via `OpenReadStream(maxAllowedSize)`.
- **Decision gate:**
  - ✅ Acceptable → proceed with MudBlazor everywhere ([ADR-0006](../adr/0006-replace-syncfusion-radzen-with-mudblazor.md)).
  - ⚠️ Edit UX unacceptable → note required customizations in
    [03-mudblazor-migration.md](03-mudblazor-migration.md) before Phase 1.
- **Verify:** cell edits reach the underlying model; `.txt` reads without the 512 KB truncation.
- **Depends on:** —

### P05-T3 — Spike C (optional): Azure AD B2C SPA/MSAL
- **Do:** add a test app registration with a **SPA redirect URI** + an **exposed API
  scope**; in the scratch app, `AddMsalAuthentication`, acquire a token, and call a
  throwaway `[Authorize]` minimal-API endpoint validating it with `AddMicrosoftIdentityWebApi`.
- **Decision gate:** confirms the end-to-end token flow works with the tenant before
  Phases 2–3 depend on it.
- **Verify:** the protected endpoint returns 200 with a token, 401 without.
- **Depends on:** —

## Spike results (2026-07-13)

Executed on a throwaway standalone Blazor WASM app (`dotnet new blazorwasm`, net10) referencing
the real `Kroiko.Domain`, driven headless through the in-app browser; every generated file was
POSTed out and re-validated with Python `openpyxl`.

### P05-T1 — Spike A: LargeXlsx in WebAssembly — ✅ PASS (gate met)
- Ran the **real** `FileGeneratorService → ExcelFileGenerator → LargeXlsx.XlsxWriter` path in the
  browser (`RuntimeInformation.RuntimeIdentifier == "browser-wasm"`, `OperatingSystem.IsBrowser()`).
  A browser-safe in-memory `ITemplateBuilder` fed it (the concrete builders live in the Server — see
  Findings), so no `template.json` File.IO was needed.
- **Debug** and **trimmed publish** (`-c Release -p:PublishTrimmed=true`, full Emscripten relink)
  both produced a valid `.xlsx`: openpyxl re-opened it, cells (incl. **Cyrillic** `ЛПДЧ 18mm`) and
  **numbers-as-numbers** (styled/centered writes) round-tripped, zero runtime/console errors.
- Also exercised the **reflection** row provider (`MegaTradingTableRowProvider`, `Type.GetProperty`)
  under the trimmed build: all 8 columns extracted correctly — the trimmer preserved the properties.
- **Decision:** keep **ADR-0004** (client-side generation).

### P05-T2 — Spike B: MudBlazor grid + upload parity — ✅ PASS (gate met)
- MudBlazor **9.7.0** (net10) renders under WASM. `MudDataGrid` with `EditMode=Cell`: only the
  `Editable="true"` column is an input, `Editable="false"` columns stay read-only, `MudDataGridPager`
  pages 25 rows (10/page).
- **Cell edit writes back to the bound item:** editing Quantity `1 → 777` moved the model’s
  `Sum(Quantity)` `325 → 1101`. (Gotcha: MudDataGrid does **not** re-render the parent on commit — an
  outer readout needs its own change signal; irrelevant to the real edit→generate flow.)
- `MudFileUpload<IBrowserFile>` → `OpenReadStream(maxAllowedSize: 5 MB)` read a **600 KB** file whole
  (614 434 bytes, Cyrillic first line intact) — the old **512 KB** `OpenReadStream` default is gone.
- **Decision:** proceed with MudBlazor everywhere (**ADR-0006**). No blocking customizations found.

### P05-T3 — Spike C: Azure AD B2C SPA/MSAL — ⚠️ DEFERRED (not executable here)
- The gate needs a real B2C tenant + a test app registration (SPA redirect URI + exposed API scope).
  Phase 0 records the production **secrets were lost**, and no tenant is reachable in this
  environment, so the live token flow (200 with token / 401 without) can’t be exercised.
- Cheap check done: `Microsoft.Authentication.WebAssembly.Msal` **10.0.9** and
  `Microsoft.Identity.Web` **4.13.0** exist for net10 → the flow is **not package-blocked**. Run the
  live token check during Phase 3 (P3-T7) once tenant access is restored.

## Findings for later phases

- **Concrete builders are not browser-safe yet.** `LoniraTemplateBuilder`/`SuliverTemplateBuilder`/
  `MegaTradingTemplateBuilder` and `Models/*Extensions.cs` (`ToLoniraDetails()` …) live in
  **`ATAFurniture.Server`**, not `Kroiko.Domain`. Phase 3 **P3-T3** must move them (already planned);
  the spike confirms client-side generation is blocked until it happens.
- **`template.json` loading breaks in the browser.** `TemplateBuilderBase.ReadTemplateAsync` uses
  `File.ReadAllTextAsync` (a file-system assumption — violates CONTEXT §8). Phase 3 must fetch the
  template via `HttpClient` instead of File.IO, and the `JsonSerializer.Deserialize` there is
  **trim-unsafe** (IL2026) → use a `JsonSerializerContext` (source-gen) under trimming.
- **Reflection row providers are trim-fragile.** `MegaTrading`/`Suliver` `TableRowProvider` use
  `Type.GetProperty` (IL2070). They **worked** trimmed here, but only because those properties are
  referenced elsewhere; harden in **P3-T8** (source-gen mapping, `[DynamicallyAccessedMembers]`, or a
  trimmer-roots descriptor) so a future change can’t silently blank cells.

## Sequencing
T1 is the priority and unblocks the biggest risk; T2 and T3 can run in parallel. None
block each other.

## Testing & verification
Each spike's "Verify" line is its test. No production tests here.

## Rollback
Delete the scratch branch/app. Nothing reaches `main`.

## Definition of done
- [x] Spike A has a clear ✅/❌; ADR-0004 confirmed or a superseding ADR recorded. *(✅; ADR-0004 confirmed.)*
- [x] Spike B notes any grid/upload customizations needed for Phase 1. *(✅; none blocking — see Spike results.)*
- [~] (If run) Spike C confirms the B2C token flow. *(Deferred — no tenant/secrets here; packages verified, run in P3-T7.)*
- [x] Scratch code discarded. *(`spike-wasm` app + `spike/phase-0.5` branch deleted; only these doc updates remain.)*

## Phase-specific risks
| Risk | Mitigation |
|---|---|
| Trimming strips LargeXlsx members | Test the **trimmed** publish, not just debug; add `[DynamicDependency]`/trimmer roots if needed. |
| Spike drags on | Time-box each to ~half a day; a ❌ is a valid, valuable outcome. |
