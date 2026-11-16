using Chirograph.Tests.Scaffolding;

namespace Chirograph.Tests;

/// <summary>
/// Build seam: <c>Authentication=ClientCredentials</c>, declared by the <c>OpenApiClient</c> item's <c>Authentication</c> metadata, gets a
/// token from <see cref="TokenServer" /> through Duende.AccessTokenManagement and sends it to <see cref="RocketsApi" />, which accepts only
/// the tokens the server issued, and echoes the one it got after the rocket's serial number. Each test builds one throwaway application,
/// then runs it once per scenario with its configuration in environment variables, and records what it wrote and what the token server
/// was asked for during the run (<c>{token-server}</c> is its address). The application asks for a rocket <c>TEST_CALLS</c> times, waiting
/// <c>TEST_DELAY</c> milliseconds between two. The secret <c>s3cr3t</c> stands in for every value that a failure must not repeat.
/// </summary>
[TargetFrameworks]
public class ClientCredentialsTests(string targetFramework) : LoggerTest<TestRecord>(TestRecord.Create())
{
    /// <summary>
    /// One token serves several calls, and a new one is fetched once it is about to expire. The client credentials go in the
    /// <c>Authorization</c> header or in the body, with the scope and resource. With <c>Authority</c>, the discovery document is read once,
    /// on the first token request: a run that makes no call reads nothing. A connection string sets the keys too, and a token request the
    /// server refuses fails the call.
    /// </summary>
    [Test]
    [Arguments(Generator.Kiota)]
    [Arguments(Generator.Refitter)]
    public async Task Should_Authenticate_With_A_Token_From_The_Token_Server(Generator generator)
    {
        await using var tokens = await TokenServer.Start();
        await using var api = RocketsApi.Start();
        api.BearerTokens = tokens.IsValid;
        using var project = new SdkTestProject(targetFramework);
        var results = await RunScenarios(
            project,
            generator,
            tokens,
            new()
            {
                ["TokenEndpoint, Header"] = Keys(api, tokens, 3, ("TokenEndpoint", tokens.TokenEndpoint.ToString())),
                ["TokenEndpoint, Body, Scope and Resource"] = Keys(
                    api,
                    tokens,
                    2,
                    ("TokenEndpoint", tokens.TokenEndpoint.ToString()),
                    ("ClientCredentialStyle", "Body"),
                    ("Scope", "rockets.read rockets.launch"),
                    ("Resource", "urn:rockets")
                ),
                ["Authority, no call"] = Keys(api, tokens, 0, ("Authority", tokens.BaseAddress.ToString())),
                ["Authority"] = Keys(api, tokens, 3, ("Authority", tokens.BaseAddress.ToString())),
                ["A token about to expire"] = With(
                    Keys(api, tokens, 3, ("TokenEndpoint", tokens.TokenEndpoint.ToString()), ("ClientId", TokenServer.ShortLivedClientId)),
                    new() { ["TEST_DELAY"] = "2500" }
                ),
                ["Connection string"] = With(
                    tokens.Environment(),
                    new()
                    {
                        ["TEST_CALLS"] = "2",
                        ["ConnectionStrings__RocketsClient"] =
                            $"Endpoint={api.BaseAddress};Authentication=ClientCredentials;TokenEndpoint={tokens.TokenEndpoint};ClientId={TokenServer.ClientId};ClientSecret={TokenServer.ClientSecret}",
                    }
                ),
                ["A secret the server refuses"] = Keys(api, tokens, 1, ("TokenEndpoint", tokens.TokenEndpoint.ToString()), ("ClientSecret", "wrong")),
            }
        );

        await project.VerifyResult(results, generator.ToString());
    }

