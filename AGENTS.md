# AGENTS.md

> Operating guide for AI agents (and humans) working in the **ATATextConverter** repo.
> **Domain & architecture context:** [CONTEXT.md](CONTEXT.md).
> **Architecture decisions (ADRs):** [docs/adr/](docs/adr/).
> **Active migration plan:** [docs/implementation/00-overview.md](docs/implementation/00-overview.md).
> **Issue tracker (agents, incl. /wayfinder):** [docs/agents/issue-tracker.md](docs/agents/issue-tracker.md) — GitHub Issues + Project 3, this repo only.

---

## What this is

A .NET web app that converts Polyboard furniture cut-list text files into
manufacturer-specific Excel/text order files, metered by a per-user credit system.
See [CONTEXT.md](CONTEXT.md) for the domain model and the ubiquitous language.

## Repository layout

```
ATATextConverter/
├── AGENTS.md                 ← you are here
├── CONTEXT.md                ← domain model + architecture + decision log
├── docs/
│   ├── adr/                  ← architecture decision records (the "why")
│   └── implementation/       ← step-by-step migration guides (start at 00-overview.md)
├── TextConverter.sln
├── ATAFurniture.Server/      ← Blazor Server web app (being migrated to Client + Api)
├── Kroiko.Domain/            ← domain library (parsing, template building, Excel gen)
├── ATAFurniture.Server.Tests/← xUnit generation smoke tests (domain tests move to Kroiko.Domain.Tests — ADR-0004)
└── UWPTextConverter/         ← DEAD legacy UWP app (scheduled for deletion)
```

Target layout after migration: `ATAFurniture.Client` (WASM UI), `ATAFurniture.Api`
(minimal API + host), `Kroiko.Domain` (shared, browser-safe), `ATAFurniture.Contracts`
(DTOs), `ATAFurniture.Tests`.

## Build / run / test

```bash
dotnet restore TextConverter.sln
dotnet build TextConverter.sln
dotnet run --project ATAFurniture.Server      # current app (needs user-secrets configured)
dotnet test                                   # generation smoke tests over checked-in Polyboard fixtures
```

- **SDK:** .NET 8 today; migrating to **.NET 10** (see [docs/implementation/01-dotnet-10-upgrade.md](docs/implementation/01-dotnet-10-upgrade.md)).
  Pin the SDK with `global.json` once .NET 10 lands.
- **Local secrets:** the app reads SQL/Storage/SendinBlue/Syncfusion values from
  user-secrets/env. Never hardcode or commit them.

## Environment

- Dev machine is **Windows**; the default shell is **PowerShell**, and a **Git Bash**
  tool is also available. Prefer plain, readable, single-line shell commands.
- Do **not** use PowerShell `-EncodedCommand` / base64-encoded command payloads
  (corporate policy). If a command would be base64-encoded, write a `.ps1` and invoke
  it by path, or use Git Bash.

## Conventions

- **C#:** nullable reference types enabled; `ImplicitUsings` on in the domain/test
  projects. Match the surrounding file's style.
- **Culture:** always parse/format numbers with `CultureInfo.InvariantCulture`
  (hosts/browsers may be `bg-BG`). This is a correctness invariant, not a preference.
- **DI:** company-specific services are registered with **keyed** DI, keyed by
  `nameof(SupportedCompanies.X)`.
- **Async:** suffix async methods with `Async`; never fire-and-forget a `Task`
  (`.ConfigureAwait(false)` without `await` is a bug — see Known issues in CONTEXT.md).
- **UI:** after the MudBlazor migration, use **MudBlazor components only**. Do **not**
  reintroduce Syncfusion or Radzen (they are paid; we removed them deliberately —
  [ADR-0006](docs/adr/0006-replace-syncfusion-radzen-with-mudblazor.md)).

## Commits, pushes & PRs — agents may (applies to every agent & sub-agent, THIS REPO ONLY)

Agents and sub-agents **may create commits, push branches, and open pull requests** on
this repository ([PavelRPavlov/Kroiko](https://github.com/PavelRPavlov/Kroiko)). This
permission is scoped to this repo only — it does not extend to any other repository.

- ✅ Work on a **feature branch** (e.g. `feat/…`, `fix/…`, `docs/…`), commit there, push it,
  and open a PR against `main` with `gh pr create`. Small, one-concern commits;
  Conventional-Commit style messages (`feat:`, `fix:`, `docs:`, `chore:` — match `git log`).
- ✅ Build and run tests before pushing; say in the PR body what was verified.
- ✅ Review what you stage (`git status` / `git diff --staged`) — commit only the files
  your task touched, never someone else's unrelated work-in-progress.
- ❌ Don't push directly to `main`, force-push (`--force` / `--force-with-lease`) any shared
  branch, or rewrite already-pushed history.
- ❌ Don't merge PRs or enable auto-merge — the user reviews and merges.
- ❌ Don't skip hooks (`--no-verify`) or signing.
- ❌ The repo is **public**: never commit secrets, customer data, or real Polyboard files.

## Guardrails (do / don't)

- ✅ Commit, push and open PRs **on a branch** (see "Commits, pushes & PRs" above); never push to `main` or merge.
- ✅ Update [CONTEXT.md](CONTEXT.md) and the relevant `docs/implementation/` guide when
  you change things; **record an architectural decision as a new ADR** in `docs/adr/`
  (see [docs/adr/README.md](docs/adr/README.md) for the format).
- ✅ Add tests when you touch parsing or file generation — these are the highest-risk
  paths and have only shape-level smoke coverage today (golden tests planned in ADR-0004).
- ✅ Keep `Kroiko.Domain` **browser-safe** (no server-only APIs) — it runs in WASM.
- ✅ Fix the relevant "Known issues" (CONTEXT.md §7) when you're already editing that code.
- ❌ Never commit secrets. Never put secrets in the WASM client (they ship to browsers).
- ❌ Don't reintroduce Syncfusion/Radzen, the UWP project, or the Cosmos scaffolding.
- ❌ Don't combine the framework upgrade and the re-architecture in one step — follow
  the phase order in `docs/implementation/`.
- ❌ Don't commit `bin/`, `obj/`, `*.user`, `.idea/`, or `UpgradeLog*.htm` (fix
  `.gitignore` — it is currently a Terraform template).

## Migration status

Work is planned but **not yet started**. Execute in the order in `docs/implementation/`:
`01` .NET 10 → `02` spikes → `03` MudBlazor → `04` API → `05` WASM client → `06` hosting.
Each guide has its own done-criteria checklist. Update the "Status" line at the
top of each phase doc as you progress (`Not started` → `In progress` → `Done`).
