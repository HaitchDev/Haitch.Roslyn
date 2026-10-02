using System.Diagnostics;
using System.Text;

namespace Haitch.Roslyn.Tests;

public class PackageSmokeTests
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(3);

    [Test]
    [NotInParallel("pack")]
    public async Task Should_build_consumer_against_packed_source_with_nullable_enabled_and_disabled()
    {
        string repositoryRoot = RepoPaths.Root;
        string sourceProjectPath = Path.Combine(repositoryRoot, "src", "Haitch.Roslyn", "Haitch.Roslyn.csproj");
        string consumerProjectPath =
            Path.Combine(repositoryRoot, "tests", "PackageSmoke", "Consumer", "Consumer.csproj");
        string packageVersion = $"0.0.1-smoke-{Guid.NewGuid():N}";
        string feedDirectory = Path.Combine(Path.GetTempPath(), $"haitch-roslyn-smoke-feed-{Guid.NewGuid():N}");
        string consumerBuildRoot =
            Path.Combine(Path.GetTempPath(), $"haitch-roslyn-smoke-consumer-{Guid.NewGuid():N}");
        // A per-run NuGet global-packages directory keeps every restore of the freshly packed
        // version out of the shared ~/.nuget/packages/haitch.roslyn cache, which would otherwise
        // grow one directory per test run forever.
        string nugetPackagesDirectory =
            Path.Combine(Path.GetTempPath(), $"haitch-roslyn-smoke-nuget-{Guid.NewGuid():N}");

        Directory.CreateDirectory(feedDirectory);
        Directory.CreateDirectory(nugetPackagesDirectory);

        try
        {
            var packResult = await RunDotNetAsync(
                repositoryRoot,
                [
                    "pack", sourceProjectPath, "-c", "Release", "-o", feedDirectory,
                    // Isolated bin/obj so packing never touches the repo's Release build output.
                    "--artifacts-path", Path.Combine(consumerBuildRoot, "pack-artifacts"),
                    $"-p:PackageVersion={packageVersion}", "-nodeReuse:false"
                ],
                nugetPackagesDirectory);

            AssertSucceeded("pack", packResult);

            string nugetConfigPath = Path.Combine(feedDirectory, "nuget.config");
            await File.WriteAllTextAsync(nugetConfigPath, BuildNuGetConfig(feedDirectory));

            var nullableEnabledResult = await RunDotNetAsync(
                repositoryRoot,
                BuildConsumerArguments(
                    consumerProjectPath, nugetConfigPath, packageVersion, consumerBuildRoot, nullableEnabled: true),
                nugetPackagesDirectory);

            AssertSucceeded("consumer build (nullable enabled)", nullableEnabledResult);

            var nullableDisabledResult = await RunDotNetAsync(
                repositoryRoot,
                BuildConsumerArguments(
                    consumerProjectPath, nugetConfigPath, packageVersion, consumerBuildRoot, nullableEnabled: false),
                nugetPackagesDirectory);

            AssertSucceeded("consumer build (nullable disabled)", nullableDisabledResult);
        }
        finally
        {
            TryDeleteDirectory(feedDirectory);
            TryDeleteDirectory(consumerBuildRoot);
            TryDeleteDirectory(nugetPackagesDirectory);
        }
    }

    private static string[] BuildConsumerArguments(
        string consumerProjectPath,
        string nugetConfigPath,
        string packageVersion,
        string consumerBuildRoot,
        bool nullableEnabled)
    {
        // BaseOutputPath/BaseIntermediateOutputPath are redirected to a per-run temp directory
        // (rather than the Consumer project's own bin/obj) so concurrent smoke-test runs never
        // race on the same restore assets or build outputs.
        string baseOutputPath = Path.Combine(consumerBuildRoot, "bin") + Path.DirectorySeparatorChar;
        string baseIntermediateOutputPath = Path.Combine(consumerBuildRoot, "obj") + Path.DirectorySeparatorChar;

        return
        [
            "build",
            consumerProjectPath,
            "-c",
            "Release",
            "--no-incremental",
            "-nodeReuse:false",
            $"-p:RestoreConfigFile={nugetConfigPath}",
            $"-p:HaitchRoslynPackageVersion={packageVersion}",
            $"-p:Nullable={(nullableEnabled ? "enable" : "disable")}",
            $"-p:BaseOutputPath={baseOutputPath}",
            $"-p:BaseIntermediateOutputPath={baseIntermediateOutputPath}"
        ];
    }

    private static string BuildNuGetConfig(string feedDirectory)
    {
        // Only the freshly-packed feed and nuget.org are visible, so Polyfill restores from
        // nuget.org (or the shared local cache) while Haitch.Roslyn can only come from this run's feed.
        return $"""
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="local-smoke-feed" value="{feedDirectory}" />
                    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
                  </packageSources>
                </configuration>
                """;
    }

    private static void AssertSucceeded(string step, (int ExitCode, string Output) result)
    {
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"{step} failed with exit code {result.ExitCode}:\n{result.Output}");
        }
    }

    private static async Task<(int ExitCode, string Output)> RunDotNetAsync(
        string workingDirectory, string[] arguments, string nugetPackagesDirectory)
    {
        string dotnetPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";

        ProcessStartInfo startInfo = new(dotnetPath)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.Environment["NUGET_PACKAGES"] = nugetPackagesDirectory;

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = startInfo };
        StringBuilder output = new();
        object outputLock = new();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (outputLock)
                {
                    output.AppendLine(e.Data);
                }
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (outputLock)
                {
                    output.AppendLine(e.Data);
                }
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using CancellationTokenSource timeoutCancellation = new(ProcessTimeout);

        try
        {
            await process.WaitForExitAsync(timeoutCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);

            string timedOutOutput;

            lock (outputLock)
            {
                timedOutOutput = output.ToString();
            }

            throw new TimeoutException(
                $"'dotnet {string.Join(' ', arguments)}' timed out after {ProcessTimeout}:\n{timedOutOutput}");
        }

        lock (outputLock)
        {
            return (process.ExitCode, output.ToString());
        }
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
