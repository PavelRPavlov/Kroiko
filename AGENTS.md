# AGENTS.md

> Operating guide for AI agents (and humans) working in the **ATATextConverter** repo.
> **Domain & architecture context:** [CONTEXT.md](CONTEXT.md).
> **Architecture decisions (ADRs):** [docs/adr/](docs/adr/).
> **Active migration plan:** [docs/implementation/00-overview.md](docs/implementation/00-overview.md).

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
├── ATAFurniture.Server.Tests/← xUnit test project (currently no tests — add some!)
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
dotnet test                                   # test project exists but is currently empty
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

## Commits — the user owns them (applies to every agent & sub-agent)

The user creates **all** commits and writes **all** commit messages **manually**,
always. No agent or sub-agent may commit on their behalf.

- ❌ Never run `git commit`, `git push`, `git commit --amend`, `git merge`,
  `git rebase`, `git cherry-pick`, or anything else that creates, rewrites, or
  publishes a commit — even when the change looks finished or you were just asked to
  "commit this".
- ✅ Do edit files, build, and run tests freely. Then **stop before committing** and
  leave the working tree for the user to review and commit themselves.
- ✅ If a commit seems warranted, describe what you'd commit (and a suggested
  message) and let the user run it.

## Guardrails (do / don't)

- ❌ **Never commit or push.** The user makes every commit manually (see "Commits" above).
- ✅ Update [CONTEXT.md](CONTEXT.md) and the relevant `docs/implementation/` guide when
  you change things; **record an architectural decision as a new ADR** in `docs/adr/`
  (see [docs/adr/README.md](docs/adr/README.md) for the format).
- ✅ Add tests when you touch parsing, credit accounting, or file generation — these
  are the highest-risk paths and currently have **zero** coverage.
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
