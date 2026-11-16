using Chirograph.Tests.Scaffolding;

namespace Chirograph.Tests;

/// <summary>
/// Build seam: the four Azure methods (<c>AzureDefault</c>, <c>AzureManagedIdentity</c>, <c>AzureWorkloadIdentity</c> and
/// <c>AzureServicePrincipal</c>), declared by <c>Azure</c> in the <c>OpenApiClient</c> item's <c>Authentication</c> metadata, ask the
/// client's Azure.Identity credential for a token for <c>Scope</c> and send it to <see cref="RocketsApi" />, which echoes it after the
/// rocket's serial number. No live Azure: the application registers a fake <c>TokenCredential</c>, keyed with the client's name, in place
/// of the credential the method would build, and it writes the scopes it is asked for. Each test builds one throwaway application, then
/// runs it once per scenario with its configuration in environment variables, and records what it wrote. The application asks for a rocket
/// <c>TEST_CALLS</c> times. The secret <c>s3cr3t</c> stands in for every value that a failure must not repeat.
/// </summary>
[TargetFrameworks]
public class AzureTests(string targetFramework) : LoggerTest<TestRecord>(TestRecord.Create())
{
    /// <summary>
    /// Each method asks the credential for its <c>Scope</c> (space-separated scopes) on each call, relying on the credential's own cache,
    /// and sends the token as <c>Authorization: Bearer</c>. A connection string sets the keys too.
    /// </summary>
    [Test]
    [Arguments(Generator.Kiota)]
    [Arguments(Generator.Refitter)]
    public async Task Should_Authenticate_With_A_Token_From_The_Credential(Generator generator)
    {
        await using var api = RocketsApi.Start();
        using var project = new SdkTestProject(targetFramework);
        var results = await RunScenarios(
            project,
            generator,
            new()
            {
                ["AzureDefault"] = Keys(api, "AzureDefault", 2, ("Scope", "api://rockets/.default")),
                ["AzureDefault, TenantId and ClientId"] = Keys(
                    api,
                    "AzureDefault",
                    1,
                    ("Scope", "api://rockets/.default"),
                    ("TenantId", "contoso.onmicrosoft.com"),
                    ("ClientId", "00000000-0000-0000-0000-000000000001")
                ),
                ["AzureManagedIdentity, user-assigned"] = Keys(
                    api,
                    "AzureManagedIdentity",
                    1,
                    ("Scope", "api://rockets/.default"),
                    ("ClientId", "00000000-0000-0000-0000-000000000001")
                ),
                ["AzureWorkloadIdentity"] = Keys(
                    api,
                    "AzureWorkloadIdentity",
                    1,
                    ("Scope", "api://rockets/read api://rockets/launch"),
                    ("TenantId", "00000000-0000-0000-0000-000000000002")
                ),
                ["AzureServicePrincipal, secret"] = Keys(
                    api,
                    "AzureServicePrincipal",
                    1,
                    ("Scope", "api://rockets/.default"),
                    ("TenantId", "00000000-0000-0000-0000-000000000002"),
                    ("ClientId", "00000000-0000-0000-0000-000000000001"),
                    ("ClientSecret", "s3cr3t")
                ),
                ["Connection string"] = new()
                {
                    ["TEST_CALLS"] = "1",
                    ["ConnectionStrings__RocketsClient"] = $"Endpoint={api.BaseAddress};Authentication=AzureManagedIdentity;Scope=api://rockets/.default",
                },
            }
        );

        await project.VerifyResult(results, generator.ToString());
    }

    /// <summary>
    /// Each method needs <c>Scope</c>; <c>AzureServicePrincipal</c> needs <c>TenantId</c>, <c>ClientId</c>, and <c>ClientSecret</c> or a
    /// certificate (the <c>Certificate*</c> keys, which then sign and are not presented for mutual TLS), not both. A <c>TenantId</c> is a
    /// tenant id or domain, and the keys of a method are those of no other. No failure repeats a value.
    /// </summary>
    [Test]
    [Arguments(Generator.Kiota)]
    [Arguments(Generator.Refitter)]
    public async Task Should_Validate_The_Azure_Keys(Generator generator)
    {
        await using var api = RocketsApi.Start();
        using var project = new SdkTestProject(targetFramework);
        var results = await RunScenarios(
            project,
            generator,
            new()
            {
                ["AzureDefault without a scope"] = Keys(api, "AzureDefault", 1),
                ["AzureManagedIdentity with a TenantId"] = Keys(api, "AzureManagedIdentity", 1, ("Scope", "api://rockets/.default"), ("TenantId", "s3cr3t")),
                ["AzureWorkloadIdentity with a ClientSecret"] = Keys(api, "AzureWorkloadIdentity", 1, ("Scope", "api://rockets/.default"), ("ClientSecret", "s3cr3t")),
                ["AzureServicePrincipal without its keys"] = Keys(api, "AzureServicePrincipal", 1, ("Scope", "api://rockets/.default")),
                ["AzureServicePrincipal with a secret and a certificate"] = Keys(
                    api,
                    "AzureServicePrincipal",
                    1,
                    ("Scope", "api://rockets/.default"),
                    ("TenantId", "00000000-0000-0000-0000-000000000002"),
                    ("ClientId", "00000000-0000-0000-0000-000000000001"),
                    ("ClientSecret", "s3cr3t"),
                    ("CertificateThumbprint", "s3cr3t")
                ),
                ["AzureServicePrincipal with a certificate that doesn't exist"] = Keys(
                    api,
                    "AzureServicePrincipal",
                    1,
                    ("Scope", "api://rockets/.default"),
                    ("TenantId", "00000000-0000-0000-0000-000000000002"),
                    ("ClientId", "00000000-0000-0000-0000-000000000001"),
                    ("CertificatePath", "s3cr3t.pfx")
                ),
                ["A TenantId that isn't one"] = Keys(api, "AzureDefault", 1, ("Scope", "api://rockets/.default"), ("TenantId", "s3cr3t/s3cr3t")),
                ["Bearer with the keys of Azure"] = Keys(api, "Bearer", 1, ("Token", "s3cr3t"), ("Scope", "s3cr3t"), ("TenantId", "s3cr3t")),
            }
        );

        await project.VerifyResult(results, generator.ToString());
    }

