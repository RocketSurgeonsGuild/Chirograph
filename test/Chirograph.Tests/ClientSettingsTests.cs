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

    /// <summary>
    /// <c>Resilience</c> as an object sets the standard resilience handler's settings, and keeps the handler unless its <c>Enabled</c> is
    /// <c>false</c>. The API fails the first two calls with a <c>503</c>: one retry fails the call after two requests, two retries get it
    /// through on the third, and <c>Enabled</c> <c>false</c> fails it on the first. A setting that doesn't parse, or that the handler
    /// rejects, fails the call before any request. A connection string takes the same settings as <c>Resilience:&lt;setting&gt;</c> keys,
    /// over the keys of the section; an unknown setting, a setting given twice and an object given a value are errors there. The result
    /// is each scenario's output and the requests the API got.
    /// </summary>
    [Test]
    [Arguments(Generator.Kiota)]
    [Arguments(Generator.Refitter)]
    public async Task Should_Apply_The_Resilience_Settings(Generator generator)
    {
        using var project = new SdkTestProject(targetFramework);
        var app = project
                 .CreateConsumer()
                 .Hosting(targetFramework)
                 .OpenApiClient(generator, "rockets.json")
                 .ItemPackageReference(ResiliencePackage, Config.CentralPackageVersion(ResiliencePackage, targetFramework));
        project
           .AddProject("app/app.csproj", app)
           .AddFile("app/rockets.json", RocketsApi.Document)
           .AddFile("app/Program.cs", RocketsApp.ProgramReportingFailure(generator));

        await project.Build("app");

        // a short constant delay, so the retries don't wait the default seconds
        var quickRetries = new Dictionary<string, string>
        {
            ["Clients__RocketsClient__Resilience__Retry__BackoffType"] = "Constant",
            ["Clients__RocketsClient__Resilience__Retry__Delay"] = "00:00:00.01",
        };
        var scenarios = new Dictionary<string, Dictionary<string, string>>
        {
            ["One retry"] = new(quickRetries) { ["Clients__RocketsClient__Resilience__Retry__MaxRetryAttempts"] = "1" },
            ["Two retries"] = new(quickRetries) { ["Clients__RocketsClient__Resilience__retry__maxretryattempts"] = "2" },
            ["Disabled"] = new(quickRetries)
            {
                ["Clients__RocketsClient__Resilience__Enabled"] = "false",
                ["Clients__RocketsClient__Resilience__Retry__MaxRetryAttempts"] = "2",
            },
            ["Settings that don't parse"] = new()
            {
                ["Clients__RocketsClient__Resilience__Enabled"] = "s3cr3t",
                ["Clients__RocketsClient__Resilience__Retry__MaxRetryAttempts"] = "s3cr3t",
                ["Clients__RocketsClient__Resilience__Retry__BackoffType"] = "s3cr3t",
                ["Clients__RocketsClient__Resilience__Retry__Delay__Seconds"] = "1",
                ["Clients__RocketsClient__Resilience__CircuitBreaker__FailureRatio"] = "s3cr3t",
                ["Clients__RocketsClient__Resilience__AttemptTimeout"] = "s3cr3t",
                ["Clients__RocketsClient__Resilience__RateLimiter__DefaultRateLimiterOptions__QueueProcessingOrder"] = "s3cr3t",
            },
            ["Settings the handler rejects"] = new() { ["Clients__RocketsClient__Resilience__Retry__MaxRetryAttempts"] = "0" },
            ["Two retries in a connection string"] = new()
            {
                ["ConnectionStrings__RocketsClient"] =
                    "Resilience:Retry:MaxRetryAttempts=2;resilience:retry:backofftype=Constant;Resilience:Retry:Delay=00:00:00.01",
            },
            ["A connection string over the keys"] = new(quickRetries)
            {
                ["Clients__RocketsClient__Resilience__Retry__MaxRetryAttempts"] = "2",
                ["ConnectionStrings__RocketsClient"] = "Resilience:Retry:MaxRetryAttempts=1",
            },
            ["Disabled in a connection string"] = new(quickRetries)
            {
                ["ConnectionStrings__RocketsClient"] = "Resilience:Retry:MaxRetryAttempts=2;Resilience:Enabled=false",
            },
            ["A connection string with bad settings"] = new()
            {
                ["ConnectionStrings__RocketsClient"] =
                    "Resilience:Retry:Retries=2;Resilience:Retry=s3cr3t;Resilience:Retry:Delay=s3cr3t;Resilience:Retry:Delay=00:00:01;Resilience=true;Resilience:Enabled=true",
            },
        };

        var results = new Dictionary<string, object>();
        foreach (var (name, environment) in scenarios)
        {
            await using var api = RocketsApi.Start();
            api.TransientFailures = 2;
            var variables = new Dictionary<string, string>(RocketsApp.Environment(api));
            foreach (var (key, value) in environment)
            {
                variables[key] = value;
            }

            var output = await project.Run("app", variables);
            results[name] = new { Output = output, api.RocketRequests };
        }

        await project.VerifyResult(results, generator.ToString());
    }

    private const string ResiliencePackage = "Microsoft.Extensions.Http.Resilience";
}
