---
title: Registration
description: The Add<ClientName>() extension every client gets, its generated <ClientName>Options, filled from Clients:<ClientName> and a connection string and validated at startup, and configuring the client further on IHttpClientBuilder.
tags: [kiota, refitter]
sidebar:
    order: 3
---

Every client gets a registration: a public extension method on `IServiceCollection`, written next to the generated code. For a client
named `RocketsClient` it is:

```csharp
public static IHttpClientBuilder AddRocketsClient(this IServiceCollection services, Action<RocketsClientOptions>? configure = null);
```

It is in the client's namespace, in a static class named `RocketsClientServiceCollectionExtensions`, next to the client's options,
`RocketsClientOptions`. Both are public, so an application can register and configure a client that a library it references generated.
The project of the client writes no code of its own.

## What it registers

- **Kiota:** a named `HttpClient`, `RocketsClient`, and the typed client `RocketsClient`, built on Kiota's `HttpClientRequestAdapter`
  with the authentication provider from the container, unless the options configure an authentication (see
  [Authentication](#authentication)).
- **Refitter:** `IRocketsClient`, through Refit's `AddRefitGeneratedClient`. Refit's source generator writes the implementation at
  build time, so there is no reflection.
- **Both:** the options, `RocketsClientOptions`, with their validation, which runs when the host starts.

Resolve the client from the container, or inject it:

```csharp
public sealed class LaunchService(RocketsClient rockets) { … }    // Kiota
public sealed class LaunchService(IRocketsClient rockets) { … }   // Refitter
```

## The options

`RocketsClientOptions` holds what the registration applies to the client each time its `HttpClient` is created. Generated code fills it
from configuration, without the reflection binder, so it is safe for trimming and AOT. The registration reads it through
`IOptionsMonitor<RocketsClientOptions>`, so a configuration reload applies when the client's handlers rotate.

The options are filled from three sources, from lowest to highest:

1. The keys under `Clients:RocketsClient`.
2. Code: the `configure` argument of `AddRocketsClient()`, and any `services.Configure<RocketsClientOptions>(…)`, wherever it is called.
3. The client's [connection string](#the-connection-string), applied as a post-configuration, so it wins over everything else.

Code that must win over the connection string uses `services.PostConfigure<RocketsClientOptions>(…)`.

```csharp
builder.Services.AddRocketsClient(options => options.Timeout = TimeSpan.FromSeconds(10));
```

## Configuration

The section's path is a constant on the registration class, `RocketsClientServiceCollectionExtensions.SectionKey`.

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
| `ConnectionString`      | string               | The client's [connection string](#the-connection-string).                                                                                                                                                   |
| `BaseAddress`           | absolute URI         | `HttpClient.BaseAddress`, `http` or `https`. Alias `Endpoint`. Without one, Kiota uses the document's first server, Refit fails when called, unless [service discovery](#service-discovery) is on.          |
| `Timeout`               | `TimeSpan`           | `HttpClient.Timeout`, written as `hh:mm:ss` in the invariant culture (`00:00:00.5` is half a second). Without one, the `HttpClient` default of 100 seconds stays.                                           |
| `DefaultRequestHeaders` | string to string map | Each entry is added to `HttpClient.DefaultRequestHeaders`, so every request sends it. Not in a connection string.                                                                                           |
| `Resilience`            | bool                 | `true` keeps the standard resilience handler (`AddStandardResilienceHandler()`): retries, timeouts and a circuit breaker. It is read only when the project references Microsoft.Extensions.Http.Resilience. |
| `AllowedHosts`          | array of hosts       | More hosts than the `BaseAddress` host that may receive the client's [credentials](#which-hosts-get-the-credentials). In a connection string, a comma-separated list.                                       |
| `Authentication`        | method               | How the client authenticates: `None` (the default, the [container](#from-the-container) decides), `ApiKey`, `Bearer` or `Basic` ([configured](#configured-authentication)).                                 |
| `ApiKey`                | string               | The key of `ApiKey`. Alias `Key`.                                                                                                                                                                           |
| `ApiKeyHeader`          | string               | The header `ApiKey` sends the key in, `X-Api-Key` by default.                                                                                                                                               |
| `ApiKeyQuery`           | string               | The query parameter `ApiKey` sends the key in, instead of a header.                                                                                                                                         |
| `Token`                 | string               | The token of `Bearer`. In code, `TokenProvider` can give it instead.                                                                                                                                        |
| `Username`              | string               | The user name of `Basic`.                                                                                                                                                                                   |
| `Password`              | string               | The password of `Basic`.                                                                                                                                                                                    |
| `CertificatePath`       | path                 | A [client certificate](#client-certificate-mtls), a PKCS#12 (`.pfx`, `.p12`) or PEM file.                                                                                                                   |
| `CertificatePassword`   | string               | The password of the PKCS#12 file, or of the encrypted PEM key.                                                                                                                                              |
| `CertificateKeyPath`    | path                 | The PEM private key, when `CertificatePath` is a PEM certificate without its key.                                                                                                                           |
| `CertificateThumbprint` | string               | A client certificate in a certificate store, instead of a file.                                                                                                                                             |
| `CertificateStore`      | `<location>/<name>`  | The store of `CertificateThumbprint`: `CurrentUser/My` (the default) or `LocalMachine/My`, say.                                                                                                             |

Keys are case-insensitive. A key that isn't set leaves the `HttpClient` as it is. Any configuration source works: the environment
variable `Clients__RocketsClient__DefaultRequestHeaders__X-Api-Version` sets a header, for example.

### The connection string

A client can take its settings as one SqlClient-style string, as a database does. It is read from
`Clients:RocketsClient:ConnectionString` (`RocketsClientServiceCollectionExtensions.ConnectionStringKey`) when that key exists, else from
`ConnectionStrings:RocketsClient`, which is where Aspire and most hosts put connection strings. The first one found is used, and the two
are never merged.

```json
{
    "ConnectionStrings": {
        "RocketsClient": "Endpoint=https://rockets.example.com/;Timeout=00:00:30"
    }
}
```

It has the syntax of `DbConnectionStringBuilder`:

- `key=value` pairs separated by `;`. Whitespace around keys and unquoted values is trimmed.
- A value that contains `;`, or leading or trailing spaces, is quoted with `'` or `"`. A doubled quote stands for itself:
  `Endpoint='https://rockets.example.com/?a=1;b=2'`.
- An unquoted value may contain `=`.

It takes the keys of the table above, except `ConnectionString` and `DefaultRequestHeaders`. The parser is stricter than
`DbConnectionStringBuilder`: an unknown key and a key given twice (an alias with its key, `Endpoint` and `BaseAddress`, too) are errors.

When an application already has a connection string named after the client for something else, a database say, set
`Clients:RocketsClient:ConnectionString` to an empty value. The client then reads no connection string:

```json
{
    "ConnectionStrings": { "RocketsClient": "Data Source=db;Initial Catalog=rockets" },
    "Clients": { "RocketsClient": { "ConnectionString": "", "BaseAddress": "https://rockets.example.com/" } }
}
```

## Validation

The registration validates the options with a generated `IValidateOptions<RocketsClientOptions>`, and asks for it at startup
(`ValidateOnStart`). Under a host, a bad client stops the host from starting, with an `OptionsValidationException` that lists every
problem. With a bare `ServiceCollection`, the same exception is thrown when the client is first created.

It fails on:

- an unknown key under `Clients:RocketsClient`, or in the connection string;
- a key given twice, or an alias with its key;
- a value that doesn't parse (`Timeout`, `Resilience`, `Authentication`), or a malformed connection string;
- a `BaseAddress` that isn't an absolute `http` or `https` URI (or, with [service discovery](#service-discovery), a service-discovery
  address), or a `Timeout` that isn't positive;
- an `Authentication` without the keys it needs, with the keys of another method (`None` included), or with no host to send the
  credentials to (neither `BaseAddress` nor `AllowedHosts`);
- a [client certificate](#client-certificate-mtls) given by both a path and a thumbprint, a `CertificatePassword` or
  `CertificateKeyPath` without a `CertificatePath`, a `CertificateStore` without a `CertificateThumbprint`, a file that doesn't exist,
  and a certificate that doesn't load with its private key (a wrong password, say).

The messages name the source (`Clients:RocketsClient:Timeout`, `ConnectionStrings:RocketsClient`) and the key, never a value, so they
are safe to log even when a value is a secret.

## Resilience

Chirograph adds no resilience package. When the project references
[Microsoft.Extensions.Http.Resilience](https://www.nuget.org/packages/Microsoft.Extensions.Http.Resilience) (directly or through
another package or project), the build sees it among the resolved references and writes the resilience code into the registration:
it calls `AddStandardResilienceHandler()` on the client's builder, and takes the handler out again when the client's handlers are
built unless `Resilience` is `true`. Without the package the registration has no such code, it compiles, and `Resilience` is ignored.

The key turns on the handler with its default options. To tune them, or to add a handler of your own, chain on the builder the
registration returns instead.

## Service discovery

Chirograph adds no service-discovery package either. When the project references
[Microsoft.Extensions.ServiceDiscovery](https://www.nuget.org/packages/Microsoft.Extensions.ServiceDiscovery) (directly, or through
another package or project such as an Aspire service-defaults project), the build sees it among the resolved references and writes the
service-discovery code into the registration:

- it calls `AddServiceDiscovery()` on the client's builder, after the authentication handler;
- when no endpoint provider is registered yet, it also calls `AddServiceDiscovery()` on the services, which adds the configuration
  provider (`Services:<name>`) and the pass-through provider; register your own providers before `AddRocketsClient()` to use them
  instead;
- `BaseAddress` may be a [service-discovery address](https://learn.microsoft.com/dotnet/core/extensions/service-discovery), such as
  `https+http://rockets`;
- a client without a `BaseAddress` calls `https+http://<ClientName>`, so its endpoints come from `Services:<ClientName>`, or from the
  platform.

```json
{
    "Services": { "RocketsClient": { "https": ["https://10.0.0.5:8443/"] } }
}
```

Without the package, a client without a `BaseAddress` behaves as before, and a service-discovery address fails validation.

The authentication handler runs before the service-discovery handler, so the [host check](#which-hosts-get-the-credentials) compares
the logical host, `rockets` in `https+http://rockets`, not the endpoint it resolves to.

## Configuring the client further

The registration returns the `IHttpClientBuilder`, so everything `IHttpClientFactory` offers chains on it: handlers, resilience,
lifetimes, and a base address of your own that wins over the configuration.

```csharp
builder.Services
    .AddRocketsClient()
    .AddHttpMessageHandler<BearerTokenHandler>()
    .AddStandardResilienceHandler();
```

Chirograph adds a handler itself only when `Authentication` asks for one, and resilience only when `Resilience` does. Handlers chained on
the builder come after the authentication handler, so they see the credentials.

## Authentication

A client authenticates one of two ways. With `Authentication` set to a method, configuration gives the credentials and a generated
handler sends them. With `None`, the default, the container gives the client its authentication.

### Configured authentication

| `Authentication` | Keys                                                                                                       | Sends                                     |
| ---------------- | ---------------------------------------------------------------------------------------------------------- | ----------------------------------------- |
| `ApiKey`         | `ApiKey` (alias `Key`), and `ApiKeyHeader` (default `X-Api-Key`) or `ApiKeyQuery` (a query parameter name) | The key in the header, or in the query.   |
| `Bearer`         | `Token`, or `TokenProvider` in code                                                                        | `Authorization: Bearer <token>`.          |
| `Basic`          | `Username`, `Password`                                                                                     | `Authorization: Basic`, the two in UTF-8. |

`ClientCredentials` and `PrivateKeyJwt`, and the four Azure methods, are more methods, which the `OpenApiClient` item declares: see
[Client credentials](#client-credentials), [Private key JWT](#private-key-jwt) and [Azure](#azure).

```json
{
    "ConnectionStrings": {
        "RocketsClient": "Endpoint=https://rockets.example.com/;Authentication=ApiKey;Key=abc123"
    }
}
```

The keys of a method other than the one chosen are an error, so a token left over from another setup can't be sent by mistake.
`TokenProvider` is asked for the token on each request that gets it, so a token that expires is fetched again by the provider, which
caches it as it needs:

```csharp
builder.Services.AddRocketsClient(options =>
{
    options.Authentication = RocketsClientAuthentication.Bearer;
    options.TokenProvider = (services, cancellationToken) => services.GetRequiredService<ITokenSource>().GetTokenAsync(cancellationToken);
});
```

The handler is added to the client's `HttpClient`, so Kiota and Refit go through the same pipeline. With `Authentication` set:

- **Kiota:** the adapter gets `AnonymousAuthenticationProvider`, not the container's provider, so nothing sets the credentials twice.
- **Refitter:** the `RefitSettings` still come from the container, since they carry Refit's serializers.
- `ConfigureRequestAdapter` and `ConfigureRefitSettings` still have [the last word](#the-last-word).

#### Which hosts get the credentials

The handler adds the credentials only to a request for the `BaseAddress` host, on its port, or for a host in `AllowedHosts`. An entry of
`AllowedHosts` that is a host alone allows each of its ports; `host:port` allows that port only. A Kiota next-link to another host goes
without them.

The handler also follows redirects in place of the primary handler (it turns the primary handler's `AllowAutoRedirect` off, and follows
as many as that allowed), so each hop is checked the same way: a redirect to another host goes without the credentials, whichever
header or query parameter carries them.

With [service discovery](#service-discovery), the host is the logical one, and the endpoint it resolves to gets the credentials as well.
A redirect to another host still goes without them.

A client with an `Authentication` needs a host to send the credentials to: a `BaseAddress`, or `AllowedHosts`. With service discovery,
the default `https+http://<ClientName>` is one.

### Client credentials

`ClientCredentials` gets an OAuth access token with the client-credentials grant and sends it as `Authorization: Bearer <token>`, through
[Duende.AccessTokenManagement](https://docs.duendesoftware.com/accesstokenmanagement/) 4.x. The `OpenApiClient` item must declare it,
because it needs that package (the declaration allows [`PrivateKeyJwt`](#private-key-jwt) too):

```xml
<OpenApiClient Include="openapi/rockets.json" Generator="Kiota" ClientName="RocketsClient" Authentication="ClientCredentials" />
```

The metadata adds a reference to `Duende.AccessTokenManagement` (the version of
[`ChirographDuendeAccessTokenManagementVersion`](../../reference/properties/), unless the project references the package itself) and
writes the code that uses it. Without it, there is neither, and configuration that names the method fails
[validation](#validation) with a message that names the metadata to set.

| Key                     | Meaning                                                                                                              |
| ----------------------- | -------------------------------------------------------------------------------------------------------------------- |
| `TokenEndpoint`         | The token endpoint, an absolute `http` or `https` URI. Or:                                                           |
| `Authority`             | An absolute `https` URI whose discovery document (`/.well-known/openid-configuration`) gives the token endpoint.     |
| `ClientId`              | The client id. Required.                                                                                             |
| `ClientSecret`          | The client secret. Required. Keep it in user secrets or a vault, not in `appsettings.json`.                          |
| `Scope`                 | The scopes to ask for, space-separated. Optional.                                                                    |
| `Resource`              | The resource to ask for (RFC 8707). Optional.                                                                        |
| `ClientCredentialStyle` | `Header` (the default) sends the id and secret as HTTP Basic authentication; `Body` sends them in the token request. |

```json
{
    "ConnectionStrings": {
        "RocketsClient": "Endpoint=https://rockets.example.com/;Authentication=ClientCredentials;Authority=https://login.example.com/;ClientId=rockets-app;ClientSecret=…;Scope=rockets.read"
    }
}
```

The registration configures a Duende client-credentials client named after the client (`RocketsClient`) from the options, and the
authentication handler sets its token, so the [host check](#which-hosts-get-the-credentials) applies to it as to the other methods.
Duende caches the token and fetches a new one shortly before it expires (its `CacheLifetimeBuffer`, 60 seconds by default), so one
token serves every request until then. A `refresh_token` in the response isn't used: RFC 6749 §4.4.3 says the server shouldn't send
one.

With `Authority`, the discovery document is read on the first token request, not at startup, and cached
(Duende.IdentityModel's `DiscoveryCache`). Startup only checks that `Authority` is an absolute `https` URI, so a host starts without
the network. The discovery document and the token request go through Duende's back-channel `HttpClient`
(`ClientCredentialsTokenManagementDefaults.BackChannelHttpClientName`): configure a proxy or a certificate for both there.

A token request that fails fails the call with an `HttpRequestException` that gives the server's error, such as `invalid_client`.

#### The token cache

Duende keeps tokens in a `HybridCache`. Chirograph registers none: Duende's registration adds the default one, in memory. To keep them
in [FusionCache](https://github.com/ZiggyCreatures/FusionCache) (2.x, which adapts itself to `HybridCache`), register it for the whole
application:

```csharp
using ZiggyCreatures.Caching.Fusion;

builder.Services.AddFusionCache().AsHybridCache();
```

or for the tokens only, keyed with Duende's `ServiceProviderKeys.ClientCredentialsTokenCache`:

```csharp
using Duende.AccessTokenManagement;
using ZiggyCreatures.Caching.Fusion;

builder.Services.AddFusionCache().AsKeyedHybridCache(ServiceProviderKeys.ClientCredentialsTokenCache);
```

The cache key is Duende's: `<CacheKeyPrefix>::RocketsClient::<hashed scope>::<hashed resource>`. A distributed cache behind
`HybridCache` shares the tokens across instances; encrypt them there if the cache is shared (see Duende's
[client credentials](https://docs.duendesoftware.com/accesstokenmanagement/advanced/client-credentials/) docs).

Duende doesn't claim compatibility with native AOT: a native publish of a client that declares `ClientCredentials` warns about
`Microsoft.Extensions.Caching.Hybrid`'s default JSON serializer (its token cache), and works. A client that doesn't declare it adds no
package and no warning.

### Private key JWT

`PrivateKeyJwt` is `ClientCredentials` with a client assertion (`private_key_jwt`, RFC 7523) in place of the secret: the client proves
who it is with a short-lived JWT signed by its certificate. The `ClientCredentials` declaration on the `OpenApiClient` item allows it.
It takes the keys of `ClientCredentials` without `ClientSecret` and `ClientCredentialStyle`, plus a certificate, given with the
[certificate keys](#client-certificate-mtls):

```json
{
    "Clients": {
        "RocketsClient": {
            "BaseAddress": "https://rockets.example.com/",
            "Authentication": "PrivateKeyJwt",
            "Authority": "https://login.example.com/",
            "ClientId": "rockets-app",
            "CertificatePath": "/run/secrets/rockets-signing.pfx",
            "CertificatePassword": "…",
            "Scope": "rockets.read"
        }
    }
}
```

The assertion has `iss` and `sub` set to `ClientId`, `aud` set to the token endpoint (the configured one, or the one the discovery
document of `Authority` gives), a random `jti`, and `exp` five minutes ahead. It is signed through `Microsoft.IdentityModel.JsonWebTokens`,
which Duende brings: RS256 with an RSA key, ES256, ES384 or ES512 with an EC key. The certificate is loaded for each assertion, and
Duende asks for one only when it requests a token, so a renewed file is read on the next token request. Startup checks that a
certificate is given, and that it loads with its private key. A `ClientSecret` alongside fails [validation](#validation).

A certificate has one role: the one `PrivateKeyJwt` signs with is not also presented for [mutual TLS](#client-certificate-mtls).

The assertion comes from an `IClientAssertionService`, registered keyed with the client's name (`"RocketsClient"`). Duende asks the
unkeyed `IClientAssertionService` for the assertion of each of its clients; the registration makes that one hand each client to the
service keyed with its name, and any other to the service registered before it (Duende's, which makes none, or the application's).
An `IClientAssertionService` of the application's own registered after `AddRocketsClient()` replaces that one: register it before.

### Azure

The Azure methods get a Microsoft Entra ID token from an [Azure.Identity](https://learn.microsoft.com/dotnet/api/overview/azure/identity-readme)
credential and send it as `Authorization: Bearer <token>`. The `OpenApiClient` item declares them with `Azure`, which adds a reference
to `Azure.Identity` (the version of [`ChirographAzureIdentityVersion`](../../reference/properties/), unless the project references the
package itself) and writes the code that uses it:

```xml
<OpenApiClient Include="openapi/rockets.json" Generator="Kiota" ClientName="RocketsClient" Authentication="Azure" />
```

| `Authentication`        | Credential                                                 | Keys                                                                                |
| ----------------------- | ---------------------------------------------------------- | ----------------------------------------------------------------------------------- |
| `AzureDefault`          | `DefaultAzureCredential`                                   | `Scope`; optional `TenantId`, and `ClientId` (a user-assigned or workload identity) |
| `AzureManagedIdentity`  | `ManagedIdentityCredential`                                | `Scope`; optional `ClientId`, a user-assigned identity (system-assigned without)    |
| `AzureWorkloadIdentity` | `WorkloadIdentityCredential`                               | `Scope`; optional `TenantId` and `ClientId` (else the platform's variables)         |
| `AzureServicePrincipal` | `ClientSecretCredential`, or `ClientCertificateCredential` | `Scope`, `TenantId`, `ClientId`, and `ClientSecret` or the certificate keys         |

`Scope` is the scopes to ask for, space-separated: `api://rockets/.default`, say. `TenantId` is a tenant id or a domain.

```json
{
    "ConnectionStrings": {
        "RocketsClient": "Endpoint=https://rockets.example.com/;Authentication=AzureManagedIdentity;Scope=api://rockets/.default"
    }
}
```

The client has one credential, made from the options the first time a token is asked for, and made again when the options are
reloaded. The authentication handler asks it for a token on each request, so the [host check](#which-hosts-get-the-credentials)
applies; the credential caches the token and gets a new one before it expires. Startup checks the keys only: it asks Azure for nothing.
A credential that can't get a token fails the call with Azure.Identity's `AuthenticationFailedException` (or
`CredentialUnavailableException`).

`AzureServicePrincipal` takes `ClientSecret` or a certificate, given with the [certificate keys](#client-certificate-mtls), not both.
The certificate then signs the credential's assertions, and is not also presented for mutual TLS.

A `TokenCredential` registered keyed with the client's name is used instead of the one the method makes, for a credential Chirograph
doesn't make (`AzureCliCredential`, a `ChainedTokenCredential`) or a fake one in tests. The keys of the method are still checked, so
pair it with `AzureDefault`, which needs only `Scope`:

```csharp
builder.Services.AddKeyedSingleton<TokenCredential>("RocketsClient", new AzureCliCredential());
```

A native AOT publish of a client that declares `Azure` reports no trimming or AOT warnings.

### From the container

With `Authentication` set to `None`, when the client is created, the registration takes its authentication from the container, under
the key of the client's name first:

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

### The last word

A delegate on the options decides what the client uses at last. It gets what the container gave; without it, the client uses that.

- **Kiota:** `ConfigureRequestAdapter`, given the adapter built over the client's `HttpClient`. Return another adapter, say with an
  authentication provider of the library's own.
- **Refitter:** `ConfigureRefitSettings`, given the settings from the container, or `null`. Return settings with a serializer or a
  handler set.

```csharp
builder.Services.AddRocketsClient(options =>
    options.ConfigureRefitSettings = (services, settings) => new RefitSettings { ContentSerializer = mySerializer });
```

## Client certificate (mTLS)

A client can present a certificate to the server, for mutual TLS, whatever its `Authentication`, `None` included, unless
[`PrivateKeyJwt`](#private-key-jwt) or [`AzureServicePrincipal`](#azure) signs with it. Give it as a file:

```json
{
    "Clients": {
        "RocketsClient": {
            "BaseAddress": "https://rockets.example.com/",
            "CertificatePath": "/run/secrets/rockets-client.pfx",
            "CertificatePassword": "…"
        }
    }
}
```

- A PKCS#12 file (`.pfx`, `.p12`), with its `CertificatePassword` if it has one.
- A PEM certificate with its key in `CertificateKeyPath`, the key encrypted with `CertificatePassword` or not.
- A PEM file that holds both the certificate and its key.

Or as a certificate in a store: `CertificateThumbprint`, in `CertificateStore` (`CurrentUser/My` unless it says otherwise). The keys
work in a connection string too: `Endpoint=https://rockets.example.com/;CertificatePath=/run/secrets/rockets-client.pfx;CertificatePassword=…`.

The registration puts the certificate on the client's primary handler (`ConfigurePrimaryHttpMessageHandler`), the
`SocketsHttpHandler` or `HttpClientHandler` the factory made, not in a `DelegatingHandler`. It loads it again each time the handler is
made, so a renewed file is picked up when the handlers rotate. The certificate is checked at startup, with the rest of the options: it
must load, with its private key.

`ConfigurePrimaryHttpMessageHandler` with a handler of your own on the builder `AddRocketsClient()` returns (or a `RefitSettings`
`HttpMessageHandlerFactory`) replaces the handler that carries the certificate. The overload that changes the handler,
`ConfigurePrimaryHttpMessageHandler((handler, services) => …)`, keeps it.

## Without the registration

A generated client is an ordinary class or interface. Create one from an `HttpClient`, for example against a test server:

```csharp
var kiota = new RocketsClient(new HttpClientRequestAdapter(new AnonymousAuthenticationProvider(), httpClient: httpClient));   // or a provider of your own
var refit = RestService.For<IRocketsClient>(httpClient);
```

## With Dovetail

A Dovetail joint is one more caller of the registration. See [With Dovetail](../../guides/with-dovetail/).
