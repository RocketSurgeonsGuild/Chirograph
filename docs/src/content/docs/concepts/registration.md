---
title: Registration
description: The Add<ClientName>() extension every client gets, the Clients:<ClientName> configuration section (BaseAddress, Timeout, DefaultRequestHeaders, Resilience), and configuring the client further on IHttpClientBuilder.
tags: [kiota, refitter]
sidebar:
    order: 3
---

Every client gets a registration: a public extension method on `IServiceCollection`, written next to the generated code. For a client
named `RocketsClient` it is:

```csharp
public static IHttpClientBuilder AddRocketsClient(this IServiceCollection services);
```

It is in the client's namespace, in a `partial` class named `RocketsClientServiceCollectionExtensions`. It is public, so an
application can register a client that a library it references generated. The project of the client must implement one hook of that
class, or it fails to compile with `CS8795`: see [Authentication](#authentication).

## What it registers

- **Kiota:** a named `HttpClient`, `RocketsClient`, and the typed client `RocketsClient`, built on Kiota's `HttpClientRequestAdapter`
  with the authentication provider from the container (see [Authentication](#authentication)).
- **Refitter:** `IRocketsClient`, through Refit's `AddRefitGeneratedClient`. Refit's source generator writes the implementation at
  build time, so there is no reflection.

Resolve the client from the container, or inject it:

```csharp
public sealed class LaunchService(RocketsClient rockets) { … }    // Kiota
public sealed class LaunchService(IRocketsClient rockets) { … }   // Refitter
```

## Configuration

The registration reads the section `Clients:<ClientName>` from `IConfiguration` each time the `HttpClient` is created. The section's
path is a constant on the class, `RocketsClientServiceCollectionExtensions.SectionKey`.

```json
{
    "Clients": {
        "RocketsClient": {
            "BaseAddress": "https://rockets.example.com/",
            "Timeout": "00:00:30",
            "DefaultRequestHeaders": {
                "X-Api-Version": "2"
            },
            "Resilience": true
        }
    }
}
```

| Key                     | Type                 | Effect                                                                                                                                                                                                      |
| ----------------------- | -------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `BaseAddress`           | absolute URI         | `HttpClient.BaseAddress`. Without one, a Kiota client uses the first server of its document, and a Refit client fails when it is called.                                                                    |
| `Timeout`               | `TimeSpan`           | `HttpClient.Timeout`, written as `hh:mm:ss` (`00:00:00.5` is half a second). Without one, the `HttpClient` default of 100 seconds stays.                                                                    |
| `DefaultRequestHeaders` | string to string map | Each entry is added to `HttpClient.DefaultRequestHeaders`, so every request sends it.                                                                                                                       |
| `Resilience`            | bool                 | `true` keeps the standard resilience handler (`AddStandardResilienceHandler()`): retries, timeouts and a circuit breaker. It is read only when the project references Microsoft.Extensions.Http.Resilience. |

A missing key leaves the `HttpClient` as it is. Any configuration source works: the environment variable
`Clients__RocketsClient__DefaultRequestHeaders__X-Api-Version` sets a header, for example.

### Resilience

Chirograph adds no resilience package. When the project references
[Microsoft.Extensions.Http.Resilience](https://www.nuget.org/packages/Microsoft.Extensions.Http.Resilience) (directly or through
another package or project), the build sees it among the resolved references and writes the resilience code into the registration:
it calls `AddStandardResilienceHandler()` on the client's builder, and takes the handler out again when the client's handlers are
built unless `Resilience` is `true`. Without the package the registration has no such code, it compiles, and `Resilience` is ignored.

The key turns on the handler with its default options. To tune them, or to add a handler of your own, chain on the builder the
registration returns instead.

## Configuring the client further

The registration returns the `IHttpClientBuilder`, so everything `IHttpClientFactory` offers chains on it: handlers, resilience,
lifetimes, and a base address of your own that wins over the configuration.

```csharp
builder.Services
    .AddRocketsClient()
    .AddHttpMessageHandler<BearerTokenHandler>()
    .AddStandardResilienceHandler();
```

Chirograph adds no handlers itself, and adds resilience only when `Resilience` asks for it.

## Authentication

When the client is created, the registration takes its authentication from the container, under the key of the client's name first:

- **Kiota:** an `IAuthenticationProvider` keyed `"RocketsClient"`, else an unkeyed one, else `AnonymousAuthenticationProvider`. An
  `IParseNodeFactory` and an `ISerializationWriterFactory` are looked up the same way, and Kiota's defaults are used without one.
- **Refitter:** `RefitSettings` keyed `"RocketsClient"`, else unkeyed ones, else none (Refit's defaults).

A provider keyed with another client's name is not this client's. The application registers them, so a client that a library generated
gets its authentication from the application that uses it:

```csharp
// Kiota: a bearer token for RocketsClient only
builder.Services.AddKeyedSingleton<IAuthenticationProvider>(
    "RocketsClient",
    (services, _) => new BaseBearerTokenAuthenticationProvider(services.GetRequiredService<IAccessTokenProvider>()));

// Refitter: settings for IRocketsClient only
builder.Services.AddKeyedSingleton(
    "RocketsClient",
    (services, _) => new RefitSettings
    {
        AuthorizationHeaderValueGetter = (request, cancellationToken) =>
            services.GetRequiredService<ITokenSource>().GetTokenAsync(cancellationToken),
    });

builder.Services.AddRocketsClient();
```

Refit's `AuthorizationHeaderValueGetter` fills an `Authorization` header that the operation declares (`[Headers("Authorization: Bearer")]`).

### The hook

The project of the client decides what the client uses at last, in a partial method of the registration's class that it must implement
(the build fails with `CS8795` without one). It gets what the container gave; returning it as is keeps that:

```csharp
namespace MyApp.Rockets;

public static partial class RocketsClientServiceCollectionExtensions
{
    // Kiota
    private static partial IRequestAdapter CreateRequestAdapter(IServiceProvider services, HttpClientRequestAdapter adapter) => adapter;

    // Refitter
    private static partial RefitSettings? CreateRefitSettings(IServiceProvider services, RefitSettings? settings) => settings;
}
```

Return something else to change it: another adapter (say with an authentication provider of the library's own), or settings with a
serializer or a handler set.

## Without the registration

A generated client is an ordinary class or interface. Create one from an `HttpClient`, for example against a test server:

```csharp
var kiota = new RocketsClient(new HttpClientRequestAdapter(new AnonymousAuthenticationProvider(), httpClient: httpClient));   // or a provider of your own
var refit = RestService.For<IRocketsClient>(httpClient);
```

## With Dovetail

A Dovetail joint is one more caller of the registration. See [With Dovetail](../../guides/with-dovetail/).
