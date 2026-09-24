using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Kroiko.Client.Tests.PublishScript;

/// <summary>
/// A throwaway git repository with its own bare <c>origin</c>, holding a copy of <c>scripts/publish-pwa.ps1</c> and a
/// minimal client csproj, so the script's branch rules run against real git. <c>dotnet</c> and <c>swa</c> are
/// replaced by stubs on <c>PATH</c> that log each call and never build, test or deploy anything. git reads only the
/// sandbox's own config, so the machine's hooks, signing or credential settings never apply.
/// </summary>
internal sealed class PublishScriptSandbox : IDisposable
{
    /// <summary>The URL the <c>swa</c> stub reports, the way the real CLI does.</summary>
    public const string StubUrl = "https://kroiko-stub-main.1.azurestaticapps.net";

    private readonly string _root;

    /// <summary>Copies the template, so each test starts from one pushed commit on <c>main</c> without re-running git.</summary>
    public PublishScriptSandbox(PublishScriptTemplate template)
    {
        _root = Directory.CreateTempSubdirectory("kroiko-publish-script-").FullName;
        CopyDirectory(template.Root, _root);
    }

    private PublishScriptSandbox(string root) => _root = root;

    public string WorkTree => Path.Combine(_root, "work");

    /// <summary>The token the runs see in <c>SWA_CLI_DEPLOYMENT_TOKEN</c> unless a run says otherwise.</summary>
    public string Token { get; } = "sandbox-token-" + Guid.NewGuid().ToString("N");

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
        File.WriteAllText(Path.Combine(sandbox.StubsPath, "swa.ps1"), SwaStub);
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

        var script = Path.Combine(sandbox.WorkTree, "scripts", "publish-pwa.ps1");
        Directory.CreateDirectory(Path.GetDirectoryName(script)!);
        File.Copy(Path.Combine(RepoPaths.Root, "scripts", "publish-pwa.ps1"), script);
        File.WriteAllText(Path.Combine(sandbox.WorkTree, "TextConverter.sln"), "");
        sandbox.SetVersion(null);
        sandbox.Commit("initial");
        sandbox.Push("main");
        sandbox.Git("fetch", "--quiet", "origin");
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
        psi.Environment["SANDBOX_TESTS_EXIT"] = options.TestsExitCode.ToString(CultureInfo.InvariantCulture);
        psi.Environment["SANDBOX_SWA_EXIT"] = options.SwaExitCode.ToString(CultureInfo.InvariantCulture);
        psi.Environment["SANDBOX_SWA_PRINTS_URL"] = options.SwaPrintsUrl ? "1" : "0";
        psi.Environment["SWA_CLI_DEBUG"] = options.SwaCliDebug;
        if (options.WithToken)
            psi.Environment["SWA_CLI_DEPLOYMENT_TOKEN"] = Token;
        else
            psi.Environment.Remove("SWA_CLI_DEPLOYMENT_TOKEN");

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

    // Logs "dotnet <args>" and whether the token reached it. `test` exits with SANDBOX_TESTS_EXIT;
    // `publish -o <dir>` writes a tiny wwwroot.
    private const string DotnetStub = """
        Add-Content -LiteralPath $env:SANDBOX_CALL_LOG -Value ("dotnet " + ($args -join ' '))
        Add-Content -LiteralPath $env:SANDBOX_CALL_LOG -Value ("dotnet-token-present " + [bool]$env:SWA_CLI_DEPLOYMENT_TOKEN)
        if ($args[0] -eq 'test') { exit [int]$env:SANDBOX_TESTS_EXIT }
        if ($args[0] -eq 'publish') {
            $out = $args[[array]::IndexOf($args, '-o') + 1]
            New-Item -ItemType Directory -Force -Path (Join-Path $out 'wwwroot') | Out-Null
            Set-Content -LiteralPath (Join-Path $out 'wwwroot/index.html') -Value '<html></html>'
        }
        exit 0
        """;

    // Logs "swa <args>", whether the token reached it, and SWA_CLI_DEBUG; echoes the token the way
    // `SWA_CLI_DEBUG=silly` would, to prove the script masks it; reports a URL like the real CLI.
    private const string SwaStub = $$"""
        Add-Content -LiteralPath $env:SANDBOX_CALL_LOG -Value ("swa " + ($args -join ' '))
        Add-Content -LiteralPath $env:SANDBOX_CALL_LOG -Value ("swa-token-present " + [bool]$env:SWA_CLI_DEPLOYMENT_TOKEN)
        Add-Content -LiteralPath $env:SANDBOX_CALL_LOG -Value ("swa-debug '" + $env:SWA_CLI_DEBUG + "'")
        Write-Output "Deployment token found in Environment Variables: $env:SWA_CLI_DEPLOYMENT_TOKEN"
        if ($env:SANDBOX_SWA_PRINTS_URL -eq '1') {
            Write-Output ([char]0x2714 + " Project deployed to {{StubUrl}} " + [char]::ConvertFromUtf32(0x1F680))
        }
        exit [int]$env:SANDBOX_SWA_EXIT
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
    public bool WithToken { get; init; } = true;
    public int TestsExitCode { get; init; }
    public int SwaExitCode { get; init; }
    public bool SwaPrintsUrl { get; init; } = true;
    public string SwaCliDebug { get; init; } = "";
}

/// <param name="Calls">One line per stubbed call (<c>dotnet …</c>, <c>swa …</c>) and what each stub saw.</param>
internal sealed record ScriptRun(int ExitCode, string Output, IReadOnlyList<string> Calls)
{
    public IEnumerable<string> DotnetCalls => Calls.Where(c => c.StartsWith("dotnet ", StringComparison.Ordinal));

    public IEnumerable<string> SwaCalls => Calls.Where(c => c.StartsWith("swa ", StringComparison.Ordinal));

    /// <summary>The <c>wwwroot</c> of the temporary folder the script published to (the stub's <c>-o</c> argument).</summary>
    public string WebRoot => Path.Combine(DotnetCalls.Single(c => c.StartsWith("dotnet publish ", StringComparison.Ordinal)).Split(' ').Last(), "wwwroot");

    /// <summary>The <c>swa</c> call the script makes for <paramref name="environment"/> (ADR-0001: never the CLI's default <c>preview</c>).</summary>
    public string ExpectedSwaDeploy(string environment) => $"swa deploy {WebRoot} --env {environment} --swa-config-location {WebRoot}";
}
