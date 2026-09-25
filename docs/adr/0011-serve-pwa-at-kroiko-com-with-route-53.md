# ADR-0011: Serve the PWA at `kroiko.com`, with its DNS in Route 53

- Status: Accepted
- Date: 2026-09-25
- Deciders: Pavel Pavlov

## Context

[ADR-0010](0010-host-pwa-on-s3-and-cloudfront.md) §1 serves the PWA at `app.kroiko.com`, a CNAME in our own DNS.
We want it at the bare domain `https://kroiko.com` instead. Nothing has been deployed yet, so no install or saved
setting is bound to either name. The origin is still permanent once operators get it.

- **The apex of a zone cannot be a CNAME.** CloudFront has no fixed IP address for an A record (static IPs are a
  paid feature). The apex therefore needs an ALIAS-type record that follows the distribution.
- Neither of our DNS hosts offers one. The domain moved on 2026-09-24 from site4now.net to SuperHosting.bg's
  nameservers, whose cPanel zone editor lists A, AAAA, CAA, CNAME, MX, SRV and TXT records only. The registrar
  is eNom (IANA 48), through the reseller where the nameservers are set.
- **Route 53** has ALIAS records to CloudFront. A hosted zone attached to a distribution's flat-rate plan is
  covered by it: the monthly zone fee, up to 50 records on Free, unlimited queries to ALIAS records that point
  at CloudFront, and 1 M other queries a month
  ([flat-rate plans: Route 53](https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/flat-rate-pricing-plan.html#costs-covered-by-plan)).
- The Free plan covers one distribution "with up to one apex (root) domain": `kroiko.com` with `www.kroiko.com`
  is one apex.
- Every name is its own origin. Serving the app at both `kroiko.com` and `www.kroiko.com` would split installs and
  per-device settings between them.

## Decision

1. **The origin is `https://kroiko.com`**, at the root path. It replaces `app.kroiko.com` from ADR-0010 §1, and
   the rest of ADR-0010 stands. Users are only ever given `https://kroiko.com`.
2. **`kroiko.com`'s DNS moves to a Route 53 public hosted zone**, attached to the production distribution's Free
   plan. At the registrar, the domain's nameservers become the zone's four Route 53 nameservers. Every record of
   the domain lives there from then on: the ALIASes, the certificate's validation record, and any future mail
   (MX, SPF, DKIM) records.
3. **Records:** `kroiko.com` and `www.kroiko.com` each get an A and an AAAA ALIAS to the production distribution.
   Staging keeps its `*.cloudfront.net` URL and gets no record.
4. **Certificate:** one ACM certificate in `us-east-1` for `kroiko.com` and `www.kroiko.com`, validated by DNS in
   the zone. Both names are the production distribution's alternate domain names.
5. **`www` redirects.** The CloudFront Function answers any `www.<domain>` request with a `301` to
   `https://<domain><path>`, dropping the query string, which the app does not use. It is the function's only
   generated response.

## Consequences

- ✅ The app lives at the plain brand domain, still at $0: the zone, its records and the ALIAS queries are part of
  the Free plan.
- ✅ Anyone typing `www.kroiko.com` lands on the one origin.
- ✅ DNS changes and the distribution live in one AWS account; validating the certificate is one click in ACM
  ("Create records in Route 53").
- ⚠️ The whole domain's DNS is in Route 53. Mail for `kroiko.com` (the Server shows `support@kroiko.com`; the
  domain has no MX today) needs its records added there, not in a hosting panel. Free allows 50 records.
- ⚠️ If non-ALIAS queries exceed 1 M a month, AWS notifies us and may move the zone to pay-as-you-go ($0.50 a
  month plus queries). Internal use will not come near it, and the zero-spend budget alert reports any charge.
- ⚠️ `kroiko.com` is the app. A public website, if one is wanted later, needs another name, such as `www` taken
  back from the redirect or a subdomain.
- ⚠️ Changing nameservers takes up to a day or two to propagate. Until then some resolvers may still ask the old
  hosts, so the go-live waits for the check that every resolver answers from Route 53.
- Follow-up: 07a steps 1 and 3, the release checklist and `hosting/aws/hosting.json` change to the new origin.

## Alternatives considered

- **Keep `app.kroiko.com`** (ADR-0010): works with any DNS host through a CNAME, but it is not the brand domain.
- **An ALIAS/ANAME record at site4now or SuperHosting.bg:** neither offers one.
- **A records for the apex pointing at CloudFront addresses:** CloudFront's addresses change, and its Anycast static
  IPs cost extra.
- **Another DNS provider with CNAME flattening** (such as Cloudflare): works, but it is a new vendor, when Route
  53 is already covered by the plan.
- **Serve the app at `www.kroiko.com` too:** a second origin, which splits installs and settings.

## Related

- Supersedes: [ADR-0010](0010-host-pwa-on-s3-and-cloudfront.md) §1 (the origin `app.kroiko.com`). The rest of
  ADR-0010 stands.
- Research: [`docs/research/aws-s3-cloudfront-hosting.md`](../research/aws-s3-cloudfront-hosting.md) §6
- Sources: [CloudFront flat-rate plans: Route 53](https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/flat-rate-pricing-plan.html),
  [Route 53 alias records](https://docs.aws.amazon.com/Route53/latest/DeveloperGuide/resource-record-sets-choosing-alias-non-alias.html),
  [SuperHosting.bg DNS zone editor](https://help.superhosting.bg/dns-zone-editor-cpanel.html)
- Implemented by: [07 — Hosting & go-live](../implementation/07-hosting-and-go-live.md)
