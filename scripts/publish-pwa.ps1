#Requires -Version 7.2
<#
.SYNOPSIS
    Tests, publishes and deploys Kroiko.Client.Blazor (the PWA) to Azure Static Web Apps.

.DESCRIPTION
    Enforces the branch model of ADR-0001 (docs/adr/0001-host-pwa-on-azure-static-web-apps.md) and refuses to
    run unless:
      1. the working tree is clean (git status --porcelain is empty);
      2. -Environment main:       HEAD equals origin/main (after a fetch);
         -Environment production: release is checked out, HEAD equals origin/release, and HEAD carries the tag
                                  vX.Y.Z of the csproj <Version>, pushed to origin and not older than any other
                                  vX.Y.Z tag there (roll forward, never back: ADR-0002 §8);
      3. the full `dotnet test` passes, E2E included.
    Then it runs `dotnet publish -c Release` into a temporary folder and
    `swa deploy <temp>/wwwroot --env <main|production>`, and prints the deployed version and URL.

    The deployment token is read by the SWA CLI from SWA_CLI_DEPLOYMENT_TOKEN in your own environment. It is never
    a parameter, never written to a file and never printed: only the swa call sees it, and every line the CLI
    prints passes through a filter that masks it.

    See docs/implementation/07-hosting-and-go-live.md (07a.2) for the rules and 07b for the release procedure.

.PARAMETER Environment
    main       - the Static Web Apps staging environment "main", from origin/main.
    production - app.kroiko.com, from a tagged origin/release.

.PARAMETER DryRun
    Runs every check, the tests and the publish, then prints the swa command instead of running it (the publish
    folder is removed afterwards). Needs neither the token nor the SWA CLI.

.EXAMPLE
    $env:SWA_CLI_DEPLOYMENT_TOKEN = Read-Host -MaskInput 'SWA deployment token'
    ./scripts/publish-pwa.ps1 -Environment main

.EXAMPLE
    ./scripts/publish-pwa.ps1 -Environment production -DryRun
