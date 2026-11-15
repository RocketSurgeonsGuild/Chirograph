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

    private static object Result(Microsoft.Build.Logging.StructuredLogger.Build build) =>
        new
        {
            Generated = build.GeneratedClients(),
            // what Chirograph adds to the compilation, relative to the folder of the project's clients (the compiler itself doesn't run
            // again after a build: it is up to date)
            Compiled = build
                      .AddedItems("_ChirographCompileOpenApiClients", "Compile")
                      .Select(z => z.Replace('\\', '/'))
                      .Where(z => z.Contains("/openapi/", StringComparison.Ordinal))
                      .Select(z => z[( z.IndexOf("/openapi/", StringComparison.Ordinal) + "/openapi/".Length )..])
                      .Distinct(StringComparer.Ordinal)
                      .Order(StringComparer.Ordinal)
                      .ToArray(),
        };
}
