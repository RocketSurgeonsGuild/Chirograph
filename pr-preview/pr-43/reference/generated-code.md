# Generated code

For a client `RocketsClient` in namespace `MyApp.Rockets`, everything is in
`obj/<configuration>/<framework>/openapi/RocketsClient/` (the parent folder is `ChirographOpenApiClientsDirectory`). Nothing is written
into the project, and nothing there is meant to be committed.

## The folder

| File                                            | What it is                                                                                                                                                                               | Compiled |
| ----------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------- |
| `generated/**`                                  | The generator's output. Kiota: the `RocketsClient` class, its request builders and models, and `kiota-lock.json`. Refitter: `RocketsClient.cs`, with `IRocketsClient` and its contracts. | `*.cs`   |
| `RocketsClientServiceCollectionExtensions.g.cs` | The [registration](#the-registration).                                                                                                                                                   | yes      |
| `RocketsClientOptions.g.cs`                     | The [options](#the-options) the registration reads, with their connection-string parser and validation.                                                                                  | yes      |
| `openapi.json`, `openapi.yaml` or `openapi.yml` | The client's copy of its document, written only when the content changes.                                                                                                                | no       |
| `settings.txt`                                  | The generator, its version, `ClientName`, `Namespace` and `Options`, written only when they change.                                                                                      | no       |
| `generated.stamp`                               | Touched after each generation. The generator runs again when the document's copy or `settings.txt` is newer.                                                                             | no       |
| `RocketsClient.refitter`                        | Refitter only: the settings file Refitter runs with.                                                                                                                                     | no       |
| `download/`                                     | URL only: the last download, before it is compared with the cache.                                                                                                                       | no       |

Every file here is added to `FileWrites`, so `dotnet clean` removes them all, `generated.stamp` included, and the next build
generates the client again. A `DocumentCache` in the project is not in this folder, and a clean leaves it alone.

## The registration

Kiota's registration:

```csharp
namespace MyApp.Rockets;

[GeneratedCode("Chirograph", "1.0.0")]
public static class RocketsClientServiceCollectionExtensions
{
    public const string SectionKey = "Clients:RocketsClient";
    public const string BaseAddressKey = SectionKey + ":BaseAddress";
    public const string ResilienceKey = SectionKey + ":Resilience";
    public const string ConnectionStringKey = SectionKey + ":ConnectionString";

    public static IHttpClientBuilder AddRocketsClient(this IServiceCollection services, Action<RocketsClientOptions>? configure = null)
    {
        // the options: keys, then code, then the connection string; validated at startup
        RocketsClientOptionsSetup.Register(services);
        if (configure is not null) services.Configure(configure);

        var builder = services.AddHttpClient("RocketsClient");
        builder.ConfigureHttpClient((provider, httpClient) =>
        {
            var options = Options(provider);
            if (options.BaseAddress is { Length: > 0 } baseAddress)
                httpClient.BaseAddress = new Uri(baseAddress, UriKind.Absolute);
            if (options.Timeout is { } timeout)
                httpClient.Timeout = timeout;
            foreach (var header in options.DefaultRequestHeaders)
                httpClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
        });
        builder.AddTypedClient<RocketsClient>((httpClient, services) =>
        {
            // keyed with "RocketsClient", else unkeyed, else none (anonymous for the authentication provider)
            var adapter = new HttpClientRequestAdapter(
                Resolve<IAuthenticationProvider>(services) ?? new AnonymousAuthenticationProvider(),
                Resolve<IParseNodeFactory>(services),
                Resolve<ISerializationWriterFactory>(services),
                httpClient);
            var configureRequestAdapter = Options(services).ConfigureRequestAdapter;
            return new RocketsClient(configureRequestAdapter is null ? adapter : configureRequestAdapter(services, adapter));
        });
        return builder;
    }

    // read when the HttpClient is created, so a configuration reload applies
    private static RocketsClientOptions Options(IServiceProvider services) =>
        services.GetRequiredService<IOptionsMonitor<RocketsClientOptions>>().CurrentValue;
}
```

When the project references Microsoft.Extensions.Http.Resilience, the build writes this before `return builder;` too, and leaves it
out otherwise:

```csharp
builder.AddStandardResilienceHandler();
builder.ConfigureAdditionalHttpMessageHandlers((handlers, provider) =>
{
    var resilience = Options(provider).Resilience;
    // the handler added just above is the last one so far
    if (!resilience && handlers.Count > 0 && handlers[^1] is ResilienceHandler)
        handlers.RemoveAt(handlers.Count - 1);
});
```

See [Configuration](../../concepts/registration/#configuration) for what each key does.

Refitter's is the same, except that it registers the Refit interface instead of a named client:

```csharp
var builder = services.AddRefitGeneratedClient<IRocketsClient>(static provider =>
{
    var settings = Resolve<RefitSettings>(provider);
    var configureRefitSettings = Options(provider).ConfigureRefitSettings;
    return configureRefitSettings is null ? settings : configureRefitSettings(provider, settings);
});
```

The real files spell every type with `global::` and call the extension methods as static methods, so they compile whatever the
project's usings are. The `GeneratedCode` version is the version of Chirograph that wrote the file.

The class name, the method name and its parameters, the `SectionKey`, `BaseAddressKey`, `ResilienceKey` and `ConnectionStringKey`
constants and the return type are public API. The body can change between releases.

## The options

`RocketsClientOptions.g.cs` holds the client's options and what fills them:

- `RocketsClientOptions`, public: `BaseAddress`, `Timeout`, `DefaultRequestHeaders`, `Resilience`, `AllowedHosts` and
  `Authentication`, plus `ConfigureRequestAdapter` (Kiota) or `ConfigureRefitSettings` (Refitter).
- `RocketsClientAuthentication`, public: the methods the client can be configured with (`None` so far).
- `RocketsClientOptionsSetup`, internal: it fills the options from the keys under `Clients:RocketsClient` (an
  `IConfigureOptions`) and from the connection string (an `IPostConfigureOptions`), validates them (an `IValidateOptions`), and
  reloads them with the configuration (an `IOptionsChangeTokenSource`). It parses the connection string itself, without
  `DbConnectionStringBuilder` or the configuration binder, so nothing in it needs reflection.

The two public types and their members are public API. See [The options](../../concepts/registration/#the-options) for the order of
the sources and [Validation](../../concepts/registration/#validation) for what fails.

## The Dovetail joint

With a Dovetail that can expand it, Dovetail (not Chirograph) also writes a joint for each client, outside the client's folder:
`obj/<configuration>/<framework>/DovetailTemplate.Chirograph.RocketsClient.g.cs`. It is an internal `[DovetailExport]` class,
`RocketsClientJoint`, in the client's namespace, whose `Register` calls `AddRocketsClient(services)`. Its name is all of it that is
API. See [With Dovetail](../../guides/with-dovetail/).