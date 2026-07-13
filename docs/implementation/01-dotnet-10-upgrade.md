# Phase 0 — Implementation plan: Upgrade to .NET 10

> **Status:** In progress — solution builds green on .NET 10, packages bumped, CI updated.
> Verified on .NET 10: parse→generate for all three companies (no-secrets tests) **and** a full
> local Development boot (SQL migrations + dev-only auth). **Real-secret smoke test (B2C sign-in +
> blob + email) still pending** (needs the lost secrets; must not auto-send order emails).
> **ADR:** [ADR-0001](../adr/0001-upgrade-to-dotnet-10.md) · **Prerequisite:** .NET 10 SDK installed
> **Est. effort:** S–M (mostly mechanical; risk is package availability)
> Index: [00-overview.md](00-overview.md) · Context: [../../CONTEXT.md](../../CONTEXT.md)

## Objective
Get the **current** Blazor Server architecture building, running, and smoke-tested on
.NET 10 with all packages updated — a green baseline before any re-architecture.

## Scope
- **In:** TFM retarget, package bumps, `global.json`, dead-code/cruft removal, CI bump.
- **Out:** any UI, WASM, MudBlazor, or API change (later phases).

## Work breakdown

### P0-T1 — Remove dead projects and repo cruft
- **Files:** `TextConverter.sln`, `UWPTextConverter/`, `Kroiko.Domain/Kroiko.Domain.csproj`, `.gitignore`
- **Do:**
  - Remove the `UWPTextConverter` project from the `.sln` and delete the folder.
  - Delete the two stale lines in `Kroiko.Domain.csproj`:
    `<Compile Remove="DataAccess\CosmosDbConfiguration.cs" />` and `…CosmosDbContext.cs`.
  - Replace the Terraform `.gitignore` with a .NET one (VisualStudio.gitignore), then
    untrack cruft: `git rm --cached` the four `*.user` files, `.idea/`, `UpgradeLog.htm`, `UpgradeLog2.htm`.
- **Verify:** `dotnet sln list` shows 3 projects; `git status` shows the cruft removed.
- **Depends on:** —

### P0-T2 — Install SDK and pin it
- **Files:** `global.json` (new, repo root)
- **Do:**
  ```json
  { "sdk": { "version": "10.0.100", "rollForward": "latestFeature" } }
  ```
- **Verify:** `dotnet --version` reports 10.x in the repo root.
- **Depends on:** —

### P0-T3 — Retarget all project TFMs
- **Files:** `ATAFurniture.Server.csproj`, `Kroiko.Domain.csproj`, `ATAFurniture.Server.Tests.csproj`
- **Do:** change `<TargetFramework>net8.0</TargetFramework>` → `net10.0` in each.
- **Verify:** `dotnet restore` succeeds (may surface package gaps → P0-T4).
- **Depends on:** P0-T1

### P0-T4 — Update NuGet packages
- **Files:** the three `.csproj`
- **Do:** bump to .NET 10-compatible versions. Risk-ranked:

  | Package | From | Risk |
  |---|---|---|
  | `Microsoft.EntityFrameworkCore*` | 9.0.1 → 10.x | ⚠️ provider/migration notes |
  | `Microsoft.Identity.Web(.UI)` | 3.6.2 → latest | ⚠️ auth surface |
  | `Syncfusion.Blazor.*` | 28.1.41 → net10 build | ⚠️⚠️ **removed in Phase 1** — if no net10 build, bring Phase 1 forward instead of chasing this |
  | `Radzen.Blazor` | → latest | removed in Phase 1 |
  | `Serilog*`, `Sentry*`, `Azure.*`, `LargeXlsx` | → latest | low |
  | `xunit`, `Microsoft.NET.Test.Sdk`, `FluentAssertions` | → latest / keep `[7.1.0]` pin | low |
- **Verify:** `dotnet restore` clean; no downgrade warnings.
- **Depends on:** P0-T3

