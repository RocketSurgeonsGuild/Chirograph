# From a server project

A server project is a project in the same build whose OpenAPI document `Microsoft.Extensions.ApiDescription.Server` writes when it
builds. Pointing a client at it keeps the server and the client in step: change an endpoint, build, and the client changes with it.

## Make the server write its document

The server references `Microsoft.AspNetCore.OpenApi`, which builds the document, and `Microsoft.Extensions.ApiDescription.Server`,
which runs the application at build time and writes every document it registers into the server's `obj` folder:

```xml
<!-- Rockets.Api/Rockets.Api.csproj -->
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="10.0.15" />
    <PackageReference Include="Microsoft.Extensions.ApiDescription.Server" Version="10.0.15" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

```csharp
// Rockets.Api/Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();

var app = builder.Build();
app.MapOpenApi();
app.MapGet("/rockets/{id:guid}", (Guid id) => new Rocket(id, "Falcon")).WithName("GetRocket");
app.Run();

record Rocket(Guid Id, string SerialNumber);
```

The server does not need Chirograph. It can be a `.csproj`, `.fsproj` or `.vbproj`.

## Point the client at it

```xml
<!-- MyApp/MyApp.csproj -->
<ItemGroup>
  <OpenApiClient Include="../Rockets.Api/Rockets.Api.csproj" Generator="Kiota" ClientName="RocketsClient" />
</ItemGroup>
```

Chirograph adds the server as a `ProjectReference` that is built before the client project, but whose assembly is not referenced
(`ReferenceOutputAssembly="false"`). The client project gets no dependency on ASP.NET Core. Then it asks the server for the documents
it wrote and copies the chosen one into the client's folder in `obj`.

A document the server writes again unchanged, which it does on most builds, regenerates nothing.

## A server with several target frameworks

A server that targets, say, `net10.0` and `net11.0` is built as a whole, not for the target framework closest to the client's. Its
document comes from the server's own build, so it doesn't matter which frameworks the client targets.

## A server with several documents

A server that registers more than one document writes each of them:

```csharp
builder.Services.AddOpenApi();       // "v1": Rockets.Api.json
builder.Services.AddOpenApi("v2");   // Rockets.Api_v2.json
```

Name the one the client is generated from with `DocumentName`:

```xml
<OpenApiClient Include="../Rockets.Api/Rockets.Api.csproj" Generator="Kiota" ClientName="RocketsV2Client" DocumentName="v2" />
```

`v1` is ASP.NET Core's default document. Without `DocumentName`, a server has to write exactly one document; with several, or with a
`DocumentName` that matches none, the build fails with [`CHIRO103`](../../reference/diagnostics/#chiro103), which lists the documents
it found.

## Things to know

- The server runs at build time. Code in its startup that needs a database, secrets or the network can fail there; check
  `Microsoft.Extensions.ApiDescription.Server`'s documentation for how to tell a build-time run from a real one.
- The document is always JSON, as `Microsoft.Extensions.ApiDescription.Server` writes it.
- The client project and the server have to be in the same build: a solution, or a `ProjectReference` chain from what you build.