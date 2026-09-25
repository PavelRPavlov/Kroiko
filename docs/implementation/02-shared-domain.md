# 02 — Shared domain

- **Status:** In progress
- **Depends on:** [01 Golden baseline](01-golden-baseline.md)  **Can run alongside:** [03 Client shell](03-client-shell.md)
- **ADRs:** [0004](../adr/0004-shared-browser-safe-conversion-domain.md), [0006](../adr/0006-known-conversion-bugs-in-pwa.md) §1–3, [0003](../adr/0003-save-order-files-to-picked-folder.md) §7, [0007](../adr/0007-parity-and-test-strategy.md) §1–2, §8, [0008](../adr/0008-upgrade-largexlsx-to-2.md), [0009](../adr/0009-cut-mt-line-endings-crlf.md)

## Goal

The whole conversion pipeline (parse, map, group, fill the template, build `.xlsx` / `.cut_mt`)
lives in a browser-safe `Kroiko.Domain` behind `PolyboardParser.Parse` and one `IOrderFormat` per
manufacturer. The compiler rejects browser-unsafe code. `ATAFurniture.Server` calls the domain and
produces **exactly the golden output from phase 01**. The domain's tests live in their own
project, and LargeXlsx is on 2.x.

## Rules for this phase

- **Golden files do not change** in steps 1–8. If a step changes one, the step is wrong.
  The only planned golden change is step 9 (ADR-0008).
- The golden tests' `RunPipeline` helper is the only test code that follows the refactor. Its
  **body** changes as calls move into the domain, its signature does not.
- Server behaviour stays as it is today, including its known quirks: bad lines → empty list and a
  log entry; no `Check`; the >6-material truncation. Exception: the `.cut_mt` line ending is
  CRLF on every host since 02.5b ([ADR-0009](../adr/0009-cut-mt-line-endings-crlf.md)), so a Linux-hosted
  Server's `.cut_mt` changes from LF to CRLF.

## Steps

### 1. Conversion-only domain types

- `User` moves to `ATAFurniture.Server` (namespace change only; EF mapping unchanged, no migration).
- `SupportedCompany` becomes `(Name, Translation)` with exactly **three** manufacturers in the domain.
  The Server keeps its own mapping for order emails and for the Kuklensko branch (which resolves to
  the Suliver format with its own email).
- `ContactInfo` becomes a plain `CompanyName` + `MobileNumber` type. The Server keeps the email
  wherever it needs it (e.g. a Server-side record that wraps `ContactInfo`).
- Remove the dead `INotifyPropertyChanged` plumbing from `Detail` and the other domain types.

Done. `User` is `ATAFurniture.Server.DataAccess.User`; the migrations still name the entity
`Kroiko.Domain.User`, and the model has no pending changes against the snapshot
(`HasPendingModelChanges()` is false, pinned by `UserModelTests` until step 8), so there is no migration. Its
`LastSelectedCompany` is the Server's `ManufacturerBranch(Name, Translation, Email)` with the same
columns; `ManufacturerBranches` holds the four branches with today's labels and emails (Kuklensko is a
branch named `Suliver`), pinned by `ManufacturerBranchesTests` (by `OrderFormatRegistrationTests` since step 8). The domain's `SupportedCompany` is a
`(Name, Translation)` record and `ContactInfo` a `(CompanyName, MobileNumber)` record, both nullable
as the Server may pass a profile without them. The Server's fields bind to `ContactInfoModel` (still
INPC, so `ConverterContext` keeps re-raising contact edits, and it carries the `Email`), which hands
the domain `ToContactInfo()`. `User` keeps its INPC: `UserCreditsComponent` listens to it.

**Green:** full `dotnet test`, golden files unchanged.

### 2. `PolyboardParser`, then the ADR-0006 parser fixes

Two PRs.

