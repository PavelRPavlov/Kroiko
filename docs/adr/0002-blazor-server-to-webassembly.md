# ADR-0002: Migrate the UI from Blazor Server to Blazor WebAssembly

- Status: Accepted
- Date: 2026-07-13
- Deciders: Repo owner

## Context

The app is Blazor **Server**: every user holds a stateful SignalR circuit, and server
memory/CPU scale with concurrent users. The owner wants to stop operating a stateful
Blazor Server host. Blazor **WebAssembly** runs the UI entirely in the browser and can
be served as static files, removing the stateful circuit.

The critical consequence: **WASM cannot do what the app does today in-process** — it
cannot reach SQL Server, hold secrets, or upload blobs, because all client code ships to
the browser. So this decision *necessitates* a backend for that logic
([ADR-0003](0003-dedicated-minimal-api-backend.md)). "Going WASM" removes the *stateful
circuit*, not the *server*.

## Decision

We will migrate the UI to **Blazor WebAssembly**, as a **UI-only** client. All logic that
touches the database, secrets, or external services moves behind an API. The client is
responsible only for presentation and in-browser computation.

## Consequences

**Positive**
- No stateful Blazor Server circuits; UI is static and cheap to serve/scale.
- Clear separation between presentation (client) and privileged logic (API).

**Negative / risks**
- Requires a backend API (ADR-0003) and a full **authentication rewrite**: server
  cookie/OpenID-Connect → browser MSAL (PKCE) + API JWT bearer validation. This is the
  single highest-risk part of the migration and needs Azure AD B2C app-registration
  changes.
- Larger initial download (mitigated by [ADR-0006](0006-replace-syncfusion-radzen-with-mudblazor.md)
  and trimming).

**Follow-ups**
- ADR-0003 (backend), ADR-0004 (where generation runs), ADR-0005 (hosting).

## Alternatives considered
- **Blazor Web App (.NET 8+) with WASM interactive render mode** — rejected: still runs a
  stateful-ish .NET host, which is what the owner wants to avoid.
- **Stay on Blazor Server** — rejected: does not meet the "stop hosting the stateful
  server" goal.

## Related
- Necessitates: [ADR-0003](0003-dedicated-minimal-api-backend.md)
- Implemented by: [../implementation/05-blazor-wasm-client.md](../implementation/05-blazor-wasm-client.md)
