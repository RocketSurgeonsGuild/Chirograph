namespace Chirograph.Tests;

/// <summary>A consumer of the Chirograph SDK without any <c>OpenApiClient</c> item.</summary>
[TargetFrameworks]
public class SdkTests(string targetFramework) : LoggerTest<TestRecord>(TestRecord.Create())
{
    /// <summary>Evaluation seam: the SDK sets its version properties and adds no package reference or download.</summary>
    [Test]
    public async Task Should_Add_Nothing_To_A_Project_Without_Clients()
    {
        using var project = new SdkTestProject(targetFramework);
        project.AddProject("app/app.csproj", project.CreateConsumer());

        await project.VerifyEvaluation("app/app.csproj");
    }

    /// <summary>
    /// Evaluation seam: each error and warning links to the docs of the installed version when it is a release, which the docs site
    /// keeps in <c>v&lt;version&gt;/</c>, and to the root, main's docs, for a prerelease, which has no folder of its own.
    /// </summary>
    [Test]
    [Arguments("1.2.3")]
    [Arguments("1.2.3-beta.4")]
    public async Task Should_Link_The_Diagnostics_To_The_Docs_Of_The_Version(string version)
    {
        using var project = new SdkTestProject(targetFramework);
        project.AddProject("app/app.csproj", project.CreateConsumer().Property("ChirographVersion", version));

        await project.VerifyResult(project.EvaluatedProperty("app/app.csproj", "_ChirographDiagnosticsUrl"), version);
    }

    /// <summary>Build seam: a consumer with no <c>OpenApiClient</c> items builds and runs, with the environment the test gives it.</summary>
    [Test]
    public async Task Should_Build_And_Run_A_Project_Without_Clients()
    {
        using var project = new SdkTestProject(targetFramework);
        project
           .AddProject("app/app.csproj", project.CreateConsumer())
           .AddFile(
                "app/Program.cs",
                """
                System.Console.WriteLine($"Clients__RocketsClient__BaseAddress={System.Environment.GetEnvironmentVariable("Clients__RocketsClient__BaseAddress")}");
                """
            );

        await project.Build("app");
        var output = await project.Run("app", new Dictionary<string, string> { ["Clients__RocketsClient__BaseAddress"] = "http://127.0.0.1:5000/" });

        await project.VerifyResult(output);
    }
}
