# Hosting the PWA on Amazon S3 + CloudFront for $0 — facts

> Research for [ADR-0010](../adr/0010-host-pwa-on-s3-and-cloudfront.md), which replaces the Azure Static Web
> Apps host of [ADR-0001](../adr/0001-host-pwa-on-azure-static-web-apps.md). Researched 2026-09-24 against the
> AWS documentation and pricing pages of that date.

**Legend.** **[V]** = verified against a primary source (link given) or by a local
measurement (method given). **[I]** = inference / recommendation drawn from verified facts.

---

## TL;DR

- **[V]** CloudFront has **flat-rate pricing plans with no overage charges**. The **Free** plan is $0/month and
  covers the distribution, its TLS certificate, CloudFront Functions and invalidations, plus 5 GB/month of S3
  Standard storage credit. S3 request fees are not covered.
- **[V]** The Free plan allows only the **managed** cache and response-header policies, no KeyValueStore, one
  distribution per plan, and at most 3 Free plans per account. An account still on the AWS Free Tier
  (free account plan) cannot subscribe.
- **[V]** CloudFront compresses on the fly by `Content-Type`, which includes `application/wasm` but not
  `application/octet-stream` (the ICU `.dat` files). It never recompresses a response that has a
  `Content-Encoding` header.
- **[V]** A CloudFront Function on the viewer request can read `Accept-Encoding` and rewrite the URI. The
  rewritten URI is what CloudFront caches and fetches.
- **[I]** Serving the published `.br` files needs a second tree of objects stored with `Content-Encoding: br`
  and a function that picks the tree. The result is ~3.7 MB per install instead of ~13.5 MB raw.

## 1. The flat-rate Free plan

