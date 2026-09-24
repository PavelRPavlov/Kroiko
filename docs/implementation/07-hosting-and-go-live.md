# 07 — Hosting & go-live

- **Status:** In progress
- **Depends on:** 07a — [03 Client shell](03-client-shell.md); 07b — [05 Saving](05-saving.md), [06 Updates & About](06-updates-and-about.md) and 07a
- **ADRs:** [0010](../adr/0010-host-pwa-on-s3-and-cloudfront.md), [0002](../adr/0002-pwa-updates-reload-prompt.md) §6, §8, [0006](../adr/0006-known-conversion-bugs-in-pwa.md) §5, [0007](../adr/0007-parity-and-test-strategy.md) §9

## Goal

The PWA is served from `https://app.kroiko.com` by Amazon CloudFront on the flat-rate Free plan, from a private
S3 bucket in Frankfurt. It gets the published Brotli files, correct MIME types and cache headers, at $0. The
`main` staging distribution is live from 07a. Operators switch to the app only after a signed-off, side-by-side
go-live run against the Server.

There is **no CI** in this plan. Deploys are manual through `scripts/publish-pwa.ps1`, which enforces the branch
model: production only from `release`, and staging from `main`.

Steps marked **(human)** need AWS or DNS access. The agent hands the human a precise checklist and records the
resulting facts (bucket, distribution IDs, generated URLs), never a credential.

> **Host change.** Steps 1 and 2 were first built for Azure Static Web Apps (ADR-0001): a
> `staticwebapp.config.json` and a `swa deploy` call. [ADR-0010](../adr/0010-host-pwa-on-s3-and-cloudfront.md)
> replaced that host before anything was deployed, so both steps are rebuilt below. The script's branch-model
> checks carry over unchanged.

## 07a — Provision & first deploy

### 1. The edge function

`hosting/cloudfront/viewer-request.js` is a CloudFront Function (runtime `cloudfront-js-2.0`, viewer request
event). It is the only logic at the edge:

- **SPA fallback:** a path whose last segment has no file extension (`/`, `/configuration`, `/no-such-page`)
  becomes `/index.html`. A missing file with an extension (`/_framework/x.js`, `/_content/MudBlazor/x.css`) goes
  to S3 unchanged and is a 404.
- **Brotli:** when `Accept-Encoding` lists `br`, the path is prefixed with `/br`, otherwise with `/raw`. A release
  folder holds both trees (step 2). CloudFront gzips `raw/` on the fly for clients without `br`, and leaves `br/`
  alone because its objects carry `Content-Encoding: br`.
- Nothing else: no redirects, no headers, no query-string handling.

It replaces `staticwebapp.config.json`. That file, its entry in `service-worker.published.js`'s
`offlineAssetsExclude` and its tests are removed.

- `Kroiko.Client.Tests/Hosting/` runs the committed function's handler over a table of requests (path ×
  `Accept-Encoding` → rewritten URI). It checks that every precached asset's path keeps its extension, so the
  fallback never swallows one.
- `StaticSiteHost` serves a publish the way the deployed host does: the same fallback rule, `br` only when
  accepted, and a 404 for a missing file.

### 2. `scripts/publish-pwa.ps1`

`scripts/publish-pwa.ps1 -Environment main|production` refuses to run unless:

1. the working tree is clean (`git status --porcelain` is empty);
2. for `main`: `HEAD` equals `origin/main`. For `production`: `HEAD` is on `release`, equals
   `origin/release`, and carries a tag `vX.Y.Z` that equals the csproj `<Version>`;
3. the full `dotnet test` passes (including E2E).

Then it runs `dotnet publish Kroiko.Client.Blazor -c Release -o <temp>` and deploys `<temp>/wwwroot`:

1. **Upload a new release folder**, `s3://<bucket>/<environment>/<release>/`, where `<release>` is
   `<UTC yyyyMMddTHHmmssZ>-v<version>-<sha7>`. It is new on every deploy and never overwritten. It holds:
   - `raw/`: every file of `wwwroot` except the `.br` and `.gz` siblings;
   - `br/`: the same paths, each holding its `.br` sibling's bytes with `Content-Encoding: br`, or the plain
     file where the publish made no `.br`.
