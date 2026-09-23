# .NET 10 Blazor WebAssembly as an offline, installable PWA — facts

> Research for [Kroiko#17](https://github.com/PavelRPavlov/Kroiko/issues/17) (parent map #15).
> Feeds decisions [#18 "How installed users receive new versions"](https://github.com/PavelRPavlov/Kroiko/issues/18)
> and [#16 "Where the PWA is hosted"](https://github.com/PavelRPavlov/Kroiko/issues/16).
> Researched 2026-09-23 against .NET SDK 10.0.401 / runtime packages 10.0.12.

**Legend.** **[V]** = verified against a primary source (link given) or by a local
measurement (method given). **[I]** = inference / recommendation drawn from verified facts.

---

## TL;DR

- **[V]** The `--pwa` option still exists in .NET 10 and produces the same model as .NET 8:
  `service-worker.js` (dev, no-op) + `service-worker.published.js` (cache-first,
  precaches everything in a build-generated `service-worker-assets.js` with SHA-256
  integrity hashes, atomic versioned cache). Offline works only for **published** builds.
- **[V]** .NET 10 changed the plumbing around it: `blazor.boot.json` is gone (inlined into
  `dotnet.js`), every `_framework` file is fingerprinted, Blazor's own Cache-Storage
  boot cache (`BlazorCacheBootResources`) was **removed** (so no more double caching
  with the SW), the template now registers the SW with `updateViaCache: 'none'`, and the
  environment comes from `WasmApplicationEnvironmentName`, not a `Blazor-Environment` header.
- **[V]** Default update lifecycle: a new deployment is **downloaded in the background on a
  visit/launch**, but only **activates after every app window/tab is closed**. A reload is
  not enough. So an installed user normally sees a new version on the **launch after** the
  one that downloaded it. `skipWaiting` is possible but gives up the consistency guarantee.
- **[V]** Install on Chrome/Edge (Windows) needs HTTPS + a manifest (name, 192 and 512 icons,
  `start_url`, `display`). Chrome's own install prompt still needs a SW with a `fetch`
  handler. The template's manifest already meets these requirements.
- **[V, measured]** Trimmed MudBlazor + LargeXlsx probe: **about 3.9 MB Brotli / 13.8 MB raw** precached
  (**about 3.3 MB / 11.1 MB** with `InvariantGlobalization`). AOT is not worth it here (about 2x size
  for CPU-bound gains we don't need).
- **[V]** Small settings: `localStorage` (≤5 MiB, sync) is fine. It sits in the same
  per-origin, best-effort bucket as IndexedDB and Cache Storage. `navigator.storage.persist()`
  is auto-granted/denied without a prompt on Chromium (installed or engaged sites are favoured).
  **All of it is tied to the origin**, so the hosting URL must never change.

---

## 1. Does the `--pwa` template still exist and work in .NET 10? What changed since .NET 8?

### Verified

- `dotnet new blazorwasm -o MyBlazorPwa --pwa` is still the documented way to create one, and the same
  `ServiceWorkerAssetsManifest` property and `<ServiceWorker Include=… PublishedContent=…>` item convert an
  existing app. — [Blazor PWA docs (.NET 10)](https://learn.microsoft.com/en-us/aspnet/core/blazor/progressive-web-app/?view=aspnetcore-10.0)
- The .NET 10 template csproj
  ([`ComponentsWebAssembly-CSharp.csproj.in`, release/10.0](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/ProjectTemplates/Web.ProjectTemplates/ComponentsWebAssembly-CSharp.csproj.in))
  sets `<OverrideHtmlAssetPlaceholders>true</OverrideHtmlAssetPlaceholders>` and, under `PWA`,
  `<ServiceWorkerAssetsManifest>service-worker-assets.js</ServiceWorkerAssetsManifest>` +
  `<ServiceWorker Include="wwwroot\service-worker.js" PublishedContent="wwwroot\service-worker.published.js" />`.
- The .NET 10 template `wwwroot`
  ([release/10.0](https://github.com/dotnet/aspnetcore/tree/release/10.0/src/ProjectTemplates/Web.ProjectTemplates/content/ComponentsWebAssembly-CSharp/wwwroot))
  ships `manifest.webmanifest`, `icon-192.png`, `icon-512.png`, `service-worker.js`, `service-worker.published.js`.
  `index.html` has `<link rel="preload" id="webassembly" />`, `<script type="importmap"></script>`,
  `<script src="_framework/blazor.webassembly#[.{fingerprint}].js">` and
  `navigator.serviceWorker.register('service-worker.js', { updateViaCache: 'none' })`.
- **Local check:** `dotnet new blazorwasm --pwa -f net10.0` + `dotnet publish -c Release` on SDK 10.0.401 works.
  The output has a versioned `service-worker.js` (first line `/* Manifest version: l1BRE5wD */`), a `service-worker-assets.js`
  listing fingerprinted URLs such as `_framework/Microsoft.AspNetCore.Components.f7fgzluddv.wasm` with
  `sha256-…` hashes, and an `index.html` rewritten to `_framework/blazor.webassembly.w3qd1tpl0e.js` plus a filled import map.

### What changed .NET 8 → .NET 10 (all [V])

| Area | .NET 8 | .NET 10 | Source |
|---|---|---|---|
| Boot manifest | `_framework/blazor.boot.json` | **Removed**; boot config inlined into `dotnet.js` | [What's new in .NET 10](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0?view=aspnetcore-10.0), [caching & integrity (.NET 10)](https://learn.microsoft.com/en-us/aspnet/core/blazor/host-and-deploy/webassembly/bundle-caching-and-integrity-check-failures?view=aspnetcore-10.0) |
| Framework file names | un-hashed | **fingerprinted** (`Name.{hash}.wasm`, `dotnet.{hash}.js`, `blazor.webassembly.{hash}.js`) via `OverrideHtmlAssetPlaceholders` + import map | [Static files: fingerprint standalone WASM](https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/static-files?view=aspnetcore-10.0#fingerprint-client-side-static-assets-in-standalone-blazor-webassembly-apps), local publish |
| Blazor's own boot-resource cache | Cache Storage `dotnet-resources-*`, toggled by `BlazorCacheBootResources` | **Removed.** Relies on browser HTTP cache. The property is removed and does nothing | [What's new in .NET 10](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0?view=aspnetcore-10.0), [dotnet/aspnetcore#60557](https://github.com/dotnet/aspnetcore/issues/60557) (javiercn: "In .NET 10.0 we've gotten rid of the local cache … only the `offline-cache` is used") |
| SW registration | `register('service-worker.js')` | `register('service-worker.js', { updateViaCache: 'none' })` | [What's new in .NET 10](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0?view=aspnetcore-10.0) |
| SW precache list | no `.webmanifest` | `/\.webmanifest$/` added to `offlineAssetsInclude` | [dotnet/aspnetcore#59231](https://github.com/dotnet/aspnetcore/pull/59231) (merged 2024-12-12) |
| Environment for standalone | `Blazor-Environment` response header / launchSettings | `<WasmApplicationEnvironmentName>`; the header is ignored. Publish defaults to `Production` | [What's new in .NET 10](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0?view=aspnetcore-10.0) |
| Disabling integrity (non-PWA) | `BlazorCacheBootResources=false` | custom `loadBootResource` without `integrity` | [caching & integrity (.NET 10)](https://learn.microsoft.com/en-us/aspnet/core/blazor/host-and-deploy/webassembly/bundle-caching-and-integrity-check-failures?view=aspnetcore-10.0) |
| Assembly packaging | Webcil (`.wasm`) default since .NET 8 | unchanged (`WasmEnableWebcil`) | [Host & deploy WASM](https://learn.microsoft.com/en-us/aspnet/core/blazor/host-and-deploy/webassembly/?view=aspnetcore-10.0#webcil-packaging-format-for-net-assemblies) |

- **[V]** Early 10.0.x regression: every reload revalidated each `.wasm` (304s) instead of using the disk cache. It was fixed in
  **10.0.2** servicing. — [dotnet/aspnetcore#64009](https://github.com/dotnet/aspnetcore/issues/64009), [#64866](https://github.com/dotnet/aspnetcore/issues/64866).
  With an active SW the app is served from `offline-cache-*` anyway. **[I]** Build with SDK ≥ 10.0.2xx (we have 10.0.401).

### Gap in our repo

- **[V, local]** `Kroiko.Client.Blazor/` (untracked) targets `net10.0` but references
  `Microsoft.AspNetCore.Components.WebAssembly` **8.0.20** and has a .NET 8-style `index.html`
  (plain `_framework/blazor.webassembly.js`, no import map, no manifest, no SW). It is **not** a PWA yet.
  **[I]** Regenerate from `dotnet new blazorwasm --pwa -f net10.0` (packages 10.0.x), or copy the 5 PWA files and the csproj bits listed above.

---

## 2. Caching: what gets precached, runtime-fetched files, non-fingerprinted assets

### Verified

- Precache source: `service-worker-assets.js` is generated at build/publish time. It lists "Any Blazor-managed resources …" and
  "All resources for publishing to the app's `wwwroot` directory … including static web assets supplied by external
  projects and NuGet packages", each with a content hash. — [PWA docs: Control asset caching](https://learn.microsoft.com/en-us/aspnet/core/blazor/progressive-web-app/?view=aspnetcore-10.0#control-asset-caching)
- Manifest `version` = first 8 chars of base64(SHA-256 over all asset hashes) unless `ServiceWorkerAssetsManifestVersion` is set.
  Compressed alternatives (`.br`/`.gz`, `AssetRole=Alternative`) are **excluded** from the manifest. The published SW gets a
  `/* Manifest version: X */` header, so **any** asset change changes the SW bytes. —
  [`GenerateServiceWorkerAssetsManifest.cs`](https://github.com/dotnet/sdk/blob/release/10.0.1xx/src/StaticWebAssetsSdk/Tasks/ServiceWorker/GenerateServiceWorkerAssetsManifest.cs),
  [`UpdateServiceWorkerFileWithVersion.cs`](https://github.com/dotnet/sdk/blob/release/10.0.1xx/src/StaticWebAssetsSdk/Tasks/ServiceWorker/UpdateServiceWorkerFileWithVersion.cs),
  [`…StaticWebAssets.ServiceWorker.targets`](https://github.com/dotnet/sdk/blob/release/10.0.1xx/src/StaticWebAssetsSdk/Targets/Microsoft.NET.Sdk.StaticWebAssets.ServiceWorker.targets)
- The .NET 10 `service-worker.published.js`
  ([source](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/ProjectTemplates/Web.ProjectTemplates/content/ComponentsWebAssembly-CSharp/wwwroot/service-worker.published.js)):
  - `offlineAssetsInclude = [ /\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/, /\.webmanifest$/ ]`, excluding `service-worker.js`.
  - `onInstall`: `cache.addAll(...)` of `new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' })` into cache `offline-cache-{version}`. It is all-or-nothing.
  - `onActivate`: deletes every other `offline-cache-*`.
  - `onFetch`: GET only. Navigation requests get cached `index.html`. Everything else is `cache.match(request)` with fallback to `fetch(request)`.
    No runtime caching of misses.
- "If there is no content cached for a certain URL … the service worker falls back on a regular network request." — [PWA docs: How requests are resolved](https://learn.microsoft.com/en-us/aspnet/core/blazor/progressive-web-app/?view=aspnetcore-10.0#how-requests-are-resolved)
- Files outside `wwwroot` can be added with `<ServiceWorkerAssetsManifestItem Include=… RelativePath=… AssetUrl=… />`, but that does
  **not** publish them. — same page.
- **Measured (probe publish):** the SW precaches **all three ICU shards** (`icudt_CJK`, `icudt_EFIGS`, `icudt_no_CJK`, 2.6 MB raw)
  even though the runtime loads one. `*.map` files are in the manifest but not precached (no matching pattern). No `.pdb` in Release.

### Consequences for our files ([I] unless marked)

- **`template.json` via `HttpClient`**: works offline **iff** it is published under `wwwroot` (it then appears in the manifest,
  matches `/\.json$/`, is precached with integrity, and a same-URL `fetch` hits the cache). **[V, local]** Today `Kroiko.Domain`
  marks the three `template.json` files `CopyToOutputDirectory`. That puts them next to the DLL, **not** in `wwwroot`, so they would
  be neither served nor precached. Options: move them under the client's `wwwroot` (e.g. `wwwroot/templates/...`), or make them
  `EmbeddedResource` in `Kroiko.Domain`. The embedded option has no network or SW dependency at all and is simpler.
  Query strings (`template.json?v=2`) would **miss** the cache (`cache.match` is exact-URL).
- **Non-fingerprinted assets** (`index.html`, `css/app.css`, `manifest.webmanifest`, icons, `_content/MudBlazor/*`, our JSON)
  are still consistent offline because they are pinned by hash in the manifest and cached atomically per version.
  Only the non-SW path (first visit, or SW bypassed) depends on HTTP cache headers for them.
- **Extensions that are NOT precached by default:** `.woff2`, `.svg`, `.webp`, `.txt`, `.xml`, `.xlsx`, `.map`. If we add
  self-hosted fonts or icons in those formats, extend `offlineAssetsInclude`.
- **MudBlazor's Roboto font**: MudBlazor's own WASM template links
  `https://fonts.googleapis.com/css2?family=Roboto…` ([MudBlazor/Templates index.html](https://github.com/MudBlazor/Templates/blob/main/src/mudblazor-wasm/wwwroot/index.html)).
  That is cross-origin, never precached, so offline the UI falls back to a system font (cosmetic). Self-host it (and add
  `/\.woff2$/`) or drop the link.
- **Trim `wwwroot`**: everything there is downloaded on install. Remove the template's Bootstrap (`lib/` was 2.4 MB raw in the
  baseline) and `sample-data/`. Consider `InvariantGlobalization` (see §5), which removes the ICU shards.

---

## 3. Default update lifecycle and pitfalls

### Verified

- On each visit the browser re-requests `service-worker.js` and `service-worker-assets.js` and compares them byte-for-byte.
  If they changed, the new SW installs into a **new** cache and must fetch every asset with matching hashes. Success → "waiting for
  activation". "As soon as the user closes the app (no remaining app tabs or windows), the new service worker becomes active".
  Failure → discarded and "attempted again on the user's next visit". — [PWA docs: Background updates](https://learn.microsoft.com/en-us/aspnet/core/blazor/progressive-web-app/?view=aspnetcore-10.0#background-updates)
- "It isn't sufficient to refresh the tab displaying the app, even if it's the only tab". Skipping waiting is possible, but "you're
  giving up the guarantee that resources are always fetched consistently from the same cache instance." — [PWA docs: Update completion…](https://learn.microsoft.com/en-us/aspnet/core/blazor/progressive-web-app/?view=aspnetcore-10.0#update-completion-after-user-navigation-away-from-app)
- "Users may run any historical version of the app". — [same page](https://learn.microsoft.com/en-us/aspnet/core/blazor/progressive-web-app/?view=aspnetcore-10.0#users-may-run-any-historical-version-of-the-app)
- Update checks happen on navigation to in-scope pages, on functional events (if not checked in 24 h), or when `reg.update()` is called.
  Chrome ≥ 68 ignores HTTP caching for the SW script by default. `updateViaCache` extends that to `importScripts` (i.e.
  `service-worker-assets.js`). The pattern for an in-app "update available" prompt is
  `updatefound` / `statechange` / `controllerchange`, plus `skipWaiting()` on request. — [web.dev: Service worker lifecycle](https://web.dev/articles/service-worker-lifecycle)
- Integrity: SW precache has its own integrity check, separate from Blazor boot. A 404/500, an HTML fallback served for a `.wasm`,
  CRLF→LF rewriting by Git-based deploys, or CDN minification all show up as integrity failures. HTTP compression (`content-encoding`) is fine.
  If integrity is disabled mid-deploy, the app "becomes stuck in a broken state until you deploy a further update". —
  [caching & integrity (.NET 10)](https://learn.microsoft.com/en-us/aspnet/core/blazor/host-and-deploy/webassembly/bundle-caching-and-integrity-check-failures?view=aspnetcore-10.0)
- Lingering files from prior deployments can corrupt a deployment. Deleting the prior deployment once usually fixes it. —
  [Host & deploy WASM: Prior deployment corruption](https://learn.microsoft.com/en-us/aspnet/core/blazor/host-and-deploy/webassembly/?view=aspnetcore-10.0#prior-deployment-corruption)
- Installed-app **manifest** changes (name, `start_url`, theme, …) are picked up by Chrome desktop on launch if not checked since
  browser start or in 24 h. Icons are not updated on desktop, and `start_url` changes need `id` (the template sets `"id": "./"`). —
  [web.dev: How Chrome handles manifest updates](https://web.dev/articles/manifest-updates)

### Inferences

- **Effective default for an installed operator:** launch N downloads and installs the new version in the background. It activates
  when all app windows are closed. Launch N+1 runs it. With the app left open for days, nothing changes until it is closed
  (an SPA makes no navigations after load). Add a periodic `registration.update()` if that matters.
- **Partial deploy is safe for installed users by default.** A half-uploaded deployment makes the SW install fail its integrity checks.
  The old version keeps working and the install is retried next launch. The risk is on the **first visit** (no SW yet) or on
  hosts that rewrite files. Prefer a host with **atomic deploys**.
- **Offline-first hides new versions:** a user who is offline at launch keeps running the cached version indefinitely. That is expected.
  Show the running build/version in the UI (for example the assembly informational version) so support can tell.
- **Low-risk "tell me and reload" option for #18:** keep the default waiting behaviour. On `updatefound` → installed-and-waiting, show a MudBlazor
  snackbar "New version ready — Reload". On click, `postMessage` the waiting worker to call `self.skipWaiting()`. On `controllerchange`,
  `location.reload()`. This keeps the atomic-cache guarantee (a whole-page reload after activation) and needs about 20 lines of JS. There is
  no backend, so there is no API-compatibility constraint, which leaves only file-format concerns such as `template.json` shape vs code.

---

## 4. Install UX on Chrome/Edge on Windows

### Verified

- Chrome install criteria: HTTPS. Manifest with `short_name` or `name`, `icons` incl. **192px and 512px**, `start_url`,
  `display` ∈ {`fullscreen`, `standalone`, `minimal-ui`, `window-controls-overlay`}, `prefer_related_applications` absent/false.
  Engagement heuristic: at least 1 click/tap and at least 30 s on the page. Then `beforeinstallprompt` fires. — [web.dev: install criteria](https://web.dev/articles/install-criteria)
- Chrome dropped the "SW with fetch handler" requirement for **menu** install (mobile 108, desktop 112), "however … the algorithm
  that displays the install prompt still requires the presence of a fetch() handler". — [Chrome blog: Revisiting installability criteria](https://developer.chrome.com/blog/update-install-criteria)
- Edge: shows an **App available** button in the address bar. "A Progressive Web App (PWA) doesn't need to have a service worker
  for Microsoft Edge to be able to install the app". HTTPS is required except `localhost`. — [Edge: Get started developing a PWA](https://learn.microsoft.com/en-us/microsoft-edge/progressive-web-apps/how-to/)
- On desktop Chromium an install button appears in the URL bar. After install the app runs in its own window without an address bar. —
  [Blazor PWA docs: Installation and app manifest](https://learn.microsoft.com/en-us/aspnet/core/blazor/progressive-web-app/?view=aspnetcore-10.0#installation-and-app-manifest)
- The .NET 10 template `manifest.webmanifest` already has `name`, `short_name`, `"id": "./"`, `"start_url": "./"`,
  `"display": "standalone"`, `prefer_related_applications: false`, 192 and 512 PNG icons. It meets the Chrome criteria as-is.

### Inferences

- Offline, the published SW's `fetch` handler also satisfies Chrome's prompt heuristic. We only need to replace the name, colours and icons.
- Optional: capture `beforeinstallprompt` and show a MudBlazor "Install app" button for the operators. This is not required.

---

## 5. Payload, AOT, compression

### Measured (local, SDK 10.0.401, `dotnet publish -c Release`, **no** `wasm-tools` workload)

Sizes are the sum of the files the default SW precaches. "Brotli" uses the published `.br` siblings, which is what crosses the wire if the host negotiates `br`.

| Variant | Precached files | Raw | Brotli |
|---|---:|---:|---:|
| Template `--pwa` baseline (incl. Bootstrap `lib/`) | 73 | 11.8 MB | 3.1 MB |
| + MudBlazor 9.10.0 + LargeXlsx 1.12.0 (a few components + one `XlsxWriter` call), Bootstrap removed | 74 | **13.8 MB** | **3.9 MB** |
| same + `InvariantGlobalization=true` (no ICU shards) | 71 | **11.1 MB** | **3.3 MB** |

Notable parts (raw / br): `dotnet.native.wasm` 3.0 / 0.98 MB. `System.Private.CoreLib` 1.76 / 0.55 MB. `MudBlazor.wasm` 1.47 / 0.38 MB.
LargeXlsx 0.06 / 0.02 MB, but it pulls **SharpCompress 0.19 / 0.07 MB + ZstdSharp 0.39 / 0.12 MB**. Restore flagged
`SharpCompress 0.39.0` with a moderate advisory ([GHSA-6c8g-7p36-r338](https://github.com/advisories/GHSA-6c8g-7p36-r338)) via LargeXlsx 1.12.0.
The latest LargeXlsx is 2.0.2 and is worth checking.

### Verified

- Publish emits Brotli and Gzip. "Blazor relies on the host to serve the appropriate compressed files". For static hosts without content negotiation,
  the docs offer a JS Brotli decoder via `loadBootResource`. `CompressionEnabled=false` turns it off. — [Host & deploy WASM: Compression](https://learn.microsoft.com/en-us/aspnet/core/blazor/host-and-deploy/webassembly/?view=aspnetcore-10.0#compression)
- AOT: "most AOT-compiled apps are about twice the size of their IL-compiled versions". The benefit is for CPU-intensive apps. It needs the `wasm-tools` workload.
  "AOT doesn't trim out managed DLLs". — [Build tools & AOT](https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly-build-tools-and-aot?view=aspnetcore-10.0)
- Standalone WASM on **Azure App Service for Linux isn't supported**. Azure Static Web Apps is recommended. — [Host & deploy WASM](https://learn.microsoft.com/en-us/aspnet/core/blazor/host-and-deploy/webassembly/?view=aspnetcore-10.0#azure-app-service)

### Inferences

- **AOT: no.** Our work is parsing a small text file and writing a few xlsx/txt files. It is not CPU-bound, and AOT would roughly double the one-time install.
- `wasm-tools` (without AOT) enables native relinking, which can shrink `dotnet.native.wasm`. It was not measured because it needs an admin workload install. It is optional.
- `InvariantGlobalization=true` saves about 0.6 MB br / 2.6 MB raw and suits our "always `InvariantCulture`" rule. The UI then cannot use culture-specific
  formatting (e.g. `bg-BG` dates in MudBlazor pickers), so check the UI needs first.
- **Compression matters for the host (#16), with a PWA-specific twist:** the SW precaches by requesting the **plain** URLs (`asset.url`), so the
  `loadBootResource` + `decode.js` workaround does **not** shrink the SW install download. Only real `Content-Encoding` negotiation (pre-compressed
  `.br`/`.gz` or on-the-fly) does. Without it, install is about 11–14 MB instead of about 3.3–3.9 MB. It happens once per version, then everything is local.
- On the first visit, Blazor boot and SW install both fetch the same files. In .NET 10 the second fetch uses `cache: 'no-cache'`, which revalidates against the
  HTTP cache, so it is cheap if the host sends `ETag`/`Last-Modified`. Unverified in a browser.
- Disk footprint per device: about the raw size (11–14 MB) in Cache Storage. The old version is deleted on activate.

---

## 6. Durability of small per-device settings

### Verified

- Storage is **per origin** and **best-effort** by default. It is evicted under storage pressure, least-recently-used origin first. Persistent origins are skipped.
  Chromium lets an origin use up to 60% of the disk. — [MDN: Storage quotas and eviction criteria](https://developer.mozilla.org/en-US/docs/Web/API/Storage_API/Storage_quotas_and_eviction_criteria)
- `localStorage`: 5 MiB per origin, synchronous, strings only. It is subject to the same eviction as IndexedDB and Cache API. — same MDN page.
- `navigator.storage.persist()`: Chrome and Edge "automatically approve or deny … and do not show any prompts". Chrome heuristics include site engagement,
  "Has the site been installed or bookmarked?", and notification permission. It covers Cache API, Local Storage, IndexedDB, and service workers. "Research by the
  Chrome team shows that data is very rarely cleared automatically by Chrome. It is far more common for users to manually clear storage." —
  [MDN](https://developer.mozilla.org/en-US/docs/Web/API/Storage_API/Storage_quotas_and_eviction_criteria), [web.dev: Persistent storage](https://web.dev/articles/persistent-storage) (2020)

### Inferences

- For a handful of settings (selected company, contact info, last options), **`localStorage` is adequate** and simplest from Blazor (one JS interop call, or a small
  wrapper). IndexedDB adds nothing for this size. Both live and die together with the origin's bucket.
- Call `navigator.storage.persist()` once after install/first save. It is free and silent on Chromium, and it also protects the offline cache.
- The real durability risks are **the user clearing site data** and **an origin change**. A new domain or subdomain starts empty: no settings, no offline cache, and the
  installed app points at the old origin. Keep the origin stable and consider an "export/import settings" button (JSON download/upload).

---

## Implications for pending decisions

### #18 — How installed users receive new versions

- The default template gives **silent, atomic, eventually-consistent updates**: downloaded on a launch, applied on the next launch after all windows close.
  Nothing breaks mid-session, offline users keep the last good version, and failed or partial downloads retry automatically.
- Choices, from least to most work:
  1. **Default as-is.** Zero code, but there is a one-launch lag and no visibility.
  2. **Default + "New version ready — Reload" snackbar** (`updatefound` → waiting → user clicks → `skipWaiting` → `controllerchange` → reload). About 20 lines of JS.
     Keeps consistency. **Recommended.**
  3. Auto `skipWaiting` + `clients.claim()`. Fastest, but it can mix versions in a running session. The docs warn against it.
- Plus: show the build version in the UI, and optionally call `registration.update()` periodically for long-open windows.

### #16 — Where the PWA is hosted

Host must be **static-only** and provide:

- **HTTPS** on a **stable origin**, forever. Storage, install identity and offline cache are bound to it.
- **`Content-Encoding: br`/`gzip` negotiation** for `.wasm/.js/.dat/.css/.json`, ideally serving the pre-compressed `.br` files. Otherwise the per-version install is about 3.5x bigger.
- Correct MIME types (`application/wasm` for `.wasm`, `.dat`, `.webmanifest` → `application/manifest+json`), and **no content rewriting** (no minification, no CRLF changes; use
  `.gitattributes` binary if deploying through Git).
- SPA fallback to `index.html` for deep links. This is needed only before the SW is installed.
- Ability to set headers: `no-cache` on `index.html`, `service-worker.js`, `service-worker-assets.js`. Fingerprinted `_framework/*` can be `immutable`.
- **Atomic deploys**, and no stale leftovers.
- Not Azure App Service **Linux** (unsupported for standalone WASM). Azure Static Web Apps is the Microsoft-recommended target. Whether GitHub Pages, Cloudflare Pages
  and others serve pre-compressed `.br` still needs checking per host in #16.

---

## Method / reproducibility

- Template and SW sources read via `gh api` from `dotnet/aspnetcore@release/10.0` and `dotnet/sdk@release/10.0.1xx`.
- Probe: `dotnet new blazorwasm --pwa -f net10.0`, then MudBlazor 9.10.0 + LargeXlsx 1.12.0, `dotnet publish -c Release` (SDK 10.0.401, runtime packs 10.0.12,
  no workloads). Sizes come from a script that applies the template's `offlineAssetsInclude` to `service-worker-assets.js` and sums file and `.br` sizes. The probe
  lived in a scratch directory and is not in the repo.
- Not verified in a real browser: first-visit double fetch/revalidation, `persist()` grant for an installed Kroiko app, host compression behaviour.
