# 05 — Saving

- **Status:** In progress
- **Depends on:** [04 Conversion flow](04-conversion-flow.md)  **Can run alongside:** [06 Updates & About](06-updates-and-about.md)
- **ADRs:** [0003](../adr/0003-save-order-files-to-picked-folder.md); research: [browser multi-file saving](../research/browser-multi-file-saving.md)

## Goal

Where the browser supports it (Chromium: Edge, Chrome), the primary action after generating is
**"Запази в папка…"**. It writes every file into a folder the operator picks, never overwrites, and
lists the final names. Every file in the list is also a download link. "Изтегли всички" (phase 04)
remains the primary action where folders are unavailable.

## Steps

### 1. Clash naming (pure C#)

A pure function: given the sanitised target names and the names already in the folder, return the
final names, appending ` (2)`, ` (3)`, … before the extension (`Egger W1000 (2).xlsx`). It must also
handle clashes **between** the files of one save. Unit tests cover no clash, one clash, a run of
clashes, names without an extension, and two generated files with the same name.

Done. `ClashNaming.FinalNames(targetNames, existingNames)`
(`Kroiko.Client.Blazor/Conversion/ClashNaming.cs`, the PWA's save layer, next to `ConverterState`)
returns one final name per target, in order: the name itself when it is free, otherwise the name
with the first free number from 2 before the extension (from the last dot, as `FileNameSanitizer`
reads it), or at the end of a name without one. Each name it gives is taken for the files after it,
so two files of one save never share a name. Names compare ignoring case, as a Windows folder does
(`egger w1000.XLSX` in the folder makes `Egger W1000.xlsx` clash). Its input is the
`FileNameSanitizer` names (`ConverterState.SavedFileName`); nothing calls it yet (step 2).
`ClashNamingTests` in `Kroiko.Client.Tests` covers no clash, one clash, a run of clashes, names
without an extension, two or more generated files with the same name, a case-only clash, and a
target named like a numbered file.

### 2. "Запази в папка…"

- Extend `wwwroot/js/files.js`: feature-detect `showDirectoryPicker`; open it with `startIn` =
  the last directory handle, stored per device in **IndexedDB** (handles cannot go into
  `localStorage`); list the existing names; write the files under the final names from step 1.
- Open the picker **directly from the click**. Generation is already done, so the writes fit the
  activation window (ADR-0003 §2). Do not await anything slow before calling it.
- Results: success → a Bulgarian confirmation listing the final names, and `ConverterState`'s saved
  flag is set. `AbortError` → nothing. `SecurityError` / `NotAllowedError` → a message pointing to
  the downloads (ADR-0003 §6).
- No folder permission is persisted or re-checked between saves (ADR-0003 §3).

Done. `ConverterState.SaveToFolderAsync` (`Kroiko.Client.Blazor/Conversion/`) orchestrates the save behind `IFolderPicker`
(`IFolderPicker.cs`), whose `PickAsync` ends picked (an `IPickedFolder` that lists its entry names and writes one file),
cancelled or blocked. It calls the picker before it awaits anything, so the pick keeps the click's user activation; then it
lists the folder — every entry, subfolders included, since a file cannot take a subfolder's name either — writes each
generated file under its `ClashNaming.FinalNames` name, and on success sets the saved flag and `FolderSave` (the folder's name
and the final names), which `OrderHandlingComponent` shows as a success alert: „Файловете са записани в папка „…“:" and the
list. A cancel (`AbortError`, which the spec also uses when the operator does not allow writing) does nothing; a blocked
picker (`SecurityError` / `NotAllowedError`) raises „Браузърът не позволява запис в папка. Изтеглете файловете с „Изтегли
всички“."; a picker, list or write that fails raises „Файловете не можаха да бъдат записани в папката. Изтеглете ги с
„Изтегли всички“." and saves nothing (files already written stay; the next save numbers around them). An edit meanwhile
stops the writes, as it stops the downloads; downloading and saving to a folder never run together, and nothing is generated
while either runs. `BrowserFolderPicker` calls `files.js`: `canSaveToFolder` (feature detection, and it starts loading the last
folder so the click need not wait for it), `pickFolder` (`showDirectoryPicker({ mode: 'readwrite', startIn })` with the last
folder's handle from IndexedDB — database `kroiko`, store `folders`, key `last` — stored again after each pick; a stored folder
that no longer exists makes the picker open at its default, per the
[spec](https://wicg.github.io/file-system-access/#api-showdirectorypicker)), `listNames` and `writeFile` (`getFileHandle(name,
{ create: true })` → `createWritable` → `write` → `close`). "Запази в папка…" appears only where `canSaveToFolder` is true,
next to "Изтегли всички"; which one is primary is step 3. Not handled: a ` (n)` suffix can push a name past what the file system
allows (nothing caps the length; such a write fails with the message above), and a file created in the folder between the
listing and the write would be overwritten.
Tests: `ConverterStateTests` (writes, clash names, a second save, cancel, blocked, failures, busy and the picker opening first,
an edit meanwhile, the confirmation cleared), `FolderPickerRegistrationTests` (the `files.js` interop, faked), and
`E2E/FolderSaveTests`, which stubs `showDirectoryPicker` with an origin-private folder (`navigator.storage.getDirectory()`) so
IndexedDB, listing, writing and the confirmation run in Chromium: the files match the golden files, a save after a reload
opens at the last folder and gets ` (2)` names, the stub saw the click's user activation, cancel and blocked behave, and
without `showDirectoryPicker` only "Изтегли всички" is offered. Its main test runs on a profile on disk
(`PublishedApp.NewPersistentContextAsync`): in Playwright's off-the-record contexts, reading a file-system handle back from
IndexedDB crashes the page (found with OPFS handles; whether an InPrivate window does the same with a real folder is unchecked).
The real picker in Edge stays manual (ADR-0007 §5).

### 3. Download links and the primary action

- Every file name in the generated list is a download link (the phase 04 download helper, one
  file). A click sets the saved flag.
- The primary button is "Запази в папка…" where the picker exists, otherwise "Изтегли всички"
  (which stays available as a secondary action in Chromium).
- E2E: a per-file link downloads that one file, with its sanitised name. The folder picker itself
  is **not** automated (ADR-0007 §5); add it to the release checklist's per-release items
  ([07](07-hosting-and-go-live.md) 07b step 1).

## Done criteria

- [x] Clash naming is a pure, unit-tested function; it never produces a name that already exists.
- [ ] In Edge, "Запази в папка…" writes all files, opens at the last folder, never overwrites and lists the final names (manual, noted in the PR).
- [x] Cancel does nothing; a blocked picker shows the fallback message.
- [ ] Every generated file is a download link; folder save and link clicks set the saved flag.
- [ ] Browsers without `showDirectoryPicker` show "Изтегли всички" as the primary action (checked in Firefox, noted in the PR).
- [ ] The per-file download E2E test passes.

## Out of this phase

- The unsaved-work confirmation on reload → [06](06-updates-and-about.md).
- Zip downloads (rejected, ADR-0003).
