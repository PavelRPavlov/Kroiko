# Phase 4 — Implementation plan: Hosting & deployment

> **Status:** Not started
> **ADR:** [ADR-0005](../adr/0005-api-serves-static-client.md) · **Prerequisite:** Phase 3
> **Est. effort:** S–M
> Index: [00-overview.md](00-overview.md) · Context: [../../CONTEXT.md](../../CONTEXT.md)

## Objective
Have `ATAFurniture.Api` serve the WASM client's static files so the whole app ships as
**one deployable unit**; update CI/CD; retire the old Server app.

## Scope
- **In:** host wiring, deploy config, migration strategy, single CI/CD pipeline, cleanup.
- **Out:** feature work.

## Work breakdown

### P4-T1 — Reference the client from the API
- **Files:** `ATAFurniture.Api.csproj`
- **Do:** add a `ProjectReference` to `ATAFurniture.Client` so `dotnet publish` bundles
  the client's static assets into the Api output.
- **Verify:** `dotnet publish ATAFurniture.Api` emits the client's `_framework/` under `wwwroot`.
- **Depends on:** —

### P4-T2 — Wire the host pipeline
- **Files:** Api `Program.cs`
- **Do:** after auth/routing:
  ```csharp
  app.UseBlazorFrameworkFiles();
  app.UseStaticFiles();
  // ... /api endpoints ...
  app.MapFallbackToFile("index.html");
  ```
  ensure `/api/*` is matched **before** the SPA fallback; carry over `UseHttpsRedirection`
  + HSTS and the sign-out rewrite if still needed.
- **Verify:** `/` serves the client; `/api/user` still routes to the API (not `index.html`).
- **Depends on:** P4-T1

### P4-T3 — Migration strategy
- **Files:** Api startup / deploy scripts
- **Do:** the old app auto-migrated only in Development — choose an explicit production
  path (migrate-on-deploy step, or `dotnet ef database update` in the pipeline). Don't
  silently auto-migrate prod.
- **Verify:** a clean environment reaches the current schema deterministically.
- **Depends on:** P4-T1

### P4-T4 — Deploy config (secrets on the host)
- **Files:** App Service settings / Key Vault
- **Do:** configure SQL, SendinBlue, (optional) Blob, Sentry DSN, Azure AD settings on the
  host. **Nothing secret in the client.** One artifact = the Api (carrying the client).
- **Verify:** deployed app starts; `GET /api/user` works after B2C sign-in.
- **Depends on:** P4-T2

### P4-T5 — Single CI/CD pipeline
- **Files:** `.github/workflows/*`
- **Do:** one workflow: build → **`dotnet test`** (new stage) → publish `ATAFurniture.Api`
  → deploy. Confirm the dead functions workflow is gone (Phase 0) and `cleanup.yml` is
  fixed; use `setup-dotnet@v4`, `upload/download-artifact@v4`, target `10.0.x`.
- **Verify:** pipeline green end-to-end; tests run.
- **Depends on:** P4-T2

### P4-T6 — Retire the old app + docs
- **Files:** `TextConverter.sln`, `ATAFurniture.Server/`, `README.md`
- **Do:** once deployed parity is confirmed, delete `ATAFurniture.Server` from the
  solution; update `README.md` (it still references the dead UWP `template.xml` + old
  workflow) to describe Client + Api + `Kroiko.Domain` JSON templates. Final sweep: no
  `Syncfusion`/`Radzen`/UWP/Cosmos references anywhere.
- **Verify:** solution builds without the old project; README accurate.
- **Depends on:** P4-T4, P4-T5

## Sequencing
T1 → T2 → (T3, T4, T5 in parallel) → T6 (last, after deployed parity). Do **not** delete
the old Server app until a deployed environment passes the full flow.

## Testing & verification
Deployed smoke test: fresh browser → client loads → B2C sign-in → `/api/user` returns
credits → deep-link `/converter` (SPA fallback serves it) → full upload→generate→download→
email flow for all three companies.

## Rollback
Keep the previous deployment slot until the new single-unit deploy passes verification
(use App Service deployment slots / swap). Revert the swap to roll back instantly.

## Definition of done
- [ ] One deployable unit: the Api serves the client and the `/api` endpoints.
- [ ] Deployed app runs the full flow; secrets only on the host/Api.
- [ ] Single green CI/CD pipeline with a test stage on .NET 10.
- [ ] Old Server/UWP projects removed; README updated; no legacy references remain.

## Phase-specific risks
| Risk | Mitigation |
|---|---|
| SPA fallback swallows `/api` routes | Order endpoints before `MapFallbackToFile`; test in P4-T2. |
| Prod migration mishap | Explicit migrate step (P4-T3), not silent auto-migrate; back up first. |
| Deleting Server app too early | Gate T6 on deployed parity + slot rollback available. |

## After migration — recommended follow-ups
Backfill parser/credit/golden-file tests; consolidate the three company implementations
(generic single-sheet builder + data-driven row provider); make adding a company
open/closed (a `CompanyModule`); resolve the Suliver/Kuklensko `Name` collision.
