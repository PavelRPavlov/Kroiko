# Browser options for saving several generated files locally

> Research for [Kroiko#19](https://github.com/PavelRPavlov/Kroiko/issues/19), which feeds the
> pending decision [Kroiko#20](https://github.com/PavelRPavlov/Kroiko/issues/20), "How generated order files are saved to the user's machine".
> Scope: facts, not the decision. Researched 2026-09-23.
> Target: standalone Blazor WebAssembly PWA (`Kroiko.Client.Blazor`, .NET 10, no backend),
> Chrome/Edge on Windows, offline, including when installed as a PWA.
>
> Legend: **[F]** verified fact with a primary source. **[L]** verified by a local check
> (method described). **[I]** inference, not verified directly.

## The problem

- Lonira produces one `.xlsx` per material, named `{Material}.xlsx`
  (`Kroiko.Domain/TemplateBuilding/Lonira/LoniraFileNameProvider.cs`). Several files per
  order is the normal case, and names contain Cyrillic.
- MegaTrading produces `{date}_{Company}.xlsx` plus `{Company}.cut_mt`
  (`MegaTradingFileNameProvider.cs`, `TextFileGeneration/MegaTradingFileGenerator.cs`).
- Material and company names come from user data, so they can contain characters such
  as `/`, `:`, `"` or `*` that Windows does not allow in file names.
- The files are small (kilobytes to low megabytes), so size limits are not the constraint.

## Summary table

| Option | Chrome/Edge (Windows) | Firefox / Safari | Several files in one click | Cyrillic names | Offline | Notes |
|---|---|---|---|---|---|---|
| **A. N plain downloads** (`Blob` + `<a download>`, fed by `DotNetStreamReference`) | Yes | Yes | The first file downloads silently. The 2nd and later files show the **"download multiple files"** prompt once per origin. After the user allows it, the choice is stored. | Yes. Illegal characters are replaced with `_`. | Yes (`blob:` URL) | Files go to the Downloads folder, or one Save-As dialog per file if the user turned on "Ask where to save". |
| **B. One `.zip`** (`System.IO.Compression.ZipArchive`, or SharpCompress, which LargeXlsx already uses) plus one download | Yes | Yes | One download, no prompt | Yes. `ZipArchive` sets the UTF-8 flag, and Explorer reads the names correctly [L]. | Yes | Adds about 108 KB of trimmed IL (about 40 KB Brotli). The user has to extract the zip. |
| **C. `showDirectoryPicker({mode:'readwrite'})`, then write N files** | Yes, since 86 | **No** | One folder pick per click, then any number of writes without further prompts | Yes, but **illegal names throw an error instead of being sanitized** | Yes | The handle can be stored in IndexedDB. An installed PWA keeps the permission automatically. A browser tab can offer "Allow on every visit" (Chrome 122+). |
| **D. `showSaveFilePicker` per file** | Yes, since 86 | **No** | One dialog per file | Yes | Yes | Only practical for a single file, for example the MegaTrading pair or a single zip. |

## 1. Plain downloads (`Blob` + `<a download>`, `DotNetStreamReference`)

### How Blazor does it
- **[F]** Microsoft recommends streaming files under about 250 MB to a JS `ArrayBuffer` through a
  `DotNetStreamReference`. The JS side then wraps the buffer in a `Blob`, calls
  `URL.createObjectURL`, clicks a temporary `<a download=fileName>` and calls `URL.revokeObjectURL`.
  The docs call revoking the URL "an important step to ensure memory isn't leaked on the client".
  The whole file is loaded into client memory.
  [Blazor file downloads (.NET 10)](https://learn.microsoft.com/en-us/aspnet/core/blazor/file-downloads?view=aspnetcore-10.0#download-from-a-stream)
- **[F]** `download` only works for same-origin, `blob:` and `data:` URLs. `/` and `\` in the
  suggested name are converted to `_`, and "browsers will adjust the suggested name if necessary".
  [MDN `<a>`: download](https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/a#download)

### Several downloads in a row (Chrome/Edge)
- **[F]** Chromium's `DownloadRequestLimiter` behaves as follows. Each tab starts in
  `ALLOW_ONE_DOWNLOAD`. The first download is allowed and moves the tab to
  `PROMPT_BEFORE_DOWNLOAD`. Only a mouse click, Enter, Space or a navigation resets the state.
  Any further download while in `PROMPT_BEFORE_DOWNLOAD` shows the prompt.
  [download_request_limiter.h](https://chromium.googlesource.com/chromium/src/+/main/chrome/browser/download/download_request_limiter.h)
  So a single "Save" click that triggers N `<a>.click()` calls gets **one silent download
  and then one prompt** covering the rest.
- **[F]** The user's answer is stored as the per-origin content setting `AUTOMATIC_DOWNLOADS`
  (`SetContentSettingDefaultScope`), so the prompt does not come back on later visits.
  Chrome's Safety Hub may auto-revoke the setting if the site goes unused for a long time.
  [download_request_limiter.cc](https://chromium.googlesource.com/chromium/src/+/main/chrome/browser/download/download_request_limiter.cc)
- **[F]** Admins can pre-allow the behaviour:
  - **Edge 110+:** `DefaultAutomaticDownloadsSetting` and `AutomaticDownloadsAllowedForUrls`. Edge describes its default as "A user gesture is required for each additional download".
    [Edge policy](https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/defaultautomaticdownloadssetting)
  - **Chrome:** the equivalent policies are listed as supported from **Chrome 151**.
    [Chromium policy definition](https://chromium.googlesource.com/chromium/src/+/main/components/policy/resources/templates/policy_definitions/ContentSettings/AutomaticDownloadsAllowedForUrls.yaml)
- **[I]** The same limiter applies inside an installed PWA window, because it is keyed on the
  tab or WebContents, not on the display mode. This has not been observed directly.

### File names (Cyrillic and illegal characters)
- **[F]** Chromium rejects or replaces only these characters in file names: `"*/:<>?\|`, control
  characters, format (`Cf`) characters and Unicode non-characters. Cyrillic letters are legal.
  [base/i18n/file_util_icu.cc](https://chromium.googlesource.com/chromium/src/+/main/base/i18n/file_util_icu.cc)
  For downloads, illegal characters are **sanitized** (replaced), not rejected (MDN, above).

### Size limits
- **[F]** Chromium's in-memory blob limit is 2 GB on desktop x64, with disk-backed blobs beyond
  that. [storage/browser/blob/README.md](https://chromium.googlesource.com/chromium/src/+/main/storage/browser/blob/README.md#blob-storage-limits)
  Microsoft's 250 MB guidance is about memory pressure, not a hard limit. Neither matters
  for Kroiko's file sizes.

### Offline
- **[I]** `blob:` URLs are created and resolved in the page's memory, with no network fetch, so
  this path works offline. This follows from how the Blob URL mechanism works and was not tested offline.

## 2. One `.zip` bundle

- **[F]** `System.IO.Compression` has a dedicated `browser` build in dotnet/runtime `release/10.0`.
  Only ZipCrypto/WinZip-AES encryption and Zstandard are replaced with `PlatformNotSupported`
  stubs on browser. Plain Deflate zip is supported.
  [System.IO.Compression.csproj](https://github.com/dotnet/runtime/blob/release/10.0/src/libraries/System.IO.Compression/src/System.IO.Compression.csproj)
- **[L]** The .NET 10.0.12 `browser-wasm` runtime pack ships `libz.a` and
  `libSystem.IO.Compression.Native.a`, and the default `dotnet.native.wasm` already exports
  `CompressionNative_Deflate*` and `CompressionNative_Inflate*`. The native zlib is
  therefore always shipped, so using zip costs no extra native code. Checked with `grep` on the
  NuGet cache.
- **[L]** A trimmed publish (`dotnet publish -c Release`, .NET 10.0.12) of an empty Blazor WASM app
  plus a page that writes a `ZipArchive` with Cyrillic entry names gave these results:
  - **0 trim warnings**.
  - One new asset, `System.IO.Compression.wasm`, at **108 KB** (40 KB `.br`, 47 KB `.gz`).
  - It ran correctly in a Chromium browser: 20 KB of input deflated to a 324-byte zip, with no console errors.
  - The probe lived only in the scratchpad and was not added to the solution.
- **[F]/[L]** For non-ASCII entry names, `ZipArchive` writes UTF-8 and sets general-purpose bit 11
  (`UnicodeFileNameAndComment = 0x800`).
  [ZipArchiveEntry.cs](https://github.com/dotnet/runtime/blob/release/10.0/src/libraries/System.IO.Compression/src/System/IO/Compression/ZipArchiveEntry.cs)
  A local check on Windows 11 (build 26200) read the zip through Explorer's own zip handler
  (`Shell.Application` / zipfldr). It **listed and extracted `ПДЧ 18мм бяло.xlsx` correctly**.
- **[L]** An alternative already in the bundle: `LargeXlsx 1.12.0` depends on **SharpCompress 0.39**
  (its `ZipWriter` and managed Deflate), not on `System.IO.Compression`. A spike in commit `3fb1efe`
  already validated LargeXlsx in trimmed WASM. Zipping with SharpCompress therefore adds no new
  assembly, while `System.IO.Compression` adds about 108 KB.
- **[I]** Drawbacks: the user gets one `.zip` and must extract it before uploading files to the
  manufacturer. That is an extra step compared with loose files.

## 3. File System Access API

### Support
- **[F]** `showSaveFilePicker`, `showDirectoryPicker` and `showOpenFilePicker` are available in Chrome 86+, and in Edge
  (which mirrors Chrome, so also 86+). **Firefox and Safari do not support them.** They are marked experimental and are not Baseline.
  [MDN browser-compat-data `api/Window.json`](https://github.com/mdn/browser-compat-data/blob/main/api/Window.json),
  [MDN showDirectoryPicker](https://developer.mozilla.org/en-US/docs/Web/API/Window/showDirectoryPicker),
  [chromestatus: File System Access (M86)](https://chromestatus.com/feature/6284708426022912)
- **[F]** Firefox 111+ and Safari 26 have `createWritable` only for the origin-private file system
  (OPFS), which is not the user's disk. OPFS does not help here. (Same BCD source, `FileSystemFileHandle.json`.)
  Some secondary write-ups list "Firefox 111 / Safari 15.2" as File System Access support. Those
  numbers refer to OPFS.
- **[F]** The spec is a WICG Draft Community Group Report: "not a W3C Standard nor is it on the
  W3C Standards Track". [WICG spec](https://wicg.github.io/file-system-access/)
- **[F]** An Edge admin policy, `DefaultFileSystemWriteGuardSetting = 2`, blocks all FSA write access
  (Edge 86+). [Edge policy](https://learn.microsoft.com/en-us/deployedge/microsoft-edge-browser-policies/defaultfilesystemwriteguardsetting)

### User activation
- **[F]** The pickers and `requestPermission()` require **transient user activation** and throw
  `SecurityError` without it. [WICG spec](https://wicg.github.io/file-system-access/),
  [MDN showSaveFilePicker](https://developer.mozilla.org/en-US/docs/Web/API/Window/showSaveFilePicker)
  After the user picks, the spec runs "activation notification". This refreshes activation so that
  follow-up permission requests work.
- **[F]** Chromium's transient activation lifetime is **5 seconds**
  (`kActivationLifespan = base::Seconds(5)`).
  [user_activation_state.h](https://github.com/chromium/chromium/blob/main/third_party/blink/public/common/frame/user_activation_state.h)
- **[F]** Cancelling the picker rejects with `AbortError` (MDN, above).

### Writing several files into a picked folder
- **[F]** After `showDirectoryPicker({ mode: 'readwrite' })` is granted, the app calls
  `dirHandle.getFileHandle(name, { create: true })`, then `createWritable()`, `write()` and `close()`
  for each file. These calls do not need user activation. They are gated only by the handle's permission state.
  [WHATWG File System spec](https://fs.spec.whatwg.org/#api-filesystemdirectoryhandle),
  [Chrome: File System Access](https://developer.chrome.com/docs/capabilities/web-apis/file-system-access)
- **[F]** **Chromium rejects illegal names instead of sanitizing them.** `getFileHandle` checks
  `IsSafePathComponent`, which runs `base::i18n::IsFilenameLegal`. Names containing `"*/:<>?\|` or
  control characters, and extensions `.lnk`, `.scf`, `.url`, `{CLSID}` and other "dangerous" types, fail with
  "Name is not allowed". Cyrillic passes.
  [file_system_access_manager_impl.cc](https://github.com/chromium/chromium/blob/main/content/browser/file_system_access/file_system_access_manager_impl.cc)
  → **The app must sanitize `{Material}` itself before writing.** Downloads sanitize
  silently; FSA does not.
- **[F]** The browser may refuse sensitive folders such as the Windows system folders
  ([Chrome docs](https://developer.chrome.com/docs/capabilities/web-apis/file-system-access)), and
  then rejects with `AbortError` ([MDN](https://developer.mozilla.org/en-US/docs/Web/API/Window/showDirectoryPicker)).

### Permission persistence and installed PWA
- **[F]** `FileSystemHandle`s are serializable and can be stored in **IndexedDB**. On a later visit
  the app calls `queryPermission()` or `requestPermission()` on the stored handle.
  [Chrome docs](https://developer.chrome.com/docs/capabilities/web-apis/file-system-access)
- **[F]** **Chrome 122+** adds persistent permissions:
  - In a browser tab, `requestPermission()` on a restored handle shows a three-way prompt:
    "Allow this time", "Allow on every visit" or "Don't allow".
  - **"Installed apps will automatically persist permissions once the user grants access"**, so
    they get no three-way prompt.

  [Chrome blog: persistent permissions](https://developer.chrome.com/blog/persistent-permissions-for-the-file-system-access-api)
- **[I]** Edge is Chromium-based and probably inherits this behaviour. No Microsoft source confirming it
  was found, so it needs a smoke test in Edge.

### Firefox/Safari fallback
- **[F]** Neither browser has the pickers (BCD, above). The fallback is feature detection
  (`'showDirectoryPicker' in window`), then option A or option B.

## 4. Blazor-specific interop gotchas

- **[F]** Byte arrays passed through `IJSRuntime` are transferred in binary, "avoid[ing]
  encoding/decoding byte arrays into Base64" (.NET 6+). `DotNetStreamReference` streams to JS as
  an `ArrayBuffer` (`streamRef.arrayBuffer()`) or a `ReadableStream` (`streamRef.stream()`).
  [Call JS from .NET: byte arrays and streams](https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/call-javascript-from-dotnet?view=aspnetcore-10.0#byte-array-support)
- **[F]** "For client-side components, the framework doesn't impose a limit on the size of
  JavaScript (JS) interop inputs and outputs". The 32 KB SignalR limit applies only to server-side components.
  [JS interop: size limits](https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/?view=aspnetcore-10.0#size-limits-on-javascript-interop-calls)
- **[F]** With `leaveOpen: false` (the default), a `DotNetStreamReference` disposes its stream after
  transmission. Transmission starts as soon as the reference is passed, so only pass it when JS
  will consume it. (Same page, "Stream from .NET to JavaScript".)
- **[F]** A zero-copy option is `[JSImport]` with `ArraySegment<byte>` or `Span<byte>`, marshalled as a
  `MemoryView`, which is not copied. A `MemoryView` over an `ArraySegment` pins the array until
  `dispose()` is called. Plain arrays **are** copied.
  [JSImport/JSExport interop](https://learn.microsoft.com/en-us/aspnet/core/client-side/dotnet-interop/?view=aspnetcore-10.0)
  At Kroiko's file sizes, the documented `DotNetStreamReference` path is sufficient.
- **[I]** **User activation across Blazor async code.** A Blazor `@onclick` handler runs .NET code
  that then calls JS. The 5-second activation window starts at the click. If the handler first generates
  all files (parsing plus LargeXlsx) and only then calls `showDirectoryPicker`, it risks a
  `SecurityError` on slow machines. The safe order is **pick the folder first (in the click), then generate
  and write**, or use a stored handle whose permission is already `granted`.
- **[I]** Every JS call in WASM is async and single-threaded. Writing N files one after another
  through JS interop is fine at these sizes.

## Implications for #20 (inferences)

- **The only option that works everywhere with one gesture and no prompt is B (one zip).** It
  costs about 40 KB Brotli, or nothing extra with SharpCompress, which is already in the bundle.
  The cost is an extraction step for the user.
- **A (N downloads)** works everywhere, but the *first* multi-file save shows Chrome/Edge's
  "download multiple files" prompt. After the user allows it once per origin, it is smooth. Files
  land in Downloads with sanitized names. This is the lowest-effort option.
- **C (pick a folder, write N files)** gives the best experience on Chrome/Edge, especially as an
  installed PWA: pick the folder once, and permission persists automatically. It needs:
  - IndexedDB handle storage.
  - `queryPermission` / `requestPermission` handling.
  - **Own file-name sanitization.**
  - A fallback for Firefox/Safari and for Edge machines where `DefaultFileSystemWriteGuardSetting` blocks writes.
- A natural combined design is **C when available, otherwise A or B**. Both paths need the
  same file-name sanitizer, which belongs in `Kroiko.Domain` and should have tests.
- D (one Save-As per file) does not scale to Lonira's multi-file output.

## Open items and unverified points

- Behaviour inside an installed PWA window was not observed directly for options A and C on Edge. A manual smoke test is recommended.
- Firefox's handling of several downloads in a row was not researched, because the target is Chrome/Edge.
- Whether revoking the object URL immediately after `click()` (as the Microsoft sample does) is
  reliable for N back-to-back downloads was not tested.
