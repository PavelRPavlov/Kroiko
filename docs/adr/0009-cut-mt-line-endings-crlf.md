# ADR-0009: Write the `.cut_mt` with CRLF line endings on every host

- Status: Accepted
- Date: 2026-09-24
- Deciders: Pavel Pavlov

## Context

`MegaTradingFileGenerator` built the MegaTrading `.cut_mt` with `StringBuilder.AppendLine`, so each
line ended in `Environment.NewLine`: **CRLF on Windows, LF on Linux, macOS and in the browser (WASM)**.
The same code now runs in both apps ([ADR-0004](0004-shared-browser-safe-conversion-domain.md)), so the
file's bytes depended on where it was generated.

Facts, from the repo:

- The golden files ([ADR-0007](0007-parity-and-test-strategy.md)) were recorded on Windows; every
  `TestData/golden/**/MegaTrading/*.cut_mt` is CRLF, and `.gitattributes` keeps git from converting them.
  The five MegaTrading golden cases therefore failed on Linux/macOS
  ([01 — Golden baseline](../implementation/01-golden-baseline.md), step 3).
- Phase 04's E2E parity scenario generates the `.cut_mt` **in the browser** and compares it with the
  same golden files byte for byte, so it would fail too.
- Step 02.5 funnelled every `.cut_mt` line through one method, `AppendRow`.
- The Server's publish profile (`atafurniture-app - Zip Deploy.pubxml`) targets `linux-x64`, while the
  GitHub workflows build and deploy on `windows-latest`. Which line ending production writes today is
  **not known**.
- What MegaTrading's software accepts is not documented. The CRLF file is the one the Windows-built
  Server has been sending, and it is the one recorded as the baseline.

## Decision

We will write **`\r\n` after every `.cut_mt` line, on every host** — Windows Server, Linux Server and
the browser. `AppendRow` appends a `"\r\n"` constant; the `.cut_mt` path uses no `AppendLine` and no
`Environment.NewLine`. A domain test asserts CRLF explicitly (every line break is `\r\n`, no bare `\n`
or `\r`), so the rule does not rest on the OS that happens to run the tests.

The golden files do not change: they are already CRLF.

## Consequences

- ✅ The `.cut_mt` is the same bytes wherever it is generated, so the PWA and the Server agree and the
  golden files are OS-independent: the MegaTrading golden cases pass on Linux/macOS and in a Linux CI.
- ✅ Phase 04's browser parity scenario can compare the `.cut_mt` bytes with the golden files as they are.
- ⚠️ **If production runs on Linux, the Server's `.cut_mt` changes from LF to CRLF** on its next deploy
  with the shared domain. Nothing else in the file changes. This is accepted: CRLF is what a
  Windows-hosted Server writes and what the baseline records; if MegaTrading's software rejects CRLF
  from a Linux-hosted Server, a new ADR reverses this one.
- ⚠️ If production has been writing LF and MegaTrading has only ever received LF, CRLF is untested
  against their software. The go-live check ([07 — Hosting & go-live](../implementation/07-hosting-and-go-live.md),
  07b step 1) already opens each file in the manufacturer's software; it now names the CRLF `.cut_mt`.

## Alternatives considered

- **Pin LF and re-record the golden files**: an explicit golden diff for a behaviour change nobody
  asked for, and a change for every Windows-hosted Server; LF has no known advantage for MegaTrading.
- **Keep `Environment.NewLine`, record LF as a PWA-vs-Server difference and normalise line endings in
  the tests**: the file would still depend on the host, the byte-for-byte golden comparison would get a
  special case for exactly the bytes this ADR is about, and CONTEXT.md §9 would carry a difference that
  exists only by accident.

## Related

- Decision: Pavel Pavlov, in chat, 2026-09-24.
- Map: [Offline installable Blazor WASM PWA (no backend)](https://github.com/PavelRPavlov/Kroiko/issues/15)
- [ADR-0004](0004-shared-browser-safe-conversion-domain.md) — the shared domain that runs on every host.
- [ADR-0007](0007-parity-and-test-strategy.md) — golden files, compared byte for byte.
- Implemented by: [02 — Shared domain](../implementation/02-shared-domain.md), step 5 (follow-up 02.5b).
