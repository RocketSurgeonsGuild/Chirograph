using Chirograph.Tests.Scaffolding;

namespace Chirograph.Tests;

/// <summary>
/// Build seam: <c>Add&lt;ClientName&gt;()</c> reads the section <c>Clients:&lt;ClientName&gt;</c> when the client is created, and applies
/// <c>BaseAddress</c>, <c>Timeout</c> and <c>DefaultRequestHeaders</c> to its <c>HttpClient</c>, and <c>Resilience</c> when the project
/// references Microsoft.Extensions.Http.Resilience. Each throwaway application calls <see cref="RocketsApi" />, configured through
/// environment variables.
/// </summary>
[TargetFrameworks]
public class ClientSettingsTests(string targetFramework) : LoggerTest<TestRecord>(TestRecord.Create())
{
    /// <summary>
    /// Each default request header reaches the API: it echoes <c>X-Serial-Number</c> as the serial number of the rocket.
    /// </summary>
    [Test]
    [Arguments(Generator.Kiota)]
    [Arguments(Generator.Refitter)]
    public async Task Should_Send_The_Default_Request_Headers(Generator generator)
    {
        await using var api = RocketsApi.Start();
        using var project = new SdkTestProject(targetFramework);
        project.AddRocketsApp(targetFramework, generator);

        await project.Build("app");
        var output = await project.Run(
            "app",
            new Dictionary<string, string>(RocketsApp.Environment(api)) { ["Clients__RocketsClient__DefaultRequestHeaders__X-Serial-Number"] = "STARSHIP-1" }
        );

        await project.VerifyResult(output, generator.ToString());
    }

    /// <summary>
    /// The timeout is the client's: the API answers after a minute, and the call fails when the configured half second elapses.
    /// </summary>
    [Test]
    [Arguments(Generator.Kiota)]
    [Arguments(Generator.Refitter)]
    public async Task Should_Time_Out_After_The_Timeout(Generator generator)
    {
        await using var api = RocketsApi.Start();
        api.RocketDelay = TimeSpan.FromMinutes(1);
        using var project = new SdkTestProject(targetFramework);
        project.AddRocketsApp(targetFramework, generator).AddFile("app/Program.cs", RocketsApp.ProgramReportingFailure(generator));

        await project.Build("app");
        var output = await project.Run(
            "app",
            new Dictionary<string, string>(RocketsApp.Environment(api)) { ["Clients__RocketsClient__Timeout"] = "00:00:00.5" }
        );

        await project.VerifyResult(output, generator.ToString());
    }

    /// <summary>
    /// The API fails the first call with a <c>503</c>. With Microsoft.Extensions.Http.Resilience referenced and <c>Resilience</c> set to
    /// <c>true</c>, the standard resilience handler retries it and the call succeeds. Set to <c>false</c>, or without the package (the
    /// registration compiles and ignores the key), the call fails on the first request.
    /// </summary>
    [Test]
    [Arguments(Generator.Kiota, true, "true")]
    [Arguments(Generator.Kiota, true, "false")]
    [Arguments(Generator.Kiota, false, "true")]
    [Arguments(Generator.Refitter, true, "true")]
    [Arguments(Generator.Refitter, true, "false")]
    [Arguments(Generator.Refitter, false, "true")]
    public async Task Should_Retry_A_Transient_Failure_Only_With_Resilience(Generator generator, bool referencesResilience, string resilience)
    {
        await using var api = RocketsApi.Start();
        api.TransientFailures = 1;
        using var project = new SdkTestProject(targetFramework);
        var app = project.CreateConsumer().Hosting(targetFramework).OpenApiClient(generator, "rockets.json");
        if (referencesResilience)
        {
            app.ItemPackageReference(ResiliencePackage, Config.CentralPackageVersion(ResiliencePackage, targetFramework));
        }

        project
           .AddProject("app/app.csproj", app)
           .AddFile("app/rockets.json", RocketsApi.Document)
           .AddFile("app/Program.cs", RocketsApp.ProgramReportingFailure(generator));

        await project.Build("app");
        var output = await project.Run(
            "app",
            new Dictionary<string, string>(RocketsApp.Environment(api)) { ["Clients__RocketsClient__Resilience"] = resilience }
        );

        await project.VerifyResult(
            new { Output = output, api.RocketRequests },
            $"{generator}_{( referencesResilience ? "Referenced" : "NotReferenced" )}_{resilience}"
        );
    }

    private const string ResiliencePackage = "Microsoft.Extensions.Http.Resilience";
}
