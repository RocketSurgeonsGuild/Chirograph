---
title: From a file
description: Generate a client from an OpenAPI document kept in the project, as JSON or YAML.
tags: [msbuild, kiota, refitter]
sidebar:
    order: 1
---

A file source is an OpenAPI document that lives with the code. It is the simplest source: the build needs no network once the packages
are restored, and a change to the API shows up as a diff of the document.

## Declare the client

The `Include` is a path relative to the project. A `.json` file is read as JSON, and a `.yaml` or `.yml` file as YAML:

```xml
<ItemGroup>
  <OpenApiClient Include="openapi/rockets.yaml" Generator="Refitter" ClientName="RocketsClient" Namespace="MyApp.Rockets" />
</ItemGroup>
```

Anything that is not a project (`.csproj`, `.fsproj`, `.vbproj`) or a URL (`http://`, `https://`) is a file. A file that does not
exist fails the build with [`CHIRO102`](../../reference/diagnostics/#chiro102).

## Implement the hook

Every client project implements one partial method of the generated registration, or the build fails with `CS8795`. Returning what it
is given uses the authentication from the application's container:

```csharp
namespace MyApp.Rockets;

public static partial class RocketsClientServiceCollectionExtensions
{
    private static partial RefitSettings? CreateRefitSettings(IServiceProvider services, RefitSettings? settings) => settings;
}
```

For a Kiota client it is
`private static partial IRequestAdapter CreateRequestAdapter(IServiceProvider services, HttpClientRequestAdapter adapter) => adapter;`.

## Authenticate the client

Register the authentication keyed with the client's name, then the client:

```csharp
// Kiota
builder.Services.AddKeyedSingleton<IAuthenticationProvider>(
    "RocketsClient",
    (services, _) => new BaseBearerTokenAuthenticationProvider(services.GetRequiredService<IAccessTokenProvider>()));

// Refitter
builder.Services.AddKeyedSingleton(
    "RocketsClient",
    (services, _) => new RefitSettings
    {
        AuthorizationHeaderValueGetter = (request, cancellationToken) =>
            services.GetRequiredService<ITokenSource>().GetTokenAsync(cancellationToken),
    });

builder.Services.AddRocketsClient();
```

See [Authentication](../../concepts/registration/#authentication) for the lookup and the hook.

## Several clients

Give each client its own `ClientName`, and its own `Namespace` when they share a project, so their generated types don't collide:

```xml
<ItemGroup>
  <OpenApiClient Include="openapi/rockets.json" Generator="Kiota" ClientName="RocketsClient" Namespace="MyApp.Rockets" />
  <OpenApiClient Include="openapi/launches.json" Generator="Refitter" ClientName="LaunchesClient" Namespace="MyApp.Launches" />
</ItemGroup>
```

Two clients can share a document, for example to compare the generators. Each one gets its own copy of the document in its own
folder in `obj`.

## Updating the document

Replace the file and build. Chirograph copies the document into `obj` only when its content changed, and the generator only runs
again when that copy, or the client's settings, changed. Saving the file with the same content regenerates nothing. See
[How generation runs](../../concepts/how-generation-runs/).

## Passing options to the generator

`Options` is added to the generator's command line: after `kiota generate …` for Kiota, and after the settings file for Refitter.

```xml
<OpenApiClient
  Include="openapi/rockets.json"
  Generator="Kiota"
  ClientName="RocketsClient"
  Options="--include-path /rockets/**"
/>
```

A change to `Options` generates the client again. Options that change where the output goes or what the client is called fight with
the ones Chirograph passes; see [Generators](../../concepts/generators/) for what those are.