### P0-T5 — Build and fix fallout
- **Do:** `dotnet build TextConverter.sln`; resolve API breaks, nullable, analyzer changes.
  Record (don't blind-suppress) obsolete-API warnings.
- **Verify:** build succeeds with no errors.
- **Depends on:** P0-T4

### P0-T6 — Smoke-test the real flow
- **Do:** `dotnet run --project ATAFurniture.Server` (user-secrets configured). Exercise
  **upload → parse → review/edit → generate → download → email** for Lonira, Suliver, MegaTrading.
- **Verify:** all three companies produce correct files; email sends.
- **Depends on:** P0-T5

### P0-T7 — Update CI
- **Files:** `.github/workflows/main_kroiko-test.yml`, `release_kroiko.yml`, `cleanup.yml`, delete `main_atafurniture-functions.yml`
- **Do:** `setup-dotnet@v1`→`@v4`, target `10.0.x`, drop `include-prerelease`;
  `upload/download-artifact@v3`→`@v4`; fix `cleanup.yml` `token: $`; delete the
  functions workflow (missing project).
- **Verify:** CI builds green on a push.
- **Depends on:** P0-T5

### P0-T8 — Commit the baseline
- **Do:** commit ("chore: upgrade solution to .NET 10"). This is the rollback point.
- **Depends on:** P0-T6, P0-T7

## Sequencing
T1 → T2 → T3 → T4 → T5 → (T6 ∥ T7) → T8. Commit boundaries: one for T1 (cleanup), one for T2–T5 (upgrade), one for T7 (CI).

## Testing & verification
No unit tests exist yet; verification is the manual smoke test (P0-T6). Optionally add
one parser smoke test now to prove the test project compiles on net10.

## Rollback
Isolated to `.csproj`, package versions, `global.json`, CI, and deletions. Revert the
Phase-0 commit(s) to return to the .NET 8 baseline.

## Definition of done
- [x] Solution builds on .NET 10, no errors. *(0 errors; only pre-existing nullable/style warnings — see CONTEXT.md §7.)*
- [~] Full flow works for all three companies. *(parse→generate verified on .NET 10 by no-secrets
  tests; local Development boot verified end-to-end (dev-auth + local SQL + migrations, `[Authorize]`
  page returns 200). Real B2C sign-in + blob + email still need the recovered secrets.)*
- [~] CI green on .NET 10; dead functions workflow removed; `cleanup.yml` fixed. *(Workflows updated + functions workflow deleted + `cleanup.yml` token fixed; "green on a push" not yet verified.)*
- [x] UWP project, Cosmos csproj lines, and tracked cruft gone; `.gitignore` is .NET.
- [x] Baseline committed. *(On branch `dotnet-10-upgrade`; not yet merged/smoke-tested.)*

## Deviations from the plan (recorded)
- **LargeXlsx** bumped `1.11.1 → 1.12.0` (latest within major 1), **not** `2.0.x`. A 1→2 major bump on the
  core Excel-generation library can silently change output, which the manual smoke test (P0-T6) — not runnable
  here — is what would catch. LargeXlsx-in-WASM already gets a dedicated **Phase 0.5 spike**; adopt 2.x there.
  This also leaves one residual **moderate** transitive advisory (`SharpCompress 0.39.0`, pulled by LargeXlsx),
  which LargeXlsx 2.x's newer dependency clears.
- **Radzen** bumped `5.7.10 → 11.1.3` — kept (it clears a **high**-severity `System.Linq.Dynamic.Core`
  advisory that 5.7.10 pulls in). One runtime break had to be fixed: Radzen 11 removed the `material3`
  theme, so `_Host.cshtml` now references `material.css`.
- **Syncfusion** bumped to `34.1.30` then **reverted to `28.1.41`** (the shipped/validated version). v34
  introduced runtime breaking changes that the build doesn't catch (e.g. `SfTab` requires an `ID` with
  `EnablePersistence`, and the grids were unverified). Since Syncfusion is **removed in Phase 1**, chasing
  v34 breakage is churn on soon-to-be-deleted code; 28.1.41 runs on .NET 10 via net8 compat and preserves
  behaviour. No vuln regression from the revert.

## Findings from the upgrade
- **EF Core 10 `PendingModelChangesWarning` now throws.** The `User` columns `Name`, `MobileNumber`
  and `CompanyName` are `string?` in the entity but the old migrations created them `NOT NULL`.
  EF Core 9 tolerated this drift at runtime; EF Core 10 promotes it to an error, which broke the
  Development auto-migrate on boot. **Resolution:** the old (never-deployed) migrations were dropped
  and replaced by a single fresh **`InitialCreate`** generated on EF Core 10 (no backwards-compat
  concern — confirmed with the repo owner). The local app now boots and applies it cleanly.

## Local verification harness (added)
Because the production secrets were lost, two no-secret paths now exist to verify the upgrade:
- **`ATAFurniture.Server.Tests/GenerationSmokeTests.cs`** — runs parse→generate for all three
  companies against the checked-in fixtures; asserts real `.xlsx` (ZIP) + the MegaTrading `.cut_mt`.
- **`docs/local-development.md`** — Docker SQL + Azurite and Development-only bypasses for the
  Syncfusion licence and Azure AD B2C, so the UI runs locally without cloud secrets. The bypasses
  are gated to Development and cannot affect production.

## Phase-specific risks
| Risk | Mitigation |
|---|---|
| No Syncfusion .NET 10 build yet | Don't block — pull Phase 1 (MudBlazor) forward; it deletes Syncfusion anyway. |
| EF Core 10 migration/runtime break | Review EF 10 breaking-changes; the model is a single `Users` table (low surface). |
| Identity.Web behavior change | Re-test sign-in in P0-T6 before committing. |
