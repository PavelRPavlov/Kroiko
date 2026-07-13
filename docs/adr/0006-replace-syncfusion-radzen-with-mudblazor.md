# ADR-0006: Replace Syncfusion + Radzen with MudBlazor

- Status: Accepted
- Date: 2026-07-13
- Deciders: Repo owner

## Context

The UI currently depends on **two** component kits: Syncfusion (commercially licensed — a
license key is registered at startup) and Radzen. Both are paid/licensed solutions. Moving
to WASM ([ADR-0002](0002-blazor-server-to-webassembly.md)) also ships the component library
to every browser, so a heavy kit (Syncfusion) inflates the download. An inventory shows the
app uses only a small set of components; the only non-trivial ones are a **file uploader**,
an **editable data grid**, and **tabs**.

## Decision

We will standardize on **[MudBlazor](https://mudblazor.com/)** (MIT-licensed) and remove
**both** Syncfusion and Radzen. The swap is done on the **still-Blazor-Server** app first
(easiest debugging), before the WASM move; the ported `.razor` files then carry forward to
the client. Key mappings: `SfUploader` → `MudFileUpload`, `SfGrid` → `MudDataGrid`,
`SfTab` → `MudTabs`, and 1:1 swaps for the Radzen layout/input primitives.

## Consequences

**Positive**
- No licensing cost and no Syncfusion license key/secret to manage.
- Significantly smaller WASM payload (MudBlazor is far lighter than Syncfusion), directly
  supporting the client-side-generation architecture.
- Unused Syncfusion packages (PivotTable, InPlaceEditor, SplitButtons, Cards) are dropped.

**Negative / risks**
- `MudDataGrid` editing UX differs from `SfGrid` (inline cell edit vs explicit edit mode) —
  the edit→generate flow must be re-validated.
- Syncfusion-specific CSS (`.e-grid`, `.e-tab`) is discarded; MudBlazor imposes a Material
  Design look (a visible restyle).
- Requires a MudBlazor release targeting .NET 10.

**Follow-ups**
- Validate grid/upload parity (Spike B); collapse the five duplicated spinners into one
  shared component during the port.

## Alternatives considered
- **Consolidate onto Radzen only** — lighter than Syncfusion but still a paid/licensed kit;
  MudBlazor is free (MIT) and lighter.
- **Keep both** — rejected: licensing cost and the largest possible WASM payload.

## Related
- Supports the payload goal of: [ADR-0002](0002-blazor-server-to-webassembly.md)
- Implemented by: [../implementation/03-mudblazor-migration.md](../implementation/03-mudblazor-migration.md)
