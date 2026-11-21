# Migrating from OpenApiReference

`Microsoft.Extensions.ApiDescription.Client` gave projects the `OpenApiReference` and `OpenApiProjectReference` items, and the
`dotnet openapi` command wrote them. Microsoft deprecated it in .NET 10
([breaking change](https://learn.microsoft.com/en-us/dotnet/core/compatibility/aspnet-core/10/apidescription-client-deprecated)), and
now points people at each generator's own tooling. Chirograph is that tooling for Kiota and Refitter, wired into the build the way
`OpenApiReference` was.

## What changes

`OpenApiReference` ran NSwag (`NSwagCSharp`, `NSwagTypeScript`) by default. Chirograph runs Kiota or Refitter, which generate a
different client, so the code that calls the client changes too. Pick the generator first:

- **Refitter** writes a [Refit](https://github.com/reactiveui/refit) interface with one method per operation. It is the closer of the
  two to NSwag's one-class-per-API client.
- **Kiota** writes a request-builder client (`client.Rockets[id].GetAsync()`).

See [Generators](../../concepts/generators/) for the differences.

## Item by item

| `OpenApiReference`                                      | `OpenApiClient`                                                                |
| ------------------------------------------------------- | ------------------------------------------------------------------------------ |
| `Include="openapi.json"`                                | `Include="openapi.json"`, unchanged.                                           |
| `CodeGenerator="NSwagCSharp"`                           | `Generator="Refitter"` or `Generator="Kiota"`. It is required.                 |
| `ClassName="RocketsClient"`                             | `ClientName="RocketsClient"`. It is required, and also names the registration. |
| `Namespace="MyApp.Rockets"`                             | `Namespace="MyApp.Rockets"`, unchanged.                                        |
| `Options="/UseBaseUrl:false"`                           | `Options="…"`, but with the new generator's command-line options.              |
| `OutputPath="RocketsClient.cs"`                         | None. The code always goes to `obj`, and is never committed.                   |
| `SourceUrl="https://…"` (from `dotnet openapi add url`) | `Include="https://…"` with `DocumentCache` set to the old `Include` file.      |

`OpenApiProjectReference Include="../Rockets.Api/Rockets.Api.csproj"` becomes an `OpenApiClient` whose `Include` is that project:

```xml
<OpenApiClient Include="../Rockets.Api/Rockets.Api.csproj" Generator="Refitter" ClientName="RocketsClient" />
```

## Step by step

1. Remove the `Microsoft.Extensions.ApiDescription.Client` and `NSwag.ApiDescription.Client` package references.
2. Add the Chirograph SDK to the project, as in [Getting started](../).
3. Turn each `OpenApiReference` or `OpenApiProjectReference` into an `OpenApiClient` with the table above.
4. For a URL that `dotnet openapi add url` added, keep the downloaded file and make it the cache:

   ```xml
    <!-- before -->
    <OpenApiReference Include="openapi/rockets.json" SourceUrl="https://rockets.example.com/openapi/v1.json" />

    <!-- after -->
    <OpenApiClient
      Include="https://rockets.example.com/openapi/v1.json"
      Generator="Refitter"
      ClientName="RocketsClient"
      DocumentCache="openapi/rockets.json"
    />
    ```

    `dotnet openapi refresh` is no longer needed: the build updates the cache itself, or doesn't, as [From a URL](../from-a-url/)
    explains.

5. Replace the client's construction. Where you created NSwag's client from an `HttpClient`, register it instead and read its base
   address from configuration:

    ```csharp
    builder.Services.AddRocketsClient();
    ```

    ```json
    { "Clients": { "RocketsClient": { "BaseAddress": "https://rockets.example.com/" } } }
    ```

6. Update the call sites to the new client's methods.