# ADR-0010: Host the PWA on Amazon S3 + CloudFront (flat-rate Free plan) at `app.kroiko.com`

- Status: Accepted
- Date: 2026-09-24
- Deciders: Pavel Pavlov

## Context

[ADR-0001](0001-host-pwa-on-azure-static-web-apps.md) chose Azure Static Web Apps. Azure is not available to us
for the PWA, and the host must cost nothing. Nothing was ever deployed there: 07a stopped before provisioning,
so no install or saved setting is bound to that host.

ADR-0001's requirements still hold:

- **HTTPS on a permanent origin**, `app.kroiko.com`. Installs, the service-worker cache and the per-device
  settings are bound to it.
- **Real `Content-Encoding` for the plain URLs**, best from the published `.br` files. Today's publish is
  13.5 MB raw and ~3.7 MB as `.br` ([research](../research/aws-s3-cloudfront-hosting.md) §3).
- Correct MIME types, no content rewriting, an SPA fallback to `index.html` that still gives a 404 for a missing
  asset, control over `Cache-Control` (`no-cache` on the update check's files,
  [ADR-0002](0002-pwa-updates-reload-prompt.md)), and **atomic deploys** with no stale leftovers.
- Independence from `ATAFurniture.Server`.

A new one: **$0, and traffic must never be able to create a bill.**

The facts that decide it ([`docs/research/aws-s3-cloudfront-hosting.md`](../research/aws-s3-cloudfront-hosting.md)):

- CloudFront's flat-rate **Free** plan costs $0 a month with **no overage charges**. Beyond its allowance
  (1 M requests, 100 GB a month), delivery may slow down but is never billed. It includes the TLS
  certificate, CloudFront Functions, invalidations and 5 GB of S3 storage credit. It allows only managed
  cache policies, no KeyValueStore, one distribution per plan and 3 Free plans per account, and it needs an
  account on the paid account plan.
- S3 request fees are not covered: about $0.001 per deploy, and less than that for a month of reads. Transfer
  from S3 to CloudFront is free.
- CloudFront compresses by `Content-Type`: `application/wasm`, JS, CSS and JSON, but not the ICU `.dat` files
  (`application/octet-stream`). It never recompresses a response that has a `Content-Encoding`.
- S3 serves stored bytes with the metadata set at upload and cannot negotiate an encoding. A viewer-request
  CloudFront Function can read `Accept-Encoding` and rewrite the URI.

## Decision

We will serve the PWA from **`https://app.kroiko.com`** at the root path (`/`) through **Amazon CloudFront on
the flat-rate Free plan**, from a **private S3 bucket in Frankfurt (`eu-central-1`)**, in our AWS account
(paid account plan).

1. **Origin.** `app.kroiko.com` is a CNAME in our own DNS (site4now) pointing at the production distribution,
   with an ACM certificate (issued in `us-east-1`, as CloudFront requires). Users are only ever given
   `app.kroiko.com`, never a `*.cloudfront.net` URL, so the host can be replaced later by changing the CNAME
   without changing the origin.
2. **Two distributions, each on its own Free plan.** Production serves `app.kroiko.com`. Staging `main` serves
   its generated `*.cloudfront.net` URL. The bucket is private: only the two distributions read it (origin
   access control). They may also list it, so a missing file is a 404, not a 403.
3. **Every deploy is a new, immutable release folder.** A deploy uploads the publish to a new
   `<environment>/<release>/` folder and never overwrites one. It then switches the distribution's origin path
   to that folder and invalidates `/*`. The switch is the deploy. Old folders stay until pruned by hand.
4. **Brotli comes from the published `.br` files.** A release holds a `raw/` tree (the plain files) and a
   `br/` tree. `br/` has the same paths, holding the `.br` file's bytes with `Content-Encoding: br` wherever
   the publish made one. One CloudFront Function on the viewer request, committed in the repo:
   - sends requests that accept `br` to `br/`, and the rest to `raw/` (which CloudFront gzips where it can);
   - rewrites a path without a file extension to `/index.html`, the SPA fallback.
5. **Headers are object metadata, set at upload:**
   - `Content-Type` comes from a fixed extension map.
   - `Cache-Control` is `no-cache` on `index.html`, `service-worker.js`, `service-worker-assets.js` and every
     other file whose name is not fingerprinted (ADR-0002). Fingerprinted `_framework/` files may be cached
     for a long time.
   - The distributions use the managed `UseOriginCacheControlHeaders` cache policy, so CloudFront follows those
     headers exactly.
