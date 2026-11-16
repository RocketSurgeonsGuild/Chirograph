using Chirograph.Tests.Scaffolding;

namespace Chirograph.Tests;

/// <summary>
/// Build seam: <c>Add&lt;ClientName&gt;()</c> fills the generated <c>RocketsClientOptions</c> from the keys under
/// <c>Clients:RocketsClient</c>, then from code, then from the client's connection string, and validates it at startup. Each test builds
/// one throwaway application, then runs it once per scenario with its configuration in environment variables, and records what the
/// application saw: the options it got from <c>IOptionsMonitor</c>, or the validation failures. The secret <c>s3cr3t</c> stands in for
/// every value that a failure must not repeat.
/// </summary>
[TargetFrameworks]
public class ClientOptionsTests(string targetFramework) : LoggerTest<TestRecord>(TestRecord.Create())
{
    /// <summary>
    /// The keys are the lowest source, then code (the <c>configure</c> argument, and <c>Configure</c> even when it comes before the
    /// registration), then the connection string, then a <c>PostConfigure</c> in code. <c>Clients:RocketsClient:ConnectionString</c>
    /// comes first and is never merged with <c>ConnectionStrings:RocketsClient</c>; set to an empty value, it shields the client from a
    /// database's connection string of the same name.
    /// </summary>
    [Test]
    [Arguments(Generator.Kiota)]
    [Arguments(Generator.Refitter)]
    public async Task Should_Apply_The_Sources_In_Order(Generator generator)
    {
        var keys = new Dictionary<string, string>
        {
            ["Clients__RocketsClient__BaseAddress"] = "http://keys.example/",
            ["Clients__RocketsClient__Timeout"] = "00:00:05",
        };

        using var project = new SdkTestProject(targetFramework);
        var results = await RunScenarios(
            project,
            generator,
            new()
            {
                ["Keys only"] = With(
                    keys,
                    new()
                    {
                        ["Clients__RocketsClient__Resilience"] = "true",
                        ["Clients__RocketsClient__AllowedHosts__0"] = "auth.example",
                        ["Clients__RocketsClient__DefaultRequestHeaders__X-Api-Version"] = "2",
                    }
                ),
                ["Code over keys"] = With(keys, new() { [CodeTimeout] = "00:00:10", [CodeBaseAddress] = "http://code.example/" }),
                ["Connection string over keys and code"] = With(
                    keys,
                    new()
                    {
                        [CodeTimeout] = "00:00:10",
                        [CodeBaseAddress] = "http://code.example/",
                        ["Clients__RocketsClient__ConnectionString"] = "Endpoint=http://connection-string.example/;Timeout=00:00:20",
                    }
                ),
                ["ConnectionStrings as the fallback"] = With(keys, new() { ["ConnectionStrings__RocketsClient"] = "BaseAddress=http://fallback.example/" }),
                ["Clients connection string first, never merged"] = With(
                    keys,
                    new()
                    {
                        ["Clients__RocketsClient__ConnectionString"] = "Timeout=00:00:20",
                        ["ConnectionStrings__RocketsClient"] = "BaseAddress=http://fallback.example/",
                    }
                ),
                ["Empty connection string shields a database's"] = With(
                    keys,
                    new()
                    {
                        ["Clients__RocketsClient__ConnectionString"] = "",
                        ["ConnectionStrings__RocketsClient"] = "Data Source=db.example;Initial Catalog=rockets;Password=s3cr3t",
                    }
                ),
                ["A database's connection string, unshielded"] = With(
                    keys,
                    new() { ["ConnectionStrings__RocketsClient"] = "Data Source=db.example;Initial Catalog=rockets;Password=s3cr3t" }
                ),
                ["PostConfigure over the connection string"] = With(
                    keys,
                    new() { ["Clients__RocketsClient__ConnectionString"] = "Timeout=00:00:20", [PostConfigureTimeout] = "00:00:30" }
                ),
            }
        );

        await project.VerifyResult(results, generator.ToString());
    }