2. **Set the metadata on every object:**
   - `Content-Type` from a fixed extension map, with the Blazor types `.wasm` → `application/wasm`,
     `.dat` → `application/octet-stream`, `.webmanifest` → `application/manifest+json` and `.woff2` →
     `font/woff2`. A file with an extension outside the map makes the script refuse.
   - `Cache-Control: no-cache` on everything except the fingerprinted `_framework/` files, which get
     `public, max-age=31536000, immutable`. `index.html`, `service-worker.js` and `service-worker-assets.js`
     are always `no-cache` (ADR-0002).
3. **Bring the environment's function up to `hosting/cloudfront/viewer-request.js`:** update and publish it
   when its live code differs.
4. **Switch:** set the distribution's origin path to `/<environment>/<release>`, wait until the distribution
   is deployed, invalidate `/*`, and wait for the invalidation.
5. **Print** the deployed version and URL.

The non-secret IDs live in `hosting/aws/hosting.json`: the region, the bucket, and per environment the
distribution ID, function name and URL. The credentials are an IAM user's access key in the AWS CLI profile
`kroiko-pwa` on the deploying machine. The script passes `--profile kroiko-pwa` to every `aws` call. The key is
**never** committed, echoed or written anywhere else. The IAM policy and the bucket policy are committed as
templates in `hosting/aws/`. The script is a plain, readable script (no encoded commands, per
[AGENTS.md](../../AGENTS.md)).

As built (carried over from the Static Web Apps version):

- It fetches `origin` first, so "equals `origin/…`" means the remote as it is now. For production the tag must
  also be on `origin` (the same tag object), so every production deploy is a pushed, tagged release; `<Version>`
  must be `X.Y.Z`; and it must not be older than any `vX.Y.Z` tag on `origin`, so an older build is never
  redeployed (ADR-0002 §8). Redeploying the newest version is allowed. It uploads a new release folder.
- `-DryRun` runs every check, the tests and the publish, then prints the plan instead of deploying: the release
  folder, the file count and size of each tree, and the origin path. It then removes the publish folder. It
  needs neither the credentials nor the AWS CLI.
- `Kroiko.Client.Tests/PublishScript/` runs the script against a throwaway repository with stubbed `dotnet` and
  `aws` (see [CONTEXT.md](../../CONTEXT.md) §5).
- The tree must be clean, untracked files included: a local tool folder such as `.claude/` belongs in
  `.git/info/exclude` on the deploying machine.

### 3. Provision (human)

All in one AWS account on the **paid** account plan. The bucket is in `eu-central-1` (Frankfurt), the
certificate in `us-east-1`, and CloudFront is global.

- [ ] **Bucket:** create an S3 bucket in `eu-central-1`, for example `kroiko-pwa-<suffix>`. Keep "Block all
      public access" on, ACLs disabled (bucket owner enforced), versioning off, default encryption SSE-S3, and
      no static website hosting.
- [ ] **Certificate:** in ACM in **`us-east-1`**, request a public certificate for `app.kroiko.com` with DNS
      validation. Add the validation CNAME it shows in the site4now DNS panel, and wait for "Issued".
- [ ] **Functions:** in CloudFront → Functions, create `kroiko-pwa-main` and `kroiko-pwa-production`
      (runtime `cloudfront-js-2.0`). Paste `hosting/cloudfront/viewer-request.js` into both and publish them.
      A function on a Free-plan distribution cannot be shared, so each distribution gets its own copy.
- [ ] **Distributions:** create two standard (not multi-tenant) distributions with the same settings, except
      for the function and the domain:
  - Origin: the bucket's REST endpoint (not a website endpoint), with origin access control (sign requests).
    Leave the origin path empty; the first deploy sets it.
  - Default behavior: redirect HTTP to HTTPS; GET and HEAD only; compress objects automatically; cache policy
    **`UseOriginCacheControlHeaders`** (managed); no origin request policy; no response headers policy.
    Viewer request: its own function.
  - No default root object (the function handles `/`), no Lambda@Edge, no standard or real-time logs.
  - Production only: the alternate domain name `app.kroiko.com` and the ACM certificate.
  - Pricing plan: subscribe **each** distribution to the **Free** flat-rate plan.
- [ ] **Bucket policy:** fill in `hosting/aws/bucket-policy.json` with the bucket and both distribution ARNs, and
      apply it. It grants `s3:GetObject` and `s3:ListBucket` to CloudFront for those two distributions only.
      `s3:ListBucket` makes a missing file a 404 instead of a 403.
