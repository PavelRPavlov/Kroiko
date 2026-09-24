# Architecture Decision Records

An **ADR** captures one significant architectural decision: the context that forced it,
the decision itself, and the consequences that follow. ADRs are **immutable once
Accepted** — if a decision changes, add a new ADR that *supersedes* the old one rather
than editing history.

This is a fresh set for the offline Blazor WASM PWA (`Kroiko.Client.Blazor`), charted in
the map [Offline installable Blazor WASM PWA (no backend)](https://github.com/PavelRPavlov/Kroiko/issues/15).
The earlier ADRs were deliberately removed (see git history) and are not carried over.

## Index

| ADR | Decision | Status |
|---|---|---|
| [0001](0001-host-pwa-on-azure-static-web-apps.md) | Host the PWA on Azure Static Web Apps at `app.kroiko.com` | Accepted |
| [0002](0002-pwa-updates-reload-prompt.md) | Deliver PWA updates silently, with an opt-in reload prompt | Accepted |
| [0003](0003-save-order-files-to-picked-folder.md) | Save order files to a user-picked folder, with a download fallback | Accepted |
| [0004](0004-shared-browser-safe-conversion-domain.md) | One browser-safe conversion domain shared by the Server and the PWA | Accepted |
| [0005](0005-copy-conversion-ui-into-pwa.md) | Copy the conversion UI into the PWA, with an app-wide Order state | Accepted |
| [0006](0006-known-conversion-bugs-in-pwa.md) | Fix the known conversion bugs in the PWA, and in the shared parser for both apps | Accepted |
| [0007](0007-parity-and-test-strategy.md) | Prove parity with shared golden files, `ConverterState` unit tests and a Playwright suite | Accepted |

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
## Related      — other ADRs, tickets and the guide(s) that implement this
```

## Conventions

- Filename: `NNNN-kebab-case-title.md`, zero-padded, monotonically increasing.
- One decision per record. Keep it short; link to `../implementation/` for the how-to detail.
- To reverse a decision: create a new ADR with `Status: Accepted` whose **Related**
  section supersedes the old one, and set the old one to `Superseded by ADR-XXXX`.
