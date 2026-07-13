# ADR-0005: Serve the WASM client as static files from the API

- Status: Accepted
- Date: 2026-07-13
- Deciders: Repo owner

## Context

With a UI-only WASM client ([ADR-0002](0002-blazor-server-to-webassembly.md)) and a
Minimal API backend ([ADR-0003](0003-dedicated-minimal-api-backend.md)), the static client
assets need a home. Options include a separate static host (e.g. Azure Static Web Apps) or
serving them from the API itself. The owner wants to minimize moving parts and deploy one
thing.

## Decision

We will have **`ATAFurniture.Api` serve the WASM client's static files** —
`UseBlazorFrameworkFiles()` + `UseStaticFiles()` + `MapFallbackToFile("index.html")` — so
the whole app ships as **one deployable unit** (the classic ASP.NET Core-hosted WASM
layout). `/api/*` routes are matched before the SPA fallback.

## Consequences

**Positive**
- One build, one artifact, one deploy; no separate static-hosting service or CORS setup.
- Client and API share an origin, simplifying auth and requests.

**Negative / risks**
- Client and API are released and scaled together (coupled cadence).
- The API process serves static assets (minor extra load; fronting with a CDN remains
  possible later).

**Follow-ups**
- Ensure API routing precedes the SPA fallback; carry over HTTPS redirect/HSTS.

## Alternatives considered
- **Azure Static Web Apps for the client + separate API** — lower-ops static hosting and
  built-in auth, but two deployables and cross-origin setup; not chosen.
- **CDN/blob static hosting** — same two-deployable trade-off.

## Related
- Builds on: [ADR-0003](0003-dedicated-minimal-api-backend.md)
- Implemented by: [../implementation/06-hosting-and-deployment.md](../implementation/06-hosting-and-deployment.md)