- **[V]** Tiers: Free $0, Pro $15, Business $200, Premium from $1,000 a month. The Free allowance is 1 M
  requests and 100 GB of data transfer a month. "There are no overage charges regardless of traffic spikes or
  attacks." Beyond the allowance, "your traffic delivery might be adjusted" (fewer or more distant edge
  locations), in proportion to the excess. Emails at 50, 80 and 100 % of the allowance.
  — [Flat-rate pricing plans](https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/flat-rate-pricing-plan.html),
  [CloudFront pricing](https://aws.amazon.com/cloudfront/pricing/)
- **[V]** Included on Free: global CDN, "fast cache invalidations", default (managed) caching rules, default
  (managed) response-header rules, origin access control (OAC), the free ACM TLS certificate, CloudFront
  Functions ("serverless edge compute"), HTTP/2, HTTP/3, IPv6, 5 cache behaviors, and 5 GB of S3 Standard
  storage credit, which offsets any S3 Standard storage in the account.
  — [Flat-rate pricing plans: features](https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/flat-rate-pricing-plan.html#pricing-plan-features)
- **[V]** Not on Free: custom cache policies ("custom caching rules", Business and up), custom response-header
  policies, custom origin request policies, KeyValueStore (Pro and up), Origin Shield, access logs, an SLA.
  — same page
- **[V]** "Each pricing plan covers one CloudFront distribution with up to one apex (root) domain." Free plans
  per AWS account: 3. A CloudFront Function, a function with a key value store, or a WAF web ACL associated
  with a distribution on a plan can be used only by that distribution.
  — [Quotas, unsupported associations](https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/flat-rate-pricing-plan.html#pricing-plan-quotas)
- **[V]** A distribution cannot subscribe while it uses multi-tenant distributions, continuous deployment /
  staging distributions, `ForwardedValues`, IAM server certificates, an origin access identity (OAI) or legacy
  cache settings. An account "using AWS Free Tier" is not eligible, and "Free Tier accounts cannot use
  CloudFront Flat-Rate Plans".
  — [Unsupported features, account constraints](https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/flat-rate-pricing-plan.html#pricing-plan-unsupported-features),
  [Pricing Plan Manager: available plans](https://docs.aws.amazon.com/PricingPlanManager/latest/UserGuide/plans.html)
- **[V]** Billed separately even on a plan: Lambda@Edge, CloudFront Functions logs, log delivery to S3 or
  Firehose, extra CloudWatch metrics.
  — [Additional features that can affect your pricing plan](https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/flat-rate-pricing-plan.html#features-affect-pricing-plan)

## 2. What the plan does not cover: S3

- **[V]** S3 Standard, eu-central-1: $0.005 per 1,000 PUT/COPY/POST/LIST requests, $0.0004 per 1,000 GET
  requests, $0.024 per GB-month of storage. Data transferred out from S3 to CloudFront is free.
  — [S3 pricing](https://aws.amazon.com/s3/pricing/)
- **[I]** One deploy uploads ~2 × 100 objects (the `raw/` and `br/` trees, section 5), about $0.001. Reads from
  CloudFront on cache misses are a few hundred a month, well under $0.001. Storage is covered by the 5 GB
  credit for ~200 releases of ~25 MB. The monthly total is a fraction of a cent.
- **[V]** AWS accounts created from July 15, 2025 start on a 6-month free account plan with credits. On
  upgrade to the paid plan the remaining credits keep applying until they expire. Paid accounts keep the
  "always free" offers. The FAQ names no hard spending cap.
  — [AWS Free Tier FAQs](https://aws.amazon.com/free/free-tier-faqs/),
  [Free Tier update, July 2025](https://aws.amazon.com/about-aws/whats-new/2025/07/aws-free-tier-credits-month-free-plan/)

## 3. Compression

- **[V]** With "Compress objects automatically" and a cache policy that enables Gzip and Brotli, CloudFront
  compresses objects of 1,000 to 10,000,000 bytes whose `Content-Type` is on its list. The list includes
  `application/wasm`, `application/javascript`, `text/javascript`, `text/css`, `text/html`, `application/json`
  and `image/svg+xml`. It does not include `application/octet-stream`, `application/manifest+json` or
  `font/woff2`. It prefers Brotli when the viewer accepts both, and browsers send `br` only over HTTPS.
  — [Serve compressed files](https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/ServingCompressedFiles.html)
- **[V]** "When a response from an origin includes the `Content-Encoding` header, CloudFront doesn't compress
  the object, regardless of the header's value." Compression is best-effort: under load CloudFront may cache
  and serve an uncompressed copy. — same page
- **[V, measured]** `dotnet publish Kroiko.Client.Blazor -c Release` on 2026-09-24: 89 plain files, 13.5 MB.
  The `.br` siblings of the `.wasm` files are 2.93 MB (raw 9.24 MB), of the three ICU `.dat` files 0.62 MB
  (raw 2.61 MB), of `.js` 0.13 MB (raw 0.56 MB), of `.css` 0.04 MB (raw 0.62 MB). The largest file,
  `dotnet.native.*.wasm`, is 3.0 MB, under CloudFront's 10 MB limit. Method: `find -printf '%s'` summed per
  extension over `wwwroot`, with and without `.br`.
- **[I]** With CloudFront's own compression only, the `.dat` files cross the wire raw: ~2 MB more per install
  or update, about 6 MB in all. Serving the published `.br` files keeps it at ~3.7 MB.

## 4. Edge functions and caching

- **[V]** In viewer request events, the read-only headers are `CDN-Loop`, `Content-Length`, `Host`,
  `Transfer-Encoding` and `Via`. `Accept-Encoding` is neither disallowed nor read-only there: a function can
  read it. — [Restrictions on all edge functions](https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/edge-function-restrictions-all.html)
- **[V]** "If a function changes the URI for a request, that doesn't change the cache behavior for the request
  or the origin that the request is forwarded to." Each event type has at most one function per cache
  behavior. — same page
- **[V]** Managed cache policies: `CachingOptimized` (`658327ea-f89d-4fab-a63d-7e88639e58f6`) has a minimum TTL
  of 1 s, so it caches `no-cache` objects for 1 s. `UseOriginCacheControlHeaders`
  (`83da9c7e-98b4-4e11-a168-04f0df8e2c65`) has minimum and default TTL 0 and maximum 365 days, Gzip and Brotli
  on, and `Host` and `Origin` in the cache key. With a minimum TTL of 0, "CloudFront and browsers respect" an
  origin's `Cache-Control: no-cache`.
  — [Managed cache policies](https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/using-managed-cache-policies.html),
  [Expiration](https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/Expiration.html)
- **[V]** An S3 origin's `Cache-Control` comes from the object's metadata, set at upload.
  — [Expiration: add headers in S3](https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/Expiration.html#ExpirationAddingHeadersInS3)

## 5. Consequences for the design

- **[I]** Upload each release twice: `raw/` (the plain files) and `br/` (the same paths, holding the `.br`
  sibling's bytes with `Content-Encoding: br` and the plain file's `Content-Type`, or the plain file where the
  publish made no `.br`). A viewer-request function sends a request accepting `br` to `br/` and the rest to
  `raw/`. The rewritten URI keeps the two in separate cache entries, and CloudFront leaves `br/` alone because
  it carries `Content-Encoding`.
- **[I]** The same function rewrites a path with no file extension to `/index.html`, which gives the SPA
  fallback without CloudFront's custom error responses. Those would also turn a missing
  `/_framework/x.js` into `index.html`.
- **[I]** A private bucket returns 403 for a missing key unless the reader may list the bucket. The bucket
  policy grants the distributions `s3:ListBucket` as well as `s3:GetObject`, so a missing file is a 404.
- **[I]** Without KeyValueStore, the atomic switch is the distribution's origin path: upload the release to a new
  folder, point the origin path at it, invalidate `/*`. The files are fingerprinted, and everything else is
  `no-cache` under `UseOriginCacheControlHeaders`, so the invalidation is a safety net, not a requirement.
- **[I]** Staging and production are two distributions, each on its own Free plan (2 of the 3 allowed), each
  with its own copy of the function.

## 6. The apex `kroiko.com` and Route 53 (added 2026-09-25, for [ADR-0011](../adr/0011-serve-pwa-at-kroiko-com-with-route-53.md))

- **[V]** A hosted zone attached to a distribution's plan is covered by it: "the monthly hosted zone fee, DNS
  records, and DNS query fees subject to respective allowances per tier". On Free: 50 records per zone, no limit
  on queries to ALIAS records that point at CloudFront, and 1 M other queries a month. CNAME records to CloudFront
  count against that 1 M. If usage exceeds it, AWS may notify you and then move the zone to pay-as-you-go. The
  zone is attached in the distribution's **Manage Plan** section.
  — [Flat-rate pricing plans: Route 53 DNS](https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/flat-rate-pricing-plan.html#costs-covered-by-plan)
- **[V]** Route 53 ALIAS records can point a zone apex at a CloudFront distribution; a CNAME cannot exist at the
  apex. — [Choosing between alias and non-alias records](https://docs.aws.amazon.com/Route53/latest/DeveloperGuide/resource-record-sets-choosing-alias-non-alias.html)
- **[V]** SuperHosting.bg's cPanel zone editor offers A, AAAA, CAA, CNAME, DMARC, MX, SRV and TXT records, with no
  ALIAS/ANAME. — [DNS zone editor in cPanel](https://help.superhosting.bg/dns-zone-editor-cpanel.html)
- **[V, measured]** On 2026-09-25 RDAP showed `kroiko.com` registered with eNom (IANA 48), with its nameservers
  changed on 2026-09-24 to `ns23`/`ns24.superhosting.bg`. Those servers refused queries for the zone, so public
  resolvers (8.8.8.8, 1.1.1.1) returned SERVFAIL. Method: `https://rdap.verisign.com/com/v1/domain/kroiko.com`,
  `Resolve-DnsName -Server`.
- **[I]** The CloudFront Function can serve the `www` redirect itself (it reads `Host` on the viewer request), so
  `www.kroiko.com` needs no bucket or distribution of its own. It needs only an alternate domain name on the
  production distribution, a name on the certificate, and an ALIAS.
