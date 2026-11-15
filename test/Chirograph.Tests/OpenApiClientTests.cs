using Chirograph.Tests.Scaffolding;

namespace Chirograph.Tests;

/// <summary>
/// An <c>OpenApiClient</c> item generates an HTTP client from an OpenAPI document with Kiota or Refitter, and a public
/// <c>Add&lt;ClientName&gt;()</c> extension that registers it with <c>IHttpClientFactory</c>, its base address read from
/// <c>Clients:&lt;ClientName&gt;:BaseAddress</c>. Each throwaway application calls <see cref="RocketsApi" />, which the test serves on
/// the loopback interface.
/// </summary>
[TargetFrameworks]
public class OpenApiClientTests(string targetFramework)
{
    /// <summary>
    /// Build seam: each generator generates a client from each kind of source, and the application registers it with
    /// <c>AddRocketsClient()</c> and calls the API at the configured base address.
    /// </summary>
    [Test]
    [Arguments(Generator.Kiota, Source.File)]
    [Arguments(Generator.Refitter, Source.File)]
    [Arguments(Generator.Kiota, Source.Url)]
    [Arguments(Generator.Refitter, Source.Url)]
    public async Task Should_Generate_A_Registered_Client(Generator generator, Source source)
    {
        await using var api = RocketsApi.Start();
        using var project = new SdkTestProject(targetFramework);
        project.AddRocketsApp(
            targetFramework,
            generator,
            source switch
            {
                Source.File => "rockets.json",
                Source.Url => api.DocumentAddress.ToString(),
                _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
            }
        );

        await project.Build("app");
        var output = await project.Run("app", RocketsApp.Environment(api));

        await project.VerifyResult(output, $"{generator}_{source}");
    }

    /// <summary>
    /// Build seam: a library generates the client, and an application that doesn't use Chirograph registers it through the library's
    /// public <c>AddRocketsClient()</c>. The library has only the packages Chirograph adds, so it also proves they are enough to compile
    /// the registration.
    /// </summary>
    [Test]
    [Arguments(Generator.Kiota)]
    [Arguments(Generator.Refitter)]
    public async Task Should_Register_A_Client_From_A_Library(Generator generator)
    {
        await using var api = RocketsApi.Start();
        using var project = new SdkTestProject(targetFramework);
        project
           .AddProject("lib/lib.csproj", project.CreateConsumer("Library").OpenApiClient(generator, "rockets.json"))
           .AddFile("lib/rockets.json", RocketsApi.Document)
           .AddProject("app/app.csproj", project.CreateProject().Hosting(targetFramework).ItemProjectReference("../lib/lib.csproj"))
           .AddFile("app/Program.cs", RocketsApp.Program(generator));

        await project.Build("app");
        var output = await project.Run("app", RocketsApp.Environment(api));

        await project.VerifyResult(output, generator.ToString());
    }

    /// <summary>
    /// Build seam: the registration Chirograph writes for each generator, a public <c>Add&lt;ClientName&gt;()</c> that returns the
    /// <c>IHttpClientBuilder</c>, stamped with the version of Chirograph.
    /// </summary>
    [Test]
    [Arguments(Generator.Kiota)]
    [Arguments(Generator.Refitter)]
    public async Task Should_Write_The_Registration(Generator generator)
    {
        using var project = new SdkTestProject(targetFramework);
        project
           .AddProject("lib/lib.csproj", project.CreateConsumer("Library").OpenApiClient(generator, "rockets.json"))
           .AddFile("lib/rockets.json", RocketsApi.Document);

        await project.Build("lib");
        var registration = await File.ReadAllTextAsync(
            Path.Combine(project.Directory, "lib", "obj", "Debug", targetFramework, "openapi", "RocketsClient", "RocketsClientServiceCollectionExtensions.g.cs")
        );

        await project.VerifyResult(registration, generator.ToString());
    }

    /// <summary>
    /// Build seam: the generators run with their telemetry and update checks off. The variables are the ones Kiota reads
    /// (<c>KIOTA_CLI_TELEMETRY_OPTOUT</c>, and <c>KIOTA_OFFLINE_ENABLED</c> for the update check) and the .NET CLI's.
    /// </summary>
    [Test]
    public async Task Should_Run_The_Generators_Without_Telemetry()
    {
        using var project = new SdkTestProject(targetFramework);
        project
           .AddProject(
                "app/app.csproj",
                project
                   .CreateConsumer("Library")
                   .OpenApiClient(Generator.Kiota, "rockets.json", "KiotaClient")
                   .OpenApiClient(Generator.Refitter, "rockets.json", "RefitterClient")
            )
           .AddFile("app/rockets.json", RocketsApi.Document);

        var build = await project.Build("app");

        await project.VerifyResult(
            build.TaskParameter("Exec", "EnvironmentVariables", z => z.ParameterValues("Command").Any(c => c.Contains("exec --roll-forward", StringComparison.Ordinal)))
        );
    }

    /// <summary>
    /// Evaluation seam: each generator a project's clients use is downloaded (<c>PackageDownload</c>), and the packages its code needs are
    /// referenced: <c>Microsoft.Kiota.Bundle</c> and <c>Microsoft.Extensions.Http</c> for Kiota, <c>Refit.HttpClientFactory</c> for
    /// Refitter.
    /// </summary>
    [Test]
    [Arguments("Kiota")]
    [Arguments("Refitter")]
    [Arguments("Kiota,Refitter")]
    public async Task Should_Add_The_Generators_The_Clients_Use(string generators)
    {
        using var project = new SdkTestProject(targetFramework);
        var consumer = project.CreateConsumer();
        foreach (var generator in generators.Split(','))
        {
            consumer.OpenApiClient(Enum.Parse<Generator>(generator), "rockets.json", $"{generator}Client");
        }

        project.AddProject("app/app.csproj", consumer);

        await project.VerifyEvaluation("app/app.csproj", generators.Replace(',', '_'));
    }

    /// <summary>
    /// Evaluation seam: the source of a client is inferred from its <c>Include</c>. A project (<c>.csproj</c>, <c>.fsproj</c>,
    /// <c>.vbproj</c>) becomes a reference that is built first but not referenced by the assembly; a file (JSON or YAML) or a URL adds no
    /// reference.
    /// </summary>
    [Test]
    public async Task Should_Infer_The_Source_From_The_Include()
    {
        using var project = new SdkTestProject(targetFramework);
        project.AddProject(
            "app/app.csproj",
            project
               .CreateConsumer()
               .OpenApiClient(Generator.Kiota, "rockets.json", "JsonClient")
               .OpenApiClient(Generator.Kiota, "openapi/rockets.yaml", "YamlClient")
               .OpenApiClient(Generator.Kiota, "../server/server.csproj", "ServerClient")
               .OpenApiClient(Generator.Kiota, "https://example.com/openapi.json", "UrlClient")
        );

        await project.VerifyEvaluation("app/app.csproj");
    }

    /// <summary>
    /// Evaluation seam: a package Chirograph adds for a client is left alone when the project references it itself: the project's own
    /// reference and version win.
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
               .OpenApiClient(Generator.Kiota, "rockets.json")
        );

        await project.VerifyEvaluation("app/app.csproj");
    }

    /// <summary>Where a document comes from.</summary>
    public enum Source
    {
        /// <summary>A file in the project.</summary>
        File,

        /// <summary>A URL, which <see cref="RocketsApi" /> serves.</summary>
        Url,
    }
}
