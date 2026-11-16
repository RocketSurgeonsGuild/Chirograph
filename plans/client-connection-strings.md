# Plan: connection strings and authentication for `Clients:<ClientName>`

Status: design agreed (2026-11-15). Nothing is implemented yet. It builds on
[#30](https://github.com/RocketSurgeonsGuild/Chirograph/pull/30) (the `Clients:<ClientName>` keys, container authentication and the
registration hooks) and [#29](https://github.com/RocketSurgeonsGuild/Chirograph/pull/29) (the Optionary schema), both merged.

Today a client is configured by four keys under `Clients:<ClientName>` (`BaseAddress`, `Timeout`, `DefaultRequestHeaders`,
`Resilience`), and its authentication comes from the container: a keyed or unkeyed `IAuthenticationProvider` (Kiota) or
`RefitSettings` (Refit). Each client project must implement a partial-method hook, written for it by a scaffolded registration file.

This plan adds:

- a SqlClient-style **connection string** for a client, read from `Clients:<ClientName>:ConnectionString` or
  `ConnectionStrings:<ClientName>`;
- **authentication declared in configuration**: API key, bearer, basic, OAuth client credentials, `private_key_jwt`, Azure
  credentials, and a client certificate (mTLS);
- a generated, public **options class** per client. It is validated at startup and replaces the partial-method hooks and the
  scaffolded registration file;
- **service discovery**: `BaseAddress` may be a logical address such as `https+http://rockets`;
- a **per-client generated schema**, in place of the two static schema files.

---

## Contents

1. [Decisions](#decisions)
2. [Configuration](#configuration)
3. [Authentication](#authentication)
4. [The options class and the registration](#the-options-class-and-the-registration)
5. [Validation](#validation)
6. [Service discovery](#service-discovery)
7. [Schema](#schema)
8. [MSBuild surface](#msbuild-surface)
9. [Breaking changes](#breaking-changes)
10. [Test strategy](#test-strategy)
11. [Milestones](#milestones)
12. [Risks](#risks)
13. [Sources](#sources)

---

## Decisions

| #   | Question                                        | Decision                                                                                                                                                                                                                                                                                                                                                     |
| --- | ----------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| 1   | Where the runtime code lives                    | Generated into the client project, like today. Chirograph still ships only MSBuild files. The heavy methods use a library that the project references only when its `OpenApiClient` item asks for it ([MSBuild surface](#msbuild-surface)).                                                                                                                  |
| 2   | Where a connection string comes from            | `Clients:<ClientName>:ConnectionString`, else `ConnectionStrings:<ClientName>`. The first one found is used. The two are never merged. `Clients:<ClientName>` is always an object, never a bare string.                                                                                                                                                      |
| 3   | Connection string against the other settings    | A value in the connection string overrides every other source, code included.                                                                                                                                                                                                                                                                                |
| 4   | How the method is chosen                        | Explicitly, with `Authentication=`, as SqlClient does. Keys of another method are an error.                                                                                                                                                                                                                                                                  |
| 5   | Methods in the first release                    | `None`, `ApiKey`, `Bearer`, `Basic`, `ClientCredentials`, `PrivateKeyJwt`, `AzureDefault`, `AzureManagedIdentity`, `AzureWorkloadIdentity`, `AzureServicePrincipal`, plus a client certificate (mTLS) with any of them.                                                                                                                                      |
| 6   | Client credentials                              | Through [Duende.AccessTokenManagement](https://docs.duendesoftware.com/accesstokenmanagement/), referenced only when the item declares it. Duende fetches a new token before the old one expires. A `refresh_token` in the response is not used: RFC 6749 §4.4.3 says the server should not send one.                                                        |
| 7   | `private_key_jwt`                               | A generated, keyed `IClientAssertionService` signs the assertion with a certificate from configuration, given the same way as the mTLS certificate.                                                                                                                                                                                                          |
| 8   | Token cache                                     | Duende's. Chirograph never registers a `HybridCache`. An app swaps in FusionCache (or any `HybridCache`) through the container, as Duende documents.                                                                                                                                                                                                         |
| 9   | Configured authentication against the container | Configuration wins. With `Authentication` set, Kiota gets `AnonymousAuthenticationProvider` instead of the container's provider, and the handler sets the credentials. Refit still takes `RefitSettings` from the container.                                                                                                                                 |
| 10  | OIDC discovery                                  | `Authority` may replace `TokenEndpoint`. The discovery document is fetched lazily, on the first token request, and cached.                                                                                                                                                                                                                                   |
| 11  | Parser                                          | Generated and strict: unknown keys and duplicate keys are errors, and no error repeats a value.                                                                                                                                                                                                                                                              |
| 12  | When a misconfiguration fails                   | At startup, through `ValidateOnStart`. Without a host, when the client is first created.                                                                                                                                                                                                                                                                     |
| 13  | Options                                         | A generated public options class per client. It replaces the `CreateRequestAdapter`, `CreateRefitSettings` and `ConfigureHttpClientBuilder` hooks and the scaffolded registration file.                                                                                                                                                                      |
| 14  | Credentials and hosts                           | Credentials go only to the `BaseAddress` host and to hosts listed in `AllowedHosts`.                                                                                                                                                                                                                                                                         |
| 15  | Service discovery                               | `BaseAddress` may be a service-discovery address. When the project references `Microsoft.Extensions.ServiceDiscovery`, the registration calls `AddServiceDiscovery()` on the client's builder (the application registers the providers, and validation fails at startup without any), and a client without a `BaseAddress` uses `https+http://<ClientName>`. |
| 16  | Schema                                          | Written per client into `obj/`. It lists only the methods that client allows, and requires each method's keys.                                                                                                                                                                                                                                               |

Rejected along the way:

- **A Chirograph runtime package.** It would end the MSBuild-only package. Generating the code keeps that, and package gating is
  already the pattern for Resilience.
- **A token client written by Chirograph.** It would mean owning token caching and renewal. Duende does both, under Apache-2.0.
- **Merging the two connection strings.** A database connection string with the client's name would break the client. With
  first-found, setting `Clients:<ClientName>:ConnectionString` (even to an empty value) shields the client from
  `ConnectionStrings:<ClientName>`.
- **Inferring the method from the keys present.** It is ambiguous, and an explicit key maps to a schema discriminator.

---

## Configuration

### Sources and order

From lowest to highest:

1. The keys under `Clients:<ClientName>`.
2. Code: the `configure` argument of `Add<ClientName>()`, then any `services.Configure<<ClientName>Options>(…)`.
3. The connection string: `Clients:<ClientName>:ConnectionString` if that key exists, else `ConnectionStrings:<ClientName>`. It is
   applied as a post-configuration, so it overrides everything else. Code that must win over it uses `PostConfigure` itself.

An empty `Clients:<ClientName>:ConnectionString` means "no connection string". `ConnectionStrings:<ClientName>` is then not read.

```json
{
    "ConnectionStrings": {
        "RocketsClient": "Endpoint=https://rockets.example.com/;Authentication=ApiKey;Key=abc123"
    },
    "Clients": {
        "RocketsClient": {
            "Timeout": "00:00:30",
            "DefaultRequestHeaders": { "X-Api-Version": "2" },
            "Resilience": true
        }
    }
}
```

### Keys

The same names are used in the object and in the connection string. They are case-insensitive.

| Key                     | Object | Connection string | Meaning                                                                                                             |
| ----------------------- | :----: | :---------------: | ------------------------------------------------------------------------------------------------------------------- |
| `ConnectionString`      |   ✓    |                   | The client's connection string.                                                                                     |
| `BaseAddress`           |   ✓    |         ✓         | Absolute URI or service-discovery address. Alias `Endpoint`.                                                        |
| `Timeout`               |   ✓    |         ✓         | `TimeSpan`, invariant culture.                                                                                      |
| `Resilience`            |   ✓    |         ✓         | `bool`. Read only when the project references `Microsoft.Extensions.Http.Resilience` (unchanged from #30).          |
| `DefaultRequestHeaders` |   ✓    |                   | String-to-string map. Object only.                                                                                  |
| `AllowedHosts`          |   ✓    |         ✓         | More hosts that may receive credentials. In the object, an array. In the connection string, a comma-separated list. |
| `Authentication`        |   ✓    |         ✓         | The method. See [Authentication](#authentication). Default `None`.                                                  |
| method keys             |   ✓    |         ✓         | See the table in [Authentication](#authentication).                                                                 |
| certificate keys        |   ✓    |         ✓         | See [Client certificate](#client-certificate-mtls).                                                                 |

### Connection-string syntax

The same syntax as `DbConnectionStringBuilder`:

- `key=value` pairs separated by `;`. Whitespace around keys and unquoted values is trimmed.
- A value that contains `;`, or leading or trailing spaces, is quoted with `'` or `"`. A doubled quote escapes it.
- An unquoted value may contain `=`.

The parser is generated, so it avoids `DbConnectionStringBuilder` and the trimming cost of its type descriptor. It is stricter:

- An unknown key is an error. So is a duplicate key, where `DbConnectionStringBuilder` would keep the last value.
- An alias and its key (`Endpoint` and `BaseAddress`) together count as a duplicate.
- Errors name the source (`Clients:RocketsClient:ConnectionString` or `ConnectionStrings:RocketsClient`) and the key, never a value.

---

## Authentication

Configured authentication runs in a `DelegatingHandler` on the client's `HttpClient`. That gives Kiota and Refit the same pipeline.
When `Authentication` is not `None`:

- Kiota's adapter gets `AnonymousAuthenticationProvider`, not the container's provider, so nothing sets the header twice.
- Refit still takes `RefitSettings` from the container (keyed, else unkeyed, else none). They carry its serializers.
- `ConfigureRequestAdapter` and `ConfigureRefitSettings` on the options still have the last word.

The handler adds credentials only to a request whose host is the `BaseAddress` host or is in `AllowedHosts`. An absolute next-link or
a redirect to another host goes without them. With service discovery, the comparison uses the logical host: the auth handler runs
before the service-discovery handler resolves it.

### Methods

| `Authentication`        | Needs on the item   | Keys                                                                                                                                                 | Implementation                                                                                     |
| ----------------------- | ------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------- |
| `None`                  | —                   | —                                                                                                                                                    | No handler.                                                                                        |
| `ApiKey`                | —                   | `ApiKey` (alias `Key`), and one of `ApiKeyHeader` (default `X-Api-Key`) or `ApiKeyQuery` (a query parameter name)                                    | Generated handler.                                                                                 |
| `Bearer`                | —                   | `Token`, or `TokenProvider` in code                                                                                                                  | Generated handler. `Authorization: Bearer <token>`.                                                |
| `Basic`                 | —                   | `Username`, `Password`                                                                                                                               | Generated handler.                                                                                 |
| `ClientCredentials`     | `ClientCredentials` | `TokenEndpoint` or `Authority`, `ClientId`, `ClientSecret`, optional `Scope`, `Resource`, `ClientCredentialStyle` (`Header`, the default, or `Body`) | Duende: a client-credentials client named after the client, and its token handler.                 |
| `PrivateKeyJwt`         | `ClientCredentials` | As `ClientCredentials`, without `ClientSecret`, plus the certificate keys                                                                            | Duende, with a generated keyed `IClientAssertionService`.                                          |
| `AzureDefault`          | `Azure`             | `Scope`, optional `TenantId`, `ClientId`                                                                                                             | `Azure.Identity` `DefaultAzureCredential`, through a generated handler that calls `GetTokenAsync`. |
| `AzureManagedIdentity`  | `Azure`             | `Scope`, optional `ClientId` (a user-assigned identity)                                                                                              | `ManagedIdentityCredential`.                                                                       |
| `AzureWorkloadIdentity` | `Azure`             | `Scope`, optional `TenantId`, `ClientId`                                                                                                             | `WorkloadIdentityCredential`.                                                                      |
| `AzureServicePrincipal` | `Azure`             | `Scope`, `TenantId`, `ClientId`, and `ClientSecret` or the certificate keys                                                                          | `ClientSecretCredential` or `ClientCertificateCredential`.                                         |

`ApiKey`, `Bearer`, `Basic` and the client certificate are always available. The others are written only when the `OpenApiClient`
item declares them ([MSBuild surface](#msbuild-surface)). A configuration that names a method the item did not declare fails
validation, and the message names the item metadata to set.

`ClientCredentials` details:

- Duende caches the token and fetches a new one shortly before it expires (its `CacheLifetimeBuffer`).
- `Authority` without `TokenEndpoint`: the token endpoint comes from `<Authority>/.well-known/openid-configuration`, fetched on the
  first token request through `Duende.IdentityModel`'s discovery client and cached. Startup validation only checks that `Authority` is
  an absolute `https` URI, so startup never needs the network.
- The cache is Duende's `HybridCache`. Chirograph registers none. An app that wants FusionCache registers it as a `HybridCache`,
  either for the whole app (`services.AddSingleton<HybridCache>(…)`) or for tokens only, keyed with
  `ServiceProviderKeys.ClientCredentialsTokenCache`. The docs show both, with the exact FusionCache calls of the version the test
  uses.

`PrivateKeyJwt` details: the assertion is a short-lived JWT with `iss` and `sub` set to `ClientId`, `aud` set to the token endpoint,
a random `jti`, and `exp` a few minutes ahead. It is signed with the certificate's private key through
`Microsoft.IdentityModel.JsonWebTokens`, which Duende already brings.

### Client certificate (mTLS)

Usable with any method, `None` included. The certificate goes on the primary handler (`ConfigurePrimaryHttpMessageHandler` with a
`SocketsHttpHandler`), not in a `DelegatingHandler`.

| Key                     | Meaning                                                                  |
| ----------------------- | ------------------------------------------------------------------------ |
| `CertificatePath`       | A `.pfx`/`.p12` or PEM file.                                             |
| `CertificatePassword`   | Its password, if any.                                                    |
| `CertificateKeyPath`    | The PEM private key, when `CertificatePath` is a PEM certificate.        |
| `CertificateThumbprint` | A certificate in a store, instead of a file.                             |
| `CertificateStore`      | `CurrentUser/My` (default) or `LocalMachine/My`, as `<location>/<name>`. |

A path and a thumbprint together are an error. `PrivateKeyJwt` and `AzureServicePrincipal` reuse these keys for their signing
certificate. When either of them sets a certificate, that certificate is not also used for mTLS: one certificate has one role.

---

## The options class and the registration

For a client named `RocketsClient`, the registration writes `RocketsClientOptions` into the client's namespace. Sketch (names may
move in review):

```csharp
public sealed class RocketsClientOptions
{
    public string? BaseAddress { get; set; }                        // absolute URI or service-discovery address
    public TimeSpan? Timeout { get; set; }
    public IDictionary<string, string> DefaultRequestHeaders { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public bool Resilience { get; set; }
    public IList<string> AllowedHosts { get; } = [];

    public RocketsClientAuthentication Authentication { get; set; }  // generated enum: only the methods the item allows
    public string? ApiKey { get; set; }
    public string? ApiKeyHeader { get; set; }
    public string? ApiKeyQuery { get; set; }
    public string? Token { get; set; }
    public Func<IServiceProvider, CancellationToken, ValueTask<string>>? TokenProvider { get; set; }   // code only
    public string? Username { get; set; }
    public string? Password { get; set; }
    public Uri? TokenEndpoint { get; set; }
    public Uri? Authority { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? Scope { get; set; }
    public string? Resource { get; set; }
    public RocketsClientCredentialStyle ClientCredentialStyle { get; set; }
    public string? TenantId { get; set; }
    public string? CertificatePath { get; set; }
    public string? CertificatePassword { get; set; }
    public string? CertificateKeyPath { get; set; }
    public string? CertificateThumbprint { get; set; }
    public string? CertificateStore { get; set; }

    // Kiota only
    public Func<IServiceProvider, HttpClientRequestAdapter, IRequestAdapter>? ConfigureRequestAdapter { get; set; }
    // Refit only
    public Func<IServiceProvider, RefitSettings?, RefitSettings?>? ConfigureRefitSettings { get; set; }
}
```

- The class is filled from `IConfiguration` by generated code, with no reflection binder. It is public, so an application can
  configure a client that a library generated: `services.Configure<RocketsClientOptions>(…)`.
- The registration reads it through `IOptionsMonitor<RocketsClientOptions>` when the `HttpClient` is created. A configuration reload
  applies when the handlers rotate.
- The registration becomes `Add<ClientName>(this IServiceCollection services, Action<<ClientName>Options>? configure = null)`. It
  still returns the `IHttpClientBuilder`, which is where handlers, resilience and the like are added.
- `SectionKey`, `BaseAddressKey` and `ResilienceKey` stay, and `ConnectionStringKey` is added.
- The registration class is no longer `partial`, and a client project writes no code of its own.

Removed:

| What                                                                                   | Replaced by                                                                       |
| -------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------- |
| `CreateRequestAdapter` (required, CS8795 without it)                                   | `ConfigureRequestAdapter` on the options; the default is the adapter as built.    |
| `CreateRefitSettings` (required, CS8795 without it)                                    | `ConfigureRefitSettings` on the options; the default is the container's settings. |
| `ConfigureHttpClientBuilder` (optional)                                                | The `IHttpClientBuilder` that `Add<ClientName>()` returns.                        |
| The scaffolded registration file, `RegistrationFile`, `ChirographScaffoldRegistration` | Nothing: there is no hook left to implement.                                      |

---

## Validation

- A generated `IValidateOptions<<ClientName>Options>` checks the parsed values: the method's required keys are present, no other
  method's keys are set, URIs are absolute, a certificate has a path or a thumbprint but not both, and the method was declared on the
  item.
- `Add<ClientName>()` calls `AddOptions<<ClientName>Options>().ValidateOnStart()`. Under a host, a bad client stops startup. With a
  bare `ServiceCollection`, the same validation runs when the client is first created.
- Parse errors (unknown key, duplicate, malformed string, bad `Timeout`) are validation failures too, so they surface at startup.
- No message contains a value from configuration.

---

## Service discovery

- When `@(ReferencePath)` contains `Microsoft.Extensions.ServiceDiscovery`, the registration writes a `//#if ServiceDiscovery` block
  that calls `AddServiceDiscovery()` on the builder, the same way #30 gates Resilience. Chirograph adds no package.
- With the package, a client without a `BaseAddress` uses `https+http://<ClientName>`. Endpoints then come from
  `Services:<ClientName>` (or the platform), as the
  [service-discovery docs](https://learn.microsoft.com/dotnet/core/extensions/service-discovery) describe.
- Without the package, a missing `BaseAddress` keeps today's behaviour: Kiota uses the document's first server, and Refit fails when
  called. A logical address such as `https+http://x` without the package fails validation.

---

## Schema

#29 ships two static files, `kiota.schema.json` and `refitter.schema.json`. They are replaced by one schema per client, written at
build into `obj/` and declared with `OptionarySchemaFragment` as today:

- The schema allows the object keys and `ConnectionString`.
- `Authentication` is an enum of the methods that client allows: `None`, `ApiKey`, `Bearer` and `Basic` always, plus those its item
  declares.
- An `if`/`then` per method requires its keys and forbids the other methods' keys. Secret keys are never required (agreed
  deviation): a secret belongs in user secrets, a vault or the environment, which the schema of `appsettings.json` doesn't see, so
  requiring it would flag a correct file. Validation at startup still requires them.
- Secret keys (`ApiKey`, `Token`, `Password`, `ClientSecret`, `CertificatePassword`) say in their description to keep them out of
  `appsettings.json`, for example in user secrets or a vault.
- `ConnectionString` stays a plain string: JSON Schema cannot check its contents.
- `ConnectionStrings:<ClientName>` is not declared, because Optionary schemas describe sections the app owns.
- `BaseAddress` is not required, for Refitter either (agreed deviation from "required for Refitter, as today"): a connection string
  can supply it, and with the service-discovery package the default address applies. A Refitter client without one fails when it is
  called, as before.

The drift test changes from "both static files have the same keys" to "the generated schema's keys match the options class and the
docs".

---

## MSBuild surface

### `OpenApiClient` metadata

| Metadata         | Default | Meaning                                                                                                                                                                                       |
| ---------------- | ------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Authentication` | empty   | A `;` list of the heavy methods the client may use: `ClientCredentials` (also allows `PrivateKeyJwt`) and `Azure` (the four Azure methods). An unknown value is a new diagnostic, `CHIRO109`. |

For each value, the SDK adds an implicit package reference, unless the project already references the package (the project's own
reference wins, as for the other implicit references), and keeps the matching `//#if` blocks in the templates:

| Value               | Package                        | Version property                               |
| ------------------- | ------------------------------ | ---------------------------------------------- |
| `ClientCredentials` | `Duende.AccessTokenManagement` | `ChirographDuendeAccessTokenManagementVersion` |
| `Azure`             | `Azure.Identity`               | `ChirographAzureIdentityVersion`               |

Both versions are packed into `Packages.props` like the existing ones, and Renovate updates them.

### Properties

`ChirographScaffoldRegistration` is removed. No other property changes.

### Templates

The registration templates gain `//#if ClientCredentials`, `//#if Azure` and `//#if ServiceDiscovery` blocks, cut by the same
mechanism as `//#if Resilience`. The options class, parser, validator and simple handlers are written as one more generated file per
client.

---

## Breaking changes

All of these follow #30, so none of them has been released yet. They are listed for the release notes:

- `Add<ClientName>()` gains an optional `configure` parameter. The registration class is no longer `partial`.
- The `CreateRequestAdapter`, `CreateRefitSettings` and `ConfigureHttpClientBuilder` partial methods are gone. A project that
  kept its scaffolded registration file fails to compile until it deletes that file.
- `RegistrationFile` and `ChirographScaffoldRegistration` are gone.
- With `Authentication` configured, a keyed `IAuthenticationProvider` for that client is no longer used.
- A key that #30 ignored (anything unknown under `Clients:<ClientName>`) now fails validation.
- The static `kiota.schema.json` and `refitter.schema.json` are no longer packed.
- With `Authentication` set, redirects are followed by the authentication handler, not the primary handler: it turns the primary
  handler's `AllowAutoRedirect` off and follows as many as that allowed, dropping the credentials on the way to another host and
  never copying the `Host` header.
- Beside `Microsoft.Extensions.ServiceDiscovery`, the implicit `Microsoft.Extensions.Http` reference is dropped (it would downgrade
  the version service discovery brings, NU1605).
- A bare string at `Clients:<ClientName>` (not an object) is an error: a connection string goes in `ConnectionString` or
  `ConnectionStrings:<ClientName>`.
- With `Microsoft.Extensions.ServiceDiscovery` referenced, the application registers service discovery itself
  (`services.AddServiceDiscovery()`, or its own providers after `AddServiceDiscoveryCore()`); validation fails at startup without a
  provider.

---

## Test strategy

Build-seam tests on net10.0 and net11.0, for both generators, with snapshot results only, as #30 does. `RocketsApi` gains endpoints that
echo the credentials it saw:

- `Authorization` and `X-Api-Key` headers;
- the `api_key` query parameter;
- the client certificate's thumbprint.

It also gains an in-test token endpoint that issues client-credentials tokens, counts the requests, accepts a `client_assertion`, and
serves a discovery document.

| Area                | Tests                                                                                                                                                                                                                                                           |
| ------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Sources and order   | Object keys only. `Clients:<Name>:ConnectionString` overriding keys and code. `ConnectionStrings:<Name>` used when the first is absent. An empty `Clients:<Name>:ConnectionString` shielding a SQL `ConnectionStrings:<Name>`. `PostConfigure` in code winning. |
| Parser              | Quoting, escaped quotes, `=` in values, aliases, unknown key, duplicate key, alias and key together, and an error that does not echo the value. Unit-level, against the generated parser compiled once.                                                         |
| Validation          | `ValidateOnStart` stops a host. A bare `ServiceCollection` fails on first creation. An undeclared method gives a message that names the metadata.                                                                                                               |
| Simple methods      | `ApiKey` (header, custom header, query), `Bearer` (`Token` and `TokenProvider`), `Basic`. The host check drops credentials on a redirect, and `AllowedHosts` lets them through.                                                                                 |
| Container interplay | A keyed `IAuthenticationProvider` used when `Authentication` is `None`, and ignored when it is set. `ConfigureRequestAdapter` and `ConfigureRefitSettings` replace what was built.                                                                              |
| mTLS                | A file certificate and a PEM pair reach `RocketsApi`. A path and a thumbprint together fail validation.                                                                                                                                                         |
| Client credentials  | One token request for several calls. A new token after expiry. `Header` and `Body` styles. `Authority` discovery fetched once, lazily. FusionCache as the keyed token cache holds the token.                                                                    |
| `PrivateKeyJwt`     | The token endpoint receives a valid assertion signed by the configured certificate.                                                                                                                                                                             |
| Azure               | Validation for each method's keys. The handler asks the credential for `Scope`, through a fake `TokenCredential` registered for the test. No live Azure.                                                                                                        |
| Service discovery   | With the package, `https+http://rockets` resolves through `Services:rockets`, and the default `https+http://<ClientName>` works. Without it, a logical address fails validation.                                                                                |
| Gating              | No `Authentication` metadata adds no package and writes no Duende or Azure code. Each value adds its package. The project's own reference wins. `CHIRO109`.                                                                                                     |
| Schema              | Per-client schema snapshot with and without each metadata value, and the drift test.                                                                                                                                                                            |
| AOT                 | A `PublishAot` smoke test of a client with `ApiKey` and of one with `ClientCredentials`, recording the warnings in a snapshot ([Risks](#risks)).                                                                                                                |

The existing `AuthenticationTests` and `RegistrationScaffoldTests` are rewritten or deleted along with the hooks. Their scenarios move
to the options-delegate tests above.

---

## Milestones

Each milestone is one pull request. Each one updates the docs (`concepts/registration.md`, the reference pages and the guides) and
the drift tests for what it adds. The sample gains one authenticated client in M2.

| #   | Milestone                                                                                   | Depends on | Issue                                                                        |
| --- | ------------------------------------------------------------------------------------------- | ---------- | ---------------------------------------------------------------------------- |
| M1  | Options class, connection strings and startup validation; remove the hooks and the scaffold | —          | [Chirograph#33](https://github.com/RocketSurgeonsGuild/Chirograph/issues/33) |
| M2  | `ApiKey`, `Bearer` and `Basic`, the auth handler and the host check                         | M1         | [Chirograph#34](https://github.com/RocketSurgeonsGuild/Chirograph/issues/34) |
| M3  | Client certificate (mTLS)                                                                   | M1         | [Chirograph#35](https://github.com/RocketSurgeonsGuild/Chirograph/issues/35) |
| M4  | Per-client generated schema                                                                 | M1         | [Chirograph#36](https://github.com/RocketSurgeonsGuild/Chirograph/issues/36) |
| M5  | `Authentication` item metadata and `ClientCredentials` through Duende                       | M2         | [Chirograph#37](https://github.com/RocketSurgeonsGuild/Chirograph/issues/37) |
| M6  | `PrivateKeyJwt`                                                                             | M3, M5     | [Chirograph#38](https://github.com/RocketSurgeonsGuild/Chirograph/issues/38) |
| M7  | Azure credentials                                                                           | M3, M5     | [Chirograph#39](https://github.com/RocketSurgeonsGuild/Chirograph/issues/39) |
| M8  | Service discovery                                                                           | M2         | [Chirograph#40](https://github.com/RocketSurgeonsGuild/Chirograph/issues/40) |

M2, M3 and M4 can run in parallel once M1 lands. So can M6, M7 and M8 once their dependencies have. Every milestone after M4 adds its
keys to the schema generator. The tracking issue is [Chirograph#32](https://github.com/RocketSurgeonsGuild/Chirograph/issues/32), on the org project
[Chirograph](https://github.com/orgs/RocketSurgeonsGuild/projects/4), with the "blocked by" links above.

---

## Risks

- **AOT and trimming.** Duende pulls in `System.IdentityModel.Tokens.Jwt` and does not claim AOT compatibility. `Azure.Identity`'s
  status is unconfirmed. Both are opt-in through item metadata, so a project that doesn't ask for them stays clean. The AOT smoke
  test records the warnings.
- **Secrets in configuration.** A connection string puts secrets in one value that is easy to log. The generated code never logs
  options or connection strings. The docs point to user secrets and vaults, and to `ConnectionStrings:<Name>` from Aspire parameters.
- **Name collision with a database connection string.** Covered by first-found and the empty-string shield. A collision that isn't
  shielded fails validation on the unknown key, with a message that suggests the shield.
- **Primary-handler conflicts.** mTLS sets the primary handler. An app that also calls `ConfigurePrimaryHttpMessageHandler` on the
  returned builder replaces it. The docs say so.
- **Duende's major versions.** Its 4.x API (strongly typed `ClientId`, `Scope` and so on) differs from 3.x. Only 4.x is supported, and
  the version property is pinned to it.

---

## Sources

- RFC 6749 §4.4.3, the client credentials access token response: "A refresh token SHOULD NOT be included."
- Duende.AccessTokenManagement 4.x docs: [client credentials](https://docs.duendesoftware.com/accesstokenmanagement/advanced/client-credentials/)
  (token caching through `HybridCache`, the keyed `ServiceProviderKeys.ClientCredentialsTokenCache`, the cache key format,
  `IClientAssertionService`) and [workers](https://docs.duendesoftware.com/accesstokenmanagement/workers/)
  (`AddClientCredentialsTokenManagement`, `AddClientCredentialsTokenHandler`).
- SqlClient [connection options](https://learn.microsoft.com/sql/connect/ado-net/connection-options#authentication-options): the
  `Authentication=` keyword and its values.
- ADO.NET [connection string syntax](https://learn.microsoft.com/dotnet/framework/data/adonet/connection-strings) and
  [builders](https://learn.microsoft.com/dotnet/framework/data/adonet/connection-string-builders).
- .NET [service discovery](https://learn.microsoft.com/dotnet/core/extensions/service-discovery): scheme lists such as
  `https+http://`, and the `Services` configuration section.
- Kiota [authentication providers](https://learn.microsoft.com/openapi/kiota/authentication), including the `AllowedHosts` idea.
- Aspire's OpenAI integration: the `Endpoint=…;Key=…` connection string, which the `Endpoint` and `Key` aliases follow.