    /// <summary>
    /// The connection string has <c>DbConnectionStringBuilder</c>'s syntax, and the parser is stricter: an unknown key, a duplicate key
    /// (an alias with its key included) and a malformed string are failures that name the source and the key, never a value.
    /// </summary>
    [Test]
    [Arguments(Generator.Kiota)]
    [Arguments(Generator.Refitter)]
    public async Task Should_Parse_The_Connection_String(Generator generator)
    {
        string[] connectionStrings =
        [
            "Endpoint=http://rockets.example/;Timeout=00:00:05;Resilience=true;AllowedHosts=a.example, b.example;Authentication=None",
            "  endpoint = http://rockets.example/ ;  TIMEOUT = 00:00:05 ; ",
            "Endpoint='http://rockets.example/?a=1;b=2'",
            "Endpoint=\"http://rockets.example/?a=1;b=2\"",
            "AllowedHosts='it''s.example';Endpoint=\"http://rockets.example/?q=\"\"x\"\"\"",
            "Endpoint=http://rockets.example/?a=b=c",
            "Endpoint=http://rockets.example/;Password=s3cr3t",
            "Timeout=00:00:05;timeout=s3cr3t",
            "Endpoint=http://rockets.example/;BaseAddress=http://s3cr3t.example/",
            "s3cr3t",
            "Endpoint='http://s3cr3t.example/",
            "Endpoint='http://rockets.example/' s3cr3t",
            "Timeout=s3cr3t",
            "Resilience=s3cr3t",
            "Authentication=s3cr3t",
            "Endpoint=s3cr3t",
        ];

        using var project = new SdkTestProject(targetFramework);
        var results = await RunScenarios(
            project,
            generator,
            connectionStrings.ToDictionary(z => z, z => new Dictionary<string, string> { ["ConnectionStrings__RocketsClient"] = z })
        );

        await project.VerifyResult(results, generator.ToString());
    }

    /// <summary>
    /// The keys under <c>Clients:RocketsClient</c> are checked as strictly: an unknown key, a value that doesn't parse, and a relative
    /// <c>BaseAddress</c> are failures that never repeat the value.
    /// </summary>
    [Test]
    [Arguments(Generator.Kiota)]
    [Arguments(Generator.Refitter)]
    public async Task Should_Validate_The_Keys(Generator generator)
    {
        using var project = new SdkTestProject(targetFramework);
        var results = await RunScenarios(
            project,
            generator,
            new()
            {
                ["Unknown key"] = new() { ["Clients__RocketsClient__Retries"] = "s3cr3t" },
                ["Timeout"] = new() { ["Clients__RocketsClient__Timeout"] = "s3cr3t" },
                ["Negative Timeout"] = new() { ["Clients__RocketsClient__Timeout"] = "-00:00:01" },
                ["Resilience"] = new() { ["Clients__RocketsClient__Resilience"] = "s3cr3t" },
                ["Authentication"] = new() { ["Clients__RocketsClient__Authentication"] = "s3cr3t" },
                ["Relative BaseAddress"] = new() { ["Clients__RocketsClient__BaseAddress"] = "s3cr3t" },
                ["DefaultRequestHeaders as a value"] = new() { ["Clients__RocketsClient__DefaultRequestHeaders"] = "s3cr3t" },
                ["AllowedHosts as a value"] = new() { ["Clients__RocketsClient__AllowedHosts"] = "s3cr3t" },
                ["Endpoint alias"] = new() { ["Clients__RocketsClient__Endpoint"] = "http://endpoint.example/" },
                ["Endpoint with BaseAddress"] = new()
                {
                    ["Clients__RocketsClient__Endpoint"] = "http://endpoint.example/",
                    ["Clients__RocketsClient__BaseAddress"] = "http://s3cr3t.example/",
                },
            }
        );

        await project.VerifyResult(results, generator.ToString());
    }

    /// <summary>
    /// A bad client stops the host when it starts (<c>ValidateOnStart</c>), before anything resolves it. Without a host, a bare
    /// <c>ServiceCollection</c> fails when the client is first created.
    /// </summary>
    [Test]
    [Arguments(Generator.Kiota)]
    [Arguments(Generator.Refitter)]
    public async Task Should_Fail_Fast_On_A_Bad_Client(Generator generator)
    {
        using var project = new SdkTestProject(targetFramework);
        var results = await RunScenarios(
            project,
            generator,
            new()
            {
                ["Host, good client"] = new() { ["Clients__RocketsClient__BaseAddress"] = "http://rockets.example/" },
                ["Host, bad client"] = new() { ["Clients__RocketsClient__BaseAddress"] = "rockets", ["Clients__RocketsClient__Retries"] = "3" },
                ["Bare ServiceCollection, good client"] = new() { [CodeBaseAddress] = "http://rockets.example/" },
                ["Bare ServiceCollection, bad client"] = new() { [CodeBaseAddress] = "rockets" },
            },
            scenario => scenario.StartsWith("Host", StringComparison.Ordinal) ? "host" : "bare"
        );

        await project.VerifyResult(results, generator.ToString());
    }

