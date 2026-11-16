using Chirograph.Tests.Scaffolding;

namespace Chirograph.Tests;

/// <summary>
/// Build seam: <c>Add&lt;ClientName&gt;()</c> takes the authentication of the client from the container when the client is created: a
/// Kiota <c>IAuthenticationProvider</c>, or Refit's <c>RefitSettings</c>, keyed with the name of the client, else unkeyed, else none.
/// Each throwaway application registers one that sends <c>X-Serial-Number</c>, which <see cref="RocketsApi" /> echoes as the serial
/// number of the rocket.
/// </summary>
[TargetFrameworks]
public class AuthenticationTests(string targetFramework) : LoggerTest<TestRecord>(TestRecord.Create())
{
    /// <summary>Where the application registers the authentication of the client, if anywhere.</summary>
    public enum Registration
    {
        /// <summary>Nothing is registered: the call is anonymous.</summary>
        None,

        /// <summary>Keyed with <c>RocketsClient</c> (sends <c>KEYED</c>), beside an unkeyed one (sends <c>UNKEYED</c>).</summary>
        Keyed,

        /// <summary>Unkeyed only (sends <c>UNKEYED</c>).</summary>
        Unkeyed,

        /// <summary>Keyed with <c>OtherClient</c> only (sends <c>OTHER</c>), which isn't this client's.</summary>
        OtherClient,
    }

    /// <summary>The client sends what the registration for it says, and nothing when there is none for it.</summary>
    [Test]
    [MatrixDataSource]
    public async Task Should_Authenticate_From_The_Container(
        [Matrix(Generator.Kiota, Generator.Refitter)] Generator generator,
        [Matrix(Registration.None, Registration.Keyed, Registration.Unkeyed, Registration.OtherClient)] Registration registration
    )
    {
        await using var api = RocketsApi.Start();
        using var project = new SdkTestProject(targetFramework);
        project.AddRocketsApp(targetFramework, generator).AddFile("app/Program.cs", Program(generator, registration));

        await project.Build("app");
        var output = await project.Run("app", RocketsApp.Environment(api));

        await project.VerifyResult(output, $"{generator}_{registration}");
    }

    /// <summary>
    /// The client uses what the delegate on its options returns, <c>ConfigureRequestAdapter</c> (Kiota) or <c>ConfigureRefitSettings</c>
    /// (Refitter): here it replaces what the container gave (sends <c>DELEGATE</c> over <c>KEYED</c>).
    /// </summary>
    [Test]
    [Arguments(Generator.Kiota)]
    [Arguments(Generator.Refitter)]
    public async Task Should_Use_What_The_Options_Delegate_Returns(Generator generator)
    {
        await using var api = RocketsApi.Start();
        using var project = new SdkTestProject(targetFramework);
        project
           .AddRocketsApp(targetFramework, generator)
           .AddFile("app/Program.cs", Program(generator, Registration.Keyed, Delegate(generator)));

        await project.Build("app");
        var output = await project.Run("app", RocketsApp.Environment(api));

        await project.VerifyResult(output, generator.ToString());
    }

    /// <summary>A library generates the client, and the application that registers it gives it its authentication.</summary>
    [Test]
    [Arguments(Generator.Kiota)]
    [Arguments(Generator.Refitter)]
    public async Task Should_Authenticate_A_Client_From_A_Library(Generator generator)
    {
        await using var api = RocketsApi.Start();
        using var project = new SdkTestProject(targetFramework);
        project
           .AddProject("lib/lib.csproj", project.CreateConsumer("Library").OpenApiClient(generator, "rockets.json"))
           .AddFile("lib/rockets.json", RocketsApi.Document)
           .AddProject("app/app.csproj", project.CreateProject().Hosting(targetFramework).ItemProjectReference("../lib/lib.csproj"))
           .AddFile("app/Program.cs", Program(generator, Registration.Keyed));

        await project.Build("app");
        var output = await project.Run("app", RocketsApp.Environment(api));

        await project.VerifyResult(output, generator.ToString());
    }

