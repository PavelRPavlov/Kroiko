# ADR-0012: Use the managed `CachingOptimized` cache policy

- Status: Accepted
- Date: 2026-09-25
- Deciders: Pavel Pavlov

## Context

[ADR-0010](0010-host-pwa-on-s3-and-cloudfront.md) §5 put both distributions on the managed
`UseOriginCacheControlHeaders` cache policy, so that CloudFront would follow the objects' `Cache-Control` exactly
(minimum TTL 0).

Provisioning showed that this policy cannot be used here. It puts `Host` in the cache key, so CloudFront forwards
the viewer's `Host` (`kroiko.com`) to the origin. An S3 REST endpoint only answers to its own name, and the
console greys the policy out for an S3 origin: "S3 expects the origin's host and cannot resolve the
distribution's host." Custom cache policies need the Business plan, not Free.

The managed policies left for an S3 origin with compression are `CachingOptimized` (the console's "Recommended for
S3") and `CachingDisabled`. `CachingDisabled` also turns off CloudFront's compression.

`CachingOptimized` (`658327ea-f89d-4fab-a63d-7e88639e58f6`): minimum TTL 1 s, default 24 h, maximum 365 days, Gzip
and Brotli on, and only the normalised `Accept-Encoding` in the cache key. With a minimum TTL above 0, CloudFront
caches a `no-cache` object for that minimum
([managed cache policies](https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/using-managed-cache-policies.html),
[expiration](https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/Expiration.html)).

## Decision

Both distributions use the managed **`CachingOptimized`** cache policy. Everything else in ADR-0010 stands: every
object's `Cache-Control` is set at upload, `no-cache` on `index.html`, the service-worker files and every file that
is not fingerprinted, and a year on the fingerprinted `_framework/` files. Every deploy invalidates `/*`.

## Consequences

- ✅ It works with an S3 origin, keeps compression on, and keeps `Host` out of the cache key, so `kroiko.com`,
  `www.kroiko.com` and the `*.cloudfront.net` name share one cache.
- ✅ Browsers still get the objects' own `Cache-Control`: `no-cache` stays `no-cache` for them (ADR-0002).
- ⚠️ The edge may serve a `no-cache` file for up to 1 s after it changed. The deploy's invalidation clears it
  anyway, and an update check that runs a second late is harmless.
- ⚠️ An object without `Cache-Control` would be cached for 24 h. The deploy sets it on every object, so none has
  that default.

## Alternatives considered

- **`UseOriginCacheControlHeaders`** (ADR-0010): cannot be used with an S3 origin, because it forwards `Host`.
- **`CachingDisabled`:** every request goes to S3, and CloudFront's own compression is off, so gzip-only clients
  would get the `raw/` tree uncompressed.
- **A custom cache policy with minimum TTL 0:** needs the Business plan ($200 a month).

## Related

- Supersedes: [ADR-0010](0010-host-pwa-on-s3-and-cloudfront.md) §5, its cache-policy sentence only.
- [ADR-0002](0002-pwa-updates-reload-prompt.md): the `no-cache` headers on the update check's files.
- Research: [`docs/research/aws-s3-cloudfront-hosting.md`](../research/aws-s3-cloudfront-hosting.md) §4
- Implemented by: [07 — Hosting & go-live](../implementation/07-hosting-and-go-live.md) step 3
