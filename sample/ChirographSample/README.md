# ChirographSample

A standalone solution that consumes Chirograph the way any other repository would: from a package, through
`<Sdk Name="Chirograph" Version="0.0.0-sample" />` in every project, never through a project reference. Its apps call a running
`Rockets.Api` through every kind of client Chirograph generates.

The projects:

| Project                   | What it shows                                                                                                                                                                                                                                                     |
| ------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Rockets.Api`             | The server: two fixed rockets at `/rockets` and `/rockets/{id}`. A request with an `X-Api-Key` header must carry the right key, `sample-key`. It writes its OpenAPI document when it builds and serves it at `/openapi/v1.json`.                                                                                                                  |
| `Rockets.Client.Kiota`    | `RocketsClient`, generated with Kiota from the server project (a `.csproj` source): Chirograph builds `Rockets.Api` first and reads the document it writes.                                                                                                       |
| `Rockets.Client.Refitter` | `IRocketsRefitClient`, generated with Refitter from a committed document, `rockets.json` (a file source).                                                                                                                                                       |
| `Rockets.Client.Url`      | `RocketsUrlClient`, generated with Kiota from `http://localhost:5179/openapi/v1.json` (a URL source). The document is cached in the committed `rockets.openapi.json`, and `UpdateDocumentCache="false"` builds from that copy, so the build needs no server. |
| `Rockets.Console`         | Registers the three clients with `Add<ClientName>()` on a plain generic host, without Dovetail. It lists the rockets and gets one through each, and fails when a call fails or the clients disagree.                                                             |
| `Rockets.Dovetail`        | A generic host on Joinery.Dovetail, built with `ConfigureDovetail()`. It declares `IRocketsDovetailClient` (Refitter, from the server project) and resolves it with no `Add<ClientName>()` call: the joint Dovetail writes for the client registers it.         |

Each client reads its base address from `Clients:<ClientName>:BaseAddress`, for example the environment variable
`Clients__RocketsClient__BaseAddress=http://localhost:5179/`. `Rockets.Console/appsettings.json` also gives each of its clients a
`Timeout` and a default request header from the same section; `RocketsUrlClient` takes its `Timeout` from a connection string
instead, and authenticates with an API key from it (`Authentication=ApiKey;Key=sample-key`), which the generated handler sends only to
the base address's host. The client projects hold no code of their own: the registration and the options are generated.

## Running it

```sh
mise run sample:run
```

It runs `build/run-sample.cs`, which works the same on Linux, macOS and Windows:

1. packs `src/**` into `artifacts/nuget-sample` at `0.0.0-sample` (`build/pack.proj`, or `mise run sample:pack`) when the package is
   missing or older than its sources;
2. builds `ChirographSample.slnx`, with no server running;
3. starts `Rockets.Api` on `http://localhost:5179/` and waits until it answers;
4. runs `Rockets.Console` and `Rockets.Dovetail` with every `Clients__<ClientName>__BaseAddress` set to it, and compares what each
   prints with `expected/<App>.txt`;
5. stops the API (its output is in `artifacts/logs/sample-api.log`).

It fails when a step fails, an app exits non-zero, or an app prints something else, and shows the difference. `samples.yml` runs it on
Linux, Windows and macOS. `Rockets.Console` prints:

```text
RocketsClient (Kiota, generated from the Rockets.Api project)
  listed 4c1e2a8e-5c0b-4c1f-9a43-1f4a9a2f0d11 FALCON-9 Falcon9
  listed 7b2d3f5a-9e1c-4d2b-8f6a-2e5b8c3d1a22 FALCON-HEAVY FalconHeavy
  got    7b2d3f5a-9e1c-4d2b-8f6a-2e5b8c3d1a22 FALCON-HEAVY FalconHeavy
IRocketsRefitClient (Refitter, generated from rockets.json)
  ...
RocketsUrlClient (Kiota, generated from http://localhost:5179/openapi/v1.json, cached in rockets.openapi.json, with an API key)
  ...
The three clients agree.
```

To run an app by hand, start the API and point the clients at it:

```sh
dotnet run --project sample/ChirographSample/Rockets.Api -- --urls http://localhost:5179/
Clients__RocketsDovetailClient__BaseAddress=http://localhost:5179/ dotnet run --project sample/ChirographSample/Rockets.Dovetail
```

Rerun `mise run sample:pack` after changing anything under `src/` if you build without `sample:run`. The sample keeps its own package
cache (`sample/ChirographSample/.nuget/packages`), which the pack clears, so the fixed version never serves a stale copy. To pick up a
change to `Rockets.Api` in `Rockets.Client.Url`, delete `Rockets.Client.Url/rockets.openapi.json` and build while the API is running.

The sample's `Directory.Build.targets` is empty on purpose: it stops MSBuild from walking up into the repository's own file, which
adds repository-only analyzers. Its `Directory.Packages.props` is the sample's own central package management: the projects name
packages without versions, and the versions (Joinery.Dovetail 0.6.0 among them) are pinned there. Chirograph itself is an MSBuild SDK,
named with its version in each project, and the packages it adds carry their own versions.
