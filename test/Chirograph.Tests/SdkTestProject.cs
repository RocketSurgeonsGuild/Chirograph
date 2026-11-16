using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Utilities.ProjectCreation;
using TUnit.Core.Exceptions;

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

        // A NuGet.config with nuget.org and the local packages, and the test run's packages folder (Config.PackagesFolder), which no
        // earlier run used, so restores never see a Chirograph package extracted by an earlier run with the same version.
        File.WriteAllText(
            Path.Combine(Directory, "NuGet.config"),
            new XDocument(
                new XElement(
                    "configuration",
                    new XElement("config", new XElement("add", new XAttribute("key", "globalPackagesFolder"), new XAttribute("value", Config.PackagesFolder))),
                    new XElement(
                        "packageSources",
                        new XElement("clear"),
                        new XElement("add", new XAttribute("key", "nuget.org"), new XAttribute("value", "https://api.nuget.org/v3/index.json")),
                        new XElement("add", new XAttribute("key", "chirograph-local"), new XAttribute("value", Config.NugetArtifactsDirectory)),
                        // Until the Dovetail the joint tests need is released, its locally packed copy (Config.DovetailVersion).
                        Config.DovetailStagingPackages is { } staging
                            ? new XElement("add", new XAttribute("key", "dovetail-staging"), new XAttribute("value", staging))
                            : null
                    )
                )
            ).ToString()
        );

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
        // The packages folder differs on every run, and a DovetailTemplate's File is in it.
        _settings.ScrubLinesWithReplace(line => line.Replace(Config.PackagesFolder, "{packages}", StringComparison.Ordinal));
        // RocketsApi listens on a free port, which differs on every run.
        _settings.ScrubLinesWithReplace(line => RocketsApiPattern().Replace(line, "{rockets-api}/"));
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

    /// <summary>
    /// A consumer: a project on the test's target framework that uses <c>&lt;Sdk Name="Chirograph" /&gt;</c>, a console application
    /// unless <paramref name="outputType" /> says otherwise.
    /// </summary>
    public ProjectCreator CreateConsumer(string outputType = "Exe") => CreateProject(outputType).Sdk("Chirograph");

    /// <summary>A project on the test's target framework that doesn't use Chirograph, a console application unless <paramref name="outputType" /> says otherwise.</summary>
    public ProjectCreator CreateProject(string outputType = "Exe") => ProjectCreator.Templates.SdkCsproj(targetFramework: _targetFramework, outputType: outputType);

    public SdkTestProject AddFile(string path, string content)
    {
        var fullPath = Path.Combine(Directory, path);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return this;
    }

    /// <summary>
    /// The evaluation seam: evaluates the project at <paramref name="path" /> in-process, without restoring or building it, and
    /// verifies Chirograph's public properties and the <c>PackageReference</c>, <c>PackageDownload</c>, <c>ProjectReference</c> and <c>DovetailTemplate</c> items, each with its own
    /// metadata. A version Directory.Packages.props pins shows as <see cref="Config.PinPlaceholder" />.
    /// </summary>
    public SettingsTask VerifyEvaluation(string path, string? variant = null, [CallerMemberName] string method = "")
    {
        var project = Evaluated(path);

        var properties = project.Properties
                                .Where(z => z.Name.StartsWith("Chirograph", StringComparison.Ordinal))
                                .OrderBy(z => z.Name, StringComparer.Ordinal)
                                .ToDictionary(z => z.Name, z => Config.ScrubPin(Config.PinnedPackageProperties.GetValueOrDefault(z.Name), z.EvaluatedValue));

        return Verify(
                new
                {
                    Properties = properties,
                    PackageReference = Items(project, "PackageReference"),
                    PackageDownload = Items(project, "PackageDownload"),
                    ProjectReference = Items(project, "ProjectReference"),
                    DovetailTemplate = Items(project, "DovetailTemplate"),
                },
                settings: Settings(variant, method)
            );
    }

    /// <summary>
    /// The evaluation seam, for what Chirograph keeps to itself: the items of type <paramref name="itemType" /> evaluation gives the
    /// project at <paramref name="path" />, each with its own metadata.
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> EvaluatedItems(string path, string itemType) => Items(Evaluated(path), itemType);

    /// <summary>The evaluation seam, for what Chirograph keeps to itself: the value of <paramref name="name" /> in the project at <paramref name="path" />.</summary>
    public string EvaluatedProperty(string path, string name) => Evaluated(path).GetPropertyValue(name);

    private Project Evaluated(string path)
    {
        var fullPath = Path.Combine(Directory, path);
        return _projects.Single(z => z.FullPath == fullPath).Project;
    }

    private static Dictionary<string, Dictionary<string, string>> Items(Project project, string itemType) =>
        project.GetItems(itemType)
               .OrderBy(z => z.EvaluatedInclude, StringComparer.Ordinal)
               .ToDictionary(
                    item => item.EvaluatedInclude,
                    item => item.DirectMetadata
                                .OrderBy(z => z.Name, StringComparer.Ordinal)
                                .ToDictionary(z => z.Name, z => z.Name == "Version" ? Config.ScrubPin(item.EvaluatedInclude, z.EvaluatedValue) : z.EvaluatedValue)
                );

    /// <summary>
    /// The build seam: runs <c>dotnet build</c> (plus <paramref name="arguments" />) for the project in <paramref name="relativePath" />
    /// and returns the build's binary log. The build must succeed.
    /// </summary>
    public Task<Microsoft.Build.Logging.StructuredLogger.Build> Build(string relativePath, string arguments = "") =>
        Build(relativePath, arguments, allowFailure: false);

    /// <summary>
    /// The build seam for a build that may fail: runs <c>dotnet build</c> (plus <paramref name="arguments" />) for the project in
    /// <paramref name="relativePath" /> and returns the errors and warnings whose code starts with <paramref name="codePrefix" /> (Chirograph's <c>CHIRO</c> codes unless it says otherwise), as <c>CODE: message</c>
    /// with the scaffold's folder written as <c>{scaffold}</c>.
    /// </summary>
    public async Task<string[]> BuildDiagnostics(string relativePath, string arguments = "", string codePrefix = "CHIRO") =>
        Diagnostics(await Build(relativePath, arguments, allowFailure: true), codePrefix);

    private async Task<Microsoft.Build.Logging.StructuredLogger.Build> Build(string relativePath, string arguments, bool allowFailure)
    {
        var projectDirectory = Path.Combine(Directory, relativePath);
        await RunDotnet($"build -bl {arguments}", projectDirectory, allowFailure: allowFailure);
        return Microsoft.Build.Logging.StructuredLogger.BinaryLog.ReadBuild(Path.Combine(projectDirectory, "msbuild.binlog"));
    }

    /// <summary>
    /// Chirograph's errors and warnings (<c>CHIRO</c> codes) in <paramref name="build" />, as <c>CODE: message</c> with the scaffold's
    /// folder written as <c>{scaffold}</c>.
    /// </summary>
    public string[] Diagnostics(Microsoft.Build.Logging.StructuredLogger.Build build, string codePrefix = "CHIRO") =>
        [.. build
              .FindChildrenRecursive<Microsoft.Build.Logging.StructuredLogger.AbstractDiagnostic>(
                   z => z.Code?.StartsWith(codePrefix, StringComparison.Ordinal) == true
               )
              // macOS gives the temp folder as /var/… and tools resolve it to /private/var/…
              .Select(
                   z => $"{z.Code}: {z.Text}"
                       .Replace($"/private{Directory}", "{scaffold}", StringComparison.Ordinal)
                       .Replace(Directory, "{scaffold}", StringComparison.Ordinal)
                       .Replace($"/private{Config.PackagesFolder}", "{packages}", StringComparison.Ordinal)
                       .Replace(Config.PackagesFolder, "{packages}", StringComparison.Ordinal)
               )
              .Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// Builds and runs the application in <paramref name="relativePath" /> with <c>dotnet run</c>, with <paramref name="environment" />
    /// added to its environment (say <c>Clients__RocketsClient__BaseAddress</c>), and returns what it wrote to standard output.
    /// </summary>
    public Task<string> Run(string relativePath, IReadOnlyDictionary<string, string>? environment = null, string arguments = "") =>
        RunDotnet(arguments == "" ? "run" : $"run -- {arguments}", Path.Combine(Directory, relativePath), environment);

    /// <summary>
    /// Runs <c>dotnet <paramref name="arguments" /></c> in <paramref name="relativePath" />, with <paramref name="environment" /> added
    /// to its environment, and returns what it wrote to standard output. It must succeed. For what <see cref="Build" /> and
    /// <see cref="Run" /> don't cover: say <c>publish</c>, then the published application.
    /// </summary>
    public Task<string> Dotnet(string relativePath, string arguments, IReadOnlyDictionary<string, string>? environment = null) =>
        RunDotnet(arguments, Path.Combine(Directory, relativePath), environment);

    /// <summary>
    /// Verifies <paramref name="target" /> with the scaffold's scrubbers (the target framework and Chirograph's version). A test whose
    /// arguments change the result names a <paramref name="variant" />, which becomes part of the snapshot's name.
    /// </summary>
    public SettingsTask VerifyResult(object target, string? variant = null, [CallerMemberName] string method = "") =>
        Verify(target, Settings(variant, method));

    // Both target frameworks share one verified file (IgnoreParametersForVerified), so a variant goes into the method name instead of
    // the parameters, which only name the received files.
    private VerifySettings Settings(string? variant, string method)
    {
        var settings = new VerifySettings(_settings);
        if (variant is not null)
        {
            settings.UseMethodName($"{method}_{variant}");
        }

        return settings;
    }

    private static async Task<string> RunDotnet(
        string arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment = null,
        bool allowFailure = false
    )
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
        return proc.ExitCode == 0 || allowFailure
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

    [GeneratedRegex(@"http://127\.0\.0\.1:\d+/")]
    private static partial Regex RocketsApiPattern();

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
