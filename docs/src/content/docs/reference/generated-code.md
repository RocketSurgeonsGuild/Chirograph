---
title: Generated code
description: The files Chirograph writes for each client in obj, which of them are compiled, and the shape of the generated Add<ClientName>() registration and <ClientName>Options.
tags: [msbuild, kiota, refitter]
sidebar:
    order: 4
---

For a client `RocketsClient` in namespace `MyApp.Rockets`, everything is in
`obj/<configuration>/<framework>/openapi/RocketsClient/` (the parent folder is `ChirographOpenApiClientsDirectory`). Nothing is written
into the project, and nothing there is meant to be committed.

## The folder

| File                                            | What it is                                                                                                                                                                               | Compiled |
| ----------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------- |
| `generated/**`                                  | The generator's output. Kiota: the `RocketsClient` class, its request builders and models, and `kiota-lock.json`. Refitter: `RocketsClient.cs`, with `IRocketsClient` and its contracts. | `*.cs`   |
| `RocketsClientServiceCollectionExtensions.g.cs` | The [registration](#the-registration).                                                                                                                                                   | yes      |
| `RocketsClientOptions.g.cs`                     | The [options](#the-options) the registration reads, with their connection-string parser and validation.                                                                                  | yes      |
| `RocketsClient.schema.json`                     | The schema of the client's `Clients:RocketsClient` section, for [Optionary](../../guides/with-optionary/). Not written with `ChirographOptionarySchema=false`.                           | no       |
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
        // the client certificate (mTLS), on the primary handler the factory made
        builder.ConfigurePrimaryHttpMessageHandler((handler, provider) => RocketsClientCertificate.Present(handler, Options(provider)));
        builder.AddTypedClient<RocketsClient>((httpClient, services) =>
        {
            // keyed with "RocketsClient", else unkeyed, else none (anonymous for the authentication provider, and with an
            // Authentication configured, whose handler sets the credentials)
            var options = Options(services);
            var adapter = new HttpClientRequestAdapter(
                (options.Authentication == RocketsClientAuthentication.None ? Resolve<IAuthenticationProvider>(services) : null)
                    ?? new AnonymousAuthenticationProvider(),
                Resolve<IParseNodeFactory>(services),
                Resolve<ISerializationWriterFactory>(services),
                httpClient);
            var configureRequestAdapter = options.ConfigureRequestAdapter;
            return new RocketsClient(configureRequestAdapter is null ? adapter : configureRequestAdapter(services, adapter));
        });
        // when Authentication isn't None, the handler that adds the credentials, after the handlers added so far
        builder.ConfigureHttpMessageHandlerBuilder(handlers => RocketsClientAuthenticationHandler.Configure(handlers));
        return builder;
    }

    // read when the HttpClient is created, so a configuration reload applies
    private static RocketsClientOptions Options(IServiceProvider services) =>
        services.GetRequiredService<IOptionsMonitor<RocketsClientOptions>>().CurrentValue;
}
```

When the project references Microsoft.Extensions.Http.Resilience, the build writes this before the authentication handler too, and
leaves it out otherwise:

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

When the project references Microsoft.Extensions.ServiceDiscovery, the build writes this after the authentication handler, and leaves
it out otherwise. The options then default `BaseAddress` to `https+http://RocketsClient`, and accept a service-discovery address:

```csharp
if (!HasServiceEndpointProvider(services))
    services.AddServiceDiscovery();
builder.AddServiceDiscovery();
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

- `RocketsClientOptions`, public: `BaseAddress`, `Timeout`, `DefaultRequestHeaders`, `Resilience`, `AllowedHosts`,
  `Authentication` and the keys of its methods (`ApiKey`, `ApiKeyHeader`, `ApiKeyQuery`, `Token`, `TokenProvider`, `Username`,
  `Password`), the certificate keys (`CertificatePath`, `CertificatePassword`, `CertificateKeyPath`, `CertificateThumbprint`,
  `CertificateStore`), plus `ConfigureRequestAdapter` (Kiota) or `ConfigureRefitSettings` (Refitter).
- `RocketsClientAuthentication`, public: the methods the client can be configured with, `None`, `ApiKey`, `Bearer` and `Basic`.
- `RocketsClientOptionsSetup`, internal: it fills the options from the keys under `Clients:RocketsClient` (an
  `IConfigureOptions`) and from the connection string (an `IPostConfigureOptions`), validates them (an `IValidateOptions`), and
  reloads them with the configuration (an `IOptionsChangeTokenSource`). It parses the connection string itself, without
  `DbConnectionStringBuilder` or the configuration binder, so nothing in it needs reflection.
- `RocketsClientAuthenticationHandler`, internal: the `DelegatingHandler` that adds the configured credentials to the requests for the
  allowed hosts, and follows redirects in place of the primary handler (see
  [Configured authentication](../../concepts/registration/#configured-authentication)).
- `RocketsClientCertificate`, internal: it loads the certificate the certificate keys give (a PKCS#12 or PEM file, or a store),
  checks those keys, and sets the certificate on the primary handler for mutual TLS.

With `Authentication="ClientCredentials"` on the item, and only then, the file also has:

- `ClientCredentials` in `RocketsClientAuthentication`, and its keys on `RocketsClientOptions`: `ClientId`, `ClientSecret`, `Scope`,
  `TokenEndpoint`, `Authority`, `Resource` and `ClientCredentialStyle`.
- `RocketsClientCredentialStyle`, public: `Header` or `Body`.
- `RocketsClientClientCredentials`, internal: it configures Duende's client-credentials client `RocketsClient` from the options,
  reads the token endpoint from the `Authority`'s discovery document, and gives the handler the token (see
  [Client credentials](../../concepts/registration/#client-credentials)). The registration calls Duende's
  `AddClientCredentialsTokenManagement()`.

The public types and their members are public API. See [The options](../../concepts/registration/#the-options) for the order of
the sources and [Validation](../../concepts/registration/#validation) for what fails.

## The Dovetail joint

With a Dovetail that can expand it, Dovetail (not Chirograph) also writes a joint for each client, outside the client's folder:
`obj/<configuration>/<framework>/DovetailTemplate.Chirograph.RocketsClient.g.cs`. It is an internal `[DovetailExport]` class,
`RocketsClientJoint`, in the client's namespace, whose `Register` calls `AddRocketsClient(services)`. Its name is all of it that is
API. See [With Dovetail](../../guides/with-dovetail/).
