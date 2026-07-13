# Documentation

Documentation for the **ATATextConverter** solution. Two kinds:

- **[`adr/`](adr/) — Architecture Decision Records.** The *why*. High-level, durable
  decisions, one per file, in a fixed format (Context → Decision → Consequences).
  Decisions change rarely; when one is reversed, a new ADR supersedes the old one.
- **[`implementation/`](implementation/) — Implementation guides.** The *how*. The
  a **dedicated implementation plan per phase** — ordered task breakdown (`PN-Tn`),
  per-task verification, sequencing, rollback, and done-criteria.
  These change as the work progresses (each has a live **Status** line).

Start here:

| I want to… | Go to |
|---|---|
| Understand *why* the architecture is the way it is | [adr/](adr/) (index: [adr/README.md](adr/README.md)) |
| Understand the domain, glossary, current/target architecture | [../CONTEXT.md](../CONTEXT.md) |
| Know how to build/test/work in the repo | [../AGENTS.md](../AGENTS.md) |
| Execute the migration, step by step | [implementation/00-overview.md](implementation/00-overview.md) |

## Relationship between ADRs and guides

An ADR records a decision and its consequences; an implementation guide tells you how
to carry it out. They cross-reference each other — e.g. [ADR-0006 (MudBlazor)](adr/0006-replace-syncfusion-radzen-with-mudblazor.md)
is realized by [implementation/03-mudblazor-migration.md](implementation/03-mudblazor-migration.md).
