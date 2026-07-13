# Migration overview

> **Status:** Not started
> Context: [../../CONTEXT.md](../../CONTEXT.md) · Working guide: [../../AGENTS.md](../../AGENTS.md)
> Architectural decisions: [../adr/](../adr/)

This folder holds a **dedicated implementation plan per phase** — each with an ordered
task breakdown (task IDs `PN-Tn`), per-task files/actions/verification, sequencing,
rollback, and a definition of done — to accomplish three goals for the ATATextConverter
solution:

1. **Upgrade the whole solution to .NET 10.**
2. **Migrate the UI from Blazor Server to Blazor WebAssembly (WASM).**
3. **Replace the paid Syncfusion + Radzen component kits with [MudBlazor](https://mudblazor.com/) (free, MIT).**

## The one reframing that shapes everything

**Blazor WASM does not remove the server — it splits the app in two.** WASM runs
entirely in the browser, so it cannot touch SQL Server, hold secrets (SQL/Storage/
email keys), or upload blobs — all of that would be shipped to and executed in the
user's browser. That logic must move **behind a backend API**. We are therefore
trading a *stateful Blazor Server circuit* for a *stateless* API + a static client.
There is still a hosted server (the API), but it is simpler to scale and, per
[ADR-0005](../adr/0005-api-serves-static-client.md), it also serves the static client so
there is **one deployable unit**.

## Target architecture

See [../CONTEXT.md §5](../../CONTEXT.md#5-target-architecture-what-we-are-migrating-to).
In short:

- **`ATAFurniture.Client`** — Blazor WASM, **UI only**, MudBlazor components,
  references `Kroiko.Domain` to **parse + generate files in the browser**.
- **`ATAFurniture.Api`** — ASP.NET Core Minimal API: EF Core + SQL, Azure AD token
  validation, email/blob endpoints, **and** serves the client's static files.
- **`Kroiko.Domain`** — shared, **must stay browser-safe**.
- **`ATAFurniture.Contracts`** — request/response DTOs shared by client and API.

## Phase sequence

Execute in order. Each phase is independently buildable and committable; **do not
combine phases**. Each has its own doc with a done-criteria checklist.

| Phase | Doc | Goal | Depends on |
|---|---|---|---|
| 0 | [01-dotnet-10-upgrade.md](01-dotnet-10-upgrade.md) | Retarget everything to .NET 10 on the **current** architecture; green baseline. | — |
| 0.5 | [02-preflight-spikes.md](02-preflight-spikes.md) | Prove the risky assumptions (esp. LargeXlsx in WASM) before writing migration code. | Phase 0 |
| 1 | [03-mudblazor-migration.md](03-mudblazor-migration.md) | Swap Syncfusion + Radzen → MudBlazor on the **still-Server** app. | Phase 0 |
| 2 | [04-backend-api.md](04-backend-api.md) | Carve out the Minimal API; move DB/credits/email/blob; fix credit bugs. | Phase 1 |
| 3 | [05-blazor-wasm-client.md](05-blazor-wasm-client.md) | Build the WASM client; move UI + domain gen client-side; rewire auth. | Phase 2 |
| 4 | [06-hosting-and-deployment.md](06-hosting-and-deployment.md) | API serves the client; single deployable; CI/CD. | Phase 3 |

```
Phase 0 ──► Phase 0.5 (spikes)
   └──────► Phase 1 (MudBlazor) ──► Phase 2 (API) ──► Phase 3 (WASM) ──► Phase 4 (host/deploy)
```

## Why this order

- **.NET 10 first, alone** — so any breakage is unambiguously the framework upgrade,
  not the re-architecture.
- **Spikes before code** — client-side generation ([ADR-0004](../adr/0004-client-side-file-generation.md))
  rests on LargeXlsx working in WASM. Validate it cheaply; if it fails, ADR-0004 flips to
  server-side generation and Phase 3 changes materially.
- **MudBlazor before WASM** — Blazor Server has the easiest debug/hot-reload loop, so
  validate that `MudDataGrid`/`MudFileUpload` replace the Syncfusion behavior *before*
  adding WASM's constraints. The `.razor` files are **moved** to the client later, not
  rewritten, so the work carries forward (and arrives license-free + lighter).
- **API before WASM client** — the client needs endpoints to call; building the API
  first lets the client target a real contract.

## Risk register

| Risk | Impact | Mitigation | Phase |
|---|---|---|---|
| LargeXlsx doesn't run under the WASM runtime/trimming | ADR-0004 invalid → generation must move server-side | **Spike A** before any migration code | 0.5 |
| Syncfusion/MudBlazor/EF/Identity lack a .NET 10 build at upgrade time | Blocks Phase 0/1 | Check package availability first; MudBlazor removes the Syncfusion dependency entirely | 0, 1 |
| `MudDataGrid` edit UX differs from `SfGrid` | Editing feels different; edit→generate flow may need rework | Validate on Server app (Phase 1) with a manual click-through | 1 |
| Azure AD B2C flow rewrite (cookie/OIDC → MSAL + JWT) | Highest single risk; auth is easy to get subtly wrong | Dedicated auth section + portal app-registration changes; test early | 2–3 |
| Credit consumption trust (client-initiated) once gen is client-side | Users could avoid charges | Atomic, authenticated `/api/credits/consume`; gate the free-credit hole | 2 |
| WASM payload too large | Slow first load | MudBlazor (light) + trimming/AOT; drop unused component packages | 1, 3 |

## How to use these docs

- Update the **Status** line at the top of each phase doc as you go.
- When you make or change a decision, record it in [../CONTEXT.md §6](../../CONTEXT.md#6-decision-log).
- When you fix a "Known issue", tick it off in [../CONTEXT.md §7](../../CONTEXT.md#7-known-issues--tech-debt-fix-opportunistically-during-migration).
- Prefer many small commits, one concern each; keep every phase green.
