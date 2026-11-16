# Chirograph

Typed HTTP clients generated from OpenAPI documents at build time: one MSBuild item, a Kiota or Refitter client, registered with `IHttpClientFactory`.

A chirograph was a medieval contract written twice on one sheet and cut apart, so each half proved the other. Here the OpenAPI document and the generated client are the two halves.

Chirograph is the MSBuild layer for the two generators .NET teams already choose, [Kiota](https://github.com/microsoft/kiota) and
[Refit](https://github.com/reactiveui/refit) through [Refitter](https://github.com/christianhelle/refitter). It generates no code of its
own, so it keeps each generator's fidelity and follows their releases by changing a version.

[![github-release-badge]][github-release]
[![github-license-badge]][github-license]
[![github-build-badge]][github-build]
[![nuget-Chirograph-badge]][nuget-Chirograph]

Documentation: <https://rocketsurgeonsguild.github.io/Chirograph/>

## Quick start

Chirograph is an MSBuild SDK, which a project adds next to its own SDK. Pin its version once in `global.json`; the current version
is the one on the NuGet badge above:

```json
{
  "msbuild-sdks": {
    "Chirograph": "0.0.10-beta.1"
  }
}
```

Then add the SDK to the project and declare a client:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Sdk Name="Chirograph" />

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

The [getting started guide](https://rocketsurgeonsguild.github.io/Chirograph/guides/) walks through a first client, and
[`sample/ChirographSample`](https://github.com/RocketSurgeonsGuild/Chirograph/tree/main/sample/ChirographSample) has a server project,
a Kiota client generated from it and a Refitter client generated from a document file.

## Sources

The kind of source is inferred from the item's `Include`, never declared:

- A file: an OpenAPI document in the project, as JSON or YAML.
- A server project: a project in the same build whose document `Microsoft.Extensions.ApiDescription.Server` writes when it builds.
- A URL: downloaded into a document cache, which can be committed so builds work offline.

## Generators

- Kiota: a request-builder client on `Microsoft.Kiota.Bundle`.
- Refitter: a Refit interface on `Refit.HttpClientFactory`.

The generators run as processes during the build, fetched with `PackageDownload`. Output goes to `obj`, builds are incremental, and
generator telemetry and update checks are off.

## Registration

Each client gets an `Add<ClientName>()` extension on `IServiceCollection` that registers it with `IHttpClientFactory`, configures it
from `Clients:<ClientName>` (`BaseAddress`, `Timeout`, `DefaultRequestHeaders`, `Resilience`), and returns the `IHttpClientBuilder` for handlers and resilience. With
[Dovetail](https://github.com/RocketSurgeonsGuild/Dovetail), each client also gets a joint. Chirograph never references Dovetail.

## Packages

| Package                        | NuGet                                         | What it does                                                                                 |
| ------------------------------ | --------------------------------------------- | -------------------------------------------------------------------------------------------- |
| [Chirograph][nuget-Chirograph] | [![nuget-Chirograph-badge]][nuget-Chirograph] | The MSBuild SDK: the `OpenApiClient` item, the generator runs and the registration templates |

## Status

Extracted from LaunchPad's MSBuild SDK. See [the plan](https://github.com/RocketSurgeonsGuild/Chirograph/blob/main/plans/chirograph.md)
for the design, the decisions and the milestones, and
[GLOSSARY.md](https://github.com/RocketSurgeonsGuild/Chirograph/blob/main/GLOSSARY.md) for the vocabulary.

## Contributing

`mise install` installs the toolchain, `mise run build` builds, tests and packs, and `hk check` lints.
[AGENTS.md](https://github.com/RocketSurgeonsGuild/Chirograph/blob/main/AGENTS.md) has the repository's conventions.

[github-release]: https://github.com/RocketSurgeonsGuild/Chirograph/releases
[github-release-badge]: https://img.shields.io/github/v/release/RocketSurgeonsGuild/Chirograph?include_prereleases&logo=github&style=flat
[github-license]: https://github.com/RocketSurgeonsGuild/Chirograph/blob/main/LICENSE
[github-license-badge]: https://img.shields.io/github/license/RocketSurgeonsGuild/Chirograph.svg?style=flat
[github-build]: https://github.com/RocketSurgeonsGuild/Chirograph/actions/workflows/build.yml
[github-build-badge]: https://github.com/RocketSurgeonsGuild/Chirograph/actions/workflows/build.yml/badge.svg
[nuget-Chirograph]: https://www.nuget.org/packages/Chirograph/
[nuget-Chirograph-badge]: https://img.shields.io/nuget/v/Chirograph.svg?logo=nuget&style=flat
