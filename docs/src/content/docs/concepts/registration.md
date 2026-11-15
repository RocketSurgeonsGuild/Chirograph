---
title: Registration
description: The Add<ClientName>() extension every client gets, the Clients:<ClientName>:BaseAddress configuration key, and configuring the client further on IHttpClientBuilder.
tags: [kiota, refitter]
sidebar:
    order: 3
---

Every client gets a registration: a public extension method on `IServiceCollection`, written next to the generated code. For a client
named `RocketsClient` it is:

```csharp
public static IHttpClientBuilder AddRocketsClient(this IServiceCollection services);
```

It is in the client's namespace, in a class named `RocketsClientServiceCollectionExtensions`. It is public, so an application can
register a client that a library it references generated.

## What it registers

- **Kiota:** a named `HttpClient`, `RocketsClient`, and the typed client `RocketsClient`, built on Kiota's `HttpClientRequestAdapter`
  with anonymous authentication.
- **Refitter:** `IRocketsClient`, through Refit's `AddRefitGeneratedClient`. Refit's source generator writes the implementation at
  build time, so there is no reflection.

Resolve the client from the container, or inject it:

```csharp
public sealed class LaunchService(RocketsClient rockets) { … }    // Kiota
public sealed class LaunchService(IRocketsClient rockets) { … }   // Refitter
```

## The base address

The registration reads the base address from configuration, at the key `Clients:<ClientName>:BaseAddress`, when the `HttpClient` is
created. The key is also a constant on the class, `RocketsClientServiceCollectionExtensions.BaseAddressKey`.

```json
{
    "Clients": {
        "RocketsClient": {
            "BaseAddress": "https://rockets.example.com/"
        }
    }
}
```

The address must be absolute. Without one, a Kiota client uses the first server of its document, and a Refit client fails when it is
called.

## Configuring the client further

The registration returns the `IHttpClientBuilder`, so everything `IHttpClientFactory` offers chains on it: handlers, resilience,
lifetimes, and a base address of your own that wins over the configuration.

```csharp
builder.Services
    .AddRocketsClient()
    .AddHttpMessageHandler<BearerTokenHandler>()
    .AddStandardResilienceHandler();
```

Chirograph configures no authentication, resilience or handlers itself.

## Without the registration

A generated client is an ordinary class or interface. Create one from an `HttpClient`, for example against a test server:

```csharp
var kiota = new RocketsClient(new HttpClientRequestAdapter(new AnonymousAuthenticationProvider(), httpClient: httpClient));
var refit = RestService.For<IRocketsClient>(httpClient);
```

## With Dovetail

A Dovetail joint is one more caller of the registration. See [With Dovetail](../../guides/with-dovetail/).