    private const string CodeTimeout = "TEST_CODE_TIMEOUT";
    private const string CodeBaseAddress = "TEST_CODE_BASEADDRESS";
    private const string PostConfigureTimeout = "TEST_POSTCONFIGURE_TIMEOUT";

    private static Dictionary<string, string> With(Dictionary<string, string> first, Dictionary<string, string> second) =>
        [with(first.Concat(second).GroupBy(z => z.Key).Select(z => z.Last()))];

    // Builds the application once, then runs it without building for each scenario, in the mode the scenario says.
    private async Task<Dictionary<string, string>> RunScenarios(
        SdkTestProject project,
        Generator generator,
        Dictionary<string, Dictionary<string, string>> scenarios,
        Func<string, string>? mode = null
    )
    {
        project.AddRocketsApp(targetFramework, generator).AddFile("app/Program.cs", Program(generator));
        await project.Build("app");

        var results = new Dictionary<string, string>();
        foreach (var (name, environment) in scenarios)
        {
            var output = await project.Dotnet("app", $"run --no-build -- {mode?.Invoke(name) ?? "options"}", environment);
            results[name] = output.Trim();
        }

        return results;
    }

    // options: writes the options the client gets, or the validation failures. host: starts the host. bare: creates the client from a
    // bare ServiceCollection, configured only in code.
    private static string Program(Generator generator) => $$"""
        using System;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Hosting;
        using Microsoft.Extensions.Logging;
        using Microsoft.Extensions.Options;
        using Throwaway.Clients;

        var mode = args[0];
        if (mode == "bare")
        {
            var services = new ServiceCollection();
            services.AddRocketsClient(o => o.BaseAddress = Environment.GetEnvironmentVariable("{{CodeBaseAddress}}"));
            using var provider = services.BuildServiceProvider();
            Console.WriteLine("Built the provider.");
            await ReportAsync(() => { Console.WriteLine($"Created {provider.GetRequiredService<{{( generator == Generator.Kiota ? "RocketsClient" : "IRocketsClient" )}}>() is not null}."); return Task.CompletedTask; });
            return;
        }

        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        if (Environment.GetEnvironmentVariable("{{CodeTimeout}}") is { } codeTimeout)
        {
            // before the registration, and still after the keys
            builder.Services.Configure<RocketsClientOptions>(o => o.Timeout = TimeSpan.Parse(codeTimeout));
        }

        builder.Services.AddRocketsClient(
            o =>
            {
                if (Environment.GetEnvironmentVariable("{{CodeBaseAddress}}") is { } codeBaseAddress)
                {
                    o.BaseAddress = codeBaseAddress;
                }
            }
        );
        if (Environment.GetEnvironmentVariable("{{PostConfigureTimeout}}") is { } postConfigureTimeout)
        {
            builder.Services.PostConfigure<RocketsClientOptions>(o => o.Timeout = TimeSpan.Parse(postConfigureTimeout));
        }

        using var host = builder.Build();
        if (mode == "host")
        {
            await ReportAsync(async () => { await host.StartAsync(); Console.WriteLine("Started."); await host.StopAsync(); });
            return;
        }

        await ReportAsync(
            () =>
            {
                var options = host.Services.GetRequiredService<IOptionsMonitor<RocketsClientOptions>>().CurrentValue;
                Console.WriteLine($"BaseAddress: {options.BaseAddress}");
                Console.WriteLine($"Timeout: {options.Timeout}");
                Console.WriteLine($"Resilience: {options.Resilience}");
                Console.WriteLine($"AllowedHosts: [{string.Join(", ", options.AllowedHosts)}]");
                Console.WriteLine($"DefaultRequestHeaders: [{string.Join(", ", options.DefaultRequestHeaders)}]");
                Console.WriteLine($"Authentication: {options.Authentication}");
                return Task.CompletedTask;
            }
        );

        static async Task ReportAsync(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (OptionsValidationException e)
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
        }
        """;
}
