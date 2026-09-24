# AGENTS.md

> Operating guide for AI agents (and humans) working in the **ATATextConverter** repo.
> **Domain & architecture context:** [CONTEXT.md](CONTEXT.md).
> **Architecture decisions (ADRs):** [docs/adr/](docs/adr/).
> **Build plan (PWA):** [docs/implementation/00-overview.md](docs/implementation/00-overview.md).
> **Issue tracker (agents, incl. /wayfinder):** [docs/agents/issue-tracker.md](docs/agents/issue-tracker.md) — GitHub Issues only (no project board), this repo only.

---

## What this is

Two .NET 10 apps that convert Polyboard furniture cut-list text files into
manufacturer-specific Excel/text order files, sharing one conversion domain:

- **`ATAFurniture.Server`** — the deployed Blazor Server app (login, per-user credits, email).
  Maintained, not developed.
- **`Kroiko.Client.Blazor`** — the new offline, installable Blazor WebAssembly PWA: no backend,
  no login, no credits, no email. Being built per [docs/implementation/](docs/implementation/00-overview.md).

See [CONTEXT.md](CONTEXT.md) for the domain model and the ubiquitous language.

## Repository layout

```
ATATextConverter/
├── AGENTS.md                 ← you are here
├── CONTEXT.md                ← domain model + architecture + decision log
├── docs/
│   ├── adr/                  ← architecture decision records (the "why")
│   ├── implementation/       ← step-by-step build guides for the PWA (start at 00-overview.md)
│   └── research/             ← research findings the ADRs rely on
├── TextConverter.sln
├── global.json               ← pins the .NET 10 SDK
├── ATAFurniture.Server/      ← Blazor Server web app (deployed; maintained, not developed)
├── Kroiko.Client.Blazor/     ← Blazor WASM PWA (being built)
├── Kroiko.Domain/            ← conversion domain shared by both apps (must stay browser-safe)
└── ATAFurniture.Server.Tests/← xUnit Server-only tests (DI wiring, bad-line policy)
```

Test projects added by the build plan ([ADR-0007](docs/adr/0007-parity-and-test-strategy.md)):
`Kroiko.Testing` (shared test data, golden files, `OrderFilesAssert`), `Kroiko.Domain.Tests`
(golden + domain tests), `Kroiko.Client.Tests` (`ConverterState` unit tests + Playwright E2E);
`ATAFurniture.Server.Tests` holds only Server-only tests (since phase 02 step 8).

## Build / run / test

```bash
dotnet restore TextConverter.sln
dotnet build TextConverter.sln
dotnet run --project ATAFurniture.Server      # Server app (needs user-secrets configured)
dotnet run --project Kroiko.Client.Blazor     # PWA (dev server; the service worker is a no-op in dev)
dotnet test                                   # everything, incl. Playwright E2E once it exists
dotnet test --filter Category!=E2E            # fast loop, no browser
```

- **SDK:** .NET 10, pinned by `global.json`. All projects target `net10.0`.
- **E2E:** the Playwright tests need a one-time `playwright.ps1 install chromium`
  (the failing test prints the exact command).
- **Local secrets (Server only):** the Server reads SQL/Storage/SendinBlue values from
  user-secrets/env. Never hardcode or commit them. The PWA has no secrets.

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
- **UI:** use **MudBlazor components only**, in both apps. Do **not** reintroduce
  Syncfusion or Radzen (they are paid; we removed them deliberately).

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
  paths. Golden files ([ADR-0007](docs/adr/0007-parity-and-test-strategy.md)) are the record
  of the output; a golden diff in a PR must be explained.
- ✅ Keep `Kroiko.Domain` **browser-safe** (no server-only APIs) — it runs in WASM.
- ✅ Fix the relevant "Known issues" (CONTEXT.md §7) when you're already editing that code.
- ❌ Never commit secrets. Never put secrets in the WASM client (they ship to browsers).
- ❌ Don't reintroduce Syncfusion/Radzen, the UWP project, or the Cosmos scaffolding.
- ❌ Don't share Razor components between the apps (no Razor Class Library) — the PWA's UI is a
  copy by design ([ADR-0005](docs/adr/0005-copy-conversion-ui-into-pwa.md)).
- ❌ Don't commit `bin/`, `obj/`, `*.user`, `.idea/`, or `UpgradeLog*.htm`.

## Build status

The offline PWA (`Kroiko.Client.Blazor`) is built phase by phase from
[docs/implementation/](docs/implementation/00-overview.md). Each guide's **Status** line is the
source of truth (`Not started` → `In progress` → `Done`). To pick up work: read `00-overview.md`,
take the next step of a guide that is not **Done** and whose dependencies are **Done**, and do
**one step per PR**.
Parallel lanes: `01 → 02` alongside `03`; then `05` alongside `06`; `07a` once `03` is done.
`ATAFurniture.Server` is **maintained, not developed**: change it only where phase 02 rewires it
onto the domain.
