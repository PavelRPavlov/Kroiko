# ADR-0017: Production deploys the commit of the pushed tag, if it is on release

- Status: Accepted
- Date: 2026-10-06
- Deciders: Pavel Pavlov

## Context

[ADR-0013](0013-deploy-production-from-release-tag-with-github-actions.md) §2 made the workflow check out `release` at
`origin/release` and run `publish-pwa.ps1 -Environment production` unchanged. The script then never looked at the tag
that was pushed: it derived the tag from the csproj `<Version>` at `release`'s tip and required that tag on that exact
commit.

The `v0.1.2` release failed on this:

> `publish-pwa: refused - HEAD carries no tag v0.1.1 matching the csproj <Version> 0.1.1 (tags on HEAD: none)`

- `v0.1.2` was pushed on main's merge commit of PR #89. `release`'s tip was a later commit, the
  "Merge branch 'main' into release" that brought that same merge in, so no tag was on HEAD.
- `<Version>` was still `0.1.1` on every branch: the bump had been forgotten. The message named `v0.1.1`, the tag the
  csproj implied, not the one that had been pushed.

## Decision

1. **The workflow deploys the pushed tag.** It builds the commit the tag points at (the checkout of the tag push, with
   all branches and tags fetched, origin's tags re-fetched with `--force`), and passes the tag to the script as
   `-Tag ${{ github.ref_name }}`, through an environment variable.
2. **The script's production rules** (with `-Tag`, or `v<csproj Version>` by hand without it):
   - the tag is `vX.Y.Z` and points at `HEAD`;
   - the csproj `<Version>` at that commit equals the tag (the build's displayed version, ADR-0002 §5–6, stays the
     hand-maintained csproj value);
   - the commit is on `origin/release` — merged into it, **not necessarily its tip**;
   - the tag is on origin unchanged, and not older than the newest `vX.Y.Z` tag there (unchanged).
   `release` no longer needs to be checked out; a detached `HEAD` at the tag is the normal case.
3. Each refusal names what to fix: a tag not on `HEAD` says `git checkout <tag>`; a version mismatch says to bump
   `<Version>`, merge it into release and tag that commit; a commit not on release says to merge it first.

## Consequences

- ✅ A tag on main's merge commit deploys once that commit is merged into `release`, whichever commit `release`'s tip
  is; tagging `release`'s tip still works.
- ✅ The failure message is about the tag that was pushed.
- ⚠️ A pushed tag whose commit has the wrong `<Version>` still never deploys, and pushed tags are never moved
  (`docs/release-checklist.md`). `v0.1.2` (csproj `0.1.1`) stays an undeployed tag; the next release is `v0.1.3`.
- An older commit of `release` can be tagged and deployed only if its tag is the newest; the roll-forward rule
  (ADR-0002 §8) still refuses anything older.

## Alternatives considered

- **Take the version from the tag** (`dotnet publish -p:Version=…`), dropping the csproj bump for releases: the
  `v0.1.2` deploy would have gone through as-is, but the csproj would no longer say what is in production, and builds
  of the same commit would differ by how they were started.
- **Keep checking out `release`'s tip and look for the pushed tag there**: still refuses a tag on main's merge commit
  when `release` has a later merge commit, which is how the release procedure usually leaves it.

## Related

- Supersedes [ADR-0013](0013-deploy-production-from-release-tag-with-github-actions.md) §2's checkout of
  `release`'s tip; the rest of ADR-0013 stands.
- [ADR-0002](0002-pwa-updates-reload-prompt.md) §5, §6, §8 — the displayed version, the csproj `<Version>`, roll forward.
- Implemented in `.github/workflows/deploy-pwa.yml`, `scripts/publish-pwa.ps1`,
  `Kroiko.Client.Tests/PublishScript/PublishScriptProductionTests.cs`; procedure in `docs/release-checklist.md`.
