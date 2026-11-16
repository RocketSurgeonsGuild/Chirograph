using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Chirograph.Tests.Scaffolding;

/// <summary>
///     The API the clients of the OpenAPI client tests are generated from and call, served from the test process on a port of the loopback
///     interface: its OpenAPI document at <c>/openapi.json</c> (a client generated from a URL downloads it from there), and one rocket at
///     <c>/rockets/{id}</c>. No test reaches the network.
/// </summary>
internal sealed class RocketsApi : IAsyncDisposable
{
    /// <summary>
    ///     The id of the rocket the clients ask for.
    /// </summary>
    public const string RocketId = "6f9619ff-8b86-d011-b42d-00cf4fc964ff";

    /// <summary>
    ///     The request header the API echoes as the serial number of the rocket (<c>FALCON-9</c> without it), so a test sees the headers a
    ///     client sends.
    /// </summary>
    public const string SerialNumberHeader = "X-Serial-Number";

    /// <summary>
    ///     The credentials the API echoes after the serial number of the rocket, as <c>(name=value, ...)</c>: the <c>Authorization</c> and
    ///     <c>X-Api-Key</c> headers and the <c>api_key</c> query parameter, so a test sees the credentials a client sends.
    /// </summary>
    public static readonly string[] EchoedCredentials = ["Authorization", "X-Api-Key", "api_key"];

    /// <summary>
    ///     The OpenAPI document of the API: one operation, GetRocket, which returns a Rocket.
    /// </summary>
    public const string Document = """
        {
          "openapi": "3.1.1",
          "info": { "title": "Rockets", "version": "1.0.0" },
          "paths": {
            "/rockets/{id}": {
              "get": {
                "operationId": "GetRocket",
                "parameters": [ { "name": "id", "in": "path", "required": true, "schema": { "type": "string", "format": "uuid" } } ],
                "responses": {
                  "200": { "description": "OK", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Rocket" } } } }
                }
              }
            }
          },
          "components": {
            "schemas": {
              "Rocket": {
                "type": "object",
                "properties": {
                  "id": { "type": "string", "format": "uuid" },
                  "serialNumber": { "type": "string" }
                }
              }
            }
          }
        }
        """;

    /// <summary>
    ///     <see cref="Document" /> in YAML.
    /// </summary>
    public const string YamlDocument = """
        openapi: 3.1.1
        info:
          title: Rockets
          version: 1.0.0
        paths:
          /rockets/{id}:
            get:
              operationId: GetRocket
              parameters:
                - name: id
                  in: path
                  required: true
                  schema:
                    type: string
                    format: uuid
              responses:
                "200":
                  description: OK
                  content:
                    application/json:
                      schema:
                        $ref: "#/components/schemas/Rocket"
        components:
          schemas:
            Rocket:
              type: object
              properties:
                id:
                  type: string
                  format: uuid
                serialNumber:
                  type: string
        """;

    /// <summary>
    ///     <see cref="Document" /> with this API as its only server, which a client without a base address of its own can fall back to.
    /// </summary>
    public string DocumentWithServer => Document.Replace("\"paths\": {", $"\"servers\": [ {{ \"url\": \"{BaseAddress}\" }} ],\n  \"paths\": {{", StringComparison.Ordinal);

    private RocketsApi(HttpListener listener, Uri baseAddress)
    {
        _listener = listener;
        BaseAddress = baseAddress;
        _serving = Task.Run(Serve);
    }

    /// <summary>
    ///     The address the API listens on, with a trailing slash.
    /// </summary>
    public Uri BaseAddress { get; }

    /// <summary>
    ///     The address of the OpenAPI document.
    /// </summary>
    public Uri DocumentAddress => new(BaseAddress, "openapi.json");

    /// <summary>
    ///     How many times the OpenAPI document was downloaded (not counting the requests <see cref="DocumentUnavailable" /> turned away).
    /// </summary>
    public int DocumentRequests => _documentRequests;

    /// <summary>
    ///     Starts the API on a free port.
    /// </summary>
    /// <returns>The API.</returns>
    public static RocketsApi Start()
    {
        // HttpListener cannot listen on port 0: take a port the system says is free, and another when a test running at the same time took
        // it first
        for (var attempt = 1; ; attempt++)
        {
            using var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ( (IPEndPoint)probe.LocalEndpoint ).Port;
            probe.Stop();

            var baseAddress = new Uri($"http://127.0.0.1:{port}/");
            var listener = new HttpListener();
            listener.Prefixes.Add(baseAddress.ToString());
            try
            {
                listener.Start();
                return new(listener, baseAddress);
            }
            catch (HttpListenerException) when (attempt < 10)
            {
                listener.Close();
            }
        }
    }

