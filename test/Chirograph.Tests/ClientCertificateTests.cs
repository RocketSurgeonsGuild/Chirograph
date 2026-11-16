using Chirograph.Tests.Scaffolding;

namespace Chirograph.Tests;

/// <summary>
/// Build seam: the certificate keys under <c>Clients:RocketsClient</c> (or in its connection string) put a client certificate on the
/// client's primary handler, for mutual TLS, whatever its <c>Authentication</c>. Each test builds one throwaway application, then runs it
/// once per scenario with its configuration in environment variables, against <see cref="RocketsTlsApi" />, which echoes the thumbprint
/// of the certificate it got as the rocket's serial number (written <c>{client-certificate}</c>). The secret <c>s3cr3t</c> stands in for
/// every value that a failure must not repeat.
/// </summary>
[TargetFrameworks]
public class ClientCertificateTests(string targetFramework) : LoggerTest<TestRecord>(TestRecord.Create())
{
    /// <summary>
    /// A PKCS#12 file with its password, a PEM certificate with its key (plain or encrypted), and a PEM file holding both each reach the
    /// API, from the keys or from the connection string. Without a certificate, the API answers <c>403</c>.
    /// </summary>
    [Test]
    [Arguments(Generator.Kiota)]
    [Arguments(Generator.Refitter)]
    public async Task Should_Send_The_Client_Certificate(Generator generator)
    {
        await using var api = await RocketsTlsApi.Start();
        using var project = new SdkTestProject(targetFramework);
        var certificates = Path.Combine(project.Directory, "certificates");
        var thumbprint = TestCertificates.WriteClientCertificate(certificates);
        string File(string name) => Path.Combine(certificates, name);

        var results = await RunScenarios(
            project,
            generator,
            api.Environment(),
            new()
            {
                ["PKCS#12 file"] = new()
                {
                    ["Clients__RocketsClient__CertificatePath"] = File("client.pfx"),
                    ["Clients__RocketsClient__CertificatePassword"] = TestCertificates.Password,
                },
                ["PEM certificate and key"] = new()
                {
                    ["Clients__RocketsClient__CertificatePath"] = File("client.crt"),
                    ["Clients__RocketsClient__CertificateKeyPath"] = File("client.key"),
                },
                ["PEM certificate and encrypted key"] = new()
                {
                    ["Clients__RocketsClient__CertificatePath"] = File("client.crt"),
                    ["Clients__RocketsClient__CertificateKeyPath"] = File("client-encrypted.key"),
                    ["Clients__RocketsClient__CertificatePassword"] = TestCertificates.Password,
                },
                ["PEM file with the certificate and its key"] = new() { ["Clients__RocketsClient__CertificatePath"] = File("client.pem") },
                ["Connection string, with Authentication None"] = new()
                {
                    ["ConnectionStrings__RocketsClient"] =
                        $"Authentication=None;CertificatePath='{File("client.pfx")}';CertificatePassword={TestCertificates.Password}",
                },
                ["No certificate"] = [],
            }
        );

        await project.VerifyResult(
            results.ToDictionary(z => z.Key, z => z.Value.Replace(thumbprint, "{client-certificate}", StringComparison.Ordinal)),
            generator.ToString()
        );
    }