    /// <summary>
    /// With <c>Authentication</c> set, configuration wins: Kiota's adapter gets an anonymous provider instead of the container's (no
    /// <c>KEYED</c>), and the handler sets the credentials. Refit still takes its <c>RefitSettings</c> from the container, which carry its
    /// serializers (and here the handler that sends <c>KEYED</c>).
    /// </summary>
    [Test]
    [Arguments(Generator.Kiota)]
    [Arguments(Generator.Refitter)]
    public async Task Should_Prefer_The_Configured_Authentication(Generator generator)
    {
        await using var api = RocketsApi.Start();
        using var project = new SdkTestProject(targetFramework);
        project.AddRocketsApp(targetFramework, generator).AddFile("app/Program.cs", Program(generator, Registration.Keyed));

        await project.Build("app");
        var environment = RocketsApp.Environment(api);
        environment["Clients__RocketsClient__Authentication"] = "Bearer";
        environment["Clients__RocketsClient__Token"] = "t0k3n";
        var output = await project.Run("app", environment);

        await project.VerifyResult(output, generator.ToString());
    }

    private static string Program(Generator generator, Registration registration, string configure = "")
    {
        var register = registration switch
        {
            Registration.None => "",
            Registration.Keyed => Keyed(generator, "RocketsClient", "KEYED") + Unkeyed(generator, "UNKEYED"),
            Registration.Unkeyed => Unkeyed(generator, "UNKEYED"),
            Registration.OtherClient => Keyed(generator, "OtherClient", "OTHER"),
            _ => throw new ArgumentOutOfRangeException(nameof(registration), registration, null),
        };
        return RocketsApp
              .Program(generator)
              .Replace("builder.Services.AddRocketsClient();", register + $"builder.Services.AddRocketsClient({configure});", StringComparison.Ordinal)
              .TrimEnd()
         + "\n" + ( generator == Generator.Kiota ? KiotaTypes : RefitTypes );
    }

    private static string Keyed(Generator generator, string key, string serialNumber) =>
        $"builder.Services.AddKeyedSingleton<{Service(generator)}>(\"{key}\", {Instance(generator, serialNumber)});\n";

    private static string Unkeyed(Generator generator, string serialNumber) =>
        $"builder.Services.AddSingleton<{Service(generator)}>({Instance(generator, serialNumber)});\n";

    private static string Service(Generator generator) => generator == Generator.Kiota
        ? "Microsoft.Kiota.Abstractions.Authentication.IAuthenticationProvider"
        : "Refit.RefitSettings";

    private static string Instance(Generator generator, string serialNumber) => generator == Generator.Kiota
        ? $"new SerialNumberProvider(\"{serialNumber}\")"
        : $"SerialNumberHandler.Settings(\"{serialNumber}\")";

    // the configure argument of AddRocketsClient() that sets the delegate on the options
    private static string Delegate(Generator generator) => generator == Generator.Kiota
        ? """
          options => options.ConfigureRequestAdapter = (services, adapter) =>
              new Microsoft.Kiota.Http.HttpClientLibrary.HttpClientRequestAdapter(
                  new SerialNumberProvider("DELEGATE"),
                  httpClient: services.GetRequiredService<System.Net.Http.IHttpClientFactory>().CreateClient("RocketsClient")
              )
          """
        : """options => options.ConfigureRefitSettings = (services, settings) => SerialNumberHandler.Settings("DELEGATE")""";

    // the provider (Kiota) and the handler (Refit) the throwaway applications register; the Refit handler is the primary handler
    // RefitSettings.HttpMessageHandlerFactory gives
    private const string KiotaTypes = $$"""
        #nullable enable
        sealed class SerialNumberProvider(string serialNumber) : Microsoft.Kiota.Abstractions.Authentication.IAuthenticationProvider
        {
            public System.Threading.Tasks.Task AuthenticateRequestAsync(
                Microsoft.Kiota.Abstractions.RequestInformation request,
                System.Collections.Generic.Dictionary<string, object>? additionalAuthenticationContext = null,
                System.Threading.CancellationToken cancellationToken = default
            )
            {
                request.Headers.Add("{{RocketsApi.SerialNumberHeader}}", serialNumber);
                return System.Threading.Tasks.Task.CompletedTask;
            }
        }
        """;

    private const string RefitTypes = $$"""
        sealed class SerialNumberHandler(string serialNumber) : System.Net.Http.DelegatingHandler(new System.Net.Http.HttpClientHandler())
        {
            public static Refit.RefitSettings Settings(string serialNumber) => new() { HttpMessageHandlerFactory = () => new SerialNumberHandler(serialNumber) };

            protected override System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> SendAsync(
                System.Net.Http.HttpRequestMessage request,
                System.Threading.CancellationToken cancellationToken
            )
            {
                request.Headers.TryAddWithoutValidation("{{RocketsApi.SerialNumberHeader}}", serialNumber);
                return base.SendAsync(request, cancellationToken);
            }
        }
        """;
}
