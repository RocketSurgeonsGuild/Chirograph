using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chirograph.Tests.Scaffolding;

namespace Chirograph.Tests;

/// <summary>
/// Build seam: when the project references Microsoft.Extensions.ServiceDiscovery, <c>Add&lt;ClientName&gt;()</c> adds service discovery
/// to the client, its <c>BaseAddress</c> may be a service-discovery address such as <c>https+http://rockets</c>, and a client without one
/// calls <c>https+http://&lt;ClientName&gt;</c>. Each test builds one throwaway application, then runs it once per scenario with its
/// configuration in environment variables (the endpoints under <c>Services</c>), and records the rocket it got, or why it couldn't.
/// </summary>
[TargetFrameworks]
public class ServiceDiscoveryTests(string targetFramework) : LoggerTest<TestRecord>(TestRecord.Create())
{
    /// <summary>
    /// With the package, a logical address resolves through <c>Services:&lt;name&gt;</c>, from the keys or a connection string, and a
    /// client without a <c>BaseAddress</c> resolves <c>https+http://RocketsClient</c>. The credentials reach the discovered endpoint, since
    /// the host check sees the logical host, and a redirect to another host goes without them.
    /// </summary>
    [Test]
    [Arguments(Generator.Kiota)]
    [Arguments(Generator.Refitter)]
    public async Task Should_Resolve_The_BaseAddress_Through_Service_Discovery(Generator generator)
    {
        await using var api = RocketsApi.Start();
        await using var redirecting = RocketsApi.Start();
        await using var other = RocketsApi.Start();
        redirecting.RedirectRocketsTo = other.BaseAddress;
        using var project = new SdkTestProject(targetFramework);
        var results = await RunScenarios(
            project,
            generator,
            referencesServiceDiscovery: true,
            new()
            {
                ["Logical address"] = Keys(("BaseAddress", "https+http://rockets"), Services("rockets", api)),
                ["Default address"] = Keys(Services("RocketsClient", api)),
                ["Connection string"] = Keys(Services("rockets", api), ("ConnectionStrings__RocketsClient", "Endpoint=https+http://rockets")),
                ["ApiKey"] = Keys(("BaseAddress", "https+http://rockets"), ("Authentication", "ApiKey"), ("ApiKey", "abc123"), Services("rockets", api)),
                ["ApiKey, default address"] = Keys(("Authentication", "ApiKey"), ("ApiKey", "abc123"), Services("RocketsClient", api)),
                ["ApiKey, redirect to another host"] = Keys(
                    ("BaseAddress", "https+http://rockets"),
                    ("Authentication", "ApiKey"),
                    ("ApiKey", "abc123"),
                    Services("rockets", redirecting)
                ),
            }
        );

        // the other API answers the redirected request only
        await project.VerifyResult(new { Results = results, OtherRocketRequests = other.RocketRequests }, generator.ToString());
    }

    /// <summary>
    /// A relative redirect from a discovered endpoint is followed through service discovery again: with an Authentication, the redirected
    /// request is for the logical host, so it keeps the credentials, as a client without one keeps following it.
    /// </summary>
    [Test]
    [Arguments(Generator.Kiota)]
    [Arguments(Generator.Refitter)]
    public async Task Should_Follow_A_Relative_Redirect_Through_Service_Discovery(Generator generator)
    {
        await using var api = RocketsApi.Start();
        using var project = new SdkTestProject(targetFramework);
        var results = await RunScenarios(
            project,
            generator,
            referencesServiceDiscovery: true,
            new()
            {
                ["No authentication"] = Keys(("BaseAddress", "https+http://rockets"), Services("rockets", api)),
                ["ApiKey"] = Keys(("BaseAddress", "https+http://rockets"), ("Authentication", "ApiKey"), ("ApiKey", "abc123"), Services("rockets", api)),
                ["ApiKey, default address"] = Keys(("Authentication", "ApiKey"), ("ApiKey", "abc123"), Services("RocketsClient", api)),
            },
            // the first request for a rocket of each scenario is redirected
            () => api.RelativeRedirects = api.RocketRequests + 1
        );

        await project.VerifyResult(new { Results = results, api.RocketRequests }, generator.ToString());
    }

