using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Utilities.ProjectCreation;
using TUnit.Core.Exceptions;
using Task = System.Threading.Tasks.Task;

namespace Chirograph.Tests;

/// <summary>
/// Scaffolds throwaway consumer projects that use <c>&lt;Sdk Name="Chirograph" /&gt;</c> from the package in the repository's
/// <c>artifacts/nuget-local</c>. It offers the two seams of the test strategy: <see cref="VerifyEvaluation" /> evaluates a project
/// in-process and snapshots what evaluation alone decides, and <see cref="Build" />/<see cref="Run" /> run a real <c>dotnet</c> out of
/// process for everything that needs the targets to run.
/// </summary>
public sealed partial class SdkTestProject : IDisposable
{
    private readonly List<ProjectCreator> _projects = [];
    private readonly VerifySettings _settings;
    private readonly string _targetFramework;

    public string Directory { get; }

    public SdkTestProject(string targetFramework)
    {
        _targetFramework = targetFramework;
        var id = Guid.NewGuid().ToString("N");
        Directory = Path.Combine(Path.GetTempPath(), "chirograph-tests", id);
        System.IO.Directory.CreateDirectory(Directory);

        // A NuGet.config with nuget.org and the local packages, and a global packages folder of the scaffold's own, so restores never
        // see a Chirograph package extracted by an earlier run with the same version.
        _ = PackageRepository.Create(Directory, feeds: [new("https://api.nuget.org/v3/index.json"), new(Config.NugetArtifactsDirectory)]);

        GlobalJsonCreator
           .Create(new DirectoryInfo(Directory))
           .MSBuildSdk("Chirograph", Config.ChirographPackageVersion)
           .TestRunner("Microsoft.Testing.Platform")
           .SdkVersion(Config.DotnetSdkVersion)
           .SdkRollForward(GlobalJsonSdkRollForward.LatestMajor)
           .Save();

        _settings = new();
        // The tests run once per target framework (see TargetFrameworksAttribute) against one shared snapshot.
        _settings.IgnoreParametersForVerified();
        // An empty item list is the result of some tests ("adds no package reference"), so show it rather than leave it out.
        _settings.DontIgnoreEmptyCollections();
        _settings.ScrubLinesWithReplace(
            line => TargetFrameworkPattern().Replace(line.Replace(Config.ChirographPackageVersion, "{chirograph-version}", StringComparison.Ordinal), "{TargetFramework}")
        );
    }

    /// <summary>
    /// Saves a project file to disk at <paramref name="path" /> (relative to the scaffold). Create <paramref name="project" /> without
    /// a path (<c>ProjectCreator.Create()</c>): MSBuild.ProjectCreation caches project elements by path across tests.
    /// </summary>
    public SdkTestProject AddProject(string path, ProjectCreator project)
    {
        project.Save(Path.Combine(Directory, path));
        _projects.Add(project);
        return this;
    }

    /// <summary>A consumer: a console application on the test's target framework that uses <c>&lt;Sdk Name="Chirograph" /&gt;</c>.</summary>
    public ProjectCreator CreateConsumer() =>
        ProjectCreator
           .Templates.SdkCsproj(targetFramework: _targetFramework, outputType: "Exe")
           .Sdk("Chirograph");

    public SdkTestProject AddFile(string path, string content)
    {
        var fullPath = Path.Combine(Directory, path);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return this;
    }

    /// <summary>
    /// The evaluation seam: evaluates the project at <paramref name="path" /> in-process, without restoring or building it, and
    /// verifies Chirograph's public properties and the <c>PackageReference</c> and <c>PackageDownload</c> items, each with its own
    /// metadata. A version Directory.Packages.props pins shows as <see cref="Config.PinPlaceholder" />.
    /// </summary>
    public async Task VerifyEvaluation(string path)
    {
        var fullPath = Path.Combine(Directory, path);
        var project = _projects.Single(z => z.FullPath == fullPath).Project;

        var properties = project.AllEvaluatedProperties
                                .Where(z => z.Name.StartsWith("Chirograph", StringComparison.Ordinal))
                                .OrderBy(z => z.Name, StringComparer.Ordinal)
                                .ToDictionary(z => z.Name, z => ScrubPin(Config.PinnedPackageProperties.GetValueOrDefault(z.Name), z.EvaluatedValue));

        await Verify(
                new
                {
                    Properties = properties,
                    PackageReference = Items(project, "PackageReference"),
                    PackageDownload = Items(project, "PackageDownload"),
                },
                settings: _settings
            ).ToTask();
    }