    /// <summary>
    /// A path and a thumbprint together, a password or a key path without a path, a store without a thumbprint, a missing file, and a
    /// certificate that can't be loaded are validation failures that never repeat a value.
    /// </summary>
    [Test]
    [Arguments(Generator.Kiota)]
    [Arguments(Generator.Refitter)]
    public async Task Should_Validate_The_Certificate_Keys(Generator generator)
    {
        using var project = new SdkTestProject(targetFramework);
        var certificates = Path.Combine(project.Directory, "certificates");
        TestCertificates.WriteClientCertificate(certificates);
        string File(string name) => Path.Combine(certificates, name);

        var results = await RunScenarios(
            project,
            generator,
            new() { ["Clients__RocketsClient__BaseAddress"] = "https://rockets.example/" },
            new()
            {
                ["Path and thumbprint"] = new()
                {
                    ["Clients__RocketsClient__CertificatePath"] = File("client.pfx"),
                    ["Clients__RocketsClient__CertificateThumbprint"] = "s3cr3t",
                },
                ["Missing file"] = new() { ["Clients__RocketsClient__CertificatePath"] = File("s3cr3t.pfx") },
                ["Missing key file"] = new()
                {
                    ["Clients__RocketsClient__CertificatePath"] = File("client.crt"),
                    ["Clients__RocketsClient__CertificateKeyPath"] = File("s3cr3t.key"),
                },
                ["Password without a path"] = new() { ["Clients__RocketsClient__CertificatePassword"] = "s3cr3t" },
                ["Key path without a path"] = new() { ["Clients__RocketsClient__CertificateKeyPath"] = File("client.key") },
                ["Store without a thumbprint"] = new() { ["Clients__RocketsClient__CertificateStore"] = "CurrentUser/My" },
                ["Malformed store"] = new()
                {
                    ["Clients__RocketsClient__CertificateThumbprint"] = "0000000000000000000000000000000000000000",
                    ["Clients__RocketsClient__CertificateStore"] = "s3cr3t",
                },
                ["Wrong password"] = new()
                {
                    ["Clients__RocketsClient__CertificatePath"] = File("client.pfx"),
                    ["Clients__RocketsClient__CertificatePassword"] = "s3cr3t",
                },
                ["PEM certificate without its key"] = new() { ["Clients__RocketsClient__CertificatePath"] = File("client.crt") },
                ["Not a certificate"] = new() { ["Clients__RocketsClient__CertificatePath"] = Path.Combine(project.Directory, "app", "rockets.json") },
                ["Path and thumbprint in the connection string"] = new()
                {
                    ["Clients__RocketsClient__ConnectionString"] = $"CertificatePath='{File("client.pfx")}';CertificateThumbprint=s3cr3t",
                },
            }
        );

        await project.VerifyResult(results, generator.ToString());
    }

    // Builds the application once, then runs it without building for each scenario, with the common environment and the scenario's.
    private async Task<Dictionary<string, string>> RunScenarios(
        SdkTestProject project,
        Generator generator,
        Dictionary<string, string> environment,
        Dictionary<string, Dictionary<string, string>> scenarios
    )
    {
        project.AddRocketsApp(targetFramework, generator).AddFile("app/Program.cs", Program(generator));
        await project.Build("app");

        var results = new Dictionary<string, string>();
        foreach (var (name, scenario) in scenarios)
        {
            var output = await project.Dotnet("app", "run --no-build", new Dictionary<string, string>([.. environment, .. scenario]));
            results[name] = output.Trim();
        }

        return results;
    }

    // Creates the client and asks for a rocket, writing its serial number, or the validation failures, or the failure of the call.
    private static string Program(Generator generator) => $$"""
        using System;
        using System.Net.Http;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Hosting;
        using Microsoft.Extensions.Logging;
        using Microsoft.Extensions.Options;
        using Throwaway.Clients;

        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        builder.Services.AddRocketsClient().ConfigurePrimaryHttpMessageHandler({{RocketsTlsApi.TrustServerCertificate}});
        using var host = builder.Build();
        var id = Guid.Parse("{{RocketsApi.RocketId}}");
        try
        {
            var client = host.Services.GetRequiredService<{{( generator == Generator.Kiota ? "RocketsClient" : "IRocketsClient" )}}>();
            var rocket = await {{( generator == Generator.Kiota ? "client.Rockets[id].GetAsync()" : "client.GetRocket(id)" )}};
            Console.WriteLine($"SerialNumber: {rocket!.SerialNumber}");
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
            for (var inner = e.InnerException; inner is not null; inner = inner.InnerException)
            {
                Console.WriteLine($"  {inner.GetType().FullName}: {inner.Message}");
            }
        }
        """;
}