#>
param(
    [Parameter(Mandatory)]
    [ValidateSet('main', 'production')]
    [string] $Environment,

    [switch] $DryRun
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false   # native exit codes are checked by hand below

$repoRoot = Split-Path -Parent $PSScriptRoot
$clientProject = Join-Path $repoRoot 'Kroiko.Client.Blazor/Kroiko.Client.Blazor.csproj'
$branch = if ($Environment -eq 'production') { 'release' } else { 'main' }
$token = $env:SWA_CLI_DEPLOYMENT_TOKEN
$savedSwaDebug = $env:SWA_CLI_DEBUG
$publishDir = Join-Path ([System.IO.Path]::GetTempPath()) "kroiko-pwa-$([guid]::NewGuid().ToString('N'))"

function Stop-Publish([string] $Reason) {
    Write-Host "publish-pwa: refused - $Reason" -ForegroundColor Red
    exit 1   # the finally block below still runs
}

function Get-Commit([string] $Ref) {
    $sha = git -C $repoRoot rev-parse --verify --quiet "$Ref^{commit}"
    if ($LASTEXITCODE -ne 0) { return $null }
    return $sha
}

# --- Tools and token (not needed for a dry run) ---------------------------------------------------------------

if (-not $DryRun) {
    if (-not $token) {
        Stop-Publish ("SWA_CLI_DEPLOYMENT_TOKEN is not set. Set it in your own environment, e.g. " +
            "`$env:SWA_CLI_DEPLOYMENT_TOKEN = Read-Host -MaskInput 'SWA deployment token'")
    }
    if (-not (Get-Command swa -ErrorAction SilentlyContinue)) {
        Stop-Publish 'the SWA CLI is not installed (npm i -g @azure/static-web-apps-cli)'
    }
}

try {
    # Only the swa call gets the token: git, the tests and the build never see it. Restored below for your session.
    $env:SWA_CLI_DEPLOYMENT_TOKEN = $null

    # --- 1. Clean working tree ---------------------------------------------------------------------------------

    $changes = git -C $repoRoot status --porcelain
    if ($LASTEXITCODE -ne 0) { Stop-Publish "git status failed in $repoRoot" }
    if ($changes) {
        Stop-Publish "the working tree is not clean (git status --porcelain):`n$($changes -join "`n")"
    }

    # --- 2. Branch model (ADR-0001) ----------------------------------------------------------------------------

    git -C $repoRoot fetch --quiet --prune origin
    if ($LASTEXITCODE -ne 0) { Stop-Publish 'git fetch origin failed, so HEAD cannot be checked against origin' }

    if ($Environment -eq 'production') {
        $checkedOut = git -C $repoRoot symbolic-ref --quiet --short HEAD
        if ($checkedOut -ne 'release') {
            Stop-Publish "production deploys only from the release branch (checked out: $(if ($checkedOut) { $checkedOut } else { 'a detached HEAD' }))"
        }
    }

    $head = Get-Commit 'HEAD'
    $originHead = Get-Commit "origin/$branch"
    if (-not $originHead) { Stop-Publish "origin has no $branch branch" }
    if ($head -ne $originHead) {
        Stop-Publish "HEAD is not origin/$branch; check out origin/$branch exactly (pull or push first)"
    }

    # The hand-maintained SemVer <Version> (ADR-0002 §6); without one the SDK builds 1.0.0.
    # [string]: no match yields '' rather than an empty pipeline, which -notmatch would treat as a collection.
    $version = [string]((Select-Xml -LiteralPath $clientProject -XPath '/Project/PropertyGroup/Version').Node.InnerText | Select-Object -First 1)

    if ($Environment -eq 'production') {
        if ($version -notmatch '^\d+\.\d+\.\d+$') {
            Stop-Publish "the client csproj <Version> must be X.Y.Z (found: '$version'); bump it in a PR to main before releasing"
        }
        $tag = "v$version"
        $tagsOnHead = @(git -C $repoRoot tag --points-at HEAD)
        if ($tagsOnHead -notcontains $tag) {
            $found = if ($tagsOnHead) { $tagsOnHead -join ', ' } else { 'none' }
            Stop-Publish "HEAD carries no tag $tag matching the csproj <Version> $version (tags on HEAD: $found)"
        }

        # origin's tags, as refs/tags/<name> -> object id (the peeled ^{} lines are skipped).
        $remoteTagLines = git -C $repoRoot ls-remote --tags --refs origin
        if ($LASTEXITCODE -ne 0) { Stop-Publish "git ls-remote origin failed, so tag $tag cannot be checked" }
        $remoteTags = @{}
        foreach ($line in $remoteTagLines) {
            $objectId, $ref = $line -split '\s+'
            $remoteTags[$ref] = $objectId
        }
        if ($remoteTags["refs/tags/$tag"] -ne (git -C $repoRoot rev-parse "refs/tags/$tag")) {
            Stop-Publish "tag $tag is not on origin, or origin's differs; push it first (git push origin $tag)"
        }
        $newest = $remoteTags.Keys |
            Where-Object { $_ -match '^refs/tags/v(\d+\.\d+\.\d+)$' } |
            ForEach-Object { [version]($_ -replace '^refs/tags/v', '') } |
            Sort-Object -Descending | Select-Object -First 1
        if ([version]$version -lt $newest) {
            Stop-Publish "$tag is older than v$newest on origin; roll forward with a new version, never redeploy an older build (ADR-0002 §8)"
        }
    }

    $shortSha = git -C $repoRoot rev-parse --short=7 HEAD
    $deploying = "v$(if ($version) { $version } else { '1.0.0' }) ($shortSha)"   # as About shows it (ADR-0002 §5)
    if (-not $version) { Write-Host 'Note: the client csproj has no <Version>, so the build is the SDK default 1.0.0.' }

    # --- 3. The full test run, E2E included (ADR-0007 §6) ------------------------------------------------------

    dotnet test (Join-Path $repoRoot 'TextConverter.sln')
    if ($LASTEXITCODE -ne 0) { Stop-Publish 'dotnet test failed' }

    # --- Publish and deploy ------------------------------------------------------------------------------------

    dotnet publish $clientProject -c Release -o $publishDir
    if ($LASTEXITCODE -ne 0) { Stop-Publish 'dotnet publish failed' }

    $webRoot = Join-Path $publishDir 'wwwroot'
    foreach ($required in 'index.html', 'staticwebapp.config.json') {
        if (-not (Test-Path -LiteralPath (Join-Path $webRoot $required))) {
            Stop-Publish "the publish output has no wwwroot/$required"
        }
    }

    # Explicit --env: the SWA CLI's default is "preview". The config sits in the deployed folder; say so anyway.
    $swaArgs = @('deploy', $webRoot, '--env', $Environment, '--swa-config-location', $webRoot)

    if ($DryRun) {
        $files = @(Get-ChildItem -LiteralPath $webRoot -Recurse -File)
        $megabytes = [math]::Round(($files | Measure-Object -Property Length -Sum).Sum / 1MB, 1)
        $commandLine = ($swaArgs | ForEach-Object { if ($_ -match '\s') { "`"$_`"" } else { $_ } }) -join ' '
        Write-Host ''
        Write-Host "Dry run: would deploy $deploying to $Environment - $($files.Count) files, $megabytes MB, with:" -ForegroundColor Yellow
        Write-Host "  swa $commandLine"
        exit 0
    }

    # SWA_CLI_DEBUG=silly makes the CLI log the token; the filter below masks it in any case.
    $env:SWA_CLI_DEBUG = $null
    $env:SWA_CLI_DEPLOYMENT_TOKEN = $token
    $deployedUrl = $null
    Push-Location -LiteralPath $publishDir   # away from the repo's .github/workflows, which the CLI would read
    try {
        & swa @swaArgs 2>&1 | ForEach-Object {
            $line = "$_".Replace($token, '***')
            Write-Host $line
            $plain = $line -replace '\x1b\[[0-9;]*m', ''
            if ($plain -match 'Project deployed to (https://\S+)') { $deployedUrl = $Matches[1] }
        }
        $swaExit = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
    if ($swaExit -ne 0) { Stop-Publish "swa deploy failed (exit code $swaExit)" }
    if (-not $deployedUrl) { Stop-Publish 'swa deploy reported no deployed URL; check its output above' }
}
finally {
    $env:SWA_CLI_DEPLOYMENT_TOKEN = $token
    $env:SWA_CLI_DEBUG = $savedSwaDebug
    Remove-Item -LiteralPath $publishDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host "Deployed $deploying to ${Environment}: $deployedUrl" -ForegroundColor Green
if ($Environment -eq 'production') {
    Write-Host '  Custom domain: https://app.kroiko.com'
}
exit 0
