using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Haitch.Roslyn.Tests;

public class PackageMetadataTests
{
    private const string Version = "1.2.3";
    private const string RepositoryUrl = "https://github.com/HaitchDev/Haitch.Roslyn";
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(3);

    [Test]
    [NotInParallel("pack")]
    public async Task Should_pack_both_projects_with_shared_metadata_and_testing_symbols_only()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), $"haitch-roslyn-metadata-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDirectory);

        try
        {
            foreach (string project in new[] { "Haitch.Roslyn", "Haitch.Roslyn.Testing" })
            {
                string projectPath = Path.Combine(RepoPaths.Root, "src", project, $"{project}.csproj");
                var result = await RunDotNetAsync(
                    RepoPaths.Root,
                    ["pack", projectPath, "-c", "Release", "-o", outputDirectory, $"-p:Version={Version}", "-nodeReuse:false"]);

                if (result.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"pack of {project} failed with exit code {result.ExitCode}:\n{result.Output}");
                }
            }

            foreach (string package in new[] { "Haitch.Roslyn", "Haitch.Roslyn.Testing" })
            {
                AssertPackage(Path.Combine(outputDirectory, $"{package}.{Version}.nupkg"));
            }

            AssertSourceOnly(Path.Combine(outputDirectory, $"Haitch.Roslyn.{Version}.nupkg"));

            await Assert.That(File.Exists(Path.Combine(outputDirectory, $"Haitch.Roslyn.Testing.{Version}.snupkg")))
                .IsTrue();
            await Assert.That(File.Exists(Path.Combine(outputDirectory, $"Haitch.Roslyn.{Version}.snupkg")))
                .IsFalse();
        }
        finally
        {
            TryDeleteDirectory(outputDirectory);
        }
    }

    private static void AssertPackage(string packagePath)
    {
        using ZipArchive archive = ZipFile.OpenRead(packagePath);
        string[] entries = archive.Entries.Select(entry => entry.FullName).ToArray();

        Require(entries.Contains("icon.png"), $"{packagePath} is missing icon.png");
        Require(entries.Contains("README.md"), $"{packagePath} is missing README.md");

        ZipArchiveEntry nuspecEntry = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec"));
        using StreamReader reader = new(nuspecEntry.Open());
        XElement metadata = XDocument.Parse(reader.ReadToEnd()).Root!.Elements().Single(e => e.Name.LocalName == "metadata");

        string Value(string name) =>
            metadata.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value ?? string.Empty;

        Require(Value("authors") == "Hayden Quinn", $"{packagePath}: authors was '{Value("authors")}'");
        Require(Value("version") == Version, $"{packagePath}: version was '{Value("version")}'");
        Require(Value("license") == "MIT", $"{packagePath}: license was '{Value("license")}'");

        XElement? repository = metadata.Elements().FirstOrDefault(e => e.Name.LocalName == "repository");
        Require(repository?.Attribute("url")?.Value == RepositoryUrl, $"{packagePath}: repository url was wrong");
        Require(
            !string.IsNullOrEmpty(repository?.Attribute("commit")?.Value),
            $"{packagePath}: repository commit was missing");
    }

    private static void AssertSourceOnly(string packagePath)
    {
        using ZipArchive archive = ZipFile.OpenRead(packagePath);

        foreach (string entry in archive.Entries.Select(e => e.FullName))
        {
            Require(!entry.StartsWith("lib/"), $"{packagePath} must not contain build output but has {entry}");
            Require(
                !entry.EndsWith(".dll") && (!entry.EndsWith(".xml") || entry == "[Content_Types].xml"),
                $"{packagePath} must not contain binaries or XML docs but has {entry}");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static async Task<(int ExitCode, string Output)> RunDotNetAsync(string workingDirectory, string[] arguments)
    {
        string dotnetPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";

        ProcessStartInfo startInfo = new(dotnetPath)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = startInfo };
        StringBuilder output = new();
        object outputLock = new();

        void Append(object? _, DataReceivedEventArgs e)
        {
            if (e.Data is not null)
            {
                lock (outputLock)
                {
                    output.AppendLine(e.Data);
                }
            }
        }

        process.OutputDataReceived += Append;
        process.ErrorDataReceived += Append;

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
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            throw new TimeoutException($"'dotnet {string.Join(' ', arguments)}' timed out after {ProcessTimeout}.");
        }

        lock (outputLock)
        {
            return (process.ExitCode, output.ToString());
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
