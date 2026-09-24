# ADR-0004: One browser-safe conversion domain shared by the Server and the PWA

- Status: Accepted
- Date: 2026-09-24
- Deciders: Pavel Pavlov

## Context

`Kroiko.Client.Blazor` must run the whole conversion in the browser, offline. Today only the last
step lives in `Kroiko.Domain`; the rest is Server code, and some of the domain is not browser-safe:

| Step | Where today | Problem for WASM |
|---|---|---|
| Parse the Polyboard file (11/23 fields) | Server `DetailsExtractorService` (takes `ILogger`) | not shared |
| Map to Lonira / Suliver / MegaTrading details | Server `Models/*Extensions.cs` | not shared |
| Group into files (Lonira: one per material) | Server `FileDisplayComponent.razor` | lives in the UI |
| Fill the template | Server `*TemplateBuilder` + domain `TemplateBuilderBase` | `File.ReadAllTextAsync` + `Assembly.Location` paths; reflection-based `JsonSerializer` (IL2026) |
| Build `.xlsx` / `.cut_mt` | domain `FileGeneratorService` | row providers use `Type.GetProperty` reflection (IL2070) |

Further forces:

- The caller wires each manufacturer by hand: keyed `ITemplateBuilder` + `ITableRowProvider` +
  `IFileNameProvider`, passed as **nullable** arguments to `FileGeneratorService.CreateFiles(…)`,
  with `generateTextFiles` decided by the caller. This is where the known null-deref crash lives.
- Numbers are turned into strings with the **current culture** (row-provider `.ToString()`,
  `double.TryParse` in `ExcelFileGenerator`, `.cut_mt` interpolation, the "СДВ" notes). In WASM the
  current culture follows the browser; on a `bg-BG` Edge `12.5` becomes `"12,5"`. The Server runs
  under en-US/invariant and has never seen it.
- The domain also carries Server-only types: `User` (EF entity with credits), `SupportedCompany.Email`
  and the Suliver/SuliverKuklensko pair that differs only by email, `ContactInfo.Email`, and dead
  `INotifyPropertyChanged` plumbing.
- `ATAFurniture.Server` stays deployed and must **behave identically**. Its existing smoke tests only
  check shapes (file counts, "PK" magic, six material rows).

## Decision

We will **move** the whole conversion pipeline (everything except the UI) into `Kroiko.Domain`,
make it browser-safe, and have **both** apps call it. "The Server stays untouched" means its
behaviour, not its source: the Server is rewired onto the domain and proven unchanged by tests.

1. **Interface.** Two entry points:

   ```csharp
   PolyboardParser.Parse(byte[] content) -> ParseResult(IReadOnlyList<Detail> Details,
                                                        IReadOnlyList<ParseError> Errors)

   interface IOrderFormat                       // one implementation per manufacturer
   {
       SupportedCompany Company { get; }
       IReadOnlyList<KroikoFile> CreateFiles(IReadOnlyList<Detail> details);    // map + group → editable
       IReadOnlyList<FileSaveContext> Generate(ContactInfo contact,
                                               IReadOnlyList<KroikoFile> files,
                                               string? differentEdgeColor);  // template + .xlsx (+ .cut_mt for MegaTrading)
   }

   OrderFormats.All / OrderFormats.For(company)  // static registry
   ```

   Template builders, table-row providers, file-name providers, the `.cut_mt` generator and
   `ExcelFileGenerator` become **`internal`**. Generation is **synchronous** (no I/O remains). The
   domain uses no DI; the Server may still register each `IOrderFormat` with keyed DI
   (`nameof(SupportedCompanies.X)`).
2. **Templates** stay as `template.json`, compiled into `Kroiko.Domain` as **embedded resources**,
   read with `GetManifestResourceStream` and deserialised through a **source-generated
   `JsonSerializerContext`**, producing a fresh sheet per build. No disk paths; the builders'
   optional `templatePath` constructor parameter is removed.
3. **Parser failures are reported, not handled.** The domain parser has no logger and never discards.
   The Server keeps today's behaviour exactly: if `Errors` is non-empty it logs them and continues
   with an empty list. What the PWA does with bad lines is decided by the known-bugs ticket.
4. **Culture.** Every number↔string conversion in the domain passes `CultureInfo.InvariantCulture`
   explicitly. The UI keeps whatever culture it wants for display.
5. **Explicit mappings.** Row providers use explicit per-manufacturer column lists (value selector +
   alignment) instead of reflection, preserving today's quirks (Lonira writes `""`, MegaTrading and
   Suliver write `null` for empty values).
6. **Conversion concepts only.** `User` moves to `ATAFurniture.Server`. The domain's
   `SupportedCompany` becomes `(Name, Translation)` with **three** manufacturers; order emails and the
   Kuklensko branch stay on the Server. `ContactInfo` becomes a plain `CompanyName` + `MobileNumber`
   type. The dead `INotifyPropertyChanged` on `Detail` goes.
7. **The compiler guards browser safety.** `Kroiko.Domain.csproj` sets `IsTrimmable` and
   `IsAotCompatible`, with `IL2026;IL2067;IL2070;IL2075;IL3050` as errors, and uses
   `Microsoft.CodeAnalysis.BannedApiAnalyzers` to ban `System.IO.File`, `System.IO.Directory`,
   `Assembly.Location` and `Environment.CurrentDirectory`.
8. **Golden tests first.** Before anything moves, characterization tests are committed and green
   on today's code: for each fixture × manufacturer, the worksheet XML unzipped from every `.xlsx`
   (with `System.IO.Compression`), the `.cut_mt` bytes, and the file names with the date normalised,
   generated under invariant culture. Plus a test that runs the pipeline under `bg-BG` and expects the
   same output.
9. **Tests live in a new `Kroiko.Domain.Tests`** (xUnit, net10) referencing only `Kroiko.Domain`.
   The order is: write the golden tests in `ATAFurniture.Server.Tests` behind one helper; refactor,
   changing only the helper's body; then move the tests (fixtures, golden files) in a separate commit.

## Consequences

- ✅ One source of truth for each manufacturer's format; a column change lands once, in both apps.
- ✅ The PWA sees three calls (`Parse`, `CreateFiles`, `Generate`), with no wiring or nullable
  arguments, which removes the null-deref crash for both apps.
- ✅ Templates are inside the DLL, so they are offline-cached with no service-worker changes.
- ✅ Browser-safety regressions (reflection, untyped JSON, filesystem) fail the build.
- ✅ Also fixes the known ambient-culture `.cut_mt` issue and the `SupportedCompanies` name collision
  (for the domain).
- ⚠️ The Server's source changes: its conversion code, the `OrderHandlingComponent` wiring, `User`,
  the manufacturer email lookup and `ContactInfo` usage. The golden tests are what back "identical".
- ⚠️ Server log lines for bad Polyboard lines lose the exception object and carry the `ParseError`
  reason instead.
- ⚠️ `ADR-0003` §7's "existing `IFileNameProvider`s" now reach the PWA through `IOrderFormat.Generate`;
  the names are unchanged and `FileNameSanitizer` still sits in the domain, called only by the PWA.
- Follow-up: the implementation guide for this phase; the known-bugs ticket decides the PWA's
  bad-line policy (the interface already allows any of them); LargeXlsx 2.x stays an open question.

## Alternatives considered

- **Copy the pipeline into the PWA** (Server literally untouched), or **freeze** the Server's copy and
  switch it later: two copies of every manufacturer's rules that would drift apart.
- **Template fetched from `wwwroot` over `HttpClient`**: needs a loader seam with two adapters for
  data that never differs, plus service-worker manifest work and a first-launch failure mode.
- **Templates rewritten as C# code**: the most trim-proof, but a rewrite whose fidelity depends on
  transcription, and less readable data.
- **Move the classes as-is** (keep the three keyed interfaces and `FileGeneratorService`'s signature):
  cheaper now, but the PWA inherits the wiring and the null-deref risk.
- **A single `OrderConverter` facade with a `switch`**: adding a manufacturer edits a central switch.
- **Pin culture in the PWA host** (`DefaultThreadCurrentCulture`): leaves a trap in shared code and
  changes MudBlazor's number/date display.
- **Parser returns just a list and keeps `ILogger`**: the PWA inherits the silent discard and fixing
  it later changes an interface both apps use.
- **Keep reflection with `[DynamicallyAccessedMembers]`**: trim-safe, but keeps string-based columns.
- **Shape-only tests, or reading `.xlsx` back with ClosedXML**: too weak, or a new dependency.
- **Keep all tests in `ATAFurniture.Server.Tests`**: nothing would prove the domain stands alone.

## Related

- Ticket: [Making Kroiko.Domain browser-safe without changing Server behaviour](https://github.com/PavelRPavlov/Kroiko/issues/21)
- Map: [Offline installable Blazor WASM PWA (no backend)](https://github.com/PavelRPavlov/Kroiko/issues/15)
- Facts: [.NET 10 Blazor WASM as an offline, installable PWA — facts](https://github.com/PavelRPavlov/Kroiko/issues/17)
- [ADR-0003](0003-save-order-files-to-picked-folder.md) — file names and `FileNameSanitizer`.
- Informs: [Share or copy the Server's conversion UI into the PWA](https://github.com/PavelRPavlov/Kroiko/issues/22),
  [Fix or replicate the known conversion bugs in the PWA](https://github.com/PavelRPavlov/Kroiko/issues/23),
  [What proves parity, and how it's tested](https://github.com/PavelRPavlov/Kroiko/issues/24)