6. **ADR-0001's branch model is unchanged:**
   - push to `release` → production;
   - push to `main` → staging;
   - both deployed by hand with `scripts/publish-pwa.ps1`;
   - pull requests build and test only, with no per-PR preview environments.

   The script also brings the environment's function up to the committed code.
7. **Deploy credentials** belong to an IAM user limited to the bucket, the two distributions and their
   functions. The script uses them through a named AWS CLI profile on the deploying machine. They are never
   committed and never printed.

## Consequences

- ✅ The CDN, certificate, function and storage cost $0 whatever the traffic. The only billable items, S3
  requests, are a fraction of a cent a month. A zero-spend AWS Budgets alert reports any charge at all.
- ✅ The same size on the wire that ADR-0001 planned for: ~3.7 MB per install or update, including the `.dat`
  files CloudFront would not compress.
- ✅ Atomic, with no leftovers: a deploy is one switch between two complete folders. Deep links get
  `index.html` with its `no-cache` header too, which Static Web Apps would not have applied to a fallback.
- ✅ No coupling to the Server's hosts.
- ⚠️ More moving parts than Static Web Apps, all provisioned by hand (07a step 3): a bucket, two distributions,
  two copies of the function, a certificate, a bucket policy and an IAM user. There is no infrastructure as
  code in this plan.
- ⚠️ The Free plan has **no SLA**, and its allowance is soft. Accepted, as in ADR-0001: installed apps run
  offline, so an outage or a slowdown only delays updates and first installs.
- ⚠️ An origin-path switch reaches every edge location within minutes, not at one instant. The service
  worker's integrity check rejects a mixed download, and the next update check retries it.
- ⚠️ Release folders accumulate at ~25 MB each; the 5 GB credit holds ~200. Prune old ones by hand, never the
  live one.
- ⚠️ Staging is a different origin, as before: installs and settings made there are separate from production.
- Follow-up: 07a steps 1 and 2 are rebuilt for this host. `staticwebapp.config.json`, its service-worker
  exclusion and tests, and the `swa deploy` call are removed. A CI workflow and infrastructure as code
  remain out of scope.

## Alternatives considered

- **Azure Static Web Apps** ([ADR-0001](0001-host-pwa-on-azure-static-web-apps.md)): not available to us.
- **AWS Amplify Hosting:** the closest match to Static Web Apps (atomic deploys, branches, rewrites, headers).
  But it cannot serve the published `.br` files, so the `.dat` files cross uncompressed, ~2 MB more per
  install. It is also pay-as-you-go, with no protection against a traffic spike.
- **S3 + CloudFront on pay-as-you-go:** its always-free usage would cover our traffic, but anything beyond it,
  such as an attack, is billed.
- **CloudFront's own compression only**, with no `br/` tree: simpler, but the `.dat` files are ~2 MB more per
  install or update.
- **Uploading in place** (`aws s3 sync` into one folder): not atomic, and leaves a window of mixed versions and
  stale files.
- **Switching releases with a CloudFront KeyValueStore:** not on the Free plan.
- **S3 static website hosting alone:** no HTTPS on a custom domain.
- **GitHub Pages:** cannot set response headers or serve the pre-compressed `.br` files (ADR-0001).
- **Cloudflare Pages:** still technically suitable, but adds a vendor account when CloudFront's Free plan
  already meets every requirement.

## Related

- Supersedes: [ADR-0001](0001-host-pwa-on-azure-static-web-apps.md) (the branch model and the permanent origin
  carry over).
- [ADR-0002](0002-pwa-updates-reload-prompt.md): the `no-cache` headers on the update check's files.
- Research: [`docs/research/aws-s3-cloudfront-hosting.md`](../research/aws-s3-cloudfront-hosting.md),
  [`docs/research/dotnet10-blazor-wasm-pwa.md`](../research/dotnet10-blazor-wasm-pwa.md)
- Sources: [CloudFront flat-rate pricing plans](https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/flat-rate-pricing-plan.html),
  [Serve compressed files](https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/ServingCompressedFiles.html),
  [Managed cache policies](https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/using-managed-cache-policies.html),
  [Restrictions on edge functions](https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/edge-function-restrictions-all.html),
  [S3 pricing](https://aws.amazon.com/s3/pricing/)
- Implemented by: [07 — Hosting & go-live](../implementation/07-hosting-and-go-live.md)
