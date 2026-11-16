# ChirographSample

A standalone solution that consumes Chirograph the way any other repository would: from a package, through
`<Sdk Name="Chirograph" Version="0.0.0-sample" />`, never through a project reference.

The projects:

| Project                   | What it shows                                                        |
| ------------------------- | -------------------------------------------------------------------- |
| `Rockets.Api`             | A server project that writes its OpenAPI document when it builds.    |
| `Rockets.Client.Kiota`    | `RocketsClient`, generated with Kiota from the server project.       |
| `Rockets.Client.Refitter` | `IRocketsRefitClient`, generated with Refitter from `rockets.json`.  |
| `Rockets.Console`         | Registers both with `Add<ClientName>()`, without Dovetail.           |

## Running it

```sh
mise run sample:pack                                  # packs src/** into artifacts/nuget-sample at 0.0.0-sample
dotnet build sample/ChirographSample/ChirographSample.slnx
dotnet run --no-build --project sample/ChirographSample/Rockets.Console/Rockets.Console.csproj
```

Rerun `mise run sample:pack` after changing anything under `src/`. The sample keeps its own package cache
(`sample/ChirographSample/.nuget/packages`), which the pack clears, so the fixed version never serves a stale copy.

The sample's `Directory.Build.targets` is empty on purpose: it stops MSBuild from walking up into the repository's own file, which
adds repository-only analyzers. Its `Directory.Packages.props` is the sample's own central package management: the projects name
packages without versions, and the versions are pinned there. Chirograph itself is an MSBuild SDK, named with its version in each
client project, and the packages it adds carry their own versions.