- [ ] **DNS:** in the site4now DNS panel, add a CNAME `app` → the production distribution's
      `dxxxxxxxxxxxxx.cloudfront.net`. It takes priority over the wildcard record for that one name. The origin
      is permanent from now on (ADR-0010).
- [ ] **Deploy user:** create the IAM user `kroiko-pwa-deployer` without console access. Give it the inline
      policy `hosting/aws/deployer-policy.json`, filled in, and create an access key. On the deploying machine,
      install AWS CLI v2 and run `aws configure --profile kroiko-pwa` (region `eu-central-1`). Enter the key
      only there.
- [ ] **Spend alert:** in AWS Budgets, create a "Zero spend budget" with your email.
- [ ] **Record:** fill in `hosting/aws/hosting.json` (bucket, distribution IDs, function names, the staging
      `*.cloudfront.net` URL) in the PR, with the date.

### 4. First deploy to `main` staging

Deploy the app with `-Environment main`, then check it by hand on the staging URL and note the results in the
PR:

- `curl -sI -H "Accept-Encoding: br" <url>/_framework/<a .wasm file>` → `Content-Encoding: br` and
  `Content-Type: application/wasm`. With `Accept-Encoding: gzip` instead → `Content-Encoding: gzip`;
- the `.dat` file with `br` → `Content-Encoding: br`, `application/octet-stream`;
  `/manifest.webmanifest` and a font return the MIME types above;
- `/configuration` and an unknown route both serve the app, with `Cache-Control: no-cache`. A missing
  `/_framework/x.js` and a missing `/_content/MudBlazor/x.css` are 404s;
- `/`, `index.html`, `service-worker.js` and `service-worker-assets.js` carry `Cache-Control: no-cache`;
- a second deploy switches all at once: afterwards `service-worker-assets.js` equals the new publish's;
- Edge installs it, and after one online visit it starts offline;
- the CloudFront console shows both distributions on the Free plan, and Billing shows $0.

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
the release procedure (with a `-DryRun` rehearsal before the deploy), every production release, and the
go-live. The manual checks that earlier phases left open are on it: the update flow from phase 06, the real
folder picker and downloads from phase 05, and Edge install and offline start. Also on it are phase 01's
Excel review, phase 02's Server smoke check, and step 4's host checks, which run again against
`app.kroiko.com`. Its host-specific parts (the tools, the credentials, the host checks) are rewritten for
S3 + CloudFront together with step 2.

Two choices go beyond this step's text:

- The update items are n/a for `v1.0.0`, because it has no earlier production build to update from.
- `release` is also the branch of the Server's `release_kroiko.yml` workflow, whose `push` trigger is
  commented out today. The procedure says to check that trigger before pushing.

### 2. First production release and sign-off

- Set `<Version>1.0.0</Version>`, follow the release procedure, and deploy to production.
- Open the GitHub issue **"Release v1.0.0 sign-off"** with the checklist pasted in. Run the
  go-live section with the operator, tick it, and close the issue.
- Only then are operators given `https://app.kroiko.com`. The Server stays deployed, unchanged.

## Done criteria

- [ ] `hosting/cloudfront/viewer-request.js` is committed and tested, and `StaticSiteHost` serves like it;
      `staticwebapp.config.json` is gone.
- [ ] `scripts/publish-pwa.ps1` uploads a new release folder with its `raw/` and `br/` trees, switches the origin
      path and invalidates. It never prints the credentials.
- [x] `scripts/publish-pwa.ps1` refuses dirty trees, wrong branches, unsynced `HEAD`, a missing or mismatched tag, and failing tests.
- [ ] The bucket and both distributions exist, each distribution on the Free plan; `app.kroiko.com` resolves to
      production over HTTPS.
- [ ] The `main` staging deploy passes the step-4 checks (07a done).
- [x] `docs/release-checklist.md` exists with the go-live, per-release and release-procedure sections.
- [ ] `v1.0.0` is tagged on `release` and deployed to production with the script.
- [ ] "Release v1.0.0 sign-off" is closed with every box ticked.
- [ ] The map's destination is reached: operators use the installed PWA.

## Out of this phase

- A CI/CD workflow (GitHub Actions, deployment environments, `-p:Version` stamping). It is a later
  improvement outside this plan, and when added it must enforce the same branch rules as the script.
- Infrastructure as code for the AWS resources.
- Any change to the Server's hosting or Azure resources.
