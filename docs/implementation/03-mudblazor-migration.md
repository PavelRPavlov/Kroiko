# Phase 1 — Implementation plan: Replace Syncfusion + Radzen with MudBlazor

> **Status:** Done (2026-07-13) — MudBlazor 9.7.0; all components ported, both kits + the
> Syncfusion license removed; solution builds clean. Manual click-through still recommended
> (see Testing & verification).
> **ADR:** [ADR-0006](../adr/0006-replace-syncfusion-radzen-with-mudblazor.md) · **Prerequisite:** Phase 0; Spike B reviewed
> **Est. effort:** M–L (the grid port is the bulk)
> Index: [00-overview.md](00-overview.md) · Context: [../../CONTEXT.md](../../CONTEXT.md)

## Objective
Swap both paid kits (Syncfusion, Radzen) for **MudBlazor** (MIT) on the **still-Blazor-
Server** app with full functional parity, so the `.razor` files arrive at Phase 3
already free and lightweight. Removes the Syncfusion license key (one fewer secret).

## Scope
- **In:** component swap, provider wiring, CSS cleanup, package removal, license removal.
- **Out:** WASM move, API, behavior changes beyond the noted "fix-while-here" bugs.

## Component mapping (only upload/grid/tabs are non-trivial)
| Current | MudBlazor | File(s) |
|---|---|---|
| `SfUploader` | `MudFileUpload<IBrowserFile>` | `FileUploadComponent.razor` |
| `SfGrid` (edit) | `MudDataGrid<T>` | `*TabItemContent.razor` |
| `SfTab` | `MudTabs`/`MudTabPanel` | `FileDisplayComponent.razor` |
| `SfButton`/`SfCheckBox` | `MudButton`/`MudCheckBox` | `OrderHandlingComponent.razor` |
| `RadzenTextBox`/`DropDown`/`Alert`/`Icon`/`Label`/`Card`/`Row`/`Column`/`Stack`/`ProgressBarCircular`/`ProfileMenu`/`TemplateForm` | `MudTextField`/`MudSelect`/`MudAlert`/`MudIcon`/`MudText`/`MudPaper`/`MudGrid`+`MudItem`/`MudStack`/`MudProgressCircular`/`MudMenu`/`MudForm` | multiple |
| `RadzenDataList` | `@foreach` + `MudTextField` | `MegaTradingTabItemContent.razor` |
- **Delete unused:** `Syncfusion.Blazor.PivotTable`, `InPlaceEditor`, `SplitButtons`, `Cards`.

## Work breakdown

### P1-T1 — Install & register MudBlazor
- **Files:** `ATAFurniture.Server.csproj`, `Startup.cs`, `Layout/MainLayout.razor`, `Pages/_Host.cshtml`, `_Imports.razor`
- **Do:** add `MudBlazor` (net10 build); `services.AddMudServices();`; add
  `@using MudBlazor` to `_Imports.razor`; providers into `MainLayout.razor`:
  ```razor
  <MudThemeProvider/> <MudPopoverProvider/> <MudDialogProvider/> <MudSnackbarProvider/>
  ```
  reference `_content/MudBlazor/MudBlazor.min.css` + `.min.js` + Roboto in `_Host.cshtml`.
- **Verify:** a trivial `<MudButton>` renders in the running app.
- **Depends on:** —

### P1-T2 — Shared spinner component
- **Files:** new `Components/Spinner.razor`
- **Do:** wrap `MudProgressCircular` (indeterminate) once; replace the 5 duplicated
  `RadzenProgressBarCircular` blocks.
- **Verify:** all five spots show the shared spinner.
- **Depends on:** P1-T1

### P1-T3 — Port the trivial 1:1 components
- **Files:** `NavMenu`, `Configuration`, `Index`, `LoginDisplay`, `UserCreditsComponent`,
  `TargetCompanySelectionComponent`, `MissingAccountInfo`, `OrderHandlingComponent`
- **Do:** mechanical swaps per the mapping table (text/button/alert/icon/card/layout/
  textbox/select/checkbox/menu/form).
- **Verify:** each page renders and its inputs bind.
- **Depends on:** P1-T1

### P1-T4 — Port the file uploader
- **Files:** `FileUploadComponent.razor`
- **Do:**
  ```razor
  <MudFileUpload T="IBrowserFile" Accept=".txt,.csv" MaximumFileCount="1"
                 FilesChanged="OnFileUpload">
    <ActivatorContent>
      <MudButton Variant="Variant.Filled" StartIcon="@Icons.Material.Filled.CloudUpload">
        Избери Polyboard файл</MudButton>
    </ActivatorContent>
  </MudFileUpload>
  ```
  read via `await file.OpenReadStream(maxAllowedSize: 10*1024*1024).CopyToAsync(ms);`.
  **Fix-while-here:** reset `_isLoadingFiles` in a `finally` (spinner-hang bug).
- **Verify:** upload → parse still populates `Context.Details`; error path clears the spinner.
- **Depends on:** P1-T1

