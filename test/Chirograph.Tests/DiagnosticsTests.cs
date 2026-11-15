using Chirograph.Tests.Scaffolding;

namespace Chirograph.Tests;

/// <summary>Build seam: what a build reports when a client cannot be generated, as its CHIRO errors and warnings.</summary>
[TargetFrameworks]
public class DiagnosticsTests(string targetFramework)
{
    /// <summary><c>CHIRO100</c> and <c>CHIRO101</c>: a client without a known generator, or without a name that is a C# identifier.</summary>
    [Test]
    [Arguments(null, "RocketsClient")]
    [Arguments("NSwag", "RocketsClient")]
    [Arguments("Kiota", null)]
    [Arguments("Kiota", "Rockets.Client")]
    public async Task Should_Fail_When_A_Client_Has_No_Generator_Or_Name(string? generator, string? clientName)
    {
        using var project = new SdkTestProject(targetFramework);
        project
           .AddProject(
                "app/app.csproj",
                project
                   .CreateConsumer("Library")
                   .ItemInclude("OpenApiClient", "rockets.json", metadata: new Dictionary<string, string?> { ["Generator"] = generator, ["ClientName"] = clientName })
            )
           .AddFile("app/rockets.json", RocketsApi.Document);

        await project.VerifyResult(await project.BuildDiagnostics("app"), $"{generator ?? "none"}_{clientName ?? "none"}");
    }

    /// <summary><c>CHIRO102</c>: the document file does not exist.</summary>
    [Test]
    public async Task Should_Fail_When_The_Document_Does_Not_Exist()
    {
        using var project = new SdkTestProject(targetFramework);
        project.AddProject("app/app.csproj", project.CreateConsumer("Library").OpenApiClient(Generator.Kiota, "missing.json"));

        await project.VerifyResult(await project.BuildDiagnostics("app"));
    }

    /// <summary><c>CHIRO105</c>: the document cannot be downloaded from its URL, and there is no cached copy to use instead.</summary>
    [Test]
    public async Task Should_Fail_When_The_Url_Cannot_Be_Reached_Without_A_Cache()
    {
        var api = RocketsApi.Start();
        var address = api.DocumentAddress.ToString();
        await api.DisposeAsync();
        using var project = new SdkTestProject(targetFramework);
        project.AddProject("app/app.csproj", project.CreateConsumer("Library").OpenApiClient(Generator.Kiota, address));

        await project.VerifyResult(await project.BuildDiagnostics("app"));
    }

    /// <summary>
    /// <c>CHIRO106</c>: the generator tool is not in the packages folders, because the build asks for a version restore never
    /// downloaded.
    /// </summary>
    [Test]
    public async Task Should_Fail_When_The_Generator_Is_Not_Restored()
    {
        using var project = new SdkTestProject(targetFramework);
        project
           .AddProject("app/app.csproj", project.CreateConsumer("Library").OpenApiClient(Generator.Kiota, "rockets.json"))
           .AddFile("app/rockets.json", RocketsApi.Document);

        await project.Build("app", "-t:Restore");
        var diagnostics = await project.BuildDiagnostics("app", "--no-restore -p:ChirographKiotaVersion=0.0.1");

        await project.VerifyResult(diagnostics);
    }

    /// <summary>
    /// <c>CHIRO107</c>: the generator exits with an error, here because the document is not an OpenAPI document. Only the first line of
    /// the error is Chirograph's: the lines after it are what the tool wrote, a stack trace included.
    /// </summary>
    [Test]
    public async Task Should_Fail_When_The_Generator_Fails()
    {
        using var project = new SdkTestProject(targetFramework);
        project
           .AddProject("app/app.csproj", project.CreateConsumer("Library").OpenApiClient(Generator.Kiota, "rockets.json"))
           .AddFile("app/rockets.json", "not an OpenAPI document");

        var diagnostics = await project.BuildDiagnostics("app");

        await project.VerifyResult(diagnostics.Select(z => z.Split('\n')[0]));
    }
}
