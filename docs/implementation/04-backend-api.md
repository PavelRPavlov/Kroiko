# Phase 2 — Implementation plan: Carve out the Minimal API backend

> **Status:** Not started
> **ADR:** [ADR-0003](../adr/0003-dedicated-minimal-api-backend.md) · **Prerequisite:** Phase 1
> **Est. effort:** M (auth + credit redesign are the substance)
> Index: [00-overview.md](00-overview.md) · Context: [../../CONTEXT.md](../../CONTEXT.md)

## Objective
Create `ATAFurniture.Api` (ASP.NET Core Minimal API) and move all DB/secret/integration
logic out of the UI behind authenticated HTTP endpoints. Fix the credit bugs here.

## Scope
- **In:** new API + Contracts projects; move EF/repo/migrations/email; endpoints; atomic
  credit model; token validation; secret hygiene.
- **Out:** UI changes, the WASM client (Phase 3), hosting the client (Phase 4).

## Work breakdown

### P2-T1 — Scaffold `ATAFurniture.Api` + `ATAFurniture.Contracts`
- **Files:** new projects; add both to `TextConverter.sln`
- **Do:** `dotnet new web -o ATAFurniture.Api` (Minimal API, net10); `dotnet new classlib
  -o ATAFurniture.Contracts`. Api references Contracts + `Kroiko.Domain`.
- **Verify:** solution builds with both projects.
- **Depends on:** —

### P2-T2 — Define DTOs in Contracts
- **Files:** `ATAFurniture.Contracts/*`
- **Do:** `UserDto`, `CreditsDto`, `ContactInfoDto`, `SendOrderEmailRequest` (contact +
  attachments), `ConsumeCreditResult`. Keep them separate from `Kroiko.Domain` entities.
- **Verify:** compiles; referenced by Api (and later the client).
- **Depends on:** P2-T1

### P2-T3 — Move data access into the API
- **Files:** move `DataAccess/KroikoDataContext.cs`, `KroikoDataRepository.cs`,
  `IKroikoDataRepository.cs`, and `Migrations/` from `ATAFurniture.Server` → `ATAFurniture.Api`
- **Do:** register EF in the Api `Program.cs` (`AddDbContext<KroikoDataContext>(… UseSqlServer …)`).
  **Fix-while-here:** rename async methods with the `Async` suffix; make `GetUserAsync`
  return `User?`.
- **Verify:** Api builds; migrations apply against a dev DB.
- **Depends on:** P2-T1

### P2-T4 — Extract `IEmailService`
- **Files:** new `ATAFurniture.Api/Services/IEmailService.cs` (+ impl)
- **Do:** move the SendinBlue logic out of `OrderHandlingComponent.razor.cs`
  (`SendEmailUsingSendInBlue`) into the service; bind `EmailSettings` from config.
- **Verify:** a unit/integration call sends a test email.
- **Depends on:** P2-T1

### P2-T5 — Authentication (token validation)
- **Files:** Api `Program.cs`, `appsettings.json` (AzureAd section, non-secret)
- **Do:**
  ```csharp
  builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
      .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAdB2C"));
  ```
  add an authorization policy requiring the exposed API scope; `app.UseAuthentication(); app.UseAuthorization();`.
- **Verify:** a protected test endpoint returns 401 without a token, 200 with one (Spike C token).
- **Depends on:** P2-T1

### P2-T6 — Endpoints
- **Files:** Api `Program.cs` (or `Endpoints/*`)
- **Do:** all `.RequireAuthorization()`:
  - `GET /api/user` — resolve/create user from the `oid` claim; **guard null `oid`**; return `UserDto`.
  - `POST /api/company/last-selected` — persist selection.
  - `POST /api/credits/consume` — atomic (P2-T7).
  - `POST /api/email/send` — send via `IEmailService` + consume a credit atomically.
  ```csharp
  app.MapGet("/api/user", async (ClaimsPrincipal u, IKroikoDataRepository repo) => {
      var oid = u.FindFirst("oid")?.Value;
      if (string.IsNullOrEmpty(oid)) return Results.Unauthorized();
      var user = await repo.GetUserAsync(oid) ?? await repo.CreateUserAsync(oid, 10);
      return Results.Ok(user.ToDto());
  }).RequireAuthorization();
  ```
- **Verify:** each endpoint returns the right DTO/status with a valid token.
- **Depends on:** P2-T3, P2-T5

### P2-T7 — Credit redesign *(fixes 4 Known issues)*
- **Files:** the consume endpoint; `User` entity + a new migration
- **Do:** atomic, guarded decrement (fixes double-decrement, fire-and-forget, lost-update, negative):
  ```csharp
  var affected = await db.Users
      .Where(x => x.AadId == oid && x.CreditsCount > 0)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.CreditsCount, x => x.CreditsCount - 1));
  return affected == 1 ? Results.Ok() : Results.Problem("No credits", statusCode: 402);
  ```
  add `byte[] RowVersion` + `IsRowVersion()` + migration; gate the free-credits action
  (`AddCredits`) behind the real payment/webhook (disable until it exists).
- **Verify:** parallel consume requests never over-spend; refuses at zero.
- **Depends on:** P2-T6

### P2-T8 — Secret hygiene
- **Files:** Api config; remove Sentry DSN from committed `appsettings.json`
- **Do:** SQL/SendinBlue/(blob) via user-secrets/env/Key Vault; **rotate + move the Sentry
  DSN**; keep Serilog + Sentry on the Api. Update [../../CONTEXT.md](../../CONTEXT.md) §7.
- **Verify:** no secret in any committed file; app reads them from config.
- **Depends on:** P2-T1

### P2-T9 — (Decide) Azure Blob
- **Do:** with client-side generation (ADR-0004), the browser holds the bytes — **drop
  Blob** unless persistent shareable links are needed. If kept, add `POST /api/files`.
  Record the decision in CONTEXT.md / a note in ADR-0003.
- **Verify:** decision recorded; unused Blob code removed if dropped.
- **Depends on:** —

## Sequencing
T1 → T2 → (T3, T4, T5 in parallel) → T6 → T7; T8, T9 alongside. Commit per task group.

## Testing & verification
Add the project's **first automated tests** here: the atomic credit endpoint (over-spend
+ zero-balance), and `GetUserAsync` null handling. Manually verify endpoints with a REST
client + a real B2C token.

## Rollback
The Server app still runs during this phase (endpoints not yet consumed). Revert per
commit; nothing user-facing changes until Phase 3.

## Definition of done
- [ ] All DB/secret/integration logic lives in `ATAFurniture.Api`; none in the UI.
- [ ] Endpoints authenticated, returning `ATAFurniture.Contracts` DTOs.
- [ ] Credit consume is atomic, guarded, non-negative; `RowVersion` added; free-credit hole gated.
- [ ] Sentry DSN rotated + out of source; no secrets outside the Api.
- [ ] First tests green.

## Phase-specific risks
| Risk | Mitigation |
|---|---|
| B2C API-scope / token audience misconfig | Validate with Spike C before wiring endpoints. |
| EF `ExecuteUpdateAsync` provider support | Supported on SQL Server EF 10; covered by the credit test. |
| Client-initiated consume trust | Endpoint is authenticated + atomic + balance-checked (server-enforced). |
