# ChirographSample

A standalone solution that consumes Chirograph the way any other repository would: from a package, through
`<Sdk Name="Chirograph" Version="0.0.0-sample" />`, never through a project reference.

The projects arrive with milestone 7 of [the plan](../../plans/chirograph.md#milestones):

| Project                   | What it shows                                                        |
| ------------------------- | -------------------------------------------------------------------- |
| `Rockets.Api`             | A server project that writes its OpenAPI document when it builds.    |
| `Rockets.Client.Kiota`    | An `OpenApiClient` generated with Kiota from the server project.     |
| `Rockets.Client.Refitter` | An `OpenApiClient` generated with Refitter from a committed file.    |
| `Rockets.Console`         | A console app that uses both clients, without Dovetail.              |

## Running it

```sh
mise run sample:pack                                  # packs src/** into artifacts/nuget-sample at 0.0.0-sample
dotnet build sample/ChirographSample/ChirographSample.slnx
```

Rerun `mise run sample:pack` after changing anything under `src/`. The sample keeps its own package cache
(`sample/ChirographSample/.nuget/packages`), which the pack clears, so the fixed version never serves a stale copy.

The sample's `Directory.Build.targets` and `Directory.Packages.props` are empty on purpose: they stop MSBuild from walking up
into the repository's own files, which turn on central package management and repository-only analyzers.
