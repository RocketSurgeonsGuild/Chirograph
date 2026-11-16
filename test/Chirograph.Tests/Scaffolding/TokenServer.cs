using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Chirograph.Tests.Scaffolding;

/// <summary>
///     An OAuth token server for the client-credentials tests, over HTTPS on Kestrel on a free port of the loopback interface (an
///     <c>Authority</c> must be https). It issues tokens <c>t1</c>, <c>t2</c>… at <c>/connect/token</c> to <see cref="ClientId" /> (and
///     <see cref="ShortLivedClientId" />, whose tokens live only a little longer than Duende's cache buffer) with
///     <see cref="ClientSecret" />, given in the <c>Authorization</c> header or in the body, and to <see cref="AssertionClientId" /> with a
///     client assertion signed by <see cref="AssertionCertificate" />, and serves a discovery document at
///     <c>/.well-known/openid-configuration</c>. <see cref="Requests" /> records each request, without a secret, so a test sees how many
///     tokens a client asked for, how, and when it read the discovery document. <see cref="IsValid" /> tells <see cref="RocketsApi" /> which
///     bearer tokens it issued. The server certificate is self-signed: an application trusts it by pinning <see cref="ServerCertificateHash" />
///     (with <see cref="RocketsTlsApi.TrustServerCertificate" />).
/// </summary>
internal sealed class TokenServer : IAsyncDisposable
{
    /// <summary>The client id the server issues tokens to, for an hour.</summary>
    public const string ClientId = "rockets-app";

    /// <summary>A client id the server issues tokens to for 61 seconds: Duende, which renews a token 60 seconds early, keeps one a second.</summary>
    public const string ShortLivedClientId = "rockets-app-short";

    /// <summary>The secret of both clients.</summary>
    public const string ClientSecret = "s3cr3t";

    /// <summary>
    ///     A client id the server issues tokens to for an hour when it authenticates with a client assertion (RFC 7523) that
    ///     <see cref="AssertionCertificate" /> signed.
    /// </summary>
    public const string AssertionClientId = "rockets-app-jwt";

    /// <summary>The certificate, without its private key, that the assertions of <see cref="AssertionClientId" /> must be signed by.</summary>
    public X509Certificate2? AssertionCertificate { get; set; }

    private TokenServer(X509Certificate2 serverCertificate) => _serverCertificate = serverCertificate;

    /// <summary>The address of the server, <c>https://127.0.0.1:&lt;port&gt;/</c>, and its authority (with the trailing slash).</summary>
    public Uri BaseAddress { get; private set; } = null!;

    /// <summary>The token endpoint.</summary>
    public Uri TokenEndpoint => new(BaseAddress, "connect/token");

    /// <summary>What each request asked for, in order: <c>discovery</c>, or <c>token tN (header|body, scope=…, resource=…)</c>, or why it failed.</summary>
    public string[] Requests => [.. _requests];

    /// <summary>The environment that trusts the server's certificate (with <see cref="RocketsTlsApi.TrustServerCertificate" />).</summary>
    public Dictionary<string, string> Environment() => new() { [RocketsTlsApi.ServerCertificateHashVariable] = ServerCertificateHash };

    /// <summary>The SHA-256 hash of the server certificate, in hexadecimal, to pin it.</summary>
    public string ServerCertificateHash => _serverCertificate.GetCertHashString(HashAlgorithmName.SHA256);

    /// <summary>Whether the server issued <paramref name="token" />, and it hasn't expired.</summary>
    public bool IsValid(string token) => _tokens.TryGetValue(token, out var expires) && expires > DateTimeOffset.UtcNow;

