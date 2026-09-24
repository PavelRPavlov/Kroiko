# ADR-0007: Prove parity with shared golden files, `ConverterState` unit tests and a Playwright suite

- Status: Accepted
- Date: 2026-09-24
- Deciders: Pavel Pavlov

## Context

Operators will switch from `ATAFurniture.Server` to the PWA only if its order files are the ones the
Server would have produced. [ADR-0004](0004-shared-browser-safe-conversion-domain.md) already
decides the **domain** golden tests (worksheet XML, `.cut_mt` bytes, date-normalised file names, a
`bg-BG` run) in a new `Kroiko.Domain.Tests`; [ADR-0006](0006-known-conversion-bugs-in-pwa.md) adds
LF, BOM and whitespace-line fixtures. What those tests cannot see:

- They run on desktop .NET. The PWA runs a **trimmed** WebAssembly build whose `CurrentCulture`
  follows the browser, reads `template.json` as an embedded resource and edits through
  `ConverterState` ([ADR-0005](0005-copy-conversion-ui-into-pwa.md)). A trimmed-away type or a
  lost resource passes every golden test and still breaks the app.
- Offline start, the service worker and saving only exist in a browser.
- The PWA deliberately differs from the Server (ADR-0003, ADR-0005, ADR-0006); without one list, a
  reviewer or operator comparing the two apps reports them as regressions.
- Fixtures live in two places today (`ATAFurniture.Server.Tests/TestFiles/`, root `TestFiles/`),
  and some are named after what look like real orders (`Kitchen Alex.txt`,
  `Wardrobes-Niki-i-Toni.txt`) in a **public** repo whose rules forbid real Polyboard files.
- `ATAFurniture.Server.Tests` holds six shape-only smoke tests that call Server classes ADR-0004
  moves into the domain.

## Decision

1. **Four test projects**, all xUnit 2.9.3 with FluentAssertions pinned to 7.1.0 (8.x is
   commercial); Snapshooter is dropped.

   | Project | References | Holds |
   |---|---|---|
   | `Kroiko.Testing` (class library) | `Kroiko.Domain` | `OrderFilesAssert`, the golden-file update switch, `TestData/` |
   | `Kroiko.Domain.Tests` | `Kroiko.Domain`, `Kroiko.Testing` | golden tests (ADR-0004), `Check`, `FileNameSanitizer`, parser fixtures (ADR-0006) |
   | `Kroiko.Client.Tests` | `Kroiko.Client.Blazor`, `Kroiko.Testing` | `ConverterState` unit tests + the Playwright suite |
   | `ATAFurniture.Server.Tests` | `ATAFurniture.Server` | Server-only concerns (§8) |

2. **Shared test data.** `Kroiko.Testing/TestData/` holds `polyboard/*.txt` (fixtures) and
   `golden/<fixture>/<manufacturer>/` (worksheet XML, `.cut_mt`, `names.txt`), as content copied to
   the output so it flows to both test projects. `OrderFilesAssert` is the one comparison both use.
   Running with `UPDATE_GOLDEN=1` rewrites the golden files; a golden-file diff in a PR is the
   reviewable record of an output change. This refines ADR-0004 §9: the tests still move to
   `Kroiko.Domain.Tests`, the data and helper move to `Kroiko.Testing`.
3. **Fixtures are synthetic.** Existing fixtures keep their content; files named after a person get
   neutral names (e.g. `kitchen-8-materials.txt`, `wardrobes-23-field.txt`). New fixtures (LF, BOM,
   bad lines, >6 materials…) are hand-made or derived from existing ones — never a new customer
   export. Real files are used only locally, for the manual acceptance check.
4. **`ConverterState` unit tests, no bUnit.** The PWA-only rules — any edit clears generated files,
   the unsaved-work definition, "Генерирай бланки" disabled on empty contacts or `Check` problems,
   the discard confirmation only when files exist, the bad-line message (first 10, "…и още N") —
   are plain xUnit tests. `ConverterState` takes the confirm dialog and device storage through
   small interfaces the tests fake. Components are not unit-tested; Playwright covers the wiring.
5. **The Playwright suite** (`Microsoft.Playwright`, only in `Kroiko.Client.Tests`, tagged
   `[Trait("Category", "E2E")]`):
   - A shared fixture runs `dotnet publish -c Release` on `Kroiko.Client.Blazor` **once per test
     run** into a temp folder and serves its `wwwroot` from an in-process Kestrel static host on a
     free `localhost` port (a secure context, so no certificate), with MIME types for `.wasm`,
     `.dat` and `.webmanifest`.
   - **Bundled Chromium** at the version pinned by the Playwright package, headless (`HEADED=1`
     to watch), so results reproduce on any machine.
   - Scenarios:
     1. **Lonira**, **Suliver**, **MegaTrading**: upload → contacts → generate → "Изтегли всички"
        → downloads compared with the golden files by `OrderFilesAssert`. Each runs under
        `bg-BG` **and** `en-US` and both must match. MegaTrading uses a fixture with ≤ 6
        materials; the 11- and 23-field formats are each exercised by at least one manufacturer.
     2. **Bad lines**: a synthetic bad fixture shows the "ред N: …" alert and loads nothing.
     3. **Offline**: load, wait for the service worker to activate and cache, go offline, reload;
        the app starts and a Lonira conversion matches the golden files.
     4. **Device storage**: generate once, reload; contacts and manufacturer are pre-filled.
   - Not automated (manual checklist, §7): the update prompt, install, the folder picker.
   - Missing browsers **fail** the E2E tests with the `playwright.ps1 install chromium` command.
