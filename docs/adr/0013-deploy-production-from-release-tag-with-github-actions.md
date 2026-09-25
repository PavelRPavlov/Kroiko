# ADR-0013: Deploy production from a pushed release tag with GitHub Actions

- Status: Accepted
- Date: 2026-09-25
- Deciders: Pavel Pavlov

## Context

[ADR-0010](0010-host-pwa-on-s3-and-cloudfront.md) §6 and §7 deploy by hand: a person runs
`scripts/publish-pwa.ps1` on their own machine, with the deploy user's access key in its AWS CLI profile
`kroiko-pwa`. [07](../implementation/07-hosting-and-go-live.md) left a CI workflow out of the plan, with one condition:
when added, it must enforce the same branch rules as the script.

The first production deploy (`v0.1.0`, 2026-09-25) worked by hand. Every step after the tag push was mechanical:
fetch, check out `release`, run the script. The script already refuses everything the branch model forbids: a tag
that is not on `release`'s tip, one that differs from the csproj `<Version>`, one older than the newest tag, and
failing tests.

The Server's Azure workflow already uses a GitHub environment named `Production`. GitHub environment names are
not case-sensitive, so the PWA's environment cannot be called `production` without sharing its secrets.

## Decision

We will deploy production from GitHub Actions whenever a release tag is pushed, in
`.github/workflows/deploy-pwa.yml`:

1. **Trigger:** every tag push. A first job checks the tag against `^v[0-9]+\.[0-9]+\.[0-9]+$`, the script's pattern
   for release tags. Any other tag ends the run there, with a notice and nothing deployed.
2. **The deploy is the script.** The deploy job checks out `release` at `origin/release` and runs
   `scripts/publish-pwa.ps1 -Environment production` unchanged. The script's checks, the full `dotnet test`
   including E2E, the upload, the function sync and the switch are therefore identical by hand and in CI. It runs
   on `windows-latest`, like the machines the script and the tests were built on.
3. **Credentials in a GitHub environment**, `pwa-production`, as the secrets `AWS_ACCESS_KEY_ID` and
   `AWS_SECRET_ACCESS_KEY`. The key is a second access key of the same IAM user `kroiko-pwa-deployer`, with the same
   narrow policy (`hosting/aws/deployer-policy.json`). The workflow writes it to the runner's AWS credentials
   file as the profile `kroiko-pwa`, which the script already uses. The environment allows only tags matching
   `v*` to deploy.
4. **One deploy at a time:** a second tag waits for the running deploy instead of cancelling it mid-switch.
5. **The script stays** for deploys by hand. It is also the fallback when Actions is unavailable, and it is the
   only way to deploy staging.

The release procedure becomes: bump `<Version>` in a PR to `main`, merge `main` into `release`, push it, tag
`vX.Y.Z`, push the tag. The workflow does the rest.

## Consequences

- ✅ A release is a tag push. The build, tests and deploy run on a clean machine, with a log in the Actions tab
  and the environment's deployment history.
- ✅ The branch model has one implementation: the script. Nothing in the workflow can deploy what the script
  would refuse.
- ✅ The deploying machine no longer needs the AWS CLI, the profile or the Playwright browser.
- ⚠️ An access key now also lives in GitHub. Its policy limits it to uploading into `kroiko-pwa` and to
  switching, invalidating and updating the function of the production distribution: it cannot create a
  distribution or a bucket. If it leaks, deactivate it in IAM; the key on the deploying machine is a separate
  one and keeps working.
- ⚠️ Anyone with write access to the repository can push a `v*` tag, and so deploy. A required reviewer on the
  environment would add an approval step, at the cost of a manual click per release.
- ⚠️ The tests now also run on a GitHub runner. A test that passes locally but not there blocks the release
  until it is fixed.
- ⚠️ GitHub does not start workflows for a push of more than three tags at once. Push release tags one at a time.
- Follow-up: [OpenID Connect](https://docs.github.com/en/actions/security-for-github-actions/security-hardening-your-deployments/configuring-openid-connect-in-amazon-web-services)
  with an IAM role would replace the stored key with short-lived credentials. That is a later improvement.

## Alternatives considered

- **Keep deploying by hand only:** works, but every release needs a configured machine and a person to wait
  through the tests and CloudFront.
- **Reimplement the checks as workflow steps** (for example `aws-actions/configure-aws-credentials` and
  `aws s3 sync`): two copies of the branch model and of the upload rules, which would drift apart.
- **Trigger on `v*` tags only:** simpler, but a tag such as `v1.0` or `v1.0.0-rc1` would start a deploy that the
  script then refuses, reported as a failure. Checking the pattern first ends such runs quietly.
- **Name the environment `production`:** it would be the Server workflow's `Production`, with its FTP secrets.
- **OpenID Connect now:** needs an IAM identity provider and a role, more provisioning than the deploy needs today.

## Related

- Supersedes: [ADR-0010](0010-host-pwa-on-s3-and-cloudfront.md) §6 and §7 for production (the deploy by hand and
  the credentials only on the deploying machine). Staging still deploys by hand.
- [ADR-0002](0002-pwa-updates-reload-prompt.md) §6, §8: the version and roll-forward rules the script enforces.
- Implemented by: [07 — Hosting & go-live](../implementation/07-hosting-and-go-live.md) step 5 and
  [`docs/release-checklist.md`](../release-checklist.md)