    /// <summary>
    /// FusionCache, registered as the <c>HybridCache</c> keyed <c>ServiceProviderKeys.ClientCredentialsTokenCache</c>, holds the token: the
    /// calls share one token, and once the application clears that FusionCache, the next call gets a new one.
    /// </summary>
    [Test]
    [Arguments(Generator.Kiota)]
    [Arguments(Generator.Refitter)]
    public async Task Should_Keep_The_Token_In_The_Keyed_Token_Cache(Generator generator)
    {
        await using var tokens = await TokenServer.Start();
        await using var api = RocketsApi.Start();
        api.BearerTokens = tokens.IsValid;
        using var project = new SdkTestProject(targetFramework);
        var results = await RunScenarios(
            project,
            generator,
            tokens,
            new() { ["FusionCache"] = Keys(api, tokens, 3, ("TokenEndpoint", tokens.TokenEndpoint.ToString())) },
            fusionCache: true
        );

        await project.VerifyResult(results, generator.ToString());
    }

    /// <summary>
    /// <c>ClientCredentials</c> needs <c>ClientId</c>, <c>ClientSecret</c>, and <c>TokenEndpoint</c> or <c>Authority</c> (not both), an
    /// <c>Authority</c> must be https (startup reads no discovery document), and its keys are those of no other method. No failure repeats
    /// a value.
    /// </summary>
    [Test]
    [Arguments(Generator.Kiota)]
    [Arguments(Generator.Refitter)]
    public async Task Should_Validate_The_Client_Credentials_Keys(Generator generator)
    {
        await using var tokens = await TokenServer.Start();
        await using var api = RocketsApi.Start();
        using var project = new SdkTestProject(targetFramework);
        var results = await RunScenarios(
            project,
            generator,
            tokens,
            new()
            {
                ["Without a client id or secret"] = Keys(api, tokens, 1, ("TokenEndpoint", "https://s3cr3t.example/token"), ("ClientId", ""), ("ClientSecret", "")),
                ["Without a token endpoint"] = Keys(api, tokens, 1),
                ["TokenEndpoint and Authority"] = Keys(api, tokens, 1, ("TokenEndpoint", "https://s3cr3t.example/token"), ("Authority", "https://s3cr3t.example/")),
                ["An http Authority"] = Keys(api, tokens, 1, ("Authority", "http://s3cr3t.example/")),
                ["A relative TokenEndpoint"] = Keys(api, tokens, 1, ("TokenEndpoint", "s3cr3t/token")),
                ["An unknown ClientCredentialStyle"] = Keys(api, tokens, 1, ("TokenEndpoint", "https://s3cr3t.example/token"), ("ClientCredentialStyle", "s3cr3t")),
                ["Bearer with the keys of ClientCredentials"] = new()
                {
                    ["Clients__RocketsClient__BaseAddress"] = api.BaseAddress.ToString(),
                    ["Clients__RocketsClient__Authentication"] = "Bearer",
                    ["Clients__RocketsClient__Token"] = "s3cr3t",
                    ["Clients__RocketsClient__ClientId"] = "s3cr3t",
                    ["Clients__RocketsClient__Scope"] = "s3cr3t",
                },
            }
        );

        await project.VerifyResult(results, generator.ToString());
    }

    // the keys under Clients:RocketsClient for ClientCredentials, with the BaseAddress of the API, the client credentials (which the
    // keys can replace, or remove with an empty value), the number of calls, and the trust of the token server's certificate
    private static Dictionary<string, string> Keys(RocketsApi api, TokenServer tokens, int calls, params (string Key, string Value)[] keys)
    {
        var environment = new Dictionary<string, string>(tokens.Environment())
        {
            ["TEST_CALLS"] = calls.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Clients__RocketsClient__BaseAddress"] = api.BaseAddress.ToString(),
            ["Clients__RocketsClient__Authentication"] = "ClientCredentials",
            ["Clients__RocketsClient__ClientId"] = TokenServer.ClientId,
            ["Clients__RocketsClient__ClientSecret"] = TokenServer.ClientSecret,
        };
        foreach (var (key, value) in keys)
        {
            environment[$"Clients__RocketsClient__{key}"] = value;
        }

        return environment;
    }

    private static Dictionary<string, string> With(Dictionary<string, string> first, Dictionary<string, string> second) =>
        [with(first.Concat(second).GroupBy(z => z.Key).Select(z => z.Last()))];

