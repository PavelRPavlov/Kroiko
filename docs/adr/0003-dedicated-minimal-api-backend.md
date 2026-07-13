# ADR-0003: Introduce a dedicated ASP.NET Core Minimal API backend

- Status: Accepted
- Date: 2026-07-13
- Deciders: Repo owner

## Context

[ADR-0002](0002-blazor-server-to-webassembly.md) moves the UI to WebAssembly, which
cannot hold secrets or touch the database. The privileged logic that lives in the Blazor
Server app today — EF Core access to SQL Server (users, credits), sending email via
SendinBlue, uploading to Azure Blob, and validating Azure AD tokens — must run somewhere
the browser cannot see. We need a backend, and we want it as thin and stateless as
possible.

## Decision

We will create a **dedicated ASP.NET Core Minimal API** project (`ATAFurniture.Api`) that
hosts all backend logic. The WASM client calls it over HTTPS with a bearer token. Request
and response shapes live in a shared `ATAFurniture.Contracts` project (kept separate from
`Kroiko.Domain` entities). The API is the **only** place secrets exist.

## Consequences

**Positive**
- Stateless, horizontally scalable backend; secrets confined to one deployable.
- The credit system moves to atomic, server-enforced endpoints — this fixes the
  double-decrement, fire-and-forget, lost-update, and negative-balance bugs at once.
- A clean HTTP contract the client can target.

**Negative / risks**
- New network boundary: DTOs, versioning, error handling, and auth to design and maintain.
- Credit consumption for a purely client-side download has no natural server touchpoint,
  so the consume endpoint must be authenticated, atomic, and balance-checked to stay
  trustworthy.

**Follow-ups**
- Define endpoints (`/api/user`, `/api/credits/consume`, `/api/email/send`, …); add a
  `RowVersion` concurrency token; gate the free-credits hole.

## Alternatives considered
- **Azure Functions (serverless)** — viable and lowest-ops, but the owner chose a single
  dedicated Minimal API project that hosts everything, including the client (ADR-0005).
- **Keep logic in a Blazor Web App server host** — rejected with ADR-0002.

## Related
- Required by: [ADR-0002](0002-blazor-server-to-webassembly.md)
- Hosts the client per: [ADR-0005](0005-api-serves-static-client.md)
- Implemented by: [../implementation/04-backend-api.md](../implementation/04-backend-api.md)