- **2a — move.** `PolyboardParser.Parse(byte[] content) → ParseResult(Details, Errors)` in the domain.
  `ParseError` is structured: line number, a `ParseErrorKind` (e.g. `FieldCount`, `InvalidNumber`),
  plus the field count or the field name. There is no logger and nothing is discarded. The Server's
  `DetailsExtractorService` becomes a thin adapter that logs each error and returns an empty list
  when there are any (ADR-0004 §3), so behaviour is unchanged. Create **`Kroiko.Domain.Tests`**
  (xUnit 2.9.3, FluentAssertions `[7.1.0]`, referencing `Kroiko.Domain` and `Kroiko.Testing`) for
  the parser's own tests: each `ParseErrorKind`, and both field formats.
- **2b — fix (ADR-0006 §1).** Split on `\r\n`, `\n` or `\r`; strip a leading UTF-8 BOM; skip empty
  and whitespace-only lines. Add synthetic fixtures derived from existing ones: LF-only, CR-only,
  BOM, whitespace lines, and a bad-lines fixture with more than 10 bad lines (used by phase 04's
  "…и още N"). Parser tests prove that each variant yields the same Details as its CRLF original.
  Add a Server test proving that a bad-line file still yields an empty list plus a log entry.

2a done. `Kroiko.Domain/CellsExtracting/PolyboardParser.Parse` returns a `ParseResult` with one
`ParseError(LineNumber, Kind, FieldCount?, Field?)` per bad line (`FieldCount` carries the line's field
count, `InvalidNumber` the `Detail` property name of the line's first bad field) and the Details of every
good line. It reads numbers as the Server did (invariant culture, a `,` is a thousands separator, an
empty numeric field is `0`, a flag is true only when `1`), except that integers are now invariant too. `DetailsExtractorService`
logs each error and returns an empty list when there are any. `Kroiko.Domain.Tests` holds
`PolyboardParserTests`; `GoldenTests` and `GenerationSmokeTests` still go through the adapter.

2b done. The parser splits on `\r\n`, `\n` or `\r`, strips a leading UTF-8 BOM and skips empty and
whitespace-only lines; a `ParseError`'s line number is still the physical line, blank lines included. New
fixtures in `Kroiko.Testing/TestData/polyboard/`: `wardrobes-4-materials-lf`, `cabinet-23-field-cr`,
`wardrobes-4-materials-bom` and `cabinet-23-field-whitespace-lines` (each its CRLF original with only that
changed; `.gitattributes` marks them `-text` so git keeps their bytes), and `bad-lines-12` (12 bad lines of
both kinds among 20, for phase 04's "…и още N"). `PolyboardParserTests` proves each variant parses to its
original's Details. `DetailsExtractorServiceTests` (Server) pins a bad-line file → empty list + one error
log entry, and that an LF-only file now parses.

### 3. Embedded templates

- The three `template.json` files become `EmbeddedResource`s of `Kroiko.Domain` (drop the
  `CopyToOutputDirectory` items), read with `GetManifestResourceStream`.
- Deserialise them through a source-generated `JsonSerializerContext`, producing a fresh sheet per build.
- Move the Server's `*TemplateBuilder`s into the domain. Remove the optional `templatePath`
  constructor parameter and every `File.*` / `Assembly.Location` use.

Done. `Kroiko.Domain.csproj` embeds `TemplateBuilding/*/template.json` (manifest names
`Kroiko.Domain.TemplateBuilding.<Manufacturer>.template.json`) and no longer copies them to the output.
`TemplateBuilderBase.ReadTemplate(manufacturer, typeInfo)` deserialises a fresh sheet from the resource stream on
every call through the source-generated `TemplateJsonContext` (default options, as before). The three builders
live in `Kroiko.Domain/TemplateBuilding/<Manufacturer>/`, take only their `ITableRowProvider` (no `templatePath`,
no DI attribute: the domain has no DI), and return their sheets without I/O. `Startup` and the golden helper's copy
of it register each builder with a factory that passes the keyed row provider. `TemplateBuilderTests` (domain)
proves each builder works with no path and starts every build from a fresh template; the golden files are unchanged.

### 4. Explicit column mappings and invariant culture

- Replace the row providers' `Type.GetProperty` reflection with explicit per-manufacturer column
  lists (value selector + alignment), keeping today's quirks: Lonira writes `""`, MegaTrading and
  Suliver write `null` for empty values.
- Pass `CultureInfo.InvariantCulture` to every number↔string conversion: row values,
  `ExcelFileGenerator`'s `double.TryParse`, the `.cut_mt` interpolation, the "СДВ" notes.
- **Un-skip the `bg-BG` golden test.** It must now pass.

Done. Each row provider lists its columns in order as `TableColumn<TDetail>(Value, ContentAlignment)`
(`Kroiko.Domain/TemplateBuilding/TableColumn.cs`), with numbers written through `TableColumn.Invariant`; Lonira
maps an empty value to `""`, MegaTrading and Suliver pass it through (`null` stays `null`), and `Rotated` is
still `True`/`False`. `ExcelFileGenerator` reads a centred value back with `double.TryParse(…, Float |
AllowThousands, InvariantCulture)`, the invariant form of what the Server's hosts did. The `.cut_mt` rows,
the "СДВ" notes (still in the Server's `LoniraExtensions`/`SuliverExtensions` until step 5), `Cell.GetCellName`
and the Suliver/MegaTrading file-name dates are invariant too. The `bg-BG` golden theory runs and passes
against the unchanged golden files. `TableRowProviderTests` pins each manufacturer's columns, alignments and
empty-value quirk under `bg-BG`; `GeneratorCultureTests` the `.xlsx` number cells and the `.cut_mt` decimals;
the Server's `OversizeNoteTests` the notes. A trial build of `Kroiko.Domain` with `-p:IsTrimmable=true
-p:IsAotCompatible=true` reports no IL warnings (it had two IL2070s).

### 5. `IOrderFormat` and the Server rewiring

- Add `IOrderFormat` (`Company`, `CreateFiles`, `Generate`) with one implementation per manufacturer,
  and the static `OrderFormats.All` / `OrderFormats.For(company)`. `CreateFiles` holds the
  `To*Details()` mapping (moved from the Server's `Models/*Extensions.cs`) and the grouping
  (moved from `FileDisplayComponent.razor`; Lonira = one file per material). `Generate` is synchronous.
- Make the builders, row providers, file-name providers, the `.cut_mt` generator and
  `ExcelFileGenerator` `internal`. `FileGeneratorService` disappears into the formats, and so do its
  nullable arguments, which removes the null-deref crash.
- Rewire the Server: keyed DI registers one `IOrderFormat` per `nameof(SupportedCompanies.X)`; the
  Server's `FileDisplayComponent` / `OrderHandlingComponent` call `CreateFiles` / `Generate`.
  `MegaTradingViewModel` stays in the **Server** (its UI is untouched). Delete what is now unused.
- Change the **body** of `RunPipeline` to `PolyboardParser.Parse` → `OrderFormats.For(m).CreateFiles`
  → `Generate`.
- Run the Server locally once and convert a fixture per manufacturer. The downloaded files must match
  the golden files (a manual smoke check of the rewired UI; say so in the PR).

Done, except the manual smoke check. `IOrderFormat` and `OrderFormats` live in the domain's root namespace
(`Kroiko.Domain/IOrderFormat.cs`, `OrderFormats.cs`); `OrderFormats.For` finds a format by the company's `Name`
and throws `ArgumentException` for an unknown one. `LoniraOrderFormat`, `SuliverOrderFormat` and
`MegaTradingOrderFormat` (in `TemplateBuilding/<Manufacturer>/`, all `internal`) hold the `To*Details()` mapping
moved from the Server's `Models/*Extensions.cs` (the "СДВ" notes still invariant) and the grouping moved from
`FileDisplayComponent` (Lonira: one file per non-empty material, in order of first use; the others: one file, none
for no Details). The shared `OrderFormatBase.Generate` fills the template, the different-edge-colour cell and the
`{CompanyName}` in the file names; MegaTrading puts its `.cut_mt` first. `FileGeneratorService`, the
`IExcelFileGenerator`/`ITextFileGenerator` interfaces and every `async` in generation are gone; the builders, row
providers, file-name providers, sheets, cells, `ExcelFileGenerator` and `MegaTradingFileGenerator` are `internal`
(`InternalsVisibleTo` `Kroiko.Domain.Tests`). Every `.cut_mt` line ends in one place (`AppendRow`). The Server's
`AddOrderFormats()` registers each format as a keyed singleton by its name;
`FileDisplayComponent` calls `CreateFiles` and `OrderHandlingComponent` calls `Generate`; `MegaTradingExtensions`
keeps only the view-model conversions. `RunPipelineAsync` is now `PolyboardParser.Parse` → `OrderFormats.For(m)
.CreateFiles` → `Generate`. `OrderFormatsTests` (domain) pins the registry, the grouping and the invariant
"СДВ" notes (they replace the Server's `OversizeNoteTests`); `OrderFormatRegistrationTests` (Server) pins that every
`SupportedCompanies` key and every dropdown branch resolves a format, and that Kuklensko resolves to the Suliver
format with its own email. The golden files are unchanged. **02.5b:** `AppendRow` appends `"\r\n"` instead of
calling `AppendLine`, so the `.cut_mt` is CRLF on every host instead of the host's `Environment.NewLine` (LF on
Linux and in WASM), and the CRLF golden files no longer depend on the OS that runs the tests
([ADR-0009](../adr/0009-cut-mt-line-endings-crlf.md)); an `OrderFormatsTests` case asserts CRLF explicitly.
**Still to do:** the manual smoke check of the rewired
UI against the golden files, once per manufacturer.

### 6. `Check` / `OrderProblem` and `FileNameSanitizer`

- `IOrderFormat.Check(files) → IReadOnlyList<OrderProblem>` (ADR-0006 §3). MegaTrading returns
  `TooManyMaterials(Max, Materials)` for more than 6 distinct `Material` values; the others return
  empty. The 6 is **one named constant**, used by both `Check` and the `.cut_mt` generator. The
  Server does not call `Check`.
- `FileNameSanitizer` (pure, in the domain, ADR-0003 §7): illegal characters and control characters
  → `_`, trailing dots/spaces trimmed, reserved Windows names prefixed with `_`, fallback `поръчка`
  keeping the extension. It is not called by the Server.
- Tests in `Kroiko.Domain.Tests`: `Check` at 6 and 7 materials, including after a rename that merges
  two materials; `FileNameSanitizer` on illegal characters, Cyrillic, reserved names, trailing
  dots/spaces, an empty result, and extension preservation.

Done. `IOrderFormat.Check(files)` returns `IReadOnlyList<OrderProblem>`; `OrderProblem` is an abstract record and
`TooManyMaterials(Max, Materials)` its one case (`Kroiko.Domain/OrderProblem.cs`, root namespace). `OrderFormatBase.Check`
returns none; `MegaTradingOrderFormat.Check` counts the distinct `Material` values of the files as they are now (so after
the operator's renames), in order of first use, through the generator's `GroupByMaterial` that also fills the header, and returns `TooManyMaterials` listing all of them when there are more
than `MegaTradingFileGenerator.MaxMaterials` (6), the constant whose loop now writes the `.cut_mt` header rows. The
Server does not call `Check`. `FileNameSanitizer.Sanitize(name)` (`Kroiko.Domain/FileNameSanitizer.cs`, next to
`OrderFormats`) replaces `\ / : * ? " < > |` and control characters (`char.IsControl`) with `_`, trims trailing dots and
spaces, falls back to `поръчка` plus the extension (from the last dot) when the name before it is empty or only dots
and spaces (e.g. `.cut_mt` for an empty company name), and prefixes `_` when the part before the first dot, trailing
spaces aside, is a reserved Windows name in any case (`CON.xlsx`, `nul .xlsx`). Nothing calls it yet (phase 05).
`OrderFormatCheckTests` covers 6 and 7 materials, a rename that merges two and one that splits one, and Lonira/Suliver;
`FileNameSanitizerTests` covers each rule, Cyrillic names left as they are, and the extension. The golden files are unchanged.

### 7. The compiler guards browser safety

In `Kroiko.Domain.csproj`: `IsTrimmable` and `IsAotCompatible` set to `true`, with
`IL2026;IL2067;IL2070;IL2075;IL3050` as errors; `Microsoft.CodeAnalysis.BannedApiAnalyzers` with a
`BannedSymbols.txt` banning `System.IO.File`, `System.IO.Directory`, `Assembly.Location` and
`Environment.CurrentDirectory`. The build must be clean without any suppressions. If one seems
needed, stop: something browser-unsafe survived.

Done. `Kroiko.Domain.csproj` sets `IsTrimmable` and `IsAotCompatible` and adds `IL2026;IL2067;IL2070;IL2075;IL3050`
and `RS0030` (the banned-API diagnostic, a warning by default) to `WarningsAsErrors`. It references
`Microsoft.CodeAnalysis.BannedApiAnalyzers` 5.6.0 (`PrivateAssets=all`, so it does not flow to the projects that reference the domain) with
`Kroiko.Domain/BannedSymbols.txt` as an `AdditionalFiles` item, banning the four APIs, each with the reason shown in the
error. The domain builds with no IL or RS diagnostics and has no suppressions (no `#pragma`, `NoWarn`,
`SuppressMessage` or trim annotations). The guard was proven by building a temporary file that used each banned API
and one call per IL code: the build failed with all nine errors (four RS0030, IL2026, IL3050, IL2067, IL2070,
IL2075), and the file was removed. The golden files are unchanged.

### 8. Move the tests

Separate commits, in this order (ADR-0004 §9, refined by ADR-0007 §2):

1. Move `GoldenTests` to `Kroiko.Domain.Tests`. `RunPipeline` now needs only the domain.
2. Delete `GenerationSmokeTests` (its coverage is in the golden and parser tests).
3. Leave `ATAFurniture.Server.Tests` with 3–4 Server-only tests (ADR-0007 §8): DI resolves an
   `IOrderFormat` for every `SupportedCompanies` key; a bad-line file yields an empty list and a log
   entry; Kuklensko resolves to the Suliver format with its own email.

Done. `GoldenTests` (31 cases before and after: 15 invariant, 15 `bg-BG`, the Suliver edge-colour case) and
`OrderFilesAssertTests`, the tests of the comparison they use, live in `Kroiko.Domain.Tests`; `RunPipelineAsync`
resolves its format through `FormatTestData.FormatNamed`, and `Kroiko.Testing`'s internals are visible to
`Kroiko.Domain.Tests` only. `UPDATE_GOLDEN=1` still records into `Kroiko.Testing/TestData/golden/` in the source
tree, as `OrderFilesAssertTests` checks from the new project. `GenerationSmokeTests` is gone: its Detail and material
counts are in `PolyboardParserTests`, its bad line in `DetailsExtractorServiceTests`, its files in the golden tests.
`ATAFurniture.Server.Tests` keeps five tests, each on the Server's wiring onto the domain: `OrderFormatRegistrationTests`
(every `SupportedCompanies` key resolves its `IOrderFormat`; every dropdown branch keeps its label and order email and
resolves a format, which absorbs `ManufacturerBranchesTests`; Kuklensko resolves to the Suliver format with its own
email) and `DetailsExtractorServiceTests` (a bad-line file → no Details and one error log entry; an LF-only file → its
Details and no log entry, the adapter's only clean-file test now that the golden tests call the parser directly).
`UserModelTests`, the step 1 guard that moving `User` out of the domain left the schema alone, is gone: it is not a
wiring test (ADR-0007 §8), and a Server-side model change, e.g. to the owned `ManufacturerBranch`, surfaces when its
migration is added. The golden files are unchanged.

### 9. LargeXlsx 2.0.2 (ADR-0008)

Its own PR, once steps 1–8 are merged.

- Bump `LargeXlsx` to `2.0.2` in `Kroiko.Domain` (and remove any direct Server reference to it).
  Keep the synchronous API. Confirm that `SharpCompress` no longer appears in
  `dotnet list package --include-transitive` for `Kroiko.Domain`.
- Run the golden tests and act on the diff as ADR-0008 says: no diff → merge; a serialization-only
  diff → regenerate with `UPDATE_GOLDEN=1`, open each changed file in Excel, and describe the diff
  in the PR; any **value** change → stop and ask.

Done, with **no golden diff** (ADR-0008 rule 1): `Kroiko.Domain` references `LargeXlsx` 2.0.2 and nothing else at
runtime, the Server's direct 1.12.0 reference is gone (it gets 2.0.2 through the domain), and the code did not change
(the synchronous `XlsxWriter` API is the same). `SharpCompress` and `ZstdSharp` are gone from the transitive graph of
both `Kroiko.Domain` and `ATAFurniture.Server`, and the build no longer reports NU1902. All 31 golden cases pass
against the phase-01 recording. Outside the golden files' scope (the worksheet XML), a full-package comparison of all
34 `.xlsx` from 1.12 and 2.0.2 showed byte-identical worksheets and `.cut_mt` files, the same value, type and resolved
style for every cell, and differences only in three other parts: `workbook.xml` (UTF-8 BOM dropped),
`sharedStrings.xml` (BOM dropped, a newline after the XML declaration; it is empty, as every string is inline),
`styles.xml` (a newline after the XML declaration) and `docProps/app.xml` (`LargeXlsx/2.0.2` as the application). The domain still builds with its guard, and a trimmed self-contained publish of a probe calling
every `IOrderFormat` reported no IL warnings.

## Done criteria

- [x] `PolyboardParser.Parse`, `IOrderFormat` (`CreateFiles`, `Generate`, `Check`) and `OrderFormats` are the only public conversion entry points; builders, providers and generators are `internal`.
- [x] The domain has no `User`, no email, no INPC; `SupportedCompany` has three manufacturers.
- [x] Templates are embedded resources read through a source-generated JSON context.
- [x] `Kroiko.Domain` builds with the trim/AOT analyzers as errors and the banned-API list, with no suppressions.
- [x] The golden tests pass under invariant **and** `bg-BG` culture, and live in `Kroiko.Domain.Tests`.
- [x] LF, CR, BOM and whitespace-line fixtures parse to the same Details as their CRLF originals.
- [x] `Check` and `FileNameSanitizer` are covered by domain tests; the 6-material limit is one constant.
- [ ] `ATAFurniture.Server` is rewired, and its UI was smoke-checked once per manufacturer against the golden files.
- [x] `ATAFurniture.Server.Tests` holds only the Server-only tests; `GenerationSmokeTests` is gone.
- [x] LargeXlsx is 2.0.2 and SharpCompress is gone from the domain's dependency graph; any golden change is explained in its PR.
- [x] [CONTEXT.md](../../CONTEXT.md) §4/§7 updated: the "not yet implemented" notes for ADR-0004 are removed, and the fixed known issues are marked fixed.

## Out of this phase

- Anything in `Kroiko.Client.Blazor` → [03](03-client-shell.md), [04](04-conversion-flow.md).
- The Bulgarian bad-line text and the ">6 materials" alert (the PWA writes them) → [04](04-conversion-flow.md).
- Any change to the Server's UI beyond the rewiring onto `IOrderFormat`.