    // Builds the application once, then runs it without building for each scenario: what it wrote, then what the token server was asked.
    private async Task<Dictionary<string, string>> RunScenarios(
        SdkTestProject project,
        Generator generator,
        TokenServer tokens,
        Dictionary<string, Dictionary<string, string>> scenarios,
        bool fusionCache = false
    )
    {
        var app = project
                 .CreateConsumer()
                 .Hosting(targetFramework)
                 .OpenApiClient(generator, "rockets.json", metadata: new Dictionary<string, string?> { ["Authentication"] = "ClientCredentials" });
        if (fusionCache)
        {
            app.ItemPackageReference("ZiggyCreatures.FusionCache", Config.CentralPackageVersion("ZiggyCreatures.FusionCache", targetFramework));
        }

        project
           .AddProject("app/app.csproj", app)
           .AddFile("app/rockets.json", RocketsApi.Document)
           .AddFile("app/Program.cs", Program(generator, fusionCache));
        await project.Build("app");

        var results = new Dictionary<string, string>();
        foreach (var (name, environment) in scenarios)
        {
            var asked = tokens.Requests.Length;
            var output = await project.Dotnet("app", "run --no-build", environment);
            var lines = new List<string> { output.Trim() };
            foreach (var request in tokens.Requests[asked..])
            {
                lines.Add($"token server: {request}");
            }

            results[name] = string.Join('\n', lines).Replace(tokens.BaseAddress.ToString(), "{token-server}/", StringComparison.Ordinal);
        }

        return results;
    }

    // starts the host (so validation runs), then asks for TEST_CALLS rockets and writes each, or why it couldn't get one
    private static string Program(Generator generator, bool fusionCache)
    {
        var (clientType, getRocket) = generator switch
        {
            Generator.Kiota => ("RocketsClient", "client.Rockets[id].GetAsync()"),
            Generator.Refitter => throw new NotImplementedException(),
            _ => ("IRocketsClient", "client.GetRocket(id)"),
        };
        // FusionCache as the token cache, cleared after the calls, before one more
        var addFusionCache = fusionCache
            ? "ZiggyCreatures.Caching.Fusion.FusionCacheBuilderExtMethods.AsKeyedHybridCache(builder.Services.AddFusionCache(), Duende.AccessTokenManagement.ServiceProviderKeys.ClientCredentialsTokenCache);"
            : "";
        var countFusionCache = fusionCache
            ? "var fusion = host.Services.GetRequiredService<ZiggyCreatures.Caching.Fusion.IFusionCache>();"
            : "";
        var clearFusionCache = fusionCache
            ? $"fusion.Clear(false);\nConsole.WriteLine(\"FusionCache cleared\");\nvar again = await {getRocket};\nConsole.WriteLine($\"{{nameof({clientType})}}: {{again!.Id}} {{again.SerialNumber}}\");"
            : "";
        return $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Hosting;
            using Microsoft.Extensions.Logging;
            using Throwaway.Clients;

            var builder = Host.CreateApplicationBuilder(args);
            builder.Logging.ClearProviders();
            // every HttpClient trusts the token server's certificate, Duende's back-channel client among them
            builder.Services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler({{RocketsTlsApi.TrustServerCertificate}}));
            builder.Services.AddRocketsClient();
            {{addFusionCache}}
            using var host = builder.Build();
            {{countFusionCache}}
            try
            {
                await host.StartAsync();
                var client = host.Services.GetRequiredService<{{clientType}}>();
                var id = Guid.Parse("{{RocketsApi.RocketId}}");
                var calls = int.Parse(Environment.GetEnvironmentVariable("TEST_CALLS") ?? "1");
                var delay = int.Parse(Environment.GetEnvironmentVariable("TEST_DELAY") ?? "0");
                for (var call = 1; call <= calls; call++)
                {
                    if (call > 1)
                    {
                        await System.Threading.Tasks.Task.Delay(delay);
                    }

                    var rocket = await {{getRocket}};
                    Console.WriteLine($"{nameof({{clientType}})}: {rocket!.Id} {rocket.SerialNumber}");
                }

                {{clearFusionCache}}
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
}
