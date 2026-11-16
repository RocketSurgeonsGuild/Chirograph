---
title: Generated code
description: The files Chirograph writes for each client in obj, which of them are compiled, and the shape of the generated Add<ClientName>() registration.
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
public static partial class RocketsClientServiceCollectionExtensions
{
    public const string SectionKey = "Clients:RocketsClient";
    public const string BaseAddressKey = SectionKey + ":BaseAddress";
    public const string ResilienceKey = SectionKey + ":Resilience";

    public static IHttpClientBuilder AddRocketsClient(this IServiceCollection services)
    {
        var builder = services.AddHttpClient("RocketsClient");
        builder.ConfigureHttpClient((provider, httpClient) =>
        {
            var configuration = provider.GetService<IConfiguration>();
            if (configuration is null) return;

            var section = configuration.GetSection(SectionKey);
            if (section["BaseAddress"] is { Length: > 0 } baseAddress)
                httpClient.BaseAddress = new Uri(baseAddress, UriKind.Absolute);
            if (section["Timeout"] is { Length: > 0 } timeout)
                httpClient.Timeout = TimeSpan.Parse(timeout, CultureInfo.InvariantCulture);
            foreach (var header in section.GetSection("DefaultRequestHeaders").GetChildren())
                if (header.Value is { } value)
                    httpClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, value);
        });
        builder.AddTypedClient<RocketsClient>((httpClient, services) =>
        {
            // keyed with "RocketsClient", else unkeyed, else none (anonymous for the authentication provider)
            var adapter = new HttpClientRequestAdapter(
                Resolve<IAuthenticationProvider>(services) ?? new AnonymousAuthenticationProvider(),
                Resolve<IParseNodeFactory>(services),
                Resolve<ISerializationWriterFactory>(services),
                httpClient);
            return new RocketsClient(CreateRequestAdapter(services, adapter));
        });
        return builder;
    }

    // the project of the client implements it: see Authentication on the Registration page
    private static partial IRequestAdapter CreateRequestAdapter(IServiceProvider services, HttpClientRequestAdapter adapter);
}
```

When the project references Microsoft.Extensions.Http.Resilience, the build writes this before `return builder;` too, and leaves it
out otherwise:

```csharp
builder.AddStandardResilienceHandler();
builder.ConfigureAdditionalHttpMessageHandlers((handlers, provider) =>
{
    var configuration = provider.GetService<IConfiguration>();
    var resilience = bool.TryParse(configuration?[ResilienceKey], out var value) && value;
    // the handler added just above is the last one so far
    if (!resilience && handlers.Count > 0 && handlers[^1] is ResilienceHandler)
        handlers.RemoveAt(handlers.Count - 1);
});
```

See [Configuration](../../concepts/registration/#configuration) for what each key does.

Refitter's is the same, except that it registers the Refit interface instead of a named client:

```csharp
var builder = services.AddRefitGeneratedClient<IRocketsClient>(
    static provider => CreateRefitSettings(provider, Resolve<RefitSettings>(provider)));

// the project of the client implements it
private static partial RefitSettings? CreateRefitSettings(IServiceProvider services, RefitSettings? settings);
```

The real files spell every type with `global::` and call the extension methods as static methods, so they compile whatever the
project's usings are. The `GeneratedCode` version is the version of Chirograph that wrote the file.

The class name, the method name, the hooks (`CreateRequestAdapter` or `CreateRefitSettings`, and `ConfigureHttpClientBuilder`, which `AddRocketsClient()` calls last), the `SectionKey`, `BaseAddressKey` and `ResilienceKey` constants and the return type are public API. The body can change between
releases.

## The Dovetail joint

With a Dovetail that can expand it, Dovetail (not Chirograph) also writes a joint for each client, outside the client's folder:
`obj/<configuration>/<framework>/DovetailTemplate.Chirograph.RocketsClient.g.cs`. It is an internal `[DovetailExport]` class,
`RocketsClientJoint`, in the client's namespace, whose `Register` calls `AddRocketsClient(services)`. Its name is all of it that is
API. See [With Dovetail](../../guides/with-dovetail/).