    /// <summary>
    ///     Whether <c>/openapi.json</c> answers <c>404 Not Found</c>, a failure a download doesn't retry, instead of the document. The API
    ///     stays up and keeps its port, so a build after this is set cannot reach the document, and no other test can take the port in
    ///     the meantime.
    /// </summary>
    public bool DocumentUnavailable { get; set; }

    /// <summary>
    ///     How long the API waits before it answers with a rocket, so a client's timeout can elapse first. Disposing the API ends the
    ///     wait.
    /// </summary>
    public TimeSpan RocketDelay { get; set; }

    /// <summary>
    ///     How many of the first requests for a rocket answer <c>503 Service Unavailable</c>, a transient failure a resilient client
    ///     retries, before the API answers with the rocket.
    /// </summary>
    public int TransientFailures { get; set; }

    /// <summary>
    ///     Where a request for a rocket is redirected (<c>302 Found</c>, to the same path there), when it is set: another API, a different
    ///     host to a client's credentials.
    /// </summary>
    public Uri? RedirectRocketsTo { get; set; }

    /// <summary>
    ///     How many times a rocket was asked for, the requests that failed included.
    /// </summary>
    public int RocketRequests => _rocketRequests;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        // Close alone: Stop and Close both unregister the prefixes, and on macOS the second time binds the port again, which fails when
        // another test took it in between.
        try
        {
            _listener.Close();
        }
        catch (HttpListenerException) { }
        catch (ObjectDisposedException) { }

        await _serving;
        _stopping.Dispose();
    }

    // One request at a time. A request that fails (its client went away, say) is dropped, and the API keeps serving.
    private async Task Serve()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            // closed, or closed before it was first asked for a request
            catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            try
            {
                await Respond(context);
            }
            catch (Exception e) when (e is HttpListenerException or IOException or ObjectDisposedException or InvalidOperationException or OperationCanceledException)
            {
                try
                {
                    context.Response.Abort();
                }
                catch (ObjectDisposedException) { }
            }
        }
    }

    private async Task Respond(HttpListenerContext context)
    {
        var path = context.Request.Url!.AbsolutePath;
        var rocketRequest = path.StartsWith("/rockets/", StringComparison.Ordinal) ? Interlocked.Increment(ref _rocketRequests) : 0;
        var (status, body) = path switch
        {
            "/openapi.json" when DocumentUnavailable => (404, ""),
            _ when rocketRequest > 0 && rocketRequest <= TransientFailures => (503, ""),
            _ when rocketRequest > 0 && RedirectRocketsTo is not null => (302, ""),
            "/openapi.json" => (200, Document),
            _ when path.StartsWith("/rockets/", StringComparison.Ordinal) => (
                200,
                $$"""{ "id": "{{path["/rockets/".Length..]}}", "serialNumber": "{{SerialNumber(context.Request)}}" }"""
            ),
            _ => (404, ""),
        };
        if (path == "/openapi.json" && status == 200) Interlocked.Increment(ref _documentRequests);
        if (path.StartsWith("/rockets/", StringComparison.Ordinal) && RocketDelay > TimeSpan.Zero)
        {
            await Task.Delay(RocketDelay, _stopping.Token);
        }

        var bytes = Encoding.UTF8.GetBytes(body);
        if (status == 302) context.Response.RedirectLocation = new Uri(RedirectRocketsTo!, path).ToString();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    // the serial number of the rocket: X-Serial-Number, else FALCON-9, then the credentials the request carried, if any
    private static string SerialNumber(HttpListenerRequest request)
    {
        var credentials = EchoedCredentials
                         .Select(name => (name, value: name == "api_key" ? request.QueryString[name] : request.Headers[name]))
                         .Where(z => z.value is not null)
                         .Select(z => $"{z.name}={z.value}")
                         .ToArray();
        var serialNumber = request.Headers[SerialNumberHeader] ?? "FALCON-9";
        return credentials.Length == 0 ? serialNumber : $"{serialNumber} ({string.Join(", ", credentials)})";
    }

    private readonly HttpListener _listener;
    private readonly Task _serving;
    private readonly CancellationTokenSource _stopping = new();
    private int _documentRequests;
    private int _rocketRequests;
}
