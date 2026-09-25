using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Kroiko.Client.Tests.PublishScript;

/// <summary>
/// A throwaway git repository with its own bare <c>origin</c>, holding a copy of <c>scripts/publish-pwa.ps1</c>, the
/// committed CloudFront Function, a <c>hosting/aws/hosting.json</c> with sandbox IDs and a minimal client csproj, so
/// the script's branch rules run against real git. <c>dotnet</c> and <c>aws</c> are replaced by stubs on <c>PATH</c>
/// that log each call and never build, test or deploy anything. git reads only the sandbox's own config, so the
/// machine's hooks, signing or credential settings never apply.
/// </summary>
internal sealed class PublishScriptSandbox : IDisposable
{
    public const string Bucket = "kroiko-pwa-sandbox";
    public const string Profile = "kroiko-pwa";
    public const string Region = "eu-central-1";
    public const string MainDistribution = "EMAINSANDBOX";
    public const string ProductionDistribution = "EPRODSANDBOX";
    public const string MainUrl = "https://dmainsandbox.cloudfront.net";

    /// <summary>The origin path the stubbed distributions start with: an earlier release.</summary>
    public const string PreviousOriginPath = "/main/20260901T000000Z-v0.9.0-0000000";

    private readonly string _root;

    /// <summary>Copies the template, so each test starts from one pushed commit on <c>main</c> without re-running git.</summary>
    public PublishScriptSandbox(PublishScriptTemplate template)
    {
        _root = Directory.CreateTempSubdirectory("kroiko-publish-script-").FullName;
        CopyDirectory(template.Root, _root);
    }

    private PublishScriptSandbox(string root) => _root = root;

    public string WorkTree => Path.Combine(_root, "work");

    public string HostingConfigPath => Path.Combine(WorkTree, "hosting", "aws", "hosting.json");

    private string OriginPath => Path.Combine(_root, "origin.git");

    private string StubsPath => Path.Combine(_root, "stubs");

    private string CallLogPath => Path.Combine(_root, "calls.log");

    private string GitConfigPath => Path.Combine(_root, "gitconfig");

    /// <summary>Builds the repository the <see cref="PublishScriptTemplate"/> copies from.</summary>
    internal static void Build(string root)
    {
        var sandbox = new PublishScriptSandbox(root);
        Directory.CreateDirectory(sandbox.StubsPath);
        File.WriteAllText(Path.Combine(sandbox.StubsPath, "dotnet.ps1"), DotnetStub);
        File.WriteAllText(Path.Combine(sandbox.StubsPath, "aws.ps1"), AwsStub);
        File.WriteAllText(sandbox.GitConfigPath, """
            [user]
                name = Sandbox
                email = sandbox@example.invalid
            [commit]
                gpgsign = false
            [tag]
                gpgsign = false
            [core]
                autocrlf = false
            """);

        sandbox.RunGit(root, "init", "--quiet", "--bare", "--template=", "--initial-branch=main", sandbox.OriginPath);
        sandbox.RunGit(root, "init", "--quiet", "--template=", "--initial-branch=main", sandbox.WorkTree);
        // Relative, so a copied sandbox needs no rewiring: git resolves the remote from the work tree (the script
        // runs `git -C <repo>`), and alternates from origin's object folder.
        sandbox.Git("remote", "add", "origin", "../origin.git");
        File.WriteAllText(Path.Combine(sandbox.OriginPath, "objects", "info", "alternates"), "../../work/.git/objects\n");

        foreach (var file in new[] { Path.Combine("scripts", "publish-pwa.ps1"), Path.Combine("hosting", "cloudfront", "viewer-request.js") })
        {
            var copy = Path.Combine(sandbox.WorkTree, file);
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(Path.Combine(RepoPaths.Root, file), copy);
        }
        sandbox.WriteHostingConfig(bucket: Bucket, mainDistribution: MainDistribution, mainUrl: MainUrl);
        File.WriteAllText(Path.Combine(sandbox.WorkTree, "TextConverter.sln"), "");
        sandbox.SetVersion(null);
        sandbox.Commit("initial");
        sandbox.Push("main");
        sandbox.Git("fetch", "--quiet", "origin");
    }

