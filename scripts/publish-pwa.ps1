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
                                  vX.Y.Z of the csproj <Version>, pushed to origin;
      3. the full `dotnet test` passes, E2E included.
    Then it runs `dotnet publish -c Release` into a temporary folder and
    `swa deploy <temp>/wwwroot --env <main|production>`, and prints the deployed version and URL.

    The deployment token is read by the SWA CLI from SWA_CLI_DEPLOYMENT_TOKEN in your own environment. It is never
    a parameter, never written to a file and never printed: SWA CLI output passes through a filter that masks it.

    See docs/implementation/07-hosting-and-go-live.md (07a.2) for the rules and 07b for the release procedure.

.PARAMETER Environment
    main       - the Static Web Apps staging environment "main", from origin/main.
    production - app.kroiko.com, from a tagged origin/release.

.PARAMETER DryRun
    Runs every check, the tests and the publish, then prints the swa command instead of running it. Needs neither
    the token nor the SWA CLI.

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

function Stop-Publish([string] $Reason) {
    Write-Host "publish-pwa: refused - $Reason" -ForegroundColor Red
    exit 1
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

# --- 1. Clean working tree -------------------------------------------------------------------------------------

$changes = git -C $repoRoot status --porcelain
if ($LASTEXITCODE -ne 0) { Stop-Publish "git status failed in $repoRoot" }
if ($changes) {
    Stop-Publish "the working tree is not clean (git status --porcelain):`n$($changes -join "`n")"
}

# --- 2. Branch model (ADR-0001) --------------------------------------------------------------------------------

git -C $repoRoot fetch --quiet --prune origin
if ($LASTEXITCODE -ne 0) { Stop-Publish 'git fetch origin failed, so HEAD cannot be checked against origin' }

if ($Environment -eq 'production') {
    $checkedOut = git -C $repoRoot symbolic-ref --quiet --short HEAD
    if ($checkedOut -ne 'release') {
        Stop-Publish "production deploys only from the release branch (checked out: $(if ($checkedOut) { $checkedOut } else { 'a detached HEAD' }))"
    }
}

$head = Get-Commit 'HEAD'
if ($head -ne (Get-Commit "origin/$branch")) {
    Stop-Publish "HEAD is not origin/$branch; check out origin/$branch exactly (pull or push first)"
}

# The hand-maintained SemVer <Version> (ADR-0002 §6); without one the SDK builds 1.0.0.
$version = (Select-Xml -LiteralPath $clientProject -XPath '/Project/PropertyGroup/Version').Node.InnerText | Select-Object -First 1

if ($Environment -eq 'production') {
    if (-not $version) {
        Stop-Publish 'the client csproj has no <Version>; bump it in a PR to main before releasing'
    }
    $tag = "v$version"
    $tagsOnHead = @(git -C $repoRoot tag --points-at HEAD)
    if ($tagsOnHead -notcontains $tag) {
        $found = if ($tagsOnHead) { $tagsOnHead -join ', ' } else { 'none' }
        Stop-Publish "HEAD carries no tag $tag matching the csproj <Version> $version (tags on HEAD: $found)"
    }
    $remoteTag = git -C $repoRoot ls-remote --tags origin "refs/tags/$tag"
    if ($LASTEXITCODE -ne 0) { Stop-Publish "git ls-remote origin failed, so tag $tag cannot be checked" }
    $localTag = git -C $repoRoot rev-parse "refs/tags/$tag"
    if (-not $remoteTag -or ($remoteTag -split '\s+')[0] -ne $localTag) {
        Stop-Publish "tag $tag is not on origin, or origin's differs; push it first (git push origin $tag)"
    }
}

$displayVersion = if ($version) { "v$version" } else { 'v1.0.0 (no <Version> in the csproj; SDK default)' }
$shortSha = git -C $repoRoot rev-parse --short=7 HEAD

# --- 3. The full test run, E2E included (ADR-0007 §6) ----------------------------------------------------------

dotnet test (Join-Path $repoRoot 'TextConverter.sln')
if ($LASTEXITCODE -ne 0) { Stop-Publish 'dotnet test failed' }

# --- Publish and deploy ----------------------------------------------------------------------------------------

$publishDir = Join-Path ([System.IO.Path]::GetTempPath()) "kroiko-pwa-$([guid]::NewGuid().ToString('N'))"
$savedSwaDebug = $env:SWA_CLI_DEBUG
try {
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
        Write-Host ''
        Write-Host "Dry run: would deploy $displayVersion ($shortSha) to $Environment - $($files.Count) files, $megabytes MB, with:" -ForegroundColor Yellow
        Write-Host "  swa $($swaArgs -join ' ')"
        exit 0
    }

    # SWA_CLI_DEBUG=silly makes the CLI log the token; the filter below masks it in any case.
    $env:SWA_CLI_DEBUG = $null
    $deployedUrl = $null
    Push-Location -LiteralPath $publishDir   # away from the repo's .github/workflows, which the CLI would read
    try {
        & swa @swaArgs 2>&1 | ForEach-Object {
            $line = "$_"
            if ($token) { $line = $line.Replace($token, '***') }
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
    $env:SWA_CLI_DEBUG = $savedSwaDebug
    Remove-Item -LiteralPath $publishDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host "Deployed $displayVersion ($shortSha) to ${Environment}: $deployedUrl" -ForegroundColor Green
if ($Environment -eq 'production') {
    Write-Host '  Custom domain: https://app.kroiko.com'
}
exit 0
