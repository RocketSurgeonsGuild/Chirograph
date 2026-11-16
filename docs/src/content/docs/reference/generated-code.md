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
public static class RocketsClientServiceCollectionExtensions
{
    public const string BaseAddressKey = "Clients:RocketsClient:BaseAddress";

    public static IHttpClientBuilder AddRocketsClient(this IServiceCollection services)
    {
        var builder = services.AddHttpClient("RocketsClient");
        builder.ConfigureHttpClient((provider, httpClient) =>
        {
            var configuration = provider.GetService<IConfiguration>();
            if (configuration?[BaseAddressKey] is { Length: > 0 } baseAddress)
            {
                httpClient.BaseAddress = new Uri(baseAddress, UriKind.Absolute);
            }
        });
        builder.AddTypedClient<RocketsClient>(httpClient => new RocketsClient(
            new HttpClientRequestAdapter(new AnonymousAuthenticationProvider(), httpClient: httpClient)));
        return builder;
    }
}
```

Refitter's is the same, except that it registers the Refit interface instead of a named client:

```csharp
var builder = services.AddRefitGeneratedClient<IRocketsClient>();
```

The real files spell every type with `global::` and call the extension methods as static methods, so they compile whatever the
project's usings are. The `GeneratedCode` version is the version of Chirograph that wrote the file.

The class name, the method name, the `BaseAddressKey` constant and the return type are public API. The body can change between
releases.

## The Dovetail joint

With a Dovetail that can expand it, Dovetail (not Chirograph) also writes a joint for each client, outside the client's folder:
`obj/<configuration>/<framework>/DovetailTemplate.Chirograph.RocketsClient.g.cs`. It is an internal `[DovetailExport]` class,
`RocketsClientJoint`, in the client's namespace, whose `Register` calls `AddRocketsClient(services)`. Its name is all of it that is
API. See [With Dovetail](../../guides/with-dovetail/).
