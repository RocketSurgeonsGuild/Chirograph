using System.Runtime.InteropServices;
using Chirograph.Tests.Scaffolding;

namespace Chirograph.Tests;

/// <summary>
/// Build seam, a smoke test: an application with a client publishes with <c>PublishAot</c> and runs. The trimming and AOT warnings the
/// publish reports (IL codes) are recorded: none for a client that authenticates with a method the options class handles itself
/// (<c>ApiKey</c>), and, for one that declares <c>ClientCredentials</c>, those of the packages Duende.AccessTokenManagement brings (the
/// default JSON serializer of Microsoft.Extensions.Caching.Hybrid, where its token cache lives). Both get their rocket.
/// </summary>
[TargetFrameworks]
public class NativeAotTests(string targetFramework) : LoggerTest<TestRecord>(TestRecord.Create())
{
    [Test]
    [Arguments("ApiKey")]
    [Arguments("ClientCredentials")]
    public async Task Should_Publish_A_Native_Application_With_A_Client(string authentication)
    {
        await using var tokens = await TokenServer.Start();
        await using var api = RocketsApi.Start();
        var clientCredentials = authentication == "ClientCredentials";
        if (clientCredentials)
        {
            api.BearerTokens = tokens.IsValid;
        }

        using var project = new SdkTestProject(targetFramework);
        project
           .AddProject(
                "app/app.csproj",
                project
                   .CreateConsumer()
                   .Property("PublishAot", "true")
                   .Hosting(targetFramework)
                   .OpenApiClient(
                        Generator.Kiota,
                        "rockets.json",
                        metadata: new Dictionary<string, string?> { ["Authentication"] = clientCredentials ? "ClientCredentials" : null }
                    )
            )
           .AddFile("app/rockets.json", RocketsApi.Document)
           .AddFile("app/Program.cs", ClientCredentialsTests.Program(Generator.Kiota));

        await project.Dotnet("app", $"publish -bl -r {RuntimeInformation.RuntimeIdentifier} -o publish");
        var warnings = project.Diagnostics(
            Microsoft.Build.Logging.StructuredLogger.BinaryLog.ReadBuild(Path.Combine(project.Directory, "app", "msbuild.binlog")),
            "IL"
        );
        var environment = new Dictionary<string, string>(tokens.Environment())
        {
            ["Clients__RocketsClient__BaseAddress"] = api.BaseAddress.ToString(),
            ["Clients__RocketsClient__Authentication"] = authentication,
        };
        if (clientCredentials)
        {
            environment["Clients__RocketsClient__TokenEndpoint"] = tokens.TokenEndpoint.ToString();
            environment["Clients__RocketsClient__ClientId"] = TokenServer.ClientId;
            environment["Clients__RocketsClient__ClientSecret"] = TokenServer.ClientSecret;
        }
        else
        {
            environment["Clients__RocketsClient__ApiKey"] = "abc123";
        }

        var output = await project.Execute(Path.Combine("app", "publish"), OperatingSystem.IsWindows() ? "app.exe" : "app", environment);

        await project.VerifyResult(new { Warnings = warnings.Order(StringComparer.Ordinal).ToArray(), Output = output.Trim() }, authentication);
    }
}
