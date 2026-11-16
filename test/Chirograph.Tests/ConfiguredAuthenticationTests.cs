using Chirograph.Tests.Scaffolding;

namespace Chirograph.Tests;

/// <summary>
/// Build seam: with <c>Authentication</c> set to <c>ApiKey</c>, <c>Bearer</c> or <c>Basic</c>, the generated authentication handler on the
/// client's <c>HttpClient</c> adds the credentials, and only to the <c>BaseAddress</c> host and the <c>AllowedHosts</c>. Each test builds
/// one throwaway application, then runs it once per scenario with its configuration in environment variables, and records the rocket it
/// got: <see cref="RocketsApi" /> echoes the credentials it saw after the serial number. The secret <c>s3cr3t</c> stands in for every
/// value that a failure must not repeat.
/// </summary>
[TargetFrameworks]
public class ConfiguredAuthenticationTests(string targetFramework) : LoggerTest<TestRecord>(TestRecord.Create())
{
    /// <summary>
    /// Each method reaches the API: <c>ApiKey</c> in <c>X-Api-Key</c>, in a header of its own and in the query; <c>Bearer</c> from
    /// <c>Token</c> and from <c>TokenProvider</c> in code; <c>Basic</c>. A connection string sets them as the keys do.
    /// </summary>
    [Test]
    [Arguments(Generator.Kiota)]
    [Arguments(Generator.Refitter)]
    public async Task Should_Send_The_Configured_Credentials(Generator generator)
    {
        await using var api = RocketsApi.Start();
        using var project = new SdkTestProject(targetFramework);
        var results = await RunScenarios(
            project,
            generator,
            new()
            {
                ["None"] = Keys(api),
                ["ApiKey"] = Keys(api, ("Authentication", "ApiKey"), ("ApiKey", "abc123")),
                ["ApiKey, alias Key"] = Keys(api, ("Authentication", "apikey"), ("Key", "abc123")),
                ["ApiKey, ApiKeyHeader"] = Keys(api, ("Authentication", "ApiKey"), ("ApiKey", "abc123"), ("ApiKeyHeader", RocketsApi.SerialNumberHeader)),
                ["ApiKey, ApiKeyQuery"] = Keys(api, ("Authentication", "ApiKey"), ("ApiKey", "abc 123&"), ("ApiKeyQuery", "api_key")),
                ["Bearer"] = Keys(api, ("Authentication", "Bearer"), ("Token", "t0k3n")),
                ["Bearer, TokenProvider"] = With(Keys(api, ("Authentication", "Bearer")), new() { [CodeToken] = "pr0v1d3d" }),
                ["Basic"] = Keys(api, ("Authentication", "Basic"), ("Username", "rocketeer"), ("Password", "p@ss:word")),
                ["Connection string"] = new()
                {
                    ["ConnectionStrings__RocketsClient"] = $"Endpoint={api.BaseAddress};Authentication=ApiKey;Key=abc123;ApiKeyQuery=api_key",
                },
            }
        );

        await project.VerifyResult(results, generator.ToString());
    }

    /// <summary>
    /// A redirect to another host (here, another port) goes without the credentials, whichever method sets them, and
    /// <c>AllowedHosts</c> lets them through: a host alone allows every port of it, <c>host:port</c> that port only.
    /// </summary>
    [Test]
    [Arguments(Generator.Kiota)]
    [Arguments(Generator.Refitter)]
    public async Task Should_Send_The_Credentials_Only_To_Allowed_Hosts(Generator generator)
    {
        await using var api = RocketsApi.Start();
        await using var other = RocketsApi.Start();
        api.RedirectRocketsTo = other.BaseAddress;
        using var project = new SdkTestProject(targetFramework);
        var results = await RunScenarios(
            project,
            generator,
            new()
            {
                ["ApiKey"] = Keys(api, ("Authentication", "ApiKey"), ("ApiKey", "abc123")),
                ["ApiKey, ApiKeyQuery"] = Keys(api, ("Authentication", "ApiKey"), ("ApiKey", "abc123"), ("ApiKeyQuery", "api_key")),
                ["Bearer"] = Keys(api, ("Authentication", "Bearer"), ("Token", "t0k3n")),
                ["Basic"] = Keys(api, ("Authentication", "Basic"), ("Username", "rocketeer"), ("Password", "secret")),
                ["ApiKey, AllowedHosts host"] = Keys(api, ("Authentication", "ApiKey"), ("ApiKey", "abc123"), ("AllowedHosts__0", other.BaseAddress.Host)),
                ["Bearer, AllowedHosts host:port"] = Keys(api, ("Authentication", "Bearer"), ("Token", "t0k3n"), ("AllowedHosts__0", other.BaseAddress.Authority)),
                ["Bearer, AllowedHosts another port"] = Keys(api, ("Authentication", "Bearer"), ("Token", "t0k3n"), ("AllowedHosts__0", $"{other.BaseAddress.Host}:1")),
            }
        );

        await project.VerifyResult(results.ToDictionary(z => z.Key, z => z.Value.Replace(other.BaseAddress.Authority, "{other}", StringComparison.Ordinal)), generator.ToString());
    }