    private static Dictionary<string, Dictionary<string, string>> Items(Project project, string itemType) =>
        project.GetItems(itemType)
               .OrderBy(z => z.EvaluatedInclude, StringComparer.Ordinal)
               .ToDictionary(
                    item => item.EvaluatedInclude,
                    item => item.DirectMetadata
                                .OrderBy(z => z.Name, StringComparer.Ordinal)
                                .ToDictionary(z => z.Name, z => z.Name == "Version" ? ScrubPin(item.EvaluatedInclude, z.EvaluatedValue) : z.EvaluatedValue)
                );

    private static string ScrubPin(string? packageId, string value) =>
        packageId is not null && value == Config.CentralPackageVersion(packageId, "netstandard2.0") ? Config.PinPlaceholder(packageId) : value;

    /// <summary>
    /// The build seam: runs <c>dotnet build</c> (plus <paramref name="arguments" />) for the project in <paramref name="relativePath" />
    /// and returns the build's binary log.
    /// </summary>
    public async Task<Microsoft.Build.Logging.StructuredLogger.Build> Build(string relativePath, string arguments = "")
    {
        var projectDirectory = Path.Combine(Directory, relativePath);
        await RunDotnet($"build -bl {arguments}", projectDirectory);
        return Microsoft.Build.Logging.StructuredLogger.BinaryLog.ReadBuild(Path.Combine(projectDirectory, "msbuild.binlog"));
    }

    /// <summary>
    /// Builds and runs the application in <paramref name="relativePath" /> with <c>dotnet run</c>, with <paramref name="environment" />
    /// added to its environment (say <c>Clients__RocketsClient__BaseAddress</c>), and returns what it wrote to standard output.
    /// </summary>
    public Task<string> Run(string relativePath, IReadOnlyDictionary<string, string>? environment = null, string arguments = "") =>
        RunDotnet(arguments == "" ? "run" : $"run -- {arguments}", Path.Combine(Directory, relativePath), environment);

    /// <summary>Verifies <paramref name="target" /> with the scaffold's scrubbers (the target framework and Chirograph's version).</summary>
    public Task VerifyResult(object target) => Verify(target, _settings).ToTask();

    private static async Task<string> RunDotnet(string arguments, string workingDirectory, IReadOnlyDictionary<string, string>? environment = null)
    {
        var psi = CreateHermeticStartInfo(arguments, workingDirectory);
        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
        {
            psi.Environment[name] = value;
        }

        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEndAsync();
        var stderr = proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return proc.ExitCode == 0
            ? await stdout
            : throw new TUnitException($"dotnet {arguments} failed in {workingDirectory}:\n{await stdout}\n{await stderr}");
    }

    private static ProcessStartInfo CreateHermeticStartInfo(string arguments, string workingDirectory)
    {
        var psi = new ProcessStartInfo("dotnet", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";
        // A net10.0 application needs the .NET 10 runtime, which a machine with only a newer runtime doesn't have. Let it run on the
        // next major runtime, prerelease included.
        psi.Environment["DOTNET_ROLL_FORWARD"] = "Major";
        psi.Environment["DOTNET_ROLL_FORWARD_TO_PRERELEASE"] = "1";
        // CI variables would flip ContinuousIntegrationBuild and make results differ between local and CI runs.
        foreach (var name in (string[])["GITHUB_ACTIONS", "CI", "TF_BUILD", "GITLAB_CI", "APPVEYOR", "TEAMCITY_VERSION"])
        {
            psi.Environment.Remove(name);
        }

        // MSBuildAssemblyResolver pins these to the in-process toolset for this process. A spawned `dotnet` would inherit them and mix
        // two toolsets when global.json resolves another SDK, so it resolves its own.
        foreach (var name in (string[])["MSBuildSDKsPath", "MSBUILD_EXE_PATH", "MSBuildExtensionsPath", "MSBuildLoadMicrosoftTargetsReadOnly"])
        {
            psi.Environment.Remove(name);
        }

        return psi;
    }

    [GeneratedRegex(@"\bnet1[01]\.0\b")]
    private static partial Regex TargetFrameworkPattern();

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: temp directory cleanup handles stragglers.
        }
    }
}