    /// <summary>
    /// Without the package, a logical address fails validation, and a client without a <c>BaseAddress</c> behaves as before: Kiota calls
    /// the first server of its document, Refit fails when called.
    /// </summary>
    [Test]
    [Arguments(Generator.Kiota)]
    [Arguments(Generator.Refitter)]
    public async Task Should_Reject_A_Logical_Address_Without_The_Package(Generator generator)
    {
        await using var api = RocketsApi.Start();
        using var project = new SdkTestProject(targetFramework);
        var results = await RunScenarios(
            project,
            generator,
            referencesServiceDiscovery: false,
            new()
            {
                ["Logical address"] = Keys(("BaseAddress", "https+http://rockets"), Services("rockets", api)),
                ["Connection string"] = Keys(Services("rockets", api), ("ConnectionStrings__RocketsClient", "Endpoint=https+http://rockets")),
                ["No address"] = Keys(Services("RocketsClient", api)),
            }
        );

        await project.VerifyResult(results, generator.ToString());
    }

    /// <summary>
    /// Build seam: the client's schema describes <c>BaseAddress</c> as a service-discovery address, with its default, when the project
    /// references the package, and as an absolute URI otherwise. The result is the <c>BaseAddress</c> of each client's schema.
    /// </summary>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Should_Describe_The_BaseAddress_In_The_Schema(bool referencesServiceDiscovery)
    {
        using var project = new SdkTestProject(targetFramework);
        var app = project
                 .CreateConsumer("Library")
                 .OpenApiClient(Generator.Kiota, "rockets.json", "KiotaClient")
                 .OpenApiClient(Generator.Refitter, "rockets.json", "RefitterClient");
        if (referencesServiceDiscovery)
        {
            app.ItemPackageReference(ServiceDiscoveryPackage, Config.CentralPackageVersion(ServiceDiscoveryPackage, targetFramework));
        }

        project.AddProject("app/app.csproj", app).AddFile("app/rockets.json", RocketsApi.Document);
        await project.Build("app");

        var clients = new JsonObject(
            ( (string[])["KiotaClient", "RefitterClient"] ).Select(
                name => KeyValuePair.Create(
                    name,
                    JsonNode.Parse(File.ReadAllText(Path.Combine(project.Directory, "app", "obj", "Debug", targetFramework, "openapi", name, $"{name}.schema.json")))!
                           ["properties"]!["BaseAddress"]!.DeepClone()
                )
            )
        );

        await project.VerifyResult(
            clients.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
            referencesServiceDiscovery ? "Referenced" : "NotReferenced"
        );
    }

    private const string ServiceDiscoveryPackage = "Microsoft.Extensions.ServiceDiscovery";

    // the endpoint of a service, under Services:<name>:http
    private static (string Key, string Value) Services(string name, RocketsApi api) => ($"Services__{name}__http__0", api.BaseAddress.ToString());

    // the keys under Clients:RocketsClient, but for those under Services and ConnectionStrings
    private static Dictionary<string, string> Keys(params (string Key, string Value)[] keys) =>
        [with(
            keys.Select(
                z => KeyValuePair.Create(
                    z.Key.StartsWith("Services__", StringComparison.Ordinal) || z.Key.StartsWith("ConnectionStrings__", StringComparison.Ordinal)
                        ? z.Key
                        : $"Clients__RocketsClient__{z.Key}",
                    z.Value
                )
            )
        )];

    // Builds the application once, then runs it without building for each scenario.
    private async Task<Dictionary<string, string>> RunScenarios(
        SdkTestProject project,
        Generator generator,
        bool referencesServiceDiscovery,
        Dictionary<string, Dictionary<string, string>> scenarios,
        Action? beforeEachScenario = null
    )
    {
        var app = project.CreateConsumer().Hosting(targetFramework).OpenApiClient(generator, "rockets.json");
        if (referencesServiceDiscovery)
        {
            app.ItemPackageReference(ServiceDiscoveryPackage, Config.CentralPackageVersion(ServiceDiscoveryPackage, targetFramework));
        }

        project.AddProject("app/app.csproj", app).AddFile("app/rockets.json", RocketsApi.Document).AddFile("app/Program.cs", Program(generator));
        await project.Build("app");

        var results = new Dictionary<string, string>();
        foreach (var (name, environment) in scenarios)
        {
            beforeEachScenario?.Invoke();
            var output = await project.Dotnet("app", "run --no-build", environment);
            results[name] = output.Trim();
        }

        return results;
    }

    // asks for a rocket and writes it, or why it couldn't (the validation failures, say)
    private static string Program(Generator generator) =>
        RocketsApp
           .Program(generator)
           .Replace("var client =", "try\n{\n    var client =", StringComparison.Ordinal)
           .TrimEnd()
      + """

        }
        catch (Microsoft.Extensions.Options.OptionsValidationException e)
        {
            Console.WriteLine($"{e.GetType().Name}:");
            foreach (var failure in e.Failures)
            {
                Console.WriteLine($"  {failure}");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"{e.GetType().FullName}: {e.Message}");
        }
        """;
}
