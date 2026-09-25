#Requires -Version 7.2
<#
.SYNOPSIS
    Tests, publishes and deploys Kroiko.Client.Blazor (the PWA) to Amazon S3 + CloudFront.

.DESCRIPTION
    Enforces the branch model of ADR-0010 (docs/adr/0010-host-pwa-on-s3-and-cloudfront.md; origin: ADR-0011) and refuses to
    run unless:
      1. the working tree is clean (git status --porcelain is empty);
      2. -Environment main:       HEAD equals origin/main (after a fetch);
         -Environment production: release is checked out, HEAD equals origin/release, and HEAD carries the tag
                                  vX.Y.Z of the csproj <Version>, pushed to origin and not older than any other
                                  vX.Y.Z tag there (roll forward, never back: ADR-0002 §8);
      3. the full `dotnet test` passes, E2E included.
    Then it runs `dotnet publish -c Release` into a temporary folder and deploys its wwwroot:
      - uploads it to a new release folder s3://<bucket>/<environment>/<release>/, as a raw/ tree (the plain
        files) and a br/ tree (each file's .br sibling, stored with Content-Encoding: br, where there is one),
        with Content-Type and Cache-Control set on every object;
      - brings the environment's CloudFront Function up to hosting/cloudfront/viewer-request.js;
      - switches the distribution's origin path to the new folder, waits until it is deployed, invalidates /*;
    and prints the deployed version and URL.

    The IDs come from hosting/aws/hosting.json. The credentials are the AWS CLI profile it names (kroiko-pwa):
    an IAM user's access key, set up with `aws configure --profile kroiko-pwa` on your machine, or written from the
    GitHub environment pwa-production by .github/workflows/deploy-pwa.yml, which runs this script for every pushed
    release tag (ADR-0013). The script never reads, prints or writes the key; it passes --profile to every aws call.

    See docs/implementation/07-hosting-and-go-live.md (07a.2) for the rules and 07b for the release procedure.

.PARAMETER Environment
    main       - the staging distribution "main" (its *.cloudfront.net URL), from origin/main.
    production - kroiko.com, from a tagged origin/release.

.PARAMETER DryRun
    Runs every check, the tests and the publish, then prints what it would upload and switch instead of doing it
    (the publish folder is removed afterwards). Needs neither the credentials nor the AWS CLI.

.EXAMPLE
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
$hostingPath = Join-Path $repoRoot 'hosting/aws/hosting.json'
$functionPath = Join-Path $repoRoot 'hosting/cloudfront/viewer-request.js'
$branch = if ($Environment -eq 'production') { 'release' } else { 'main' }
$publishDir = Join-Path ([System.IO.Path]::GetTempPath()) "kroiko-pwa-$([guid]::NewGuid().ToString('N'))"

# Every file type the publish holds. A file with any other extension makes the deploy refuse, so nothing is ever
# served with a type S3 guessed.
$contentTypes = @{
    '.html'        = 'text/html; charset=utf-8'
    '.css'         = 'text/css; charset=utf-8'
    '.js'          = 'text/javascript; charset=utf-8'
    '.txt'         = 'text/plain; charset=utf-8'
    '.json'        = 'application/json'
    '.map'         = 'application/json'
    '.webmanifest' = 'application/manifest+json'
    '.wasm'        = 'application/wasm'
    '.dat'         = 'application/octet-stream'
    '.woff2'       = 'font/woff2'
    '.png'         = 'image/png'
    '.ico'         = 'image/x-icon'
}
# The update check reads index.html and the service-worker files, so they and every other file whose name does
# not change with its content are revalidated on every request (ADR-0002). A fingerprinted _framework/ file
# never changes under its name.
$noCache = 'no-cache'
$immutable = 'public, max-age=31536000, immutable'
$fingerprinted = '^_framework/.+\.[a-z0-9]{10}\.[^./]+$'

function Stop-Publish([string] $Reason) {
    Write-Host "publish-pwa: refused - $Reason" -ForegroundColor Red
    exit 1   # the finally block below still runs
}

function Get-Commit([string] $Ref) {
    $sha = git -C $repoRoot rev-parse --verify --quiet "$Ref^{commit}"
    if ($LASTEXITCODE -ne 0) { return $null }
    return $sha
}

# Runs one aws call with the deploy profile and region; its output as one trimmed string. Its errors go to the
# console, and a failure stops the deploy with $What (and $Hint, when there is something to do about it).
function Invoke-Aws([string] $What, [string[]] $Arguments, [string] $Hint = '') {
    $output = & aws @Arguments --profile $hosting.profile --region $hosting.region
    if ($LASTEXITCODE -ne 0) {
        Stop-Publish ("$What failed (aws exit code $LASTEXITCODE; its message is above)" + $(if ($Hint) { ". $Hint" } else { '' }))
    }
    return ([string]($output -join "`n")).Trim()
}

# --- Configuration and tools (the tools are not needed for a dry run) ------------------------------------------

if (-not (Test-Path -LiteralPath $hostingPath)) { Stop-Publish "there is no hosting/aws/hosting.json" }
$hosting = Get-Content -Raw -LiteralPath $hostingPath | ConvertFrom-Json
$target = $hosting.environments.$Environment

if (-not $DryRun) {
    $missing = @()
    if (-not $hosting.bucket) { $missing += 'bucket' }
    foreach ($field in 'distributionId', 'functionName', 'url') {
        if (-not $target.$field) { $missing += "environments.$Environment.$field" }
    }
    if ($missing) {
        Stop-Publish "hosting/aws/hosting.json has no $($missing -join ', '); fill it in from the provisioning (07a.3)"
    }
    if (-not (Get-Command aws -ErrorAction SilentlyContinue)) {
        Stop-Publish 'the AWS CLI v2 is not installed (https://aws.amazon.com/cli/)'
    }
}

try {
    # --- 1. Clean working tree ---------------------------------------------------------------------------------

    $changes = git -C $repoRoot status --porcelain
    if ($LASTEXITCODE -ne 0) { Stop-Publish "git status failed in $repoRoot" }
    if ($changes) {
        Stop-Publish "the working tree is not clean (git status --porcelain):`n$($changes -join "`n")"
    }

    # --- 2. Branch model (ADR-0010, from ADR-0001) -------------------------------------------------------------

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
    $shownVersion = if ($version) { $version } else { '1.0.0' }
    $deploying = "v$shownVersion ($shortSha)"   # as About shows it (ADR-0002 §5)
    if (-not $version) { Write-Host 'Note: the client csproj has no <Version>, so the build is the SDK default 1.0.0.' }

    # Before the long test run, so missing credentials fail fast.
    if (-not $DryRun) {
        Invoke-Aws "checking the credentials of the AWS CLI profile $($hosting.profile)" @('sts', 'get-caller-identity', '--query', 'Arn', '--output', 'text') `
            -Hint "Set them up once with: aws configure --profile $($hosting.profile) (07a.3)" | Out-Null
    }

    # --- 3. The full test run, E2E included (ADR-0007 §6) ------------------------------------------------------

    dotnet test (Join-Path $repoRoot 'TextConverter.sln')
    if ($LASTEXITCODE -ne 0) { Stop-Publish 'dotnet test failed' }

    # --- Publish -----------------------------------------------------------------------------------------------

    dotnet publish $clientProject -c Release -o $publishDir
    if ($LASTEXITCODE -ne 0) { Stop-Publish 'dotnet publish failed' }

    $webRoot = Join-Path $publishDir 'wwwroot'
    if (-not (Test-Path -LiteralPath (Join-Path $webRoot 'index.html'))) {
        Stop-Publish 'the publish output has no wwwroot/index.html'
    }

    # --- The release folder: raw/ and br/, grouped by the metadata each object gets --------------------------------

    # One folder per (Content-Type, Cache-Control, Content-Encoding), each holding raw/<path> and br/<path>, so one
    # `aws s3 cp --recursive` uploads a whole group with its headers.
    $uploadDir = Join-Path $publishDir 'upload'
    $groups = [ordered]@{}
    function Add-ReleaseObject([string] $RelativePath, [string] $Source, [string] $ContentType, [string] $CacheControl, [string] $Encoding) {
        $key = "$ContentType|$CacheControl|$Encoding"
        if (-not $groups.Contains($key)) {
            $groups[$key] = [pscustomobject]@{
                Folder = Join-Path $uploadDir "group$($groups.Count)"
                ContentType = $ContentType
                CacheControl = $CacheControl
                Encoding = $Encoding
            }
        }
        $destination = Join-Path $groups[$key].Folder $RelativePath
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
        Copy-Item -LiteralPath $Source -Destination $destination
    }

    $unknownTypes = @()
    $fileCount = 0
    $bytes = @{ raw = 0L; br = 0L }
    $plainFiles = Get-ChildItem -LiteralPath $webRoot -Recurse -File | Where-Object { $_.Extension -notin '.br', '.gz' }
    foreach ($file in $plainFiles) {
        $relative = [System.IO.Path]::GetRelativePath($webRoot, $file.FullName).Replace('\', '/')
        $contentType = $contentTypes[$file.Extension]
        if (-not $contentType) { $unknownTypes += $relative; continue }
        $cacheControl = if ($relative -cmatch $fingerprinted) { $immutable } else { $noCache }

        Add-ReleaseObject "raw/$relative" $file.FullName $contentType $cacheControl ''
        $brotli = Get-Item -LiteralPath "$($file.FullName).br" -ErrorAction SilentlyContinue
        if ($brotli) {
            Add-ReleaseObject "br/$relative" $brotli.FullName $contentType $cacheControl 'br'
            $bytes.br += $brotli.Length
        }
        else {
            Add-ReleaseObject "br/$relative" $file.FullName $contentType $cacheControl ''
            $bytes.br += $file.Length
        }
        $bytes.raw += $file.Length
        $fileCount++
    }
    if ($unknownTypes) {
        Stop-Publish "no Content-Type for $($unknownTypes -join ', '); add the extension to `$contentTypes in scripts/publish-pwa.ps1"
    }

    $release = '{0}-v{1}-{2}' -f [DateTime]::UtcNow.ToString('yyyyMMdd\THHmmss\Z', [cultureinfo]::InvariantCulture), $shownVersion, $shortSha
    $prefix = "$Environment/$release"
    $releaseUrl = "s3://$($hosting.bucket)/$prefix/"

    if ($DryRun) {
        $megabytes = { param($count) [math]::Round($count / 1MB, 1).ToString([cultureinfo]::InvariantCulture) }
        $distribution = if ($target.distributionId) { $target.distributionId } else { '(distributionId not set)' }
        if (-not $hosting.bucket) { $releaseUrl = "s3://(bucket not set)/$prefix/" }
        Write-Host ''
        Write-Host "Dry run: would deploy $deploying to $Environment - $fileCount files, raw $(& $megabytes $bytes.raw) MB, br $(& $megabytes $bytes.br) MB:" -ForegroundColor Yellow
        Write-Host "  upload  $releaseUrl (raw/ and br/, in $($groups.Count) uploads)"
        Write-Host "  switch  $distribution to origin path /$prefix, then invalidate /*"
        exit 0
    }

    # --- Deploy ------------------------------------------------------------------------------------------------

    Write-Host "Uploading $deploying to $releaseUrl"
    foreach ($group in $groups.Values) {
        $arguments = @('s3', 'cp', $group.Folder, $releaseUrl, '--recursive', '--only-show-errors',
            '--content-type', $group.ContentType, '--cache-control', $group.CacheControl)
        if ($group.Encoding) { $arguments += @('--content-encoding', $group.Encoding) }
        Invoke-Aws "uploading to $releaseUrl" $arguments -Hint 'Nothing was switched; the live release is unchanged' | Out-Null
    }

    # The environment's function runs the committed code, so a change to it ships through the same branch rules.
    $functionName = $target.functionName
    $code = ([System.IO.File]::ReadAllText($functionPath)) -replace "`r`n", "`n"
    $codePath = Join-Path $publishDir 'viewer-request.js'
    [System.IO.File]::WriteAllText($codePath, $code)
    $livePath = Join-Path $publishDir 'viewer-request.live.js'
    Invoke-Aws "reading the live code of the function $functionName" @('cloudfront', 'get-function', '--name', $functionName, '--stage', 'LIVE', $livePath) | Out-Null
    $liveCode = if (Test-Path -LiteralPath $livePath) { ([System.IO.File]::ReadAllText($livePath)) -replace "`r`n", "`n" } else { '' }
    if ($liveCode -ne $code) {
        Write-Host "Updating the function $functionName to hosting/cloudfront/viewer-request.js"
        $functionConfigPath = Join-Path $publishDir 'function-config.json'
        [System.IO.File]::WriteAllText($functionConfigPath, '{ "Comment": "Kroiko PWA viewer request (ADR-0010)", "Runtime": "cloudfront-js-2.0" }')
        $functionTag = Invoke-Aws "reading the function $functionName" @('cloudfront', 'describe-function', '--name', $functionName, '--query', 'ETag', '--output', 'text')
        $functionTag = Invoke-Aws "updating the function $functionName" @('cloudfront', 'update-function', '--name', $functionName, '--if-match', $functionTag,
            '--function-config', "file://$functionConfigPath", '--function-code', "fileb://$codePath", '--query', 'ETag', '--output', 'text')
        Invoke-Aws "publishing the function $functionName" @('cloudfront', 'publish-function', '--name', $functionName, '--if-match', $functionTag) | Out-Null
    }

    # The switch: only the origin path changes. The config is edited as text, so nothing else is re-serialised.
    $distributionId = $target.distributionId
    $distributionTag = Invoke-Aws "reading the distribution $distributionId" @('cloudfront', 'get-distribution-config', '--id', $distributionId, '--query', 'ETag', '--output', 'text')
    $configJson = Invoke-Aws "reading the distribution $distributionId" @('cloudfront', 'get-distribution-config', '--id', $distributionId, '--query', 'DistributionConfig', '--output', 'json')
    # The config comes back through the console, whose encoding on Windows follows the system locale: text beyond
    # ASCII (a Cyrillic comment, say) could come back changed, and would be sent back changed.
    if ($configJson -match '[^\x00-\x7F]') {
        Stop-Publish "the distribution $distributionId config holds text beyond ASCII (its Comment?); keep it ASCII (07a.3)"
    }
    $config = $configJson | ConvertFrom-Json
    if ($config.Origins.Quantity -ne 1) {
        Stop-Publish "the distribution $distributionId has $($config.Origins.Quantity) origins; it must have exactly the bucket (07a.3)"
    }
    $viewerRequest = @($config.DefaultCacheBehavior.FunctionAssociations.Items) | Where-Object { $_ -and $_.EventType -eq 'viewer-request' }
    if (-not $viewerRequest -or $viewerRequest.FunctionARN -notlike "*:function/$functionName") {
        Stop-Publish "the distribution $distributionId does not run the function $functionName on viewer request (07a.3)"
    }
    $originPaths = [regex]::Matches($configJson, '"OriginPath"\s*:\s*"([^"]*)"')
    if ($originPaths.Count -ne 1) { Stop-Publish "the distribution $distributionId config has $($originPaths.Count) origin paths, not 1" }
    $previousPath = $originPaths[0].Groups[1].Value
    $switched = $configJson.Substring(0, $originPaths[0].Index) + "`"OriginPath`": `"/$prefix`"" +
        $configJson.Substring($originPaths[0].Index + $originPaths[0].Length)
    $switchedPath = Join-Path $publishDir 'distribution-config.json'
    [System.IO.File]::WriteAllText($switchedPath, $switched)

    Write-Host "Switching $distributionId from $(if ($previousPath) { $previousPath } else { '(no origin path)' }) to /$prefix"
    Invoke-Aws "switching the distribution $distributionId" @('cloudfront', 'update-distribution', '--id', $distributionId, '--if-match', $distributionTag,
        '--distribution-config', "file://$switchedPath", '--query', 'Distribution.Status', '--output', 'text') `
        -Hint 'Nothing was switched; the live release is unchanged' | Out-Null

    $afterSwitch = "The distribution already serves /$prefix. Invalidate it by hand: aws cloudfront create-invalidation --distribution-id $distributionId --paths '/*' --profile $($hosting.profile)"
    Write-Host 'Waiting until every edge location has it (a few minutes)'
    Invoke-Aws "waiting for the distribution $distributionId" @('cloudfront', 'wait', 'distribution-deployed', '--id', $distributionId) -Hint $afterSwitch | Out-Null
    $invalidation = Invoke-Aws "invalidating the distribution $distributionId" @('cloudfront', 'create-invalidation', '--distribution-id', $distributionId,
        '--paths', '/*', '--query', 'Invalidation.Id', '--output', 'text') -Hint $afterSwitch
    Invoke-Aws "waiting for the invalidation $invalidation" @('cloudfront', 'wait', 'invalidation-completed', '--distribution-id', $distributionId, '--id', $invalidation) `
        -Hint "The distribution already serves /$prefix; the invalidation $invalidation is still running" | Out-Null
}
finally {
    Remove-Item -LiteralPath $publishDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host "Deployed $deploying to ${Environment}: $($target.url)" -ForegroundColor Green
Write-Host "  Release folder: $releaseUrl"
exit 0