    /// <summary>Writes <c>hosting/aws/hosting.json</c> (not committed); an empty value is one not filled in yet.</summary>
    public void WriteHostingConfig(string bucket, string mainDistribution, string mainUrl)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(HostingConfigPath)!);
        File.WriteAllText(HostingConfigPath, $$"""
            {
              "profile": "{{Profile}}",
              "region": "{{Region}}",
              "bucket": "{{bucket}}",
              "environments": {
                "main": { "distributionId": "{{mainDistribution}}", "functionName": "kroiko-pwa-main", "url": "{{mainUrl}}" },
                "production": { "distributionId": "{{ProductionDistribution}}", "functionName": "kroiko-pwa-production", "url": "https://kroiko.com" }
              }
            }
            """);
    }

    /// <summary>
    /// Points origin's <paramref name="branchOrTag"/> at the local one, like <c>git push</c> but in one cheap call:
    /// origin borrows the work tree's objects through alternates, so only the ref has to move.
    /// </summary>
    public void Push(string branchOrTag)
    {
        var refName = Git("rev-parse", "--symbolic-full-name", branchOrTag);
        RunGit(OriginPath, "update-ref", refName, Git("rev-parse", refName));
    }

    /// <summary>Writes the csproj with this <c>&lt;Version&gt;</c>, or none when null (not committed).</summary>
    public void SetVersion(string? version)
    {
        var versionElement = version is null ? "" : $"<Version>{version}</Version>";
        var path = Path.Combine(WorkTree, "Kroiko.Client.Blazor", "Kroiko.Client.Blazor.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $"""
            <Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                {versionElement}
              </PropertyGroup>
            </Project>
            """);
    }

    public void Commit(string message)
    {
        Git("add", "-A");
        Git("commit", "--quiet", "--allow-empty", "-m", message);
    }

    public string Git(params string[] args) => RunGit(WorkTree, args);

    public ScriptRun Run(string environment, RunOptions? options = null)
    {
        options ??= new RunOptions();
        File.Delete(CallLogPath);

        var psi = StartInfo("pwsh", WorkTree,
            "-NoProfile", "-NonInteractive", "-File", Path.Combine(WorkTree, "scripts", "publish-pwa.ps1"), "-Environment", environment);
        if (options.DryRun)
            psi.ArgumentList.Add("-DryRun");

        psi.Environment["PATH"] = StubsPath + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        psi.Environment["SANDBOX_CALL_LOG"] = CallLogPath;
        psi.Environment["SANDBOX_FUNCTION_SOURCE"] = Path.Combine(WorkTree, "hosting", "cloudfront", "viewer-request.js");
        psi.Environment["SANDBOX_TESTS_EXIT"] = options.TestsExitCode.ToString(CultureInfo.InvariantCulture);
        psi.Environment["SANDBOX_PUBLISH_EXTRA"] = options.PublishExtraFile ?? "";
        psi.Environment["SANDBOX_AWS_FAIL_ON"] = options.AwsFailsOn ?? "";
        psi.Environment["SANDBOX_LIVE_FUNCTION_DIFFERS"] = options.LiveFunctionDiffers ? "1" : "0";
        psi.Environment["SANDBOX_DISTRIBUTION_FUNCTION"] = options.DistributionFunction ?? "";
        psi.Environment["SANDBOX_DISTRIBUTION_COMMENT"] = options.DistributionComment ?? "";
        // The real CLI would read these before the profile's; the script must not depend on them.
        foreach (var variable in new[] { "AWS_PROFILE", "AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY", "AWS_SESSION_TOKEN" })
            psi.Environment.Remove(variable);

        var (exitCode, stdout, stderr) = RunProcess(psi, "PowerShell 7 (pwsh) is needed to test scripts/publish-pwa.ps1");
        var calls = File.Exists(CallLogPath) ? File.ReadAllLines(CallLogPath) : [];
        return new ScriptRun(exitCode, stdout + stderr, calls);
    }

    public void Dispose() => DeleteDirectory(_root);

    internal static void DeleteDirectory(string path)
    {
        // git marks object files read-only, which Directory.Delete refuses.
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(path, recursive: true);
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
    }

    private string RunGit(string workingDirectory, params string[] args)
    {
        var (exitCode, stdout, stderr) = RunProcess(StartInfo("git", workingDirectory, args), "git is needed on PATH");
        if (exitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stderr}");
        return stdout.Trim();
    }

    /// <summary>A process whose git reads only the sandbox's config and repository, never the caller's.</summary>
    private ProcessStartInfo StartInfo(string fileName, string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        psi.Environment["GIT_CONFIG_GLOBAL"] = GitConfigPath;
        psi.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        // Set when the tests run from a git hook; they would point git at the real repository.
        foreach (var variable in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES" })
            psi.Environment.Remove(variable);
        return psi;
    }

    private static (int ExitCode, string Stdout, string Stderr) RunProcess(ProcessStartInfo psi, string whenMissing)
    {
        Process process;
        try
        {
            process = Process.Start(psi)!;
        }
        catch (Win32Exception e)
        {
            throw new InvalidOperationException($"{whenMissing}: could not start '{psi.FileName}'.", e);
        }

        using (process)
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(TimeSpan.FromMinutes(2)))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException($"{psi.FileName} {string.Join(' ', psi.ArgumentList)} did not finish in 2 minutes.");
            }

            return (process.ExitCode, stdoutTask.GetAwaiter().GetResult(), stderrTask.GetAwaiter().GetResult());
        }
    }

    // Logs "dotnet <args>". `test` exits with SANDBOX_TESTS_EXIT; `publish -o <dir>` writes a small wwwroot with
    // .br and .gz siblings, whose contents name the file, plus SANDBOX_PUBLISH_EXTRA when it is set.
    private const string DotnetStub = """
        Add-Content -LiteralPath $env:SANDBOX_CALL_LOG -Value ("dotnet " + ($args -join ' '))
        if ($args[0] -eq 'test') { exit [int]$env:SANDBOX_TESTS_EXIT }
        if ($args[0] -eq 'publish') {
            $webRoot = Join-Path $args[[array]::IndexOf($args, '-o') + 1] 'wwwroot'
            $files = [ordered]@{
                'index.html' = 'index'; 'index.html.br' = 'index-br'; 'index.html.gz' = 'index-gz'
                'service-worker.js' = 'worker'; 'service-worker.js.br' = 'worker-br'
                'service-worker-assets.js' = 'assets'
                '_framework/dotnet.native.abcdefgh12.wasm' = 'wasm'; '_framework/dotnet.native.abcdefgh12.wasm.br' = 'wasm-br'
                '_framework/dotnet.native.abcdefgh12.wasm.gz' = 'wasm-gz'
                '_framework/icudt_EFIGS.tptq2av103.dat' = 'dat'; '_framework/icudt_EFIGS.tptq2av103.dat.br' = 'dat-br'
                'fonts/roboto.woff2' = 'font'
                'manifest.webmanifest' = 'manifest'; 'manifest.webmanifest.br' = 'manifest-br'
                'css/app.css' = 'css'
            }
            if ($env:SANDBOX_PUBLISH_EXTRA) { $files[$env:SANDBOX_PUBLISH_EXTRA] = 'extra' }
            foreach ($file in $files.Keys) {
                $path = Join-Path $webRoot $file
                New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path) | Out-Null
                Set-Content -LiteralPath $path -Value $files[$file] -NoNewline
            }
        }
        exit 0
        """;

    // Logs "aws <args>" and answers like the real CLI. `s3 cp --recursive` logs one "aws-upload" line per object
    // with its metadata; `update-function` and `update-distribution` log what they were given. SANDBOX_AWS_FAIL_ON
    // makes the call whose "<service> <command>" contains it fail.
    private const string AwsStub = """
        $all = @($args)
        Add-Content -LiteralPath $env:SANDBOX_CALL_LOG -Value ("aws " + ($all -join ' '))
        function Get-Option([string] $Name) { $i = [array]::IndexOf($all, $Name); if ($i -ge 0) { $all[$i + 1] } else { '' } }
        function Log([string] $Line) { Add-Content -LiteralPath $env:SANDBOX_CALL_LOG -Value $Line }
        $command = "$($all[0]) $($all[1])"
        if ($env:SANDBOX_AWS_FAIL_ON -and $command.Contains($env:SANDBOX_AWS_FAIL_ON)) {
            [Console]::Error.WriteLine("An error occurred (AccessDenied) when calling $command")
            exit 254
        }
        $committedCode = ([System.IO.File]::ReadAllText($env:SANDBOX_FUNCTION_SOURCE)) -replace "`r`n", "`n"
        $functionName = if ($env:SANDBOX_DISTRIBUTION_FUNCTION) { $env:SANDBOX_DISTRIBUTION_FUNCTION }
            elseif ((Get-Option '--id') -eq 'EMAINSANDBOX') { 'kroiko-pwa-main' } else { 'kroiko-pwa-production' }
        $config = @'
        {
            "CallerReference": "2026-09-24T10:00:00Z",
            "Aliases": { "Quantity": 0 },
            "DefaultRootObject": "",
            "Origins": {
                "Quantity": 1,
                "Items": [
                    {
                        "Id": "kroiko-pwa-bucket",
                        "DomainName": "kroiko-pwa-sandbox.s3.eu-central-1.amazonaws.com",
                        "OriginPath": "/main/20260901T000000Z-v0.9.0-0000000",
                        "S3OriginConfig": { "OriginAccessIdentity": "" },
                        "OriginAccessControlId": "E2OACSANDBOX"
                    }
                ]
            },
            "DefaultCacheBehavior": {
                "TargetOriginId": "kroiko-pwa-bucket",
                "ViewerProtocolPolicy": "redirect-to-https",
                "Compress": true,
                "CachePolicyId": "658327ea-f89d-4fab-a63d-7e88639e58f6",
                "FunctionAssociations": {
                    "Quantity": 1,
                    "Items": [ { "FunctionARN": "arn:aws:cloudfront::123456789012:function/FUNCTION_NAME", "EventType": "viewer-request" } ]
                }
            },
            "Comment": "Kroiko PWA \"sandbox\"",
            "Enabled": true
        }
        '@ -replace 'FUNCTION_NAME', $functionName
        if ($env:SANDBOX_DISTRIBUTION_COMMENT) { $config = $config.Replace('Kroiko PWA \"sandbox\"', $env:SANDBOX_DISTRIBUTION_COMMENT) }
        switch ($command) {
            'sts get-caller-identity' { 'arn:aws:iam::123456789012:user/kroiko-pwa-deployer' }
            's3 cp' {
                $source = $all[2]; $destination = $all[3]
                foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -File) {
                    $relative = [System.IO.Path]::GetRelativePath($source, $file.FullName).Replace('\', '/')
                    Log ("aws-upload $destination$relative|$(Get-Option '--content-type')|$(Get-Option '--cache-control')|" +
                        "$(Get-Option '--content-encoding')|$([System.IO.File]::ReadAllText($file.FullName))")
                }
            }
            'cloudfront get-function' {
                $code = if ($env:SANDBOX_LIVE_FUNCTION_DIFFERS -eq '1') { "function handler(event) { return event.request; }`n" } else { $committedCode }
                [System.IO.File]::WriteAllText($all[[array]::IndexOf($all, '--stage') + 2], $code)
                '{ "ContentType": "application/octet-stream", "ETag": "ETAG-LIVE" }'
            }
            'cloudfront describe-function' { 'ETAG-DEV-1' }
            'cloudfront update-function' {
                $code = [System.IO.File]::ReadAllText(((Get-Option '--function-code') -replace '^fileb://', ''))
                Log "aws-function-code-is-committed $($code -ceq $committedCode)"
                'ETAG-DEV-2'
            }
            'cloudfront publish-function' { }
            'cloudfront get-distribution-config' { if ((Get-Option '--query') -eq 'ETag') { 'ETAG-DIST-1' } else { $config } }
            'cloudfront update-distribution' {
                $sent = [System.IO.File]::ReadAllText(((Get-Option '--distribution-config') -replace '^file://', ''))
                $originPath = ($sent | ConvertFrom-Json).Origins.Items[0].OriginPath
                Log "aws-origin-path $originPath"
                Log "aws-config-otherwise-unchanged $($sent.Replace($originPath, '/main/20260901T000000Z-v0.9.0-0000000') -ceq $config)"
                'InProgress'
            }
            'cloudfront wait' { }
            'cloudfront create-invalidation' { 'I2SANDBOXINVALIDATION' }
            default { [Console]::Error.WriteLine("aws stub: unexpected call $command"); exit 1 }
        }
        exit 0
        """;
}

/// <summary>Builds the sandbox repository once per test class; each test copies it.</summary>
public sealed class PublishScriptTemplate : IDisposable
{
    public PublishScriptTemplate()
    {
        Root = Directory.CreateTempSubdirectory("kroiko-publish-template-").FullName;
        PublishScriptSandbox.Build(Root);
    }

    internal string Root { get; }

    public void Dispose() => PublishScriptSandbox.DeleteDirectory(Root);
}

internal sealed record RunOptions
{
    public bool DryRun { get; init; }
    public int TestsExitCode { get; init; }

    /// <summary>A file the stubbed publish adds to <c>wwwroot</c>, e.g. <c>notes.xyz</c>.</summary>
    public string? PublishExtraFile { get; init; }

    /// <summary>The aws call that fails: part of its "&lt;service&gt; &lt;command&gt;", e.g. <c>s3 cp</c>.</summary>
    public string? AwsFailsOn { get; init; }

    /// <summary>The environment's live function runs older code than the committed file.</summary>
    public bool LiveFunctionDiffers { get; init; }

    /// <summary>The function the stubbed distribution runs on viewer request; by default the environment's own.</summary>
    public string? DistributionFunction { get; init; }

    /// <summary>The comment of the stubbed distribution, in place of the ASCII one.</summary>
    public string? DistributionComment { get; init; }
}

/// <summary>One object <c>aws s3 cp</c> uploaded, with the metadata it was given.</summary>
internal sealed record Upload(string Key, string ContentType, string CacheControl, string ContentEncoding, string Content);

/// <param name="Calls">One line per stubbed call (<c>dotnet …</c>, <c>aws …</c>) and what the stubs saw.</param>
internal sealed record ScriptRun(int ExitCode, string Output, IReadOnlyList<string> Calls)
{
    public IEnumerable<string> DotnetCalls => Calls.Where(c => c.StartsWith("dotnet ", StringComparison.Ordinal));

    public IReadOnlyList<string> AwsCalls => Calls.Where(c => c.StartsWith("aws ", StringComparison.Ordinal)).ToList();

    /// <summary>Each aws call as "&lt;service&gt; &lt;command&gt;", e.g. <c>s3 cp</c>, <c>cloudfront wait</c>.</summary>
    public IReadOnlyList<string> AwsCommands => AwsCalls.Select(c => string.Join(' ', c.Split(' ').Skip(1).Take(2))).ToList();

    /// <summary>Every uploaded object, by its key under the bucket (e.g. <c>main/&lt;release&gt;/raw/index.html</c>).</summary>
    public IReadOnlyList<Upload> Uploads => Calls
        .Where(c => c.StartsWith("aws-upload ", StringComparison.Ordinal))
        .Select(c => c["aws-upload ".Length..].Split('|'))
        .Select(f => new Upload(f[0].Replace($"s3://{PublishScriptSandbox.Bucket}/", ""), f[1], f[2], f[3], f[4]))
        .ToList();

    /// <summary>The origin path the script switched the distribution to, if it did.</summary>
    public string? OriginPath => Calls.SingleOrDefault(c => c.StartsWith("aws-origin-path ", StringComparison.Ordinal))?["aws-origin-path ".Length..];

    /// <summary>The <c>wwwroot</c> of the temporary folder the script published to (the stub's <c>-o</c> argument).</summary>
    public string WebRoot => Path.Combine(DotnetCalls.Single(c => c.StartsWith("dotnet publish ", StringComparison.Ordinal)).Split(' ').Last(), "wwwroot");
}
