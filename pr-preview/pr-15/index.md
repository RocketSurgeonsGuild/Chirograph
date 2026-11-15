# Chirograph

A chirograph was a medieval contract written twice on one sheet and cut apart, so each half proved the other. Here the OpenAPI
document and the generated client are the two halves: Chirograph keeps them together by generating the client from the document on
every build that needs it.

Declare a client with one item. Where its document comes from is inferred from the `Include`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Sdk Name="Chirograph" Version="1.0.0" />

  <ItemGroup>
    <OpenApiClient Include="openapi/rockets.json" Generator="Kiota" ClientName="RocketsClient" />
  </ItemGroup>
</Project>
```

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Sdk Name="Chirograph" Version="1.0.0" />

  <ItemGroup>
    <OpenApiClient Include="../Rockets.Api/Rockets.Api.csproj" Generator="Kiota" ClientName="RocketsClient" />
  </ItemGroup>
</Project>
```

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Sdk Name="Chirograph" Version="1.0.0" />

  <ItemGroup>
    <OpenApiClient
      Include="https://rockets.example.com/openapi/v1.json"
      Generator="Refitter"
      ClientName="RocketsClient"
      DocumentCache="openapi/rockets.json"
    />
  </ItemGroup>
</Project>
```

Then register it, and give it a base address in `Clients:RocketsClient:BaseAddress`:

```csharp
builder.Services.AddRocketsClient();
```

> [!NOTE]
> Chirograph has no release on nuget.org yet. [Getting started](guides/) shows how to pack it from a clone until the first beta ships.

**Two generators you already use**
Kiota, or Refit through Refitter. Chirograph generates no code of its own, so it keeps each generator's fidelity and follows their
        releases by changing a version.

**Three sources**
A file in the project, a server project in the same build, or a URL with a document cache you can commit.

**Quiet builds**
Tools come from the packages folder, output stays in `obj`, generation is incremental, and the generators' telemetry is off.

**Registered**
Every client gets an `Add<ClientName>()` extension that registers it with `IHttpClientFactory` and returns the
        `IHttpClientBuilder`.

[Getting started](guides/)
    [Concepts](concepts/)
    [Reference](reference/)
    [Compared with](concepts/compared-with/)