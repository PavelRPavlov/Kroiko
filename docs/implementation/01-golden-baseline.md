# 01 — Golden baseline

- **Status:** Not started
- **Depends on:** —  **Can run alongside:** [03 Client shell](03-client-shell.md)
- **ADRs:** [0004](../adr/0004-shared-browser-safe-conversion-domain.md) §8–9, [0007](../adr/0007-parity-and-test-strategy.md) §1–3

## Goal

Every order file the Server produces **today** is recorded byte for byte as a golden file, and a
test proves that today's code still reproduces it. This phase changes **no production code**. It
is the safety net that phase 02's refactor, the Server rewiring and the LargeXlsx bump are
measured against.

## Steps

### 1. `Kroiko.Testing` and the synthetic fixtures

- Add a class library **`Kroiko.Testing`** (net10.0, nullable on, implicit usings on), referencing
  `Kroiko.Domain`, to `TextConverter.sln`. `ATAFurniture.Server.Tests` references it.
- Move every fixture into `Kroiko.Testing/TestData/polyboard/`: all of
  `ATAFurniture.Server.Tests/TestFiles/*.txt` plus the root `TestFiles/Wardrobes-Niki-i-Toni.txt`.
  Mark them as content copied to the output directory so that every referencing test project gets them.
- Rename them to **neutral, descriptive, kebab-case** names that say what the fixture exercises,
  e.g. `Kitchen Alex.txt` → `kitchen-8-materials.txt`, `Wardrobes-Niki-i-Toni.txt` →
  `wardrobes-23-field.txt`, `invalid format.txt` → `bad-field-count.txt`. Check each file's field
  count and material count, and name it by what you find. Do not change any file's **content**.
  If two fixtures are byte-identical, delete one.
- Delete the now-empty `ATAFurniture.Server.Tests/TestFiles/` and root `TestFiles/`, and point
  `GenerationSmokeTests` at the new paths (a `TestData.Polyboard(name)` helper in `Kroiko.Testing`).
- Remove the unused `Snapshooter` / `Snapshooter.Xunit` references (ADR-0007 §1).

**Green:** `dotnet test` — the six existing smoke tests pass unchanged.

### 2. `OrderFilesAssert` and the golden-file layout

In `Kroiko.Testing`:

- **`OrderFilesAssert.MatchGolden(fixture, manufacturer, IReadOnlyList<FileSaveContext> files)`**
  compares generated files with `TestData/golden/<fixture>/<manufacturer>/`:
  - for each `.xlsx`: every `xl/worksheets/*.xml` entry, unzipped with `System.IO.Compression`,
    compared as text. Only worksheet XML counts, because zip timestamps and other package parts
    are not part of the output contract;
  - for each `.cut_mt`: the bytes, with the generation date normalised to a fixed token;
  - `names.txt`: the file names in generation order, date normalised the same way.
  - On a mismatch the message names the fixture, the manufacturer, the file and the first
    differing line.
- **The update switch.** With the environment variable `UPDATE_GOLDEN=1`, `MatchGolden` writes the
  golden files into the **source** tree (resolve the path to `Kroiko.Testing/TestData/golden/`
  from the test assembly location, not the output copy) instead of comparing, and the test passes.
  Golden files are content copied to the output like the fixtures.
- Keep date normalisation in one place: a regex for the date format the file-name providers and
  the `.cut_mt` header use today.

### 3. Golden tests on today's code

In `ATAFurniture.Server.Tests`, add `GoldenTests` driven by **one helper**:

```csharp
// The only place that knows how today's Server turns a fixture into order files.
// Phase 02 changes this method's body — and nothing else in the tests.
static IReadOnlyList<FileSaveContext> RunPipeline(string fixture, string manufacturer, string? differentEdgeColor = null)
```

- The body reproduces today's live flow exactly: `DetailsExtractorService` → the
  `To*Details()` extensions → the grouping currently done in `FileDisplayComponent.razor` (copy it
  verbatim, with a comment pointing at the source) → the keyed builder, row provider and
  file-name provider → `FileGeneratorService.CreateFiles` with MegaTrading's `generateTextFiles: true`.
- It uses a fixed synthetic contact (e.g. `Тест ООД`, `0888123456`) and pins
  `CultureInfo.CurrentCulture`/`CurrentUICulture` to `InvariantCulture` for the duration, restoring them afterwards.
- A `[Theory]` over **every valid fixture × Lonira / Suliver / MegaTrading** calls
  `OrderFilesAssert.MatchGolden`. Add one Suliver case with a `differentEdgeColor` value, so the
  "СДВ" notes path is recorded.
- Fixtures with more than 6 materials are recorded for MegaTrading as they are today, with the truncated
  `.cut_mt` header, as characterization (ADR-0007 §7).
- Generate the golden files once with `UPDATE_GOLDEN=1`, **review them** (open a few `.xlsx` files in
  Excel; check that the `.cut_mt` files hold `╪` and Cyrillic correctly), then commit them.

### 4. The culture test (recorded, expected to fail today)

- Add a test that runs the same theory with `CurrentCulture = bg-BG` and expects the **same**
  golden output.
- On today's code it fails: `.cut_mt` and row values use the ambient culture (CONTEXT.md §7).
  Commit it with `[Theory(Skip = "Un-skipped in phase 02 step 4 — invariant culture")]`, after
  confirming locally that it does fail for that reason and no other.

## Done criteria

- [ ] `Kroiko.Testing` exists in the solution; all fixtures live in `Kroiko.Testing/TestData/polyboard/` with neutral names; the old fixture folders are gone.
- [ ] No fixture file name refers to a person or a customer.
- [ ] `OrderFilesAssert` compares worksheet XML, `.cut_mt` bytes and `names.txt`, with dates normalised; `UPDATE_GOLDEN=1` rewrites the source golden files.
- [ ] `GoldenTests` covers every valid fixture × all three manufacturers, plus the Suliver different-edge-colour case, through a single `RunPipeline` helper.
- [ ] The golden files are committed and were reviewed in Excel.
- [ ] The `bg-BG` test exists, is skipped with a reason pointing at phase 02, and fails only because of culture when un-skipped.
- [ ] `dotnet test` is green; no file under `ATAFurniture.Server/` or `Kroiko.Domain/` changed.

## Out of this phase

- Any change to production code, including the culture fix → [02](02-shared-domain.md).
- New fixtures (LF, BOM, whitespace lines, bad lines, >6 materials) → [02](02-shared-domain.md) step 2.
- `Kroiko.Domain.Tests` and moving the golden tests → [02](02-shared-domain.md).
