# ADR-0001: Host the PWA on Azure Static Web Apps at `app.kroiko.com`

- Status: Accepted
- Date: 2026-09-23
- Deciders: Pavel Pavlov

## Context

`Kroiko.Client.Blazor` is a standalone, backend-free Blazor WebAssembly PWA. After the first
online visit it runs offline; the host is only needed for the first load and for updates.
Research ([.NET 10 PWA facts](https://github.com/PavelRPavlov/Kroiko/issues/17),
[`docs/research/dotnet10-blazor-wasm-pwa.md`](../research/dotnet10-blazor-wasm-pwa.md)) fixed
what the host must provide:

- **HTTPS on a permanent origin.** Install identity, the service-worker cache and the per-device
  settings (customer contact info, last manufacturer) are all bound to the origin. Changing it
  loses every install and every saved setting.
- **`Content-Encoding: br`/`gzip` for the plain URLs.** The service worker precaches the plain
  URLs, so the in-browser Brotli decoder workaround does not help; without real negotiation each
  update download is ~3.5× larger (~11–14 MB instead of ~3.3–3.9 MB).
- Correct MIME types, no content rewriting, an SPA fallback to `index.html`, control over
  `Cache-Control` headers, and **atomic deploys** with no stale leftovers.
- Independence from `ATAFurniture.Server`, which stays deployed and untouched.

## Decision

We will serve the PWA from **`https://app.kroiko.com`** at the root path (`/`), on **Azure Static
Web Apps, Free plan**, in ATA's existing Azure subscription.

- `app.kroiko.com` is a CNAME in ATA's own DNS pointing at the Static Web App. Users are only
  ever given `app.kroiko.com`, never the generated `*.azurestaticapps.net` URL, so the host can be
  replaced later by changing the CNAME without changing the origin.
- Releases follow the Server's branch model:
  - push to **`release`** → production (`app.kroiko.com`);
  - push to **`main`** → a named Static Web Apps staging environment `main` (its own generated URL,
    for operators to try builds);
  - both only when `Kroiko.Client.Blazor/**`, `Kroiko.Domain/**` or the PWA workflow file change,
    plus manual `workflow_dispatch`;
  - pull requests build and test only — no per-PR preview environments.

## Consequences

- ✅ Pre-compressed `.br` files are served automatically; SPA fallback, per-route headers and MIME
  types are set in `staticwebapp.config.json`; deploys are atomic; HTTPS on the custom domain is
  free and auto-renewing.
- ✅ The trimmed app (~14 MB raw) is far below the Free plan's 250 MB limit; internal traffic is
  far below its bandwidth limits.
- ✅ No coupling to the Server's App Service or FTP host.
- ⚠️ The Free plan has **no SLA**. Accepted: once installed the app works offline, so an outage
  only delays updates and first-time installs.
- ⚠️ The staging environment is a different origin: installs and settings made there are
  separate from production. Fine for testing; never hand that URL to operators as "the app".
- ⚠️ Azure dependency — mitigated by owning the DNS name.
- Follow-up (deferred, not decided here): the CI workflow internals — build steps, SDK pinning,
  deployment authentication, the contents of `staticwebapp.config.json`, runner OS — and
  provisioning the Static Web App resource, the DNS record and the deployment secret.

## Alternatives considered

- **GitHub Pages** — cannot set response headers or serve the pre-compressed `.br` files; would
  need a `/Kroiko/` base path without a custom domain.
- **Existing FTP host (Shark ASP.NET, IIS)** — FTP uploads are not atomic and leave stale files;
  Brotli depends on the host having the IIS URL Rewrite module.
- **Existing Azure App Service** — couples the PWA to the Server app; standalone WASM on App
  Service for Linux is unsupported.
- **Cloudflare Pages** — technically suitable (on-the-fly Brotli, `_headers`), but adds a new vendor
  account for no gain over Azure, which ATA already uses.
- **Using the generated `*.azurestaticapps.net` URL** — ties every install and saved setting to one
  Azure resource forever.

## Related

- Ticket: [Where the PWA is hosted and how CI publishes it](https://github.com/PavelRPavlov/Kroiko/issues/16)
- Map: [Offline installable Blazor WASM PWA (no backend)](https://github.com/PavelRPavlov/Kroiko/issues/15)
- Research: [`docs/research/dotnet10-blazor-wasm-pwa.md`](../research/dotnet10-blazor-wasm-pwa.md)
- Sources: [Azure Static Web Apps configuration](https://learn.microsoft.com/en-us/azure/static-web-apps/configuration),
  [plans](https://learn.microsoft.com/en-us/azure/static-web-apps/plans),
  [FAQ (pre-compressed files)](https://learn.microsoft.com/en-us/azure/static-web-apps/faq)
- Implemented by: [07 — Hosting & go-live](../implementation/07-hosting-and-go-live.md)
