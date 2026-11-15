namespace Chirograph.Tests;

/// <summary>A consumer of the Chirograph SDK, before any <c>OpenApiClient</c> item does anything.</summary>
[TargetFrameworks]
public class SdkTests(string targetFramework)
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
    /// Evaluation seam: a package the SDK adds implicitly is added at the SDK's version, unless the project references it itself, in
    /// which case the project's own reference and version win. Until <c>OpenApiClient</c> items declare them (ticket #5), the project
    /// declares the SDK's private <c>_ChirographImplicitPackageReference</c> items itself.
    /// </summary>
    [Test]
    public async Task Should_Leave_A_Package_The_Project_References_Alone()
    {
        using var project = new SdkTestProject(targetFramework);
        project.AddProject(
            "app/app.csproj",
            project
               .CreateConsumer()
               .ItemPackageReference("Microsoft.Kiota.Bundle", version: "2.0.0")
               .ItemInclude(
                    "_ChirographImplicitPackageReference",
                    "Microsoft.Kiota.Bundle",
                    metadata: new Dictionary<string, string?> { ["Version"] = "$(ChirographKiotaBundleVersion)" }
                )
               .ItemInclude(
                    "_ChirographImplicitPackageReference",
                    "Refit.HttpClientFactory",
                    metadata: new Dictionary<string, string?> { ["Version"] = "$(ChirographRefitVersion)", ["PrivateAssets"] = "analyzers" }
                )
        );

        await project.VerifyEvaluation("app/app.csproj");
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
