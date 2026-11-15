using Chirograph.Tests.Scaffolding;

namespace Chirograph.Tests;

/// <summary>Build seam: what a build reports when a client cannot be generated, as its CHIRO errors and warnings.</summary>
[TargetFrameworks]
public class DiagnosticsTests(string targetFramework) : LoggerTest<TestRecord>(TestRecord.Create())
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

    /// <summary><c>CHIRO108</c>: two clients have the same <c>ClientName</c>, so they would be generated into the same folder and types.</summary>
    [Test]
    public async Task Should_Fail_When_Two_Clients_Have_The_Same_Name()
    {
        using var project = new SdkTestProject(targetFramework);
        project
           .AddProject(
                "app/app.csproj",
                project.CreateConsumer("Library").OpenApiClient(Generator.Kiota, "rockets.json").OpenApiClient(Generator.Refitter, "other.json")
            )
           .AddFile("app/rockets.json", RocketsApi.Document)
           .AddFile("app/other.json", RocketsApi.Document);

        await project.VerifyResult(await project.BuildDiagnostics("app"));
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
        await using var api = RocketsApi.Start();
        api.DocumentUnavailable = true;
        using var project = new SdkTestProject(targetFramework);
        project.AddProject("app/app.csproj", project.CreateConsumer("Library").OpenApiClient(Generator.Kiota, api.DocumentAddress.ToString()));

        await project.VerifyResult(await project.BuildDiagnostics("app"));
    }

    /// <summary>
    /// <c>CHIRO103</c>: the server writes two documents, and the client doesn't say with <c>DocumentName</c> which one it is generated
    /// from.
    /// </summary>
    [Test]
    public async Task Should_Fail_When_The_Server_Writes_Several_Documents()
    {
        using var project = new SdkTestProject(targetFramework);
        project
           .AddRocketsServer([targetFramework], "v1", "v2")
           .AddProject("app/app.csproj", project.CreateConsumer("Library").OpenApiClient(Generator.Kiota, RocketsServer.Include));

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
