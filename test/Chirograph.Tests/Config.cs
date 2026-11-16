using System.Text.RegularExpressions;
using System.Xml.Linq;
using Chirograph.Tests.Scaffolding;
using Microsoft.Build.Utilities.ProjectCreation;

// Each test scaffolds its own temp folder. The out-of-process tests run one `dotnet` build at a time, which uses several cores itself;
// one test per core keeps a CI runner from being swamped.
[assembly: ParallelLimiter<Chirograph.Tests.OneTestPerProcessor>]

namespace Chirograph.Tests;

public sealed class OneTestPerProcessor : TUnit.Core.Interfaces.IParallelLimit
{
    public int Limit => Environment.ProcessorCount;
}

internal static class Config
{
    /// <summary>
    /// The central pin behind each version property that Chirograph.Pack.targets writes into Sdk/Packages.props. The pack targets
    /// target netstandard2.0, so a pin split per release train resolves to the one below net11.0.
    /// </summary>
    public static IReadOnlyDictionary<string, string> PinnedPackageProperties { get; } = new Dictionary<string, string>
    {
        ["ChirographKiotaVersion"] = "Microsoft.OpenApi.Kiota",
        ["ChirographRefitterVersion"] = "Refitter",
        ["ChirographKiotaBundleVersion"] = "Microsoft.Kiota.Bundle",
        ["ChirographRefitVersion"] = "Refit.HttpClientFactory",
        ["ChirographMicrosoftExtensionsHttpVersion"] = "Microsoft.Extensions.Http",
        ["ChirographDuendeAccessTokenManagementVersion"] = "Duende.AccessTokenManagement",
        ["ChirographAzureIdentityVersion"] = "Azure.Identity",
    };

    /// <summary>
    /// A placeholder for the version Directory.Packages.props pins for <paramref name="packageId" />, so a dependency update doesn't
    /// rewrite every snapshot. A version that differs from the pin is left as it is, and shows in the snapshot.
    /// </summary>
    public static string PinPlaceholder(string packageId) => $"{{pin: {packageId}}}";

    /// <summary>
    /// <paramref name="value" /> with the version Chirograph packs for <paramref name="packageId" /> (the pin below net11.0, see
    /// <see cref="PinnedPackageProperties" />) written as <see cref="PinPlaceholder" />: the version itself, or the exact version a
    /// <c>PackageDownload</c> asks for, <c>[1.2.3]</c>, as <c>[{pin: …}]</c>. Any other value is left as it is.
    /// </summary>
    public static string ScrubPin(string? packageId, string value)
    {
        if (packageId is null)
        {
            return value;
        }

        var pin = CentralPackageVersion(packageId, "netstandard2.0");
        return value == pin ? PinPlaceholder(packageId)
            : value == $"[{pin}]" ? $"[{PinPlaceholder(packageId)}]"
            : value;
    }

    /// <summary>
    /// The packages folder every scaffold of this test run restores into: new for each run, so no scaffold sees a Chirograph package an
    /// earlier run extracted with the same version, and shared within the run, so the tests don't each download the generators and
    /// the ASP.NET Core packages again.
    /// </summary>
    public static string PackagesFolder { get; } = Path.Combine(Path.GetTempPath(), "chirograph-tests", $"packages-{Guid.NewGuid():N}");

    /// <summary>
    /// The Dovetail the joint tests build with: the first one whose <c>DovetailTemplate</c> replaces every metadata value as a token,
    /// and sets <c>DovetailTemplateMetadataTokens</c>.
    /// </summary>
    public const string DovetailVersion = "0.6.0";

    /// <summary>
    /// A package source with a locally packed <see cref="DovetailVersion" />, added to every scaffold's NuGet.config, or
    /// <see langword="null" /> when that version is on nuget.org.
    /// </summary>
    public static string? DovetailStagingPackages { get; } = null;

    /// <summary>A released Dovetail without <c>DovetailTemplateMetadataTokens</c>, which must get no <c>DovetailTemplate</c>.</summary>
    public const string DovetailVersionWithoutMetadataTokens = "0.5.3";

    /// <summary>
    /// The Optionary the schema tests build with: one that reads <c>OptionarySchemaFragment</c> items (Optionary#40, released in 0.1.0).
    /// 0.1.0 was not yet restorable from nuget.org when this was set, so the tests use the build of main right after it.
    /// </summary>
    public const string OptionaryVersion = "0.1.1-beta.0";

    /// <summary>
    /// A package source with a locally packed <see cref="OptionaryVersion" />, added to every scaffold's NuGet.config, or
    /// <see langword="null" /> when that version is on nuget.org.
    /// </summary>
    public static string? OptionaryStagingPackages { get; } = null;

