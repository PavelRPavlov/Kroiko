# Phase 0.5 — Implementation plan: Pre-flight spikes

> **Status:** Not started
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

## Sequencing
T1 is the priority and unblocks the biggest risk; T2 and T3 can run in parallel. None
block each other.

## Testing & verification
Each spike's "Verify" line is its test. No production tests here.

## Rollback
Delete the scratch branch/app. Nothing reaches `main`.

## Definition of done
- [ ] Spike A has a clear ✅/❌; ADR-0004 confirmed or a superseding ADR recorded.
- [ ] Spike B notes any grid/upload customizations needed for Phase 1.
- [ ] (If run) Spike C confirms the B2C token flow.
- [ ] Scratch code discarded.

## Phase-specific risks
| Risk | Mitigation |
|---|---|
| Trimming strips LargeXlsx members | Test the **trimmed** publish, not just debug; add `[DynamicDependency]`/trimmer roots if needed. |
| Spike drags on | Time-box each to ~half a day; a ❌ is a valid, valuable outcome. |