    // the keys under Clients:RocketsClient for the method, with the BaseAddress of the API and the number of calls
    private static Dictionary<string, string> Keys(RocketsApi api, string method, int calls, params (string Key, string Value)[] keys)
    {
        var environment = new Dictionary<string, string>
        {
            ["TEST_CALLS"] = calls.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Clients__RocketsClient__BaseAddress"] = api.BaseAddress.ToString(),
            ["Clients__RocketsClient__Authentication"] = method,
        };
        foreach (var (key, value) in keys)
        {
            environment[$"Clients__RocketsClient__{key}"] = value;
        }

        return environment;
    }

    // Builds the application once, then runs it without building for each scenario.
    private async Task<Dictionary<string, string>> RunScenarios(SdkTestProject project, Generator generator, Dictionary<string, Dictionary<string, string>> scenarios)
    {
        project
           .AddProject(
                "app/app.csproj",
                project
                   .CreateConsumer()
                   .Hosting(targetFramework)
                   .OpenApiClient(generator, "rockets.json", metadata: new Dictionary<string, string?> { ["Authentication"] = "Azure" })
            )
           .AddFile("app/rockets.json", RocketsApi.Document)
           .AddFile("app/Program.cs", Program(generator));
        await project.Build("app");

        var results = new Dictionary<string, string>();
        foreach (var (name, environment) in scenarios)
        {
            results[name] = ( await project.Dotnet("app", "run --no-build", environment) ).Trim();
        }

        return results;
    }

    // registers a fake credential for the client, starts the host (so validation runs), then asks for TEST_CALLS rockets and writes each,
    // or why it couldn't get one
    internal static string Program(Generator generator)
    {
        var (clientType, getRocket) = generator switch
        {
            Generator.Kiota => ("RocketsClient", "client.Rockets[id].GetAsync()"),
            Generator.Refitter => ("IRocketsClient", "client.GetRocket(id)"),
            _ => throw new ArgumentOutOfRangeException(nameof(generator), generator, null),
        };
        return $$"""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Azure.Core;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Hosting;
            using Microsoft.Extensions.Logging;
            using Throwaway.Clients;

            var builder = Host.CreateApplicationBuilder(args);
            builder.Logging.ClearProviders();
            builder.Services.AddKeyedSingleton<TokenCredential>("RocketsClient", new FakeCredential());
            builder.Services.AddRocketsClient();
            using var host = builder.Build();
            try
            {
                await host.StartAsync();
                var client = host.Services.GetRequiredService<{{clientType}}>();
                var id = Guid.Parse("{{RocketsApi.RocketId}}");
                var calls = int.Parse(Environment.GetEnvironmentVariable("TEST_CALLS") ?? "1");
                for (var call = 1; call <= calls; call++)
                {
                    var rocket = await {{getRocket}};
                    Console.WriteLine($"{nameof({{clientType}})}: {rocket!.Id} {rocket.SerialNumber}");
                }
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

            // stands in for the Azure.Identity credential: writes the scopes it is asked for, and gives a token for them
            sealed class FakeCredential : TokenCredential
            {
                private int _tokens;

                public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
                    GetTokenAsync(requestContext, cancellationToken).AsTask().GetAwaiter().GetResult();

                public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
                {
                    var token = Interlocked.Increment(ref _tokens);
                    Console.WriteLine($"credential: a token for {string.Join(" ", requestContext.Scopes)}");
                    return new(new AccessToken($"azure-token-{token}", DateTimeOffset.UtcNow.AddHours(1)));
                }
            }
            """;
    }
}
