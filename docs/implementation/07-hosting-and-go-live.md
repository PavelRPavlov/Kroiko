# 07 — Hosting & go-live

- **Status:** Not started
- **Depends on:** 07a — [03 Client shell](03-client-shell.md); 07b — [05 Saving](05-saving.md), [06 Updates & About](06-updates-and-about.md) and 07a
- **ADRs:** [0001](../adr/0001-host-pwa-on-azure-static-web-apps.md), [0002](../adr/0002-pwa-updates-reload-prompt.md) §6, §8, [0006](../adr/0006-known-conversion-bugs-in-pwa.md) §5, [0007](../adr/0007-parity-and-test-strategy.md) §9

## Goal

The PWA is served from `https://app.kroiko.com` on Azure Static Web Apps (Free) with real Brotli,
correct MIME types and cache headers. The `main` staging environment is live from 07a. Operators
switch to the app only after a signed-off, side-by-side go-live run against the Server.

There is **no CI** in this plan. Deploys are manual through `scripts/publish-pwa.ps1`, which
enforces ADR-0001's branch model: production only from `release`, and staging from `main`.

Steps marked **(human)** need Azure or DNS access. The agent hands the human a precise checklist and
records the resulting facts (resource name, generated URLs), never the token.

## 07a — Provision & first deploy

### 1. `staticwebapp.config.json`

Add it to `Kroiko.Client.Blazor/wwwroot/` so it is published with the app:

- `navigationFallback` → `/index.html`, **excluding** `/_framework/*`, `/css/*`, `/js/*`,
  `/fonts/*` and files with an extension (`*.{js,css,wasm,dat,json,webmanifest,png,ico,woff2}`),
  so a missing asset is a 404, not `index.html`.
- `Cache-Control: no-cache` on `/index.html`, `/service-worker.js` and `/service-worker-assets.js`,
  so the update check always sees the current manifest (ADR-0002).
- `mimeTypes` for `.webmanifest` (`application/manifest+json`), `.dat` (`application/octet-stream`),
  `.wasm` (`application/wasm`) and `.woff2` (`font/woff2`).
- No auth, no API, no routes beyond these.

### 2. `scripts/publish-pwa.ps1`

`scripts/publish-pwa.ps1 -Environment main|production` refuses to run unless:

1. the working tree is clean (`git status --porcelain` is empty);
2. for `main`: `HEAD` equals `origin/main`. For `production`: `HEAD` is on `release`, equals
   `origin/release`, and carries a tag `vX.Y.Z` that equals the csproj `<Version>`;
3. the full `dotnet test` passes (including E2E).

Then it runs `dotnet publish Kroiko.Client.Blazor -c Release -o <temp>` and
`swa deploy <temp>/wwwroot --env <main|production>` (Azure Static Web Apps CLI). The deployment
token is read from `SWA_CLI_DEPLOYMENT_TOKEN` in the operator's own environment. It is
**never** committed, echoed or written to a file. The script prints the deployed version and URL.
It is a plain, readable script (no encoded commands, per [AGENTS.md](../../AGENTS.md)).

### 3. Provision (human)

- [ ] Create the Static Web App (Free plan) in ATA's existing subscription; no API, no repo link.
- [ ] Copy its deployment token into `SWA_CLI_DEPLOYMENT_TOKEN` on the machine that deploys.
- [ ] Install the SWA CLI (`npm i -g @azure/static-web-apps-cli`).
- [ ] Add the CNAME `app.kroiko.com` → the Static Web App's default host in ATA's DNS, and add the
      custom domain to the **production** environment. The origin is permanent from now on (ADR-0001).
- [ ] Record the resource name, the `main` environment URL and the date in the PR.

### 4. First deploy to `main` staging

Deploy the phase 03 shell with `-Environment main`, then check it by hand on the staging URL and note the results in the PR:

- `curl -sI -H "Accept-Encoding: br" <url>/_framework/<a .wasm file>` → `Content-Encoding: br`
  and `Content-Type: application/wasm`;
- `/manifest.webmanifest`, a `.dat` file and a font return the MIME types above;
- `/configuration` and an unknown route both serve the app; a missing `/_framework/x.js` is a 404;
- `index.html` and `service-worker.js` carry `Cache-Control: no-cache`;
- Edge installs it, and after one online visit it starts offline.

Nothing is deployed to production in 07a. Nobody is given any URL.

## 07b — Go-live

### 1. `docs/release-checklist.md`

Write it from ADR-0007 §9, with two sections:

- **Go-live (once).** Pavel plus one operator; ~10 recent real orders across all three manufacturers,
  both field formats and Cyrillic material names. Run each through the deployed Server and through
  the installed PWA on `app.kroiko.com` in Edge. Files match (Excel identical; `.cut_mt` byte-equal
  apart from the date), open in each manufacturer's software, and decode as UTF-8, including a check
  that Polyboard never exports another encoding (ADR-0006 §5). Every row of
  [CONTEXT.md §9](../../CONTEXT.md) behaves as listed. Real files stay on the local machine and are
  never committed or attached to issues.
- **Every production release (~10 minutes)** on the installed Edge PWA: the update snackbar and
  reload; About shows the new version; one conversion per manufacturer; "Запази в папка…" (clash
  renaming, last folder) and "Изтегли всички"; offline start.
- **Release procedure.** Bump `<Version>` in a PR to `main`, merge `main` into `release`, tag
  `vX.Y.Z` on `release`, run `scripts/publish-pwa.ps1 -Environment production`, then open the
  sign-off issue. A bad release is fixed forward: revert, bump, deploy. **Never** redeploy an older
  build (ADR-0002 §8).

### 2. First production release and sign-off

- Set `<Version>1.0.0</Version>`, follow the release procedure, and deploy to production.
- Open the GitHub issue **"Release v1.0.0 sign-off"** with the checklist pasted in. Run the
  go-live section with the operator, tick it, and close the issue.
- Only then are operators given `https://app.kroiko.com`. The Server stays deployed, unchanged.

## Done criteria

- [ ] `staticwebapp.config.json` is in the published output with the fallback excludes, `no-cache` headers and MIME types.
- [ ] `scripts/publish-pwa.ps1` refuses dirty trees, wrong branches, unsynced `HEAD`, a missing or mismatched tag, and failing tests; it never prints the token.
- [ ] The Static Web App exists; `app.kroiko.com` resolves to it over HTTPS.
- [ ] The `main` staging deploy passes the step-4 checks (07a done).
- [ ] `docs/release-checklist.md` exists with the go-live, per-release and release-procedure sections.
- [ ] `v1.0.0` is tagged on `release` and deployed to production with the script.
- [ ] "Release v1.0.0 sign-off" is closed with every box ticked.
- [ ] The map's destination is reached: operators use the installed PWA.

## Out of this phase

- A CI/CD workflow (GitHub Actions, deployment environments, `-p:Version` stamping). It is a later
  improvement outside this plan, and when added it must enforce the same branch rules as the script.
- Any change to the Server's hosting or Azure resources.
