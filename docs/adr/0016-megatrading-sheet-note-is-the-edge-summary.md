# ADR-0016: MegaTrading's sheet "Забележка" shows the banded sides, as Lonira's "Кантиране" does

- Status: Accepted
- Date: 2026-10-06
- Deciders: Pavel Pavlov

## Context

MegaTrading's `.xlsx` ends with a "Забележка" column, which held the part's `Note` — empty unless the operator typed one
in the grid. Nothing in the sheet said which sides of a part are edge-banded; the `.cut_mt` has the four edges, the
sheet only the edge-banding material ("Кант").

Lonira's sheet shows it in its "Кантиране" column: how many short (`k`) and long (`d`) sides are banded — `2 k 2 d`,
`1 k`, `2 d` — counted along the grain (a part with its grain reversed is turned first). Its "Забележка" holds the
"СДВ с краен размер …" note.

## Decision

1. **MegaTrading's "Забележка" is the edge summary, and only that**, in exactly Lonira's format and counting. The
   counting moved out of `LoniraOrderFormat` into one `EdgeBandingSummary.Describe` that both formats call, so they
   cannot drift apart.
2. **It is counted when the sheet is generated, from the part's four MegaTrading edges as they are then** — a side
   counts when its cell is not empty (a width or thickness still to pick still counts) — mapped back to Polyboard's
   sides (MegaTrading right/top/bottom/left = Polyboard top/left/right/bottom, ADR-0015 §2). An edge the operator
   cleared with the bucket no longer counts.
3. **The operator's note is no longer written to the sheet.** It still goes to the `.cut_mt`'s note field, which is
   where the grid's "Забележка" column is edited.

## Consequences

- ✅ MegaTrading's sheet shows which sides are banded, the way the operators already read it on Lonira's.
- ✅ A test checks, for every combination of banded sides, part orientation and grain, that MegaTrading's summary is
  Lonira's for the same Polyboard part; the Lonira golden files did not change.
- ⚠️ The MegaTrading `.xlsx` golden files are re-recorded: only column H changes, from empty to the summary
  (532 cells over the six MegaTrading goldens). The Server shares the domain, so its sheet changes the same way.
- ⚠️ A note typed in the MegaTrading grid is in the `.cut_mt` but no longer in the `.xlsx`.

## Alternatives considered

- **The summary followed by Lonira's note** (`2 k 2 d; СДВ с краен размер …`): everything in one column, but not what
  was asked for.
- **A separate "Кантиране" column before "Забележка"**, exactly Lonira's layout: changes MegaTrading's template.

## Related

- [ADR-0015](0015-megatrading-edge-banding-width-and-thickness.md) — the MegaTrading edges this counts.
- [ADR-0007](0007-parity-and-test-strategy.md) — golden files.
- Implemented in `Kroiko.Domain/TemplateBuilding/EdgeBandingSummary.cs`, `MegaTradingOrderFormat.EdgeSummary`,
  `MegaTradingTableRowProvider`.