    /// <summary>A released Optionary without <c>OptionarySchemaFragment</c>, on which the items must be inert.</summary>
    public const string OptionaryVersionWithoutSchemaFragments = "0.1.0-beta.1";

    [Before(Assembly)]
    public static async Task Setup(AssemblyHookContext context)
    {
        MSBuildAssemblyResolver.Register();
        await WarmPackagesFolder();
    }

    [After(Assembly)]
    public static void Cleanup(AssemblyHookContext context)
    {
        try
        {
            Directory.Delete(PackagesFolder, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: temp directory cleanup handles stragglers.
        }
    }

    // Restores, once and one at a time, what the tests' scaffolds use on each target framework (both generators, hosting and a
    // server), so the tests that run in parallel find it in PackagesFolder instead of each downloading it at the same time.
    private static async Task WarmPackagesFolder()
    {
        foreach (var targetFramework in (string[])["net10.0", "net11.0"])
        {
            using var project = new SdkTestProject(targetFramework);
            project
               .AddRocketsServer([targetFramework])
               .AddProject(
                    "app/app.csproj",
                    project
                       .CreateConsumer()
                       .Hosting(targetFramework)
                       .OpenApiClient(Generator.Kiota, "rockets.json", "KiotaClient")
                       .OpenApiClient(Generator.Refitter, "rockets.json", "RefitterClient")
                );
            await project.Build("server", "-t:Restore");
            await project.Build("app", "-t:Restore");
        }
    }

    public static string RootDirectory => field ??= FindRootDirectory();

    public static string NugetArtifactsDirectory => field ??= Path.Combine(RootDirectory, "artifacts", "nuget-local");

    /// <summary>
    /// The .NET SDK the repository builds with, pinned in <c>.config/mise.toml</c> (the first <c>dotnet</c> tool entry), so scaffolded
    /// projects use the same SDK instead of whatever is newest on the machine.
    /// </summary>
    public static string DotnetSdkVersion => field ??= Regex.Match(
                                                 File.ReadAllText(Path.Combine(RootDirectory, ".config", "mise.toml")),
                                                 // Whitespace and comment lines may sit between the list and its first entry.
                                                 @"dotnet = \[(?:\s|#[^\n]*)*\{ version = ""(?<version>[^""]+)"""
                                             ) is { Success: true, } match
        ? match.Groups["version"].Value
        : throw new InvalidOperationException("No dotnet SDK version found in .config/mise.toml.");

    /// <summary>
    /// The Chirograph package that <c>PackChirographSourcePackages</c> (Chirograph.Tests.csproj) packed before this build.
    /// </summary>
    public static string ChirographPackage => field ??= Directory.EnumerateFiles(NugetArtifactsDirectory, "Chirograph.*.nupkg")
                                                                 .Where(z => char.IsDigit(Path.GetFileName(z)["Chirograph.".Length]))
                                                                 .OrderByDescending(z => z, StringComparer.Ordinal)
                                                                 .FirstOrDefault()
                                                     ?? throw new InvalidOperationException($"No packed Chirograph nupkg in {NugetArtifactsDirectory}.");

    public static string ChirographPackageVersion => field ??= Path.GetFileName(ChirographPackage)["Chirograph.".Length..^".nupkg".Length];

    /// <summary>
    /// The version Directory.Packages.props pins for <paramref name="packageId" />. Packages split per release train sit in two item
    /// groups conditioned on net11.0 compatibility; <paramref name="targetFramework" /> picks the matching one.
    /// </summary>
    public static string CentralPackageVersion(string packageId, string targetFramework)
    {
        var path = Path.Combine(RootDirectory, "Directory.Packages.props");
        var isNet11OrGreater = targetFramework.StartsWith("net", StringComparison.Ordinal)
         && Version.TryParse(targetFramework["net".Length..], out var version)
         && version.Major >= 11;
        return XDocument.Load(path)
                        .Descendants("PackageVersion")
                        .Where(e => string.Equals(e.Attribute("Include")?.Value, packageId, StringComparison.OrdinalIgnoreCase))
                        .FirstOrDefault(
                             e => e.Parent?.Attribute("Condition")?.Value is not { } condition
                              || !condition.Contains("IsTargetFrameworkCompatible", StringComparison.Ordinal)
                              || condition.TrimStart().StartsWith('!') != isNet11OrGreater
                         )
                       ?.Attribute("Version")
                       ?.Value
         ?? throw new InvalidOperationException($"No PackageVersion entry found for '{packageId}' in {path}.");
    }

    private static string FindRootDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Chirograph.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate the repository root. This test must be run from within the repository.");
    }
}