6. **Running.** `dotnet test` runs everything; `dotnet test --filter Category!=E2E` is the fast
   loop. Any change to `Kroiko.Client.Blazor` or `Kroiko.Domain` runs the full suite before its PR,
   and the PR body says so. CI, when set up, runs the same commands.
7. **Intended differences are one table.** `CONTEXT.md` keeps "Intended differences from the
   Server": each row names the difference, its ADR and the test that pins the PWA behaviour.
   Golden files stay the Server's output (the >6-material `.cut_mt` keeps its truncated header as
   characterization); the PWA side is proven by its own test. A new difference needs an ADR, a
   row and a test in the same PR.
8. **`ATAFurniture.Server.Tests` shrinks to Server-only concerns** (3–4 tests): DI resolves an
   `IOrderFormat` for every `SupportedCompanies` key; a bad-line file yields an empty list and a
   log entry (ADR-0004 §3); Kuklensko resolves to the Suliver format with its own email.
   `GenerationSmokeTests` is deleted in the commit that moves its coverage to the golden tests.
9. **Manual acceptance** lives in `docs/release-checklist.md`, each run recorded as a GitHub
   issue ("Release vX.Y.Z sign-off") with the checklist pasted and ticked:
   - **Go-live, once**, before operators switch — Pavel plus one operator: ~10 recent real orders
     across all three manufacturers, both field formats and Cyrillic material names, through the
     deployed Server and the installed PWA on `app.kroiko.com` in Edge. Files match (Excel
     identical; `.cut_mt` byte-equal apart from the date), open in each manufacturer's software,
     decode as UTF-8 (ADR-0006 §5), and every intended-difference row behaves as listed.
   - **Every production release** — ~10 minutes on the installed Edge PWA: update snackbar and
     reload, About shows the new version, one conversion per manufacturer, save to a picked folder
     and "Изтегли всички", offline start.

## Consequences

- ✅ The shipped artifact — trimmed, in a browser, in `bg-BG` — is proven against the same golden
  files as the domain, so trimming, embedded-resource and culture regressions fail a test.
- ✅ One comparison helper and one copy of the test data; an output change is a visible diff.
- ✅ The PWA's deliberate strictness is documented once and each difference is pinned by a test.
- ✅ No new customer data enters the public repo.
- ⚠️ `dotnet test` now publishes the client and needs a one-time Playwright browser install; the
  full run is noticeably slower than today.
- ⚠️ Real Edge, the installed window, the folder picker and updates are covered only manually.
- ⚠️ `ConverterState` must be designed for testing (dialog and storage behind interfaces).
- Follow-up: the implementation guides place `Kroiko.Testing` + golden tests at the start of the
  domain phase, `ConverterState` tests in the UI phase, the Playwright suite once the client runs,
  and `docs/release-checklist.md` before go-live; the fixture renames happen with the move to
  `TestData/`.

## Alternatives considered

- **No browser tests** (golden tests + manual check): trimming and offline bugs surface only at
  the manual step, after every release.
- **bUnit component tests**: brittle against MudBlazor markup for components that mostly bind to
  `ConverterState`, which is already unit-tested.
- **Test an already-running app via a URL**: easy to test a stale build or the dev server with the
  no-op service worker.
- **Installed Edge (`msedge` channel), `bg-BG` only**: closer to operators, but the browser
  version varies per machine; reproducibility was preferred and Edge is covered manually.
- **Automate the update flow** (publish twice with different versions): about double the E2E
  setup for a ~20-line prompt.
- **Linked test data and helper file**, or **`Kroiko.Client.Tests` referencing
  `Kroiko.Domain.Tests`**: a third project is cleaner than linked items or a test-to-test reference.
- **Differences recorded only in the ADRs**: reviewers would have to hunt through six records.
- **Delete `ATAFurniture.Server.Tests`**: leaves the Server's rewiring onto the domain untested.
- **Only a per-release smoke test**: skips the one side-by-side run on real orders that proves the
  switch is safe.

## Related

- Ticket: [What proves parity, and how it's tested](https://github.com/PavelRPavlov/Kroiko/issues/24)
- Map: [Offline installable Blazor WASM PWA (no backend)](https://github.com/PavelRPavlov/Kroiko/issues/15)
- [ADR-0004](0004-shared-browser-safe-conversion-domain.md) — golden tests; §9 refined by decision 2.
- [ADR-0005](0005-copy-conversion-ui-into-pwa.md), [ADR-0006](0006-known-conversion-bugs-in-pwa.md),
  [ADR-0003](0003-save-order-files-to-picked-folder.md) — the intended differences.
- [ADR-0001](0001-host-pwa-on-azure-static-web-apps.md), [ADR-0002](0002-pwa-updates-reload-prompt.md) — host and update flow the release checklist covers.
- Informs: [Phases and order of the PWA implementation guides](https://github.com/PavelRPavlov/Kroiko/issues/31)
