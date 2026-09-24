# 07 — Hosting & go-live

- **Status:** In progress
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
- SWA reads the file but never serves it, so `service-worker.published.js` lists it in
  `offlineAssetsExclude`: precaching it would 404 and fail the whole service-worker install
  ([Azure/static-web-apps#259](https://github.com/Azure/static-web-apps/issues/259),
  [#490](https://github.com/Azure/static-web-apps/issues/490)). The test `StaticSiteHost` also 404s it.
- `Kroiko.Client.Tests/Hosting/` checks the config, and against the publish output that it sits at the
  `wwwroot` root, is not precached, and that every precached asset is excluded from the fallback.
- SWA applies no route rules to a fallback response
  ([configuration](https://learn.microsoft.com/en-us/azure/static-web-apps/configuration#fallback-routes)), so a
  deep link such as `/configuration` gets `index.html` without the `no-cache` header, while `/` and
  `/index.html` get it. Step 4 checks it on those two. Once the service worker is installed it answers every
  navigation itself, so only a first visit through a deep link depends on SWA's default caching.

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

As built:

- It fetches `origin` first, so "equals `origin/…`" means the remote as it is now. For production the tag must
  also be on `origin` (the same tag object), so every production deploy is a pushed, tagged release; `<Version>`
  must be `X.Y.Z`; and it must not be older than any `vX.Y.Z` tag on `origin`, so an older build is never
  redeployed (ADR-0002 §8). Redeploying the newest version is allowed.
- It passes `--env` explicitly (the CLI's default is `preview`) and `--swa-config-location <temp>/wwwroot`, and runs
  `swa` from the publish folder, away from the repo's `.github/workflows/`, which the CLI would otherwise read
  ([`swa deploy` options](https://learn.microsoft.com/en-us/azure/static-web-apps/static-web-apps-cli-deploy#options)).
- Only the `swa` call sees the token: the script takes it out of the environment for git, the tests and the
  build, and puts it back when it ends. The CLI logs it only under `SWA_CLI_DEBUG=silly`, so the script clears
  that variable for the call and masks the token in each line the CLI prints. The CLI can exit `0` after a
  failure, so success is its `Project deployed to <url>` line; without it the script fails.
- `-DryRun` runs every check, the tests and the publish, then prints the `swa` command, file count and size
  instead of deploying, and removes the publish folder. It needs neither the token nor the CLI.
- `Kroiko.Client.Tests/PublishScript/` runs the script against a throwaway repository with stubbed `dotnet` and
  `swa` (see [CONTEXT.md](../../CONTEXT.md) §5).
- The tree must be clean, untracked files included: a local tool folder such as `.claude/` belongs in
  `.git/info/exclude` on the deploying machine.

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
- `/configuration` and an unknown route both serve the app; a missing `/_framework/x.js` is a 404, and so
  is a missing `/_content/MudBlazor/x.css` (only the `*.{…}` extension exclude covers it; the tests match it
  the way the SWA CLI emulator does, so this is its check against the real service);
- `index.html` and `service-worker.js` carry `Cache-Control: no-cache`;
- Edge installs it, and after one online visit it starts offline.

Nothing is deployed to production in 07a. Nobody is given any URL.

## 07b — Go-live

### 1. `docs/release-checklist.md`

Write it from ADR-0007 §9, with two sections:

- **Go-live (once).** Pavel plus one operator; ~10 recent real orders across all three manufacturers,
  both field formats and Cyrillic material names. Run each through the deployed Server and through
  the installed PWA on `app.kroiko.com` in Edge. Files match (Excel identical; `.cut_mt` byte-equal
  apart from the date), open in each manufacturer's software (including a CRLF `.cut_mt` in
  MegaTrading's, [ADR-0009](../adr/0009-cut-mt-line-endings-crlf.md); the deployed Server must include
  it, or a Linux-hosted one still writes LF), and decode as UTF-8, including a check
  that Polyboard never exports another encoding (ADR-0006 §5). Every row of
  [CONTEXT.md §9](../../CONTEXT.md) behaves as listed. Real files stay on the local machine and are
  never committed or attached to issues.
- **Every production release (~10 minutes)** on the installed Edge PWA: the update snackbar and
  reload; About shows the new version; one conversion per manufacturer; "Запази в папка…" with the
  real folder picker, which no test drives ([ADR-0007](../adr/0007-parity-and-test-strategy.md) §5;
  clash renaming, last folder, the final names listed); "Изтегли всички" and one file's link; offline start.
- **Release procedure.** Bump `<Version>` in a PR to `main`, merge `main` into `release`, tag
  `vX.Y.Z` on `release`, run `scripts/publish-pwa.ps1 -Environment production`, then open the
  sign-off issue. A bad release is fixed forward: revert, bump, deploy. **Never** redeploy an older
  build (ADR-0002 §8).

Done. [`docs/release-checklist.md`](../release-checklist.md) has three parts, in the order they are used:
the release procedure, every production release, and the go-live.

- **Release procedure.** It covers the deploying machine's one-time setup and the git commands for the
  version bump, merging `main` into `release`, and the tag. It runs `scripts/publish-pwa.ps1 -Environment
  production -DryRun` as a rehearsal before the real deploy, then opens the sign-off issue, and it says how to
  fix a bad release forward.
- **Every production release.** It adds the update checks from phase 06, run on the installed Edge PWA while a
  new version is deployed: the snackbar within about an hour, or on visibility or `online`; "Презареди" asking
  over an unsaved Order; "По-късно" moving the offer into About; About's up-to-date and offline reports; a
  second window reloading. Next come the real folder picker, cancelling it, "Изтегли всички" with Edge's
  multiple-downloads prompt, one file's link, and an offline start.
- **Go-live.** It repeats the step-4 host checks against `app.kroiko.com`, with a PowerShell snippet that finds
  a `.wasm`, `.dat` and font URL in `service-worker-assets.js`. It installs from Edge and checks offline start.
  It saves from an Edge tab and twice from an Edge InPrivate window (the Chromium 153 IndexedDB crash). It
  checks a picker blocked by Edge's `DefaultFileSystemWriteGuardSetting`, and Firefox's downloads. It closes
  phase 01's open Excel review and phase 02's Server smoke check (when the deployed Server includes phase 02).
  It runs the side-by-side comparison with snippets: a strict UTF-8 check of each Polyboard file (ADR-0006 §5),
  an `.xlsx` worksheet-hash comparison, and `fc.exe /b` for the `.cut_mt`. If only CR bytes differ, the Server
  lacks ADR-0009. Last, it walks each row of CONTEXT.md §9 and the sign-off.

The checklist marks the update items n/a for `v1.0.0`, which has no earlier production build to update from.
It notes that `release` is also the branch of the Server's `release_kroiko.yml` workflow, whose `push`
trigger is commented out today.

### 2. First production release and sign-off

- Set `<Version>1.0.0</Version>`, follow the release procedure, and deploy to production.
- Open the GitHub issue **"Release v1.0.0 sign-off"** with the checklist pasted in. Run the
  go-live section with the operator, tick it, and close the issue.
- Only then are operators given `https://app.kroiko.com`. The Server stays deployed, unchanged.

## Done criteria

- [x] `staticwebapp.config.json` is in the published output with the fallback excludes, `no-cache` headers and MIME types.
- [x] `scripts/publish-pwa.ps1` refuses dirty trees, wrong branches, unsynced `HEAD`, a missing or mismatched tag, and failing tests; it never prints the token.
- [ ] The Static Web App exists; `app.kroiko.com` resolves to it over HTTPS.
- [ ] The `main` staging deploy passes the step-4 checks (07a done).
- [x] `docs/release-checklist.md` exists with the go-live, per-release and release-procedure sections.
- [ ] `v1.0.0` is tagged on `release` and deployed to production with the script.
- [ ] "Release v1.0.0 sign-off" is closed with every box ticked.
- [ ] The map's destination is reached: operators use the installed PWA.

## Out of this phase

- A CI/CD workflow (GitHub Actions, deployment environments, `-p:Version` stamping). It is a later
  improvement outside this plan, and when added it must enforce the same branch rules as the script.
- Any change to the Server's hosting or Azure resources.