### P1-T5 — Port the tab host
- **Files:** `FileDisplay/FileDisplayComponent.razor`
- **Do:** `SfTab`→`MudTabs`; keep the per-file loop + company `switch`:
  ```razor
  <MudTabs Rounded="true" ApplyEffectsToContainer="true">
    @foreach (var file in Context.Files) { <MudTabPanel Text="@file.FileName"> … </MudTabPanel> }
  </MudTabs>
  ```
  **Fix-while-here:** delete dead `OnTabCreated`; replace the 1s `Task.Delay(...).ContinueWith`
  with `await Task.Delay(...)` (or drop the artificial delay).
- **Verify:** one tab per file; switching tabs works.
- **Depends on:** P1-T1

### P1-T6 — Port the editable grids *(hardest — do last)*
- **Files:** `LoniraTabItemContent.razor`, `MegaTradingTabItemContent.razor`, `SuliverTabItemContent.razor`
- **Do:** `SfGrid`→`MudDataGrid`, read-only columns `Editable="false"`, editable ones `true`:
  ```razor
  <MudDataGrid T="LoniraDetail" Items="@_source" ReadOnly="false"
               EditMode="DataGridEditMode.Cell" EditTrigger="DataGridEditTrigger.OnRowClick">
    <Columns>
      <PropertyColumn Property="x => x.Height"     Title="Размер по фладер [мм]"   Editable="false"/>
      <PropertyColumn Property="x => x.Width"      Title="Размер срещу фладер [мм]" Editable="false"/>
      <PropertyColumn Property="x => x.Quantity"   Title="Брой детайли"            Editable="false"/>
      <PropertyColumn Property="x => x.LoniraEdges" Title="Кантиране"              Editable="false"/>
      <PropertyColumn Property="x => x.Note"       Title="Забележка"               Editable="true"/>
    </Columns>
    <PagerContent><MudDataGridPager/></PagerContent>
  </MudDataGrid>
  ```
  MegaTrading: also port the material-rename list (`RadzenDataList`→`@foreach`+`MudTextField`)
  and keep `OnMaterialsReplaced`. Edits mutate `_source` in place (feeds generation).
- **Verify:** edit a `Note`/edge cell → generate → the change appears in the output file, for each company.
- **Depends on:** P1-T5

### P1-T7 — Remove the old kits, license, and dead CSS
- **Files:** `.csproj`, `_Imports.razor`, `_Host.cshtml`, `Program.cs`, tab/display `<style>` blocks
- **Do:** remove all `Syncfusion.Blazor.*` + `Radzen.Blazor` package refs, their `@using`s
  and theme CSS/JS; delete the Syncfusion license registration + `SyncfusionLicenseKey`
  lookup in `Program.cs`; remove the `.e-grid`/`.e-tab` `<style>` blocks. Update
  [../../CONTEXT.md](../../CONTEXT.md) (§4 secrets: one fewer).
- **Verify:** `grep -ri "syncfusion\|radzen"` returns nothing; app builds and runs.
- **Depends on:** P1-T3..T6

## Sequencing
T1 → (T2, T3, T4, T5 in parallel) → T6 → T7. Commit after T3 (trivial swaps), after T6
(grids), and after T7 (removal). Keep the app runnable at each commit.

## Testing & verification
Manual click-through for all three companies: upload → tabbed review → **edit a cell** →
generate → download → email; plus the MegaTrading material-rename flow. (Automated tests
come in Phase 2+.)

## Rollback
Per-commit revert. T7 (removal) is the point of no return for the old kits — verify T6
thoroughly before it.

## Definition of done
- [x] Zero `Syncfusion.*` / `Radzen.*` references anywhere. *(Verified: package refs, `@using`s, CSS/JS, and DI registrations all removed; `grep -rE "Syncfusion|Radzen"` over source is empty.)*
- [x] No Syncfusion license key; `Program.cs` no longer registers one; CONTEXT.md updated. *(License block deleted from `Program.cs`; §4 secrets updated; `init-local-secrets.ps1` comment updated.)*
- [~] Full flow works for all companies on MudBlazor; one shared spinner; dead CSS gone. *(Shared `Components/Spinner.razor` in all five spots; `.e-grid`/`.e-tab` `<style>` blocks gone; solution builds clean. **End-to-end click-through per company still to be run manually** — see Testing & verification.)*
- [x] Fix-while-here bugs (spinner-hang, dead `OnTabCreated`, `ContinueWith`) resolved. *(Upload spinner reset in a `finally`; `OnTabCreated`/`OnTabItemSelected` deleted; `ContinueWith` replaced with `await Task.Delay`.)*

## Phase-specific risks
| Risk | Mitigation |
|---|---|
| `MudDataGrid` edit UX differs from `SfGrid` | Validated in Spike B; confirm edit→generate in P1-T6. |
| Visual regression (Material look) | Expected; restyle with MudBlazor theme; screenshot before/after. |
| No MudBlazor net10 build at start | Confirm in Spike B / P0-T4 before starting. |
