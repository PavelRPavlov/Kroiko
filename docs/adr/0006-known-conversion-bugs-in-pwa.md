# ADR-0006: Fix the known conversion bugs in the PWA, and in the shared parser for both apps

- Status: Accepted
- Date: 2026-09-24
- Deciders: Pavel Pavlov

## Context

CONTEXT.md §7 lists conversion bugs found in the Server. `Kroiko.Domain` is shared
([ADR-0004](0004-shared-browser-safe-conversion-domain.md)), so each fix has to say whether the
Server's behaviour changes too. Several bugs are already handled by earlier decisions:

| Bug | Handled by |
|---|---|
| Missing template builder / file-name provider is dereferenced (null crash) | ADR-0004 — `IOrderFormat` has no nullable wiring |
| `.cut_mt` doubles formatted with ambient culture (`bg-BG` comma) | ADR-0004 — invariant culture throughout the domain |
| Suliver / SuliverKuklensko share `Name = "Suliver"` | ADR-0004 — the domain knows three manufacturers with unique names, so the persisted last manufacturer is unambiguous |
| Stale output after an edit; edits lost on manufacturer switch / re-upload; MegaTrading view-model write-back; generating with empty contacts | [ADR-0005](0005-copy-conversion-ui-into-pwa.md) |

Still open, found in the code:

- **Line endings.** `DetailsExtractorService` splits on `"\r\n"` only. An LF-only file becomes one
  line with the wrong field count and is rejected; a UTF-8 BOM (`﻿`, not whitespace to
  `double.Parse`) breaks the first line. All checked-in fixtures are CRLF without a BOM.
- **Bad lines.** One line with a wrong field count or an unparsable number makes the Server discard
  the whole file behind a generic alert. ADR-0004's parser now reports `ParseResult.Errors`
  (line number + reason); the PWA's policy was left open.
- **More than 6 materials in `.cut_mt`.** The header always has exactly 6 material rows; materials
  beyond the 6th are left out of it while their parts are still written as detail rows. The fixture
  `Kitchen Alex.txt` has 8 materials, so this is not hypothetical. We cannot verify how
  MegaTrading's software treats more than 6 header rows or a split order.
- **Spinners.** The Server's upload spinner already clears in `finally`; its generate spinner is left
  on if generation throws.

## Decision

1. **Line endings — fixed in the domain, for both apps.** `PolyboardParser` splits on `\r\n`, `\n`
   or `\r`, strips a leading UTF-8 BOM, and skips empty and whitespace-only lines. The Server's
   behaviour changes only for files it rejects outright today (they now parse); every file it
   accepts today produces the same output, which the golden tests over the CRLF fixtures keep
   proving. New fixtures cover LF-only, BOM and whitespace-only lines.
2. **Bad lines — the PWA rejects the file, with details.** If `Errors` is non-empty nothing is
   loaded. The upload panel's alert lists the first 10 bad lines (line number and reason, e.g.
   "ред 14: 9 полета, очакват се 11 или 23") followed by "…и още N", and keeps the link to
   `/configuration`. `ParseError` stays structured (line number, kind, field count / field); the
   PWA writes the Bulgarian text. A file with no Details and no errors is rejected as empty. The
   Server keeps "any error → empty list" (ADR-0004 §3).
3. **More than 6 MegaTrading materials — the PWA blocks generation.** `IOrderFormat` gains

   ```csharp
   IReadOnlyList<OrderProblem> Check(IReadOnlyList<KroikoFile> files);
   ```

   MegaTrading returns `TooManyMaterials(Max, Materials)` when the files hold more than 6 distinct
   `Material` values (after material renames); Lonira and Suliver return an empty list. The PWA
   calls `Check` on every `ConverterState` change and disables "Генерирай бланки" while any
   problem exists, showing the offending materials and pointing to the material rename row. The
   limit is one named constant used by both the check and the `.cut_mt` generator. `Generate` is
   unchanged and the Server does not call `Check`, so the Server keeps writing the truncated header.
4. **Spinners — a PWA rule.** Every busy flag is cleared in `finally`; an exception from parsing or
   generation shows a Bulgarian error snackbar and leaves the Order as it was.
5. **Encoding unchanged.** Files are still decoded as UTF-8. Whether Polyboard ever exports another
   encoding (e.g. Windows-1251 material names) is unverified and goes to the release checklist.

## Consequences

- ✅ An operator can no longer send a short order: a bad line stops the upload and says where it is.
- ✅ A MegaTrading `.cut_mt` from the PWA always lists every material it uses.
- ✅ LF-only and BOM files work in both apps.
- ⚠️ The Server changes on inputs it rejects today (LF-only, BOM, whitespace-only lines) — accepted.
- ⚠️ More intended differences for the parity checklist: the PWA rejects files with details instead
  of a generic alert, and refuses MegaTrading orders with more than 6 materials that the Server
  still generates (truncated).
- ⚠️ An order with more than 6 materials must be reduced with the rename row or split in Polyboard;
  if MegaTrading later confirms their software accepts more rows or split orders, a new ADR can
  relax the check.
- Follow-up: the parity ticket owns the new fixtures and tests for these rules; the implementation
  guide places `Check` / `OrderProblem` in the domain phase and the alerts in the UI phase.

## Alternatives considered

- **Normalise line endings in the PWA only**: keeps a known trap in shared code to protect a Server
  behaviour (rejecting LF files) nobody relies on.
- **Skip bad lines and warn**: a missed warning becomes a missing part at assembly.
- **Replicate the generic rejection**: gives the operator nothing to fix in Polyboard.
- **Split the `.cut_mt` into files of ≤6 materials**, or **grow the header**: both guess at a
  format we cannot test against MegaTrading's software.
- **Replicate the truncated header**: keeps a silently inconsistent file.
- **Make `Generate` throw above 6 materials**: changes the Server's output for those orders.
- **Count materials in the MegaTrading tab**: puts the format's rule in UI code, away from the
  generator that depends on it.

## Related

- Ticket: [Fix or replicate the known conversion bugs in the PWA](https://github.com/PavelRPavlov/Kroiko/issues/23)
- Map: [Offline installable Blazor WASM PWA (no backend)](https://github.com/PavelRPavlov/Kroiko/issues/15)
- [ADR-0004](0004-shared-browser-safe-conversion-domain.md) — `PolyboardParser`, `ParseResult`, `IOrderFormat` extended here.
- [ADR-0005](0005-copy-conversion-ui-into-pwa.md) — `ConverterState`, the upload panel and the generate button this constrains.
- Informs: [What proves parity, and how it's tested](https://github.com/PavelRPavlov/Kroiko/issues/24),
  [Phases and order of the PWA implementation guides](https://github.com/PavelRPavlov/Kroiko/issues/31)
