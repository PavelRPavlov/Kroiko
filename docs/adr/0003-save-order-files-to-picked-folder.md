# ADR-0003: Save order files to a user-picked folder, with a download fallback

- Status: Accepted
- Date: 2026-09-23
- Deciders: Pavel Pavlov

## Context

`Kroiko.Client.Blazor` generates order files entirely in the browser; there is no Blob Storage
and no email sending to hand them off. One order produces several files:

| Manufacturer | Files | Name (from `Kroiko.Domain`'s `IFileNameProvider` / text generator) |
|---|---|---|
| Lonira | one `.xlsx` **per material** — often many | `{Material}.xlsx` |
| Suliver | one `.xlsx` | `{yyyy-MM-dd}_{CompanyName}.xlsx` |
| MegaTrading | one `.xlsx` + one `.cut_mt` | `{yyyy-MM-dd}_{CompanyName}.xlsx`, `{CompanyName}.cut_mt` |

The Server shows one download link per file. The browser options
([Browser options for saving several generated files locally — facts](https://github.com/PavelRPavlov/Kroiko/issues/19),
[`docs/research/browser-multi-file-saving.md`](../research/browser-multi-file-saving.md)) are:

- **Downloads** work everywhere, but Chrome/Edge ask once per origin to allow multiple downloads,
  and they silently replace illegal characters in the name with `_`.
- **`showDirectoryPicker`** (Chrome/Edge only; the PWA installs only there anyway) writes any number
  of files after one folder pick, but needs a user gesture (≈5 s window), can be blocked by Edge
  policy, and **rejects** illegal names ("Name is not allowed") instead of sanitizing them.
- **A `.zip`** works everywhere without a prompt, but the operator must extract it every time before
  attaching the files to the email to the manufacturer.

Names are built from free text (material names, the end customer's company name), so they can
contain `\ / : * ? " < > |`. Lonira names carry no date, so two orders using the same board
produce the same file name.

## Decision

We will save into a folder the user picks on each save, with per-file downloads always available
as the fallback. No zip.

1. **Two steps.** "Генерирай бланки за поръчка" generates the files in memory and lists them.
   Nothing is saved automatically. Changing the input discards the generated files (as today).
2. **Save to folder.** Where `showDirectoryPicker` exists, the primary action is
   **"Запази в папка…"**. It opens the folder picker (a fresh gesture — generation is already
   done, so the writes fit the activation window) and writes every file into the chosen folder.
3. **Folder is picked on every save.** The picker opens at the last folder used (`startIn` = the
   last directory handle, stored per device in IndexedDB). No folder permission is persisted or
   re-checked between saves; the pick itself grants access.
4. **Never overwrite.** If a target name already exists in the folder, append ` (2)`, ` (3)`, …
   before the extension (`Egger W1000 (2).xlsx`). The confirmation lists the final names, so
   renamed files are visible.
5. **Downloads are always there.** Every file name in the list is a download link (`Blob` +
   `<a download>`, streamed via `DotNetStreamReference`). Where folder saving is unavailable, the
   primary action becomes **"Изтегли всички"**, which triggers the downloads one after another.
6. **Failures.** Cancelling the picker (`AbortError`) does nothing. A blocked picker
   (`SecurityError` / `NotAllowedError`, e.g. Edge policy) shows a message and points to the
   downloads.
7. **Names are the Server's names.** The PWA uses the existing `IFileNameProvider`s and the
   `.cut_mt` name unchanged. A new pure **`FileNameSanitizer`** in `Kroiko.Domain`, called **only by
   the PWA's save layer**, runs before the ` (n)` suffix:
   - replaces `\ / : * ? " < > |` and control characters with `_` (what browsers do for downloads,
     so both paths produce the same name);
   - trims trailing dots and spaces;
   - prefixes Windows reserved names (`CON`, `PRN`, `AUX`, `NUL`, `COM1`–`COM9`, `LPT1`–`LPT9`)
     with `_`;
   - falls back to `поръчка` (keeping the extension) if nothing is left.
8. **"Saved" for the update prompt** ([ADR-0002](0002-pwa-updates-reload-prompt.md) §2): an order
   counts as saved once, since the last generation, a folder save has succeeded or at least one
   download has been triggered.

## Consequences

- ✅ On Chrome/Edge a whole order — however many Lonira files — is saved with one click and one
  folder pick, and nothing needs extracting before attaching to an email.
- ✅ No earlier order's file is ever lost; renames are shown to the user.
- ✅ Works in every browser through downloads, and the per-file links match today's Server UX.
- ✅ The Server is untouched: name providers keep their output; the sanitizer is only called from the PWA.
- ⚠️ Two save paths to build and test, plus a small JS module (picker, handle storage in
  IndexedDB, existence checks, writes) — the File System Access API is a WICG draft, not a standard.
- ⚠️ Suffixed names (`… (2).xlsx`) differ from what the manufacturer usually receives; the operator
  must pick the right file when re-saving an order into the same folder.
- ⚠️ "Изтегли всички" triggers Chrome/Edge's one-time "download multiple files" prompt; admins can
  pre-allow it (Edge `DefaultAutomaticDownloadsSetting`).
- Follow-up: `FileNameSanitizer` needs unit tests (illegal characters, Cyrillic, reserved names,
  trailing dots, empty result) and the suffixing logic needs tests; the Bulgarian texts for the
  confirmation and error messages; a manual smoke test of multiple downloads and the picker inside
  an **installed Edge PWA window** belongs on the release/parity checklist.

## Alternatives considered

- **Remembered root folder with an automatic subfolder per order** — zero dialogs after the first
  pick, but needs persisted permission and re-checks; the user preferred choosing the location
  for each order.
- **One `.zip` per order** — works everywhere with no prompt, but adds an extract step to every
  order and gets in the way of attaching separate `.xlsx` files to the manufacturer's email.
- **Downloads only** — least code, but Lonira orders mean many clicks or the multi-download prompt,
  and files land in the Downloads folder mixed with everything else.
- **Overwrite silently / ask before overwriting** — overwriting can lose an earlier order's file;
  asking adds a dialog. Automatic suffixing was preferred.
- **One button that picks the folder, then generates and saves** — fewer clicks, but no chance to
  review the list first, and a large generation could exceed the picker's activation window.
- **New file names (e.g. dated Lonira names) or sanitizing inside the name providers** — would
  change the Server's output; the names are left to the known-bugs decision if they need to change.

## Related

- Ticket: [How generated order files are saved to the user's machine](https://github.com/PavelRPavlov/Kroiko/issues/20)
- Map: [Offline installable Blazor WASM PWA (no backend)](https://github.com/PavelRPavlov/Kroiko/issues/15)
- [ADR-0002](0002-pwa-updates-reload-prompt.md) — its unsaved-work guard uses the "saved" rule in §8.
- Research: [`docs/research/browser-multi-file-saving.md`](../research/browser-multi-file-saving.md)
- Sources: [Blazor file downloads](https://learn.microsoft.com/en-us/aspnet/core/blazor/file-downloads?view=aspnetcore-10.0),
  [`showDirectoryPicker` (MDN)](https://developer.mozilla.org/en-US/docs/Web/API/Window/showDirectoryPicker)
