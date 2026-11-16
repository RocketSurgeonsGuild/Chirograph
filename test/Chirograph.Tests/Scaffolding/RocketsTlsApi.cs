using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Chirograph.Tests.Scaffolding;

/// <summary>
///     The Rockets API over HTTPS, on Kestrel on a free port of the loopback interface, for what <see cref="RocketsApi" /> (HttpListener,
///     no TLS) cannot see: the client certificate. <c>/rockets/{id}</c> requires one, and answers with the rocket whose serial number is
///     the thumbprint of the certificate the client presented; without one, it answers <c>403 Forbidden</c>. Any client certificate is
///     accepted, self-signed included. The server certificate is self-signed, made for the test: an application trusts it by pinning
///     <see cref="ServerCertificateHash" /> (see <see cref="TrustServerCertificate" />).
/// </summary>
internal sealed class RocketsTlsApi : IAsyncDisposable
{
    /// <summary>
    ///     The environment variable that <see cref="TrustServerCertificate" /> reads the pinned server certificate hash from.
    /// </summary>
    public const string ServerCertificateHashVariable = "TEST_ROCKETS_TLS_API_CERTIFICATE";

    /// <summary>
    ///     A C# lambda for a test application's <c>Program.cs</c>, to pass to <c>ConfigurePrimaryHttpMessageHandler</c> on the builder
    ///     <c>AddRocketsClient()</c> returns: it makes the client trust only the server certificate whose SHA-256 hash is in
    ///     <see cref="ServerCertificateHashVariable" /> (a comma-separated list, to trust more than one server). It changes the primary handler instead of replacing it, so the client certificate
    ///     the registration set stays. (Not through <c>ConfigureHttpClientDefaults</c>: Refit replaces the primary handler after the defaults.)
    /// </summary>
    public const string TrustServerCertificate = $$"""
        static (handler, _) =>
        {
            var pin = System.Environment.GetEnvironmentVariable("{{ServerCertificateHashVariable}}");
            System.Net.Security.RemoteCertificateValidationCallback trust = (_, certificate, _, _) =>
                certificate is not null && pin is not null && System.Array.Exists(
                    pin.Split(','),
                    z => string.Equals(certificate.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256), z, System.StringComparison.OrdinalIgnoreCase)
                );
            switch (handler)
            {
                case System.Net.Http.SocketsHttpHandler sockets:
                    sockets.SslOptions.RemoteCertificateValidationCallback = trust;
                    break;
                case System.Net.Http.HttpClientHandler client:
                    client.ServerCertificateCustomValidationCallback = (message, certificate, chain, errors) => trust(message, certificate, chain, errors);
                    break;
            }
        }
        """;

    private RocketsTlsApi(WebApplication application, X509Certificate2 serverCertificate, Uri baseAddress)
    {
        _application = application;
        _serverCertificate = serverCertificate;
        BaseAddress = baseAddress;
    }

    /// <summary>The address the API listens on, <c>https://127.0.0.1:&lt;port&gt;/</c>.</summary>
    public Uri BaseAddress { get; }

    /// <summary>The SHA-256 hash of the server certificate, in hexadecimal, to pin it.</summary>
    public string ServerCertificateHash => _serverCertificate.GetCertHashString(HashAlgorithmName.SHA256);

    /// <summary>
    ///     The environment that points <c>RocketsClient</c> at the API and trusts its certificate (with <see cref="TrustServerCertificate" />).
    /// </summary>
    public Dictionary<string, string> Environment() => new()
    {
        ["Clients__RocketsClient__BaseAddress"] = BaseAddress.ToString(),
        [ServerCertificateHashVariable] = ServerCertificateHash,
    };

    /// <summary>Starts the API on a free port, with a new self-signed server certificate.</summary>
    public static async Task<RocketsTlsApi> Start()
    {
        var serverCertificate = TestCertificates.Create("rockets-tls-api");
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(
            kestrel => kestrel.Listen(
                IPAddress.Loopback,
                0,
                listen => listen.UseHttps(
                    new HttpsConnectionAdapterOptions
                    {
                        ServerCertificate = serverCertificate,
                        // asked for at the handshake; the endpoint, not TLS, turns a request without one away, so the client sees a 403
                        ClientCertificateMode = ClientCertificateMode.AllowCertificate,
                        ClientCertificateValidation = static (_, _, _) => true,
                    }
                )
            )
        );

        var application = builder.Build();
        application.MapGet(
            "/rockets/{id}",
            static (string id, HttpContext context) => context.Connection.ClientCertificate is { } certificate
                ? Results.Json(new Rocket(id, certificate.Thumbprint))
                : Results.StatusCode(StatusCodes.Status403Forbidden)
        );
        await application.StartAsync();

        var address = application.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new(application, serverCertificate, new Uri($"https://127.0.0.1:{new Uri(address).Port}/"));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _application.StopAsync();
        await _application.DisposeAsync();
        _serverCertificate.Dispose();
    }

    private sealed record Rocket(string Id, string SerialNumber);

    private readonly WebApplication _application;
    private readonly X509Certificate2 _serverCertificate;
}
