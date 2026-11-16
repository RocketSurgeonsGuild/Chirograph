---
title: With Optionary
description: Chirograph declares the schema of each client's Clients:<ClientName> section to Optionary, so appsettings.json gets IntelliSense and validation for it.
tags: [optionary]
sidebar:
    order: 5
---

[Optionary](https://rocketsurgeonsguild.github.io/Optionary/) writes an `appsettings.schema.json` for an application from the
configuration sections it and its references declare. Chirograph declares each client's section, `Clients:<ClientName>`, so an
application that references Optionary gets completion and validation for its clients in `appsettings.json`. Chirograph never
references Optionary, and the registration still reads the section from `IConfiguration` without it.

## Turning it on

Reference Optionary in the project that declares the clients:

```xml
<ItemGroup>
  <PackageReference Include="Optionary" Version="…" />
</ItemGroup>
```

> [!NOTE]
> This needs Optionary 0.1.0 or later, the first release that reads the `OptionarySchemaFragment` item. With an older Optionary, or
> none, the item is ignored and the build still succeeds.

Build, and the application's `obj/appsettings.schema.json` (copied next to the application) holds a `Clients` section with one
entry per client. A library that declares the clients puts them in its own schema instead, and every application that references it
merges them, as Optionary does for any section.

## The keys

The registration reads the whole section (see [Registration](../../concepts/registration/)). The schema describes:

| Key                     | Type                    | Meaning                                                                                                                                              |
| ----------------------- | ----------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------- |
| `ConnectionString`      | string                  | The client's connection string. It wins over the other keys, and set (even empty), `ConnectionStrings:<ClientName>` is not read.                     |
| `BaseAddress`           | absolute URI            | The `HttpClient.BaseAddress`. A Refitter client needs one, here or in a connection string; a Kiota client falls back to its document's first server. |
| `Endpoint`              | absolute URI            | An alias of `BaseAddress`. Set one or the other, not both.                                                                                           |
| `Timeout`               | TimeSpan, `hh:mm:ss`    | The `HttpClient.Timeout`, written as `IConfiguration` binds a `TimeSpan`, such as `00:00:30`.                                                        |
| `DefaultRequestHeaders` | object of string values | Headers sent with every request, each name with its value.                                                                                           |
| `Resilience`            | boolean                 | `true` adds the standard resilience handler. Only takes effect when the project references `Microsoft.Extensions.Http.Resilience`.                   |
| `AllowedHosts`          | array of strings        | More hosts than the `BaseAddress` host that may receive the client's credentials.                                                                    |
| `Authentication`        | `None`                  | How the client authenticates. `None`, the default, takes the authentication from the container.                                                      |

```json
{
    "Clients": {
        "RocketsClient": {
            "BaseAddress": "https://rockets.example.com/",
            "Timeout": "00:00:30",
            "DefaultRequestHeaders": { "X-Api-Key": "…" },
            "Resilience": true
        }
    }
}
```

## What Chirograph declares

For each client with a valid `ClientName`, Chirograph adds an `OptionarySchemaFragment` item at evaluation, whose `File` is the schema
of its generator, shipped in the SDK:

```xml
<OptionarySchemaFragment Include="Clients:RocketsClient" File="…/Sdk/schemas/kiota.schema.json" />
<OptionarySchemaFragment Include="Clients:RocketsRefitClient" File="…/Sdk/schemas/refitter.schema.json" />
```

The two files differ only in their descriptions. Neither requires `BaseAddress`, since a connection string can give it. Optionary
merges a section that several items or options types declare, so your own fragment for `Clients:RocketsClient` adds to Chirograph's.

`ChirographOptionarySchema=false` turns the items off for the project. See [Properties](../../reference/properties/).