    /// <summary>
    /// Each method needs its keys, and the keys of another method are an error, <c>None</c> included. A method the item's
    /// <c>Authentication</c> metadata doesn't declare (<c>ClientCredentials</c>) fails with the metadata to set, and its keys are unknown.
    /// No failure repeats a value.
    /// </summary>
    [Test]
    [Arguments(Generator.Kiota)]
    [Arguments(Generator.Refitter)]
    public async Task Should_Validate_The_Method_Keys(Generator generator)
    {
        await using var api = RocketsApi.Start();
        using var project = new SdkTestProject(targetFramework);
        var results = await RunScenarios(
            project,
            generator,
            new()
            {
                ["Authentication"] = Keys(api, ("Authentication", "s3cr3t")),
                ["ApiKey without a key"] = Keys(api, ("Authentication", "ApiKey")),
                ["ApiKey with ApiKeyHeader and ApiKeyQuery"] = Keys(api, ("Authentication", "ApiKey"), ("ApiKey", "s3cr3t"), ("ApiKeyHeader", "X-Key"), ("ApiKeyQuery", "key")),
                ["ApiKey and Key"] = Keys(api, ("Authentication", "ApiKey"), ("ApiKey", "s3cr3t"), ("Key", "s3cr3t")),
                ["Bearer without a token"] = Keys(api, ("Authentication", "Bearer")),
                ["Bearer with Token and TokenProvider"] = With(Keys(api, ("Authentication", "Bearer"), ("Token", "s3cr3t")), new() { [CodeToken] = "s3cr3t" }),
                ["Basic without a password"] = Keys(api, ("Authentication", "Basic"), ("Username", "rocketeer")),
                ["Basic with a colon in the username"] = Keys(api, ("Authentication", "Basic"), ("Username", "s3:cr3t"), ("Password", "s3cr3t")),
                ["Bearer with the keys of Basic and ApiKey"] = Keys(api, ("Authentication", "Bearer"), ("Token", "s3cr3t"), ("Username", "s3cr3t"), ("Password", "s3cr3t"), ("ApiKeyHeader", "X-Key")),
                ["None with a token"] = Keys(api, ("Token", "s3cr3t")),
                ["None with TokenProvider"] = With(Keys(api), new() { [CodeToken] = "s3cr3t" }),
                ["ClientCredentials, not declared"] = Keys(api, ("Authentication", "clientcredentials")),
                ["ClientId, not declared"] = Keys(api, ("ClientId", "s3cr3t")),
                ["Connection string with the keys of another method"] = new()
                {
                    ["ConnectionStrings__RocketsClient"] = $"Endpoint={api.BaseAddress};Authentication=Basic;Username=rocketeer;Password=s3cr3t;Token=s3cr3t",
                },
                ["No host to send the credentials to"] = new()
                {
                    ["Clients__RocketsClient__Authentication"] = "Bearer", ["Clients__RocketsClient__Token"] = "s3cr3t",
                },
            }
        );

        await project.VerifyResult(results, generator.ToString());
    }

    private const string CodeToken = "TEST_CODE_TOKEN";

    // the keys under Clients:RocketsClient, with the BaseAddress of the API
    private static Dictionary<string, string> Keys(RocketsApi api, params (string Key, string Value)[] keys) =>
        new(keys.Select(z => KeyValuePair.Create($"Clients__RocketsClient__{z.Key}", z.Value)))
        {
            ["Clients__RocketsClient__BaseAddress"] = api.BaseAddress.ToString(),
        };

    private static Dictionary<string, string> With(Dictionary<string, string> first, Dictionary<string, string> second) =>
        [with(first.Concat(second).GroupBy(z => z.Key).Select(z => z.Last()))];

    // Builds the application once, then runs it without building for each scenario.
    private async Task<Dictionary<string, string>> RunScenarios(
        SdkTestProject project,
        Generator generator,
        Dictionary<string, Dictionary<string, string>> scenarios
    )
    {
        project.AddRocketsApp(targetFramework, generator).AddFile("app/Program.cs", Program(generator));
        await project.Build("app");

        var results = new Dictionary<string, string>();
        foreach (var (name, environment) in scenarios)
        {
            var output = await project.Dotnet("app", "run --no-build", environment);
            results[name] = output.Trim();
        }

        return results;
    }

    // asks for a rocket and writes it, or why it couldn't (the validation failures, say); TokenProvider returns the token in the
    // environment, when it is there
    private static string Program(Generator generator) =>
        RocketsApp
           .Program(generator)
           .Replace(
                "builder.Services.AddRocketsClient();",
                $$"""
                builder.Services.AddRocketsClient(
                    o =>
                    {
                        if (Environment.GetEnvironmentVariable("{{CodeToken}}") is { } token)
                        {
                            o.TokenProvider = (services, cancellationToken) => new System.Threading.Tasks.ValueTask<string>(token);
                        }
                    }
                );
                """,
                StringComparison.Ordinal
            )
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