    /// <summary>Starts the server on a free port, with a new self-signed server certificate.</summary>
    public static async Task<TokenServer> Start()
    {
        var serverCertificate = TestCertificates.Create("token-server");
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(serverCertificate)));
        var application = builder.Build();
        var server = new TokenServer(serverCertificate) { _application = application };
        // as delegates, so the results are written
        application.MapPost("/connect/token", (Func<HttpContext, Task<IResult>>)server.Token);
        application.MapGet("/.well-known/openid-configuration", server.Discovery);
        await application.StartAsync();
        var address = application.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        server.BaseAddress = new Uri($"https://127.0.0.1:{new Uri(address).Port}/");
        return server;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _application.StopAsync();
        await _application.DisposeAsync();
        _serverCertificate.Dispose();
    }

    private async Task<IResult> Token(HttpContext context)
    {
        var form = await context.Request.ReadFormAsync();
        if (form.ContainsKey("client_assertion") || form.ContainsKey("client_assertion_type"))
        {
            return Assertion(form);
        }

        string? clientId, clientSecret, style;
        if (context.Request.Headers.Authorization.ToString() is { Length: > 0 } authorization && authorization.StartsWith("Basic ", StringComparison.Ordinal))
        {
            var pair = Encoding.UTF8.GetString(Convert.FromBase64String(authorization["Basic ".Length..])).Split(':', 2);
            (clientId, clientSecret, style) = (Uri.UnescapeDataString(pair[0]), Uri.UnescapeDataString(pair[1]), "header");
        }
        else
        {
            (clientId, clientSecret, style) = (form["client_id"], form["client_secret"], "body");
        }

        if (form["grant_type"] != "client_credentials" || clientId is not ( ClientId or ShortLivedClientId ) || clientSecret != ClientSecret)
        {
            _requests.Enqueue($"token refused ({style})");
            return Results.Json(new { error = "invalid_client" }, statusCode: StatusCodes.Status400BadRequest);
        }

        return Issue(clientId == ShortLivedClientId ? 61 : 3600, $"{style}, scope={form["scope"]}, resource={form["resource"]}");
    }

    private IResult Issue(int lifetime, string how)
    {
        var token = $"t{Interlocked.Increment(ref _issued)}";
        _tokens[token] = DateTimeOffset.UtcNow.AddSeconds(lifetime);
        _requests.Enqueue($"token {token} ({how})");
        return Results.Json(new { access_token = token, token_type = "Bearer", expires_in = lifetime });
    }

    // checks a client assertion (RFC 7523) by hand, independently of the library that signed it: its type, its RS256 signature against
    // AssertionCertificate, iss and sub (the client id), aud (the token endpoint), a jti never seen before, and an exp in the future and
    // at most ten minutes away; records its lifetime (exp - iat)
    private IResult Assertion(IFormCollection form)
    {
        string? Refuse(string why)
        {
            _requests.Enqueue($"token refused (assertion: {why})");
            return null;
        }

        string? Check()
        {
            if (form["client_assertion_type"] != "urn:ietf:params:oauth:client-assertion-type:jwt-bearer")
            {
                return Refuse("client_assertion_type");
            }

            if (form["grant_type"] != "client_credentials" || form.ContainsKey("client_secret"))
            {
                return Refuse("grant_type or client_secret");
            }

            var parts = form["client_assertion"].ToString().Split('.');
            if (parts.Length != 3 || AssertionCertificate is null)
            {
                return Refuse("not a JWS");
            }

            using var header = JsonDocument.Parse(Base64UrlDecode(parts[0]));
            using var payload = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            using var key = AssertionCertificate.GetRSAPublicKey()!;
            if (header.RootElement.GetProperty("alg").GetString() != "RS256"
             || !key.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), Base64UrlDecode(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            {
                return Refuse("signature");
            }

            var claims = payload.RootElement;
            string? Claim(string name) => claims.TryGetProperty(name, out var value) ? value.ToString() : null;
            if (Claim("iss") != AssertionClientId || Claim("sub") != AssertionClientId)
            {
                return Refuse("iss or sub");
            }

            if (Claim("aud") != TokenEndpoint.ToString())
            {
                return Refuse("aud");
            }

            if (Claim("jti") is not { Length: > 0 } jti || !_assertionIds.TryAdd(jti, 0))
            {
                return Refuse("jti");
            }

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return !claims.TryGetProperty("exp", out var exp) || exp.GetInt64() <= now || exp.GetInt64() > now + 600
                ? Refuse("exp")
                : claims.TryGetProperty("iat", out var iat)
                    ? $"{exp.GetInt64() - iat.GetInt64()}s"
                    : "no iat";
        }

        return Check() is { } lifetime
            ? Issue(3600, $"assertion RS256, lifetime {lifetime}, scope={form["scope"]}, resource={form["resource"]}")
            : Results.Json(new { error = "invalid_client" }, statusCode: StatusCodes.Status400BadRequest);
    }

    private static byte[] Base64UrlDecode(string value) =>
        Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/').PadRight(value.Length + ( ( 4 - ( value.Length % 4 ) ) % 4 ), '='));

    private IResult Discovery()
    {
        _requests.Enqueue("discovery");
        return Results.Json(new { issuer = BaseAddress.ToString().TrimEnd('/'), token_endpoint = TokenEndpoint.ToString() });
    }

    private WebApplication _application = null!;
    private readonly X509Certificate2 _serverCertificate;
    private readonly ConcurrentQueue<string> _requests = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _tokens = new();
    private readonly ConcurrentDictionary<string, byte> _assertionIds = new();
    private int _issued;
}
