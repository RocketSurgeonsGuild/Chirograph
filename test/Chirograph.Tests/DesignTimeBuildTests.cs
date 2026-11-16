using Chirograph.Tests.Scaffolding;

namespace Chirograph.Tests;

/// <summary>
/// Build seam: a design-time build (the one an IDE runs to load the project) never runs a generator, but still compiles the code an
/// earlier build generated.
/// </summary>
[TargetFrameworks]
public class DesignTimeBuildTests(string targetFramework) : LoggerTest<TestRecord>(TestRecord.Create())
{
    // What an IDE runs: a design-time build up to the compiler, which only gives its command line.
    private const string DesignTime = "-t:Compile -p:DesignTimeBuild=true -p:SkipCompilerExecution=true -p:ProvideCommandLineArgs=true";

    [Test]
    public async Task Should_Not_Generate_But_Compile_What_Was_Generated_Before()
    {
        using var project = new SdkTestProject(targetFramework);
        project.AddRocketsApp(targetFramework, Generator.Kiota);

        var beforeAnyBuild = await project.Build("app", DesignTime);
        await project.Build("app");
        var afterABuild = await project.Build("app", DesignTime);

        await project.VerifyResult(new { BeforeAnyBuild = Result(beforeAnyBuild), AfterABuild = Result(afterABuild) });
    }

    /// <summary>
    /// A design-time build of a client that a build would refuse (no known generator, a name that isn't a C# identifier, a document
    /// that doesn't exist) raises no error and creates no folder: an IDE loads the project as it is.
    /// </summary>
    [Test]
    public async Task Should_Neither_Check_Nor_Create_Anything()
    {
        using var project = new SdkTestProject(targetFramework);
        project.AddProject(
            "app/app.csproj",
            project
               .CreateConsumer("Library")
               .ItemInclude("OpenApiClient", "missing.json", metadata: new Dictionary<string, string?> { ["Generator"] = "NSwag", ["ClientName"] = "Rockets.Client" })
        );

        var diagnostics = await project.BuildDiagnostics("app", DesignTime);

        // no folder is the result, so show the false rather than leave it out
        await project
             .VerifyResult(new { Diagnostics = diagnostics, ClientsFolderExists = Directory.Exists(Path.Combine(project.Directory, "app", "obj", "Debug", targetFramework, "openapi")) })
             .AddExtraSettings(z => z.DefaultValueHandling = Argon.DefaultValueHandling.Include);
    }

    private static object Result(Microsoft.Build.Logging.StructuredLogger.Build build) =>
        new
        {
            Generated = build.GeneratedClients(),
            Compiled = build.CompiledClientFiles(),
        };
}
