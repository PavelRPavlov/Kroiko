# ADR-0015: MegaTrading edges carry a width and a thickness from MegaTrading's lists; the PWA asks for missing ones

- Status: Accepted
- Date: 2026-10-06
- Deciders: Pavel Pavlov

## Context

MegaTrading opened our `.cut_mt` in their desktop app (`KKMital_Razkroi.exe`, a .NET WinForms grid) and got an
error for every edge dropdown. Decompiling the app (`monodis`) showed the cause:

- Each of the four edge columns (`left`, `Dolu`, `right`, `up`) is a `DataGridViewComboBoxColumn` with a fixed item
  list: `22/0.5`, `22/0.8/1.0`, `22/2.0`, `28/0.5`, `28/0.8/1.0`, `28/2.0`, `42/0.5`, `42/0.8/1.0`, `42/2.0` — the
  edge-banding **width** (mm, wider than the panel) then its **thickness**. The app handles no `DataError`, so any
  other value raises WinForms' default "DataGridViewComboBoxCell value is not valid" dialog, per cell.
- We wrote `{Polyboard edge material}/{thickness}`: `/0.5`, `Same/2.0`, `Falc/0.5`, … — never a width, so no edge
  value we ever wrote was accepted. When Polyboard gave no thickness (the 11-field format never does) we wrote `0.5`
  anyway. A TODO in `MegaTradingOrderFormat` already noted the missing width.
- The rest of the file reads correctly: `File.ReadAllLines` (UTF-8, BOM or not; CRLF or LF), the `╪` separator, and
  the X/Y/quantity fields, whose `.`/`,` the app swaps to the PC's decimal separator. Material, rotation and
  edge-material dropdowns get values from their lists.
- Polyboard never gives the band's width, and gives a thickness only in the 23-field format, only when filled in.
- The Server edits the four edges as free text in a view-model copy (`MegaTradingViewModel`); it is maintained, not
  developed.

## Decision

1. **An edge is MegaTrading's cell value.** `MegaTradingDetail`'s four edges stay strings holding exactly the
   `.cut_mt` cell, `{width}/{thickness}`; an empty string is a side with no edge. `MegaTradingEdge` (domain) parses and
   writes it — the width is everything before the first `/` — and owns the two lists, `Widths` (22, 28, 42) and
   `Thicknesses` (0.5, 0.8/1.0, 2.0). An edge is complete when both parts are from the lists.
2. **What Polyboard does not give stays empty.** A banded side gets the thickness Polyboard gives, mapped to the
   list (≤ 0.5 → 0.5, ≤ 1 → 0.8/1.0, more → 2.0), or empty when it gives none (0); the width is always empty. So a
   new edge is `/0.5`, `/2.0` or `/`. Polyboard's edge material (`Same`, `Falc`, …) is no longer written there. The
   sides keep today's mapping (Polyboard top → MegaTrading right, left → top, right → bottom, bottom → left).
3. **`Check` reports it.** MegaTrading's `IOrderFormat.Check` returns a `MissingEdgeBanding` problem — per material, in
   order of first use: how many banded sides miss a value, and whether a width and/or a thickness is missing —
   after `TooManyMaterials`.
4. **The PWA asks while generating, not instead of it.** `MissingEdgeBanding` does not disable "Генерирай бланки за
   поръчка" (`CanGenerate` ignores it). `ConverterState.GenerateAsync` first asks through `IEdgeBandingPrompt` — the
   app's `EdgeBandingDialog`, one row per material with a width and/or thickness picker for what that material
   misses — then `MegaTradingEdges.FillMissing` completes only the parts not on the lists (values the operator picked
   in the grid stay), as an input edit, and generates. Cancelling generates and fills nothing; a dialog that fails to
   open shows a snackbar.
5. **The grid picks, it does not type.** The MegaTrading tab's four edge columns are a width and a thickness
   `MudSelect` each, with "—" for empty (both "—" removes the edge), and a bucket button that clears both at once,
   removing the edge (disabled on a side with no edge); a part an edge misses shows in red — only red: the picker's
   empty error helper row is hidden, so a cell is the same height in every state — and a warning
   alert counts the missing edges and names their materials. The tab's warnings (this one and `TooManyMaterials`)
   share one slot at the top of the tab that always keeps room for one warning — more on narrower screens, where the
   text wraps (`.tab-warnings` in `app.css`) — so the rename rows and the grid do not jump as warnings come and go.
6. **Golden files.** The five `*/MegaTrading/*.cut_mt` golden files are re-recorded: only edge cells change (`/0.5` →
   `/` where Polyboard gave no thickness, the Polyboard edge-material prefix dropped). A new
   `bathroom-4-materials/MegaTrading-edges-filled` golden records the order after the dialog is answered with
   22 / 0.5 for every material (`TestData.GoldenEdgeBanding`); the browser parity test answers the dialog the same way
   and compares with it.

## Consequences

- ✅ A `.cut_mt` the PWA generates has only edge values MegaTrading's app lists, so it opens without the dropdown
  errors; the operator cannot type an invalid edge.
- ✅ The operator is asked once per material, which is how the width is chosen in practice (it follows the panel
  thickness), and can still fine-tune any side in the grid first.
- ⚠️ **The Server's `.cut_mt` changes too** (shared domain): its edges become `/0.5`, `/` instead of `Same/0.5`,
  `/0.5`. Neither was accepted by MegaTrading; the Server has no dialog or `Check`, so its MegaTrading files still miss
  widths unless the operator types them (`22/0.5`) in its free-text edge cells. The Server is not developed further.
- ⚠️ Every MegaTrading order with edges now needs the operator's answer (Polyboard never gives a width) — one dialog
  per generation, until the edges are complete.
- The material rows' thickness is still `0` for 11-field files (Polyboard gives none); MegaTrading's app does not
  validate it, so it is left for a later change.

## Alternatives considered

- **Derive the width from the panel thickness** (≤ 19 mm → 22, ≤ 25 → 28, else 42): no prompt, but a rule nobody at
  MegaTrading confirmed, wrong for 11-field files (no thickness) and invisible to the operator.
- **Block generating until the grid is complete** (a blocking `OrderProblem`): many clicks for a large order; the
  dialog fills a whole material at once.
- **One width per part instead of per side**: fewer pickers, but the `.cut_mt` and MegaTrading's grid have a width
  per side.
- **Structured edges (`MegaTradingEdge?` properties) on `MegaTradingDetail`**: cleaner types, but the Server's
  free-text edge editing would need rewiring; the string already is MegaTrading's value.

## Related

- [ADR-0006](0006-known-conversion-bugs-in-pwa.md) §3 — `IOrderFormat.Check` and Order problems.
- [ADR-0007](0007-parity-and-test-strategy.md) — golden files and the parity test.
- [ADR-0009](0009-cut-mt-line-endings-crlf.md) — the other `.cut_mt` format decision.
- Implemented in `Kroiko.Domain/TemplateBuilding/MegaTradingEdge.cs`, `MegaTradingEdges.cs`,
  `Kroiko.Client.Blazor/Components/EdgeBandingDialog.razor`, `MegaTradingEdgeCell.razor`.
