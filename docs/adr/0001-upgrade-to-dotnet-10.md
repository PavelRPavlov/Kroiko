# ADR-0001: Upgrade the solution to .NET 10

- Status: Accepted
- Date: 2026-07-13
- Deciders: Repo owner

## Context

The solution targets `net8.0` across all projects. We want the current LTS runtime for
long-term support, newer EF Core / Blazor / Identity features, and to avoid compounding
a framework upgrade with the larger re-architecture (WASM split, component-kit swap)
that follows. A large re-architecture on an old runtime would mix two sources of risk.

## Decision

We will upgrade every project to **.NET 10** first, on the **current** Blazor Server
architecture, and reach a green (builds + runs + smoke-tested) baseline before starting
any structural migration.

## Consequences

**Positive**
- Any breakage in this step is unambiguously the framework upgrade, not the migration.
- We land on current-LTS EF Core, Identity, and Blazor tooling that later phases assume.

**Negative / risks**
- Package availability on a brand-new major: `Syncfusion.Blazor.*`, EF Core, and
  `Microsoft.Identity.Web` must have .NET 10 builds. (Syncfusion is removed in
  [ADR-0006](0006-replace-syncfusion-radzen-with-mudblazor.md), so a missing Syncfusion
  .NET 10 build is a reason to bring that phase forward rather than a blocker.)

**Follow-ups**
- Pin the SDK with `global.json`; bump CI to `setup-dotnet@v4` / `10.0.x`.

## Alternatives considered
- **Stay on .NET 8 and migrate first** — rejected: we'd re-do the upgrade on new project
  shapes, and lose runtime/library features the later phases want.
- **Do the upgrade and re-architecture together** — rejected: entangles two large sources
  of breakage in one step.

## Related
- Implemented by: [../implementation/01-dotnet-10-upgrade.md](../implementation/01-dotnet-10-upgrade.md)
- Enables: all subsequent ADRs.
