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

| Key                     | Type                                                                                                                                                                | Meaning                                                                                                                                                                                                                                                                                        |
| ----------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `ConnectionString`      | string                                                                                                                                                              | The client's connection string. It wins over the other keys, and set (even empty), `ConnectionStrings:<ClientName>` is not read.                                                                                                                                                               |
| `BaseAddress`           | absolute URI                                                                                                                                                        | The `HttpClient.BaseAddress`. A Refitter client needs one, here or in a connection string; a Kiota client falls back to its document's first server.                                                                                                                                           |
| `Endpoint`              | absolute URI                                                                                                                                                        | An alias of `BaseAddress`. Set one or the other, not both.                                                                                                                                                                                                                                     |
| `Timeout`               | TimeSpan, `hh:mm:ss`                                                                                                                                                | The `HttpClient.Timeout`, written as `IConfiguration` binds a `TimeSpan`, such as `00:00:30`.                                                                                                                                                                                                  |
| `DefaultRequestHeaders` | object of string values                                                                                                                                             | Headers sent with every request, each name with its value.                                                                                                                                                                                                                                     |
| `Resilience`            | boolean, or an object when the project references `Microsoft.Extensions.Http.Resilience`                                                                            | `true` adds the standard resilience handler; an object sets its [settings](../../concepts/registration/#resilience) (`Retry`, `CircuitBreaker`, `AttemptTimeout`, `TotalRequestTimeout`, `RateLimiter`). Only takes effect when the project references `Microsoft.Extensions.Http.Resilience`. |
| `AllowedHosts`          | array of strings                                                                                                                                                    | More hosts than the `BaseAddress` host that may receive the client's credentials: a host alone allows each of its ports, `host:port` that port.                                                                                                                                                |
| `Authentication`        | `None`, `ApiKey`, `Bearer`, `Basic`, `ClientCredentials`, `PrivateKeyJwt`, `AzureDefault`, `AzureManagedIdentity`, `AzureWorkloadIdentity`, `AzureServicePrincipal` | How the client authenticates. `None`, the default, takes the authentication from the container. `ClientCredentials`, `PrivateKeyJwt` and the Azure methods need the item's metadata.                                                                                                           |
| `ApiKey`                | string                                                                                                                                                              | The key of `ApiKey`.                                                                                                                                                                                                                                                                           |
| `Key`                   | string                                                                                                                                                              | An alias of `ApiKey`. Set one or the other, not both.                                                                                                                                                                                                                                          |
| `ApiKeyHeader`          | string                                                                                                                                                              | The header `ApiKey` sends the key in, `X-Api-Key` by default.                                                                                                                                                                                                                                  |
| `ApiKeyQuery`           | string                                                                                                                                                              | The query parameter `ApiKey` sends the key in, instead of a header.                                                                                                                                                                                                                            |
| `Token`                 | string                                                                                                                                                              | The token of `Bearer`.                                                                                                                                                                                                                                                                         |
| `Username`              | string                                                                                                                                                              | The user name of `Basic`.                                                                                                                                                                                                                                                                      |
| `Password`              | string                                                                                                                                                              | The password of `Basic`.                                                                                                                                                                                                                                                                       |
| `CertificatePath`       | string                                                                                                                                                              | A client certificate, for mutual TLS or for `PrivateKeyJwt` or `AzureServicePrincipal` to sign with: a PKCS#12 (`.pfx`, `.p12`) or PEM file. Not with `CertificateThumbprint`.                                                                                                                 |
| `CertificatePassword`   | string                                                                                                                                                              | The password of the PKCS#12 file, or of its encrypted PEM key.                                                                                                                                                                                                                                 |
| `CertificateKeyPath`    | string                                                                                                                                                              | The PEM private key, when `CertificatePath` is a PEM certificate without its key.                                                                                                                                                                                                              |
| `CertificateThumbprint` | string                                                                                                                                                              | A client certificate in `CertificateStore`, instead of a file.                                                                                                                                                                                                                                 |
| `CertificateStore`      | `<location>/<name>`                                                                                                                                                 | The store of `CertificateThumbprint`: `CurrentUser/My` (the default) or `LocalMachine/My`, say.                                                                                                                                                                                                |
| `ClientId`              | string                                                                                                                                                              | The client id of `ClientCredentials` and `PrivateKeyJwt`, or of the application or user-assigned identity of an Azure method.                                                                                                                                                                  |
| `ClientSecret`          | string                                                                                                                                                              | The client secret of `ClientCredentials` or `AzureServicePrincipal`, not of `PrivateKeyJwt`. Keep it out of `appsettings.json`.                                                                                                                                                                |
| `Scope`                 | string                                                                                                                                                              | The scopes a token is asked for, space-separated: optional for `ClientCredentials` and `PrivateKeyJwt`, needed by an Azure method.                                                                                                                                                             |
| `TokenEndpoint`         | absolute URI                                                                                                                                                        | The token endpoint of `ClientCredentials` and `PrivateKeyJwt`. Not with `Authority`.                                                                                                                                                                                                           |
| `Authority`             | absolute https URI                                                                                                                                                  | The authority whose discovery document gives the token endpoint of `ClientCredentials` and `PrivateKeyJwt`. Not with `TokenEndpoint`.                                                                                                                                                          |
| `Resource`              | string                                                                                                                                                              | The resource `ClientCredentials` and `PrivateKeyJwt` ask for.                                                                                                                                                                                                                                  |
| `ClientCredentialStyle` | `Header`, `Body`                                                                                                                                                    | How `ClientCredentials` sends the client id and secret.                                                                                                                                                                                                                                        |
| `TenantId`              | string                                                                                                                                                              | The tenant of an Azure method, its id or a domain.                                                                                                                                                                                                                                             |

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

## The rules

Beyond the keys, the schema checks what a validator can see in `appsettings.json`:

- `Authentication` takes only the methods the client allows: `None`, `ApiKey`, `Bearer` and `Basic`.
- When `Authentication` names a method, the keys of the other methods are not allowed, and the keys the method needs are required
  (`Username` for `Basic`). A secret is never required, since it belongs elsewhere.
- A section without `Authentication` is not checked against the methods: a connection string can name the method.
- The secrets (`ApiKey`, `Key`, `Token`, `Password`, `CertificatePassword`) say in their description to keep them out of
  `appsettings.json`, in user secrets, an environment variable or a vault.
- `ConnectionString` is a plain string: a schema cannot check what is inside it. `ConnectionStrings:<ClientName>` is not described,
  since Optionary describes the sections the application owns.
- Neither generator requires `BaseAddress`: a connection string, in the section or in `ConnectionStrings:<ClientName>`, can give it,
  and with service discovery the client has a default address. With service discovery, the description of `BaseAddress`
  says it may be a service-discovery address, and gives its default, `https+http://<ClientName>`.

## What Chirograph declares

For each client with a valid `ClientName`, Chirograph writes the client's schema when the project builds (a design-time build too),
into the client's folder, and adds an `OptionarySchemaFragment` item at evaluation whose `File` is that schema:

```xml
<OptionarySchemaFragment Include="Clients:RocketsClient" File="obj/Debug/net10.0/openapi/RocketsClient/RocketsClient.schema.json" />
<OptionarySchemaFragment Include="Clients:RocketsRefitClient" File="obj/Debug/net10.0/openapi/RocketsRefitClient/RocketsRefitClient.schema.json" />
```

The schema is the client's own: its descriptions name the client and its generator, and its `Authentication` lists the methods the
client allows. Optionary merges a section that several items or options types declare, so your own fragment for
`Clients:RocketsClient` adds to Chirograph's.

`ChirographOptionarySchema=false` turns the items and the schemas off for the project. See [Properties](../../reference/properties/).
