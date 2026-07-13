# Architecture Decision Records

An **ADR** captures one significant architectural decision: the context that forced it,
the decision itself, and the consequences that follow. ADRs are **immutable once
Accepted** — if a decision changes, add a new ADR that *supersedes* the old one rather
than editing history.

## Index

| ADR | Decision | Status |
|---|---|---|
| [0001](0001-upgrade-to-dotnet-10.md) | Upgrade the solution to .NET 10 | Accepted |
| [0002](0002-blazor-server-to-webassembly.md) | Migrate the UI from Blazor Server to Blazor WebAssembly | Accepted |
| [0003](0003-dedicated-minimal-api-backend.md) | Introduce a dedicated ASP.NET Core Minimal API backend | Accepted |
| [0004](0004-client-side-file-generation.md) | Parse and generate order files in the browser | Accepted (pending Spike A) |
| [0005](0005-api-serves-static-client.md) | Serve the WASM client as static files from the API | Accepted |
| [0006](0006-replace-syncfusion-radzen-with-mudblazor.md) | Replace Syncfusion + Radzen with MudBlazor | Accepted |

## Format

Each ADR follows:

```
# ADR-NNNN: Title
- Status: Proposed | Accepted | Deprecated | Superseded by ADR-XXXX
- Date: YYYY-MM-DD
- Deciders: who

## Context      — the forces, constraints, and problem
## Decision     — what we decided (active voice: "We will …")
## Consequences — positive, negative, and follow-up work created
## Alternatives considered — options rejected, and why
## Related      — other ADRs and the guide(s) that implement this
```

## Conventions
- Filename: `NNNN-kebab-case-title.md`, zero-padded, monotonically increasing.
- One decision per record. Keep it short; link to [../implementation/](../implementation/)
  for the how-to detail.
- To reverse a decision: create a new ADR with `Status: Accepted` whose **Related**
  section supersedes the old one, and set the old one to `Superseded by ADR-XXXX`.
