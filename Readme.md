# Chirograph

Typed HTTP clients generated from OpenAPI documents at build time: one MSBuild item, a Kiota or Refitter client, registered with `IHttpClientFactory`.

A chirograph was a medieval contract written twice on one sheet and cut apart, so each half proved the other. Here the OpenAPI document and the generated client are the two halves.

Chirograph is the MSBuild layer for the two generators .NET teams already choose, [Kiota](https://github.com/microsoft/kiota) and
[Refit](https://github.com/reactiveui/refit) through [Refitter](https://github.com/christianhelle/refitter). It generates no code of its
own, so it keeps each generator's fidelity and follows their releases by changing a version.

## Quick start

> [!NOTE]
> Chirograph has no release yet. This is the shape it is being built to.

Add the SDK next to your project's own, and declare a client:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Sdk Name="Chirograph" Version="1.0.0" />

  <ItemGroup>
    <OpenApiClient Include="openapi/rockets.json" Generator="Kiota" ClientName="RocketsClient" />
  </ItemGroup>
</Project>
```

Register it and give it a base address:

```csharp
services.AddRocketsClient();
```

```json
{ "Clients": { "RocketsClient": { "BaseAddress": "https://rockets.example.com/" } } }
```

## Sources

The kind of source is inferred from the item's `Include`, never declared:

- **A file**: an OpenAPI document in the project.
- **A server project**: a project in the same build whose document `Microsoft.Extensions.ApiDescription.Server` writes when it builds.
- **A URL**: downloaded into a document cache, which can be committed so builds work offline.

## Generators

- **Kiota**: a request-builder client on `Microsoft.Kiota.Bundle`.
- **Refitter**: a Refit interface on `Refit.HttpClientFactory`.

The generators run as processes during the build, fetched with `PackageDownload`. Output goes to `obj`, builds are incremental, and
generator telemetry and update checks are off.

## Registration

Each client gets an `Add<ClientName>()` extension on `IServiceCollection` that registers it with `IHttpClientFactory`, configures it
from `Clients:<ClientName>` (`BaseAddress`, `Timeout`, `DefaultRequestHeaders`, `Resilience`), and returns the `IHttpClientBuilder` for handlers and resilience. With
[Dovetail](https://github.com/RocketSurgeonsGuild/Dovetail), each client also gets a joint. Chirograph never references Dovetail.

## Status

Being extracted from LaunchPad's MSBuild SDK. See [the plan](plans/chirograph.md) for the design, the decisions and the milestones,
and [GLOSSARY.md](GLOSSARY.md) for the vocabulary.

## Contributing

`mise install` installs the toolchain, `mise run build` builds, tests and packs, and `hk check` lints. [AGENTS.md](AGENTS.md) has the
repository's conventions.
