# ADR-0008: Upgrade LargeXlsx to 2.x behind the golden tests

- Status: Accepted
- Date: 2026-09-24
- Deciders: Pavel Pavlov

## Context

`Kroiko.Domain` writes every `.xlsx` with **LargeXlsx 1.12.0**, which depends on **SharpCompress
0.39.0**. That version carries a moderate advisory (GHSA-6c8g-7p36-r338). The domain is shared:
it runs in the Server and, per [ADR-0004](0004-shared-browser-safe-conversion-domain.md), in the
browser.

Facts, from NuGet and the project's release notes:

- **LargeXlsx 2.0.0** multi-targets .NET Core 3.1+ and .NET Standard 2.0. On .NET Core 3.1+ it has
  **no dependency on SharpCompress** (it uses `System.IO.Compression`). It also adds an async API and a
  native-UTF-8 writer for performance. **2.0.1** encodes illegal XML characters as `_xHHHH_`.
  **2.0.2** (latest) only updates SharpCompress for the .NET Standard target.
- Dropping SharpCompress removes the advisory, and it also removes a compression library from the
  WASM download and from the trim analysis that ADR-0004 turns into errors.
- The new writer may serialize the worksheet XML **differently** (bytes) even where Excel shows
  identical content. The golden tests ([ADR-0007](0007-parity-and-test-strategy.md)) compare exactly that XML.
- ADR-0007 §7: golden files are the Server's output, and a golden diff in a PR is the reviewable record
  of an output change.

## Decision

We will upgrade `Kroiko.Domain` to **LargeXlsx 2.0.2** as the **last step of the shared-domain phase**,
in its own PR, after the pipeline has moved and the golden tests pass on 1.12. The golden baseline
stays the output of the Server as deployed today.

The golden diff decides what happens:

1. **No diff** → merge.
2. **Serialization-only diff** (the same cell values, types and styles, spelled differently in XML) →
   regenerate with `UPDATE_GOLDEN=1`, open every changed file in Excel once, and describe the diff in the PR.
3. **Any value change** → stop. That is a decision for Pavel, not part of this ADR.

The synchronous API stays (`Generate` is synchronous, ADR-0004 §1).

## Consequences

- ✅ SharpCompress and its advisory leave both apps, and the WASM payload loses a library.
- ✅ The golden tests turn the upgrade into a reviewable diff instead of a leap of faith.
- ⚠️ The Server's `.xlsx` bytes may change after its next deploy (the shared domain), accepted under
  rule 2 because Excel shows the same content.
- ⚠️ Cells with illegal XML control characters are now written as `_xHHHH_` instead of whatever 1.12 did.
  Polyboard exports are not expected to contain them; if a golden file shows one, rule 3 applies.

## Alternatives considered

- **Upgrade before recording the golden files**: the baseline would no longer be the deployed Server's
  output, and the upgrade's own effect would never be visible.
- **Stay on 1.12 and pin a patched SharpCompress directly**: keeps a compression library the platform
  already provides, in a browser payload, for no gain.
- **Leave it**: keeps a known advisory in shipped code.

## Related

- Ticket: [Phases and order of the PWA implementation guides](https://github.com/PavelRPavlov/Kroiko/issues/31)
- Map: [Offline installable Blazor WASM PWA (no backend)](https://github.com/PavelRPavlov/Kroiko/issues/15)
- [ADR-0004](0004-shared-browser-safe-conversion-domain.md) — the shared domain and its trim analyzers.
- [ADR-0007](0007-parity-and-test-strategy.md) — golden files as the record of output changes.
- Implemented by: [02 — Shared domain](../implementation/02-shared-domain.md), step 9.
