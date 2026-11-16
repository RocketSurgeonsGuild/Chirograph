# Plan: Chirograph

Status: design agreed (2026-11-15). Nothing is implemented yet. This file is the only artefact of the planning session.

Chirograph generates typed HTTP clients from OpenAPI documents at build time. A project declares a client with one MSBuild item
and gets a compiled client registered with `IHttpClientFactory`. The name comes from the medieval chirograph: a contract written
twice on one sheet and cut apart, so each half proves the other. The OpenAPI document and the generated client are the two halves.

Repository: <https://github.com/RocketSurgeonsGuild/Chirograph>

The feature already exists inside LaunchPad's MSBuild SDK, in
[RocketSurgeonsGuild/LaunchPad#3238](https://github.com/RocketSurgeonsGuild/LaunchPad/pull/3238) (branch
`claude/3218-sdk-openapi-clients-5b8493`, head
[`e5d30f78e`](https://github.com/RocketSurgeonsGuild/LaunchPad/commit/e5d30f78e28db5214ee19f3d3b0be9556e3a4161)), from
[RocketSurgeonsGuild/LaunchPad#3218](https://github.com/RocketSurgeonsGuild/LaunchPad/issues/3218). This plan extracts it. It does not
redesign it. The stacked dogfooding PR,
[RocketSurgeonsGuild/LaunchPad#3241](https://github.com/RocketSurgeonsGuild/LaunchPad/pull/3241), is covered in [LaunchPad](#6-how-launchpad-consumes-chirograph).

Links to the PR's files below are pinned to that head commit. `PR:` is short for
`https://github.com/RocketSurgeonsGuild/LaunchPad/blob/e5d30f78e28db5214ee19f3d3b0be9556e3a4161/`.

---

## Contents

1. [Glossary](#glossary)
2. [Goals and non-goals](#goals-and-non-goals)
3. [Positioning](#positioning)
4. [Decisions](#decisions)
5. [Target repository tree](#target-repository-tree)
6. [Files brought across from Dovetail](#files-brought-across-from-dovetail)
7. [Extraction map: PR 3238 to Chirograph](#extraction-map-pr-3238-to-chirograph)
8. [Public surface](#public-surface)
9. [Test strategy](#test-strategy)
10. [Docs outline](#docs-outline)
11. [CI, release and NuGet publishing](#ci-release-and-nuget-publishing)
12. [Milestones](#milestones)
13. [Dovetail tickets (drafts)](#dovetail-tickets-drafts)
14. [Risks](#risks)
15. [Open questions](#open-questions)
16. [Verification of this plan](#verification-of-this-plan)

---

## Glossary

The plan was asked to be one file, so this glossary lives here rather than in a separate `GLOSSARY.md`. Move it to `GLOSSARY.md` in milestone 1.

| Term               | Meaning                                                                                                                                                                                                                                    |
| ------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **Client**         | One `OpenApiClient` item, and everything generated for it: the client code, its registration and, with Dovetail, its joint. It is named by `ClientName`, which is unique within a project.                                                 |
| **Source**         | Where a client's document comes from: a **file**, a **server project**, or a **URL**. The kind is inferred from the item's `Include`, never declared.                                                                                      |
| **Server project** | A project in the same build whose OpenAPI document `Microsoft.Extensions.ApiDescription.Server` writes when it builds.                                                                                                                     |
| **Document**       | The OpenAPI description a client is generated from, as Chirograph copies it into `obj`. A source has a document. A client has exactly one.                                                                                                 |
| **Document cache** | For a URL source only: the last document downloaded. It lives in `obj`, or in a project file named by `DocumentCache`, which can be committed. **Updating** the cache means downloading again and overwriting it when the content differs. |
| **Generator**      | The tool that turns a document into client code: **Kiota** or **Refitter**. A generator is never a source generator: it runs as a process during the build.                                                                                |
| **Registration**   | The generated `Add<ClientName>()` extension method that registers the client with `IHttpClientFactory` and reads its base address from configuration.                                                                                      |
| **Joint**          | Dovetail's unit of registration. Chirograph never writes a joint itself: it declares a **template** that Dovetail expands into one, which calls the registration.                                                                          |
| **Base address**   | The URI a client calls, read from `Clients:<ClientName>:BaseAddress`.                                                                                                                                                                      |

---

## Goals and non-goals

### Goals

- **One item, one client.** `<OpenApiClient Include="…" Generator="Kiota|Refitter" ClientName="…" />` in any SDK-style C# project
  yields a compiled client and a registration, with the behaviour of PR 3238 carried over unchanged unless this plan says otherwise:
    - file, server-project and URL sources;
    - the committable document cache and its update switch;
    - tools fetched with `PackageDownload`, not a tool manifest;
    - incremental generation into `obj`;
    - design-time builds that only add what an earlier build generated;
    - generator telemetry and update checks off;
    - the diagnostics.
- **Works without Dovetail.** Registration is a plain `IServiceCollection` extension. Dovetail users get a joint as well, through
  Dovetail's own template extension point, without Chirograph referencing Dovetail.
- **Becomes LaunchPad's implementation.** LaunchPad's SDK chains the Chirograph SDK instead of carrying its own copy.
- **Repository conventions identical to Dovetail's**, so the same tooling, agents and release process apply.

### Non-goals (1.0)

- Generators other than Kiota and Refitter (NSwag, AutoSDK, openapi-generator), and a pluggable generator model.
- An in-process Roslyn source generator. The generators are external tools, run as processes.
- Committing generated code to the repository. It stays in `obj`. Only the document cache can be committed.
- Supporting `netstandard2.0` or .NET Framework consumers, and .NET 8. They are not blocked, but untested and undocumented.
- Authentication, resilience or handler configuration. Callers chain those on the returned `IHttpClientBuilder`.
- A `dotnet` CLI or IDE "add reference" experience.
- Visual Basic and F# client projects. Server projects can be `.fsproj`/`.vbproj`, as in the PR.

---

## Positioning

Microsoft deprecated `Microsoft.Extensions.ApiDescription.Client` (`OpenApiReference`, `dotnet openapi`) in .NET 10 Preview 7. It
now tells people to use each generator's own tooling, and to commit the generated code or run generation in a custom pre-build step
([breaking change](https://learn.microsoft.com/en-us/dotnet/core/compatibility/aspnet-core/10/apidescription-client-deprecated),
[dotnet/aspnetcore#59208](https://github.com/dotnet/aspnetcore/issues/59208)). Kiota dropped its own MSBuild integration for lack of
use and points people at an `Exec` task ([microsoft/kiota#3005](https://github.com/microsoft/kiota/issues/3005)). Blog posts show that
hand-rolled target for one server project and one tool manifest
([Meziantou](https://www.meziantou.net/generate-a-kiota-client-at-build-time-from-an-asp-net-core-openapi-file.htm)).
[Refitter](https://github.com/christianhelle/refitter) ships [Refitter.MSBuild](https://www.nuget.org/packages/Refitter.MSBuild),
which scans for `.refitter` files and runs on every build with no inputs or outputs. It writes into the project folder by default,
and has telemetry on by default. It is Refit-only.

[AutoSDK](https://github.com/tryAGI/AutoSDK) and
[OpenApiWeaver](https://dev.to/polymind/openapiweaver-generate-type-safe-c-clients-from-openapi-at-compile-time-with-source-generators-5e79)
offer a one-item experience, but with their own code generators: AutoSDK is still pre-1.0. OpenApiWeaver's item
(`OpenApiWeaverDocument`) is close to Chirograph's.

Chirograph is the missing MSBuild layer for the two generators .NET teams already choose, Kiota and
[Refit](https://github.com/reactiveui/refit) through Refitter:

- one item;
- three sources, including a sibling server project and a URL with a committable cache;
- incremental, offline-capable, telemetry-free builds;
- an `IHttpClientFactory` registration, and with it a Dovetail joint.

It does not generate code itself, so it inherits each generator's fidelity and keeps up with their releases by changing a version.

---

## Decisions

Each decision gives the recommendation, the alternative, and who decided. All of them were settled in the planning session on
2026-11-15.

### 1. Packaging: an MSBuild SDK only

**Decision (owner):** one package, ID **`Chirograph`**, `PackageType` `MSBuildSdk`. A project uses it alongside its own SDK:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Sdk Name="Chirograph" Version="1.0.0" />
  …
</Project>
```

The alternative is to put `"msbuild-sdks": { "Chirograph": "1.0.0" }` in `global.json` and omit `Version`. LaunchPad uses
`<Import Sdk="Chirograph" Version="$(LaunchPadChirographVersion)" />` (see [§6](#6-how-launchpad-consumes-chirograph)).

**Why not an ordinary package with `build/` and `buildTransitive/`.** NuGet restore never sees `PackageDownload` or
`PackageReference` items that a package's `build/` targets declare:

- Restore evaluates the project without the package imports (`nuget.g.targets`).
- A throwaway probe in this session confirmed it. A local package whose `build/Probe.targets` declared
  `<PackageDownload Include="Refitter" …/>` and `<PackageReference Include="Humanizer.Core" …/>` was referenced from an app. After two
  `dotnet build` runs, the packages folder held only `probe`.

So an ordinary package could neither fetch the tools nor add `Microsoft.Kiota.Bundle` or `Refit.HttpClientFactory`. The PR works
because it lives in an SDK. Its `PackageDownload` and implicit package references are declared at evaluation, in targets an SDK
imports ([`PR:src/Sdk/Sdk/Rocket.Surgery.LaunchPad.OpenApiClients.targets` L67–94](https://github.com/RocketSurgeonsGuild/LaunchPad/blob/e5d30f78e28db5214ee19f3d3b0be9556e3a4161/src/Sdk/Sdk/Rocket.Surgery.LaunchPad.OpenApiClients.targets#L67-L94)).

**Alternatives considered:**

- An ordinary package that bundles the tool binaries, as NSwag.MSBuild and Refitter.MSBuild do. That means a large package and tool
  versions locked to Chirograph's releases.
- Both an SDK and an ordinary package. That is two packaging paths to test.

### 2. Dovetail coupling: plain registration, joint through `DovetailTemplate`

**Decision (owner):**

- **Every client** gets a public `services.Add<ClientName>()` extension that returns `IHttpClientBuilder` and reads
  `Clients:<ClientName>:BaseAddress`. This replaces the body of the PR's joint templates
  ([`PR:src/Sdk/Sdk/OpenApiClients/KiotaJoint.cs.template`](https://github.com/RocketSurgeonsGuild/LaunchPad/blob/e5d30f78e28db5214ee19f3d3b0be9556e3a4161/src/Sdk/Sdk/OpenApiClients/KiotaJoint.cs.template),
  [`PR:src/Sdk/Sdk/OpenApiClients/RefitterJoint.cs.template`](https://github.com/RocketSurgeonsGuild/LaunchPad/blob/e5d30f78e28db5214ee19f3d3b0be9556e3a4161/src/Sdk/Sdk/OpenApiClients/RefitterJoint.cs.template)).
  It is public so that an application without Dovetail can register a client that a library generated.
- **The joint** is not written by Chirograph. For each client, Chirograph declares a `DovetailTemplate` item: Dovetail's existing,
  documented extension point for packages that ship `.cs.template` files
  ([Dovetail `plans/template-extension-point.md`](https://github.com/RocketSurgeonsGuild/Dovetail/blob/main/plans/template-extension-point.md),
  [`src/Joinery.Dovetail/build/Joinery.Dovetail.Templates.targets`](https://github.com/RocketSurgeonsGuild/Dovetail/blob/main/src/Joinery.Dovetail/build/Joinery.Dovetail.Templates.targets)).
  Dovetail writes the joint only into projects that run its generator, the same way it gates its own ZeroAlloc joints
  ([`Joinery.Dovetail.ZeroAlloc.targets`](https://github.com/RocketSurgeonsGuild/Dovetail/blob/main/src/Joinery.Dovetail/build/Joinery.Dovetail.ZeroAlloc.targets)).
  The template calls `Add<ClientName>()`.
- `DovetailTemplate` substitutes only `{HostName}` and `{HostType}` today (open question 2 of Dovetail's plan). So **Dovetail ticket
  D1** ([below](#d1-dovetailtemplate-substitutes-every-metadata-value)) makes every metadata value a token, in both the `{Foo}` and
  the `__Foo__` forms. Chirograph's templates use `__Foo__`, so they stay valid C# identifiers. That is a step towards **ticket D2**
  ([below](#d2-templates-that-compile-and-type-check-but-are-not-compiled-into-the-project)): templates that are real C# files the
  compiler type-checks.
- The items are declared only when Dovetail advertises D1, through the capability property D1 adds. That way an older Dovetail never
  receives a template it cannot expand. `ChirographGenerateDovetailJoint=false` turns the joints off.

**Alternatives considered:**

- A `Chirograph.Dovetail` companion package.
- A new `GenerateDovetailJoint` item in Dovetail. Rejected because `DovetailTemplate` already exists and has the gating.
- Chirograph detecting Dovetail and writing the joint itself. That couples Chirograph to Dovetail's attribute and interface names.

### 3. Naming

**Decision (owner):**

- **Package ID:** `Chirograph`. It and `Chirograph.Sdk` / `Chirograph.Dovetail` were all free on nuget.org on 2026-11-15 (each URL
  returned 404).
- **Item:** `OpenApiClient`. Its metadata are unchanged: `Generator`, `ClientName`, `Namespace`, `DocumentName`, `DocumentCache`,
  `UpdateDocumentCache` and `Options`.
- **Properties:** the `LaunchPad` prefix becomes `Chirograph`, and the `_LaunchPad` prefix of private names becomes `_Chirograph`.
- **Diagnostic prefix:** `LPSDK01xx` becomes **`CHIRO1xx`**, keeping the last digits so the migration is a search-and-replace
  ([table](#diagnostics)).
- **Configuration key:** unchanged, `Clients:<ClientName>:BaseAddress`.

### 4. Refitter: keep driving the CLI

**Decision (owner):** keep the PR's approach. Download the `Refitter` tool package, write a `.refitter` settings file into `obj`, and
run `dotnet exec --roll-forward Major refitter.dll --settings-file …`
([targets L309–315, L376](https://github.com/RocketSurgeonsGuild/LaunchPad/blob/e5d30f78e28db5214ee19f3d3b0be9556e3a4161/src/Sdk/Sdk/Rocket.Surgery.LaunchPad.OpenApiClients.targets#L309-L315)).

Refitter.MSBuild finds `.refitter` files by scanning the project directory. Its target has no `Inputs`/`Outputs` and no design-time
guard, and it writes into the project directory by default (findings recorded in the PR description and confirmed against Refitter's
`src/Refitter.MSBuild`). Delegating to it would lose per-item control and the incrementality and design-time rules that Kiota
clients keep. Its one advantage, bundling the CLI, doesn't matter because the SDK can use `PackageDownload`.

### 5. Supported .NET range

**Decision (owner):**

- Builds need the **.NET SDK 10.0.100 or later**, because the tools run on a .NET 10 runtime: the PR runs the `tools/net10.0/any`
  build with roll-forward
  ([targets L46, L374](https://github.com/RocketSurgeonsGuild/LaunchPad/blob/e5d30f78e28db5214ee19f3d3b0be9556e3a4161/src/Sdk/Sdk/Rocket.Surgery.LaunchPad.OpenApiClients.targets#L46)).
- Generated code is supported and tested for **`net10.0` and `net11.0`**, the same range as Dovetail and LaunchPad.
- .NET 8 left support on 2026-11-10. Other target frameworks are not blocked, but they are untested, and the docs say so.

### 6. How LaunchPad consumes Chirograph

**Decision (owner):** **replace PR 3238, don't merge it.**

1. PR 3238 stays open and unmerged as the reference until Chirograph 1.0 ships.
2. A new LaunchPad PR makes the LaunchPad SDK chain Chirograph:
    - It imports `Sdk.props`/`Sdk.targets` with `Sdk="Chirograph" Version="$(LaunchPadChirographVersion)"`. This is the same
      mechanism LaunchPad uses for the Dovetail SDK today
      ([`PR:src/Sdk/Sdk/Sdk.props` L18](https://github.com/RocketSurgeonsGuild/LaunchPad/blob/e5d30f78e28db5214ee19f3d3b0be9556e3a4161/src/Sdk/Sdk/Sdk.props#L18)).
    - `LaunchPadChirographVersion` is written into `Sdk/Packages.props` at pack time, like `LaunchPadDovetailSdkVersion`
      ([`PR:src/Sdk/Rocket.Surgery.LaunchPad.Sdk.Pack.targets`](https://github.com/RocketSurgeonsGuild/LaunchPad/blob/e5d30f78e28db5214ee19f3d3b0be9556e3a4161/src/Sdk/Rocket.Surgery.LaunchPad.Sdk.Pack.targets)).
3. PR 3238 is closed in favour of that PR, and
   [#3241](https://github.com/RocketSurgeonsGuild/LaunchPad/pull/3241) (Kiota functional client, Refit sample tests, NSwag removed) is
   rebased onto it.
4. That PR waits for **Chirograph 1.0** and for **the Dovetail release that contains D1**. Without D1, LaunchPad applications would
   lose the joints that PR 3238 gives them.

LaunchPad 2.0 is unreleased, so LaunchPad never ships the `LPSDK01xx` codes or `LaunchPad*OpenApi*` properties.

**Alternative considered:** merge 3238 and 3241 now and migrate before 2.0. That unblocks 2.0 sooner, but two copies would drift
apart.

### 7. Repository to mirror: Dovetail

**Decision (owner):** mirror **Dovetail only**. Dovetail and Specular come from the same template. Most shared files are
byte-identical: `.editorconfig`, `GitVersion.yml`, `GitReleaseManager.yaml`, `.github/release.yml`, `.codecov.yml`, `.oxfmtrc.json`,
`Skillfile`, and four of the ten workflows. Dovetail alone has the plumbing Chirograph needs:

- an MSBuild SDK package
  ([`src/Joinery.Dovetail.Sdk/Joinery.Dovetail.Sdk.csproj`](https://github.com/RocketSurgeonsGuild/Dovetail/blob/main/src/Joinery.Dovetail.Sdk/Joinery.Dovetail.Sdk.csproj))
  that generates `Packages.props` at pack time;
- a test project that packs `src/**` before it builds and scaffolds consumer projects with MSBuild.ProjectCreation
  ([`test/Joinery.Dovetail.Sdk.Tests/Joinery.Dovetail.Sdk.Tests.csproj`](https://github.com/RocketSurgeonsGuild/Dovetail/blob/main/test/Joinery.Dovetail.Sdk.Tests/Joinery.Dovetail.Sdk.Tests.csproj));
- a standalone sample solution on local packages (`sample/SdkSample`);
- `.config/mise.ci.toml`.

Dovetail's stale `Readme.md` ("Rocket Surgeons - Conventions"), empty `CLAUDE.md` and "Conventions Constitution" `AGENTS.md` are
**rewritten** for Chirograph, not copied.

### 8. NuGet ID: a placeholder now

**Decision (owner):** push a hand-built `Chirograph` **0.0.1** placeholder as soon as the repository exists (milestone 0), so nobody
else can take the ID. Then register trusted publishing so that every later version comes from CI. This step is manual and belongs to
the owner. Nothing in this session pushed anything.

### Folded-in corrections to the PR

- **Kiota telemetry was not off.** The PR sets `KIOTA_TELEMETRY_OPTOUT`, `KIOTA_UPDATE_DISABLED` and `KIOTA_OFFLINE_ENABLED`
  ([targets L394](https://github.com/RocketSurgeonsGuild/LaunchPad/blob/e5d30f78e28db5214ee19f3d3b0be9556e3a4161/src/Sdk/Sdk/Rocket.Surgery.LaunchPad.OpenApiClients.targets#L394)).
  Kiota actually reads:
    - `KIOTA_CLI_TELEMETRY_OPTOUT`
      ([`src/kiota/Extension/KiotaHostExtensions.cs`](https://github.com/microsoft/kiota/blob/main/src/kiota/Extension/KiotaHostExtensions.cs));
    - `OFFLINE_ENABLED` under the `KIOTA_` prefix, which turns the update check off
      ([`src/kiota/KiotaConfigurationExtensions.cs`](https://github.com/microsoft/kiota/blob/main/src/kiota/KiotaConfigurationExtensions.cs)).

    Chirograph sets `KIOTA_CLI_TELEMETRY_OPTOUT=true`, `KIOTA_OFFLINE_ENABLED=true` and `DOTNET_CLI_TELEMETRY_OPTOUT=1`, and drops
    `KIOTA_TELEMETRY_OPTOUT` and `KIOTA_UPDATE_DISABLED`. A test pins this (milestone 3).

- **`LPSDK0107` is undocumented.** The targets raise it when a generator exits non-zero
  ([L404–408](https://github.com/RocketSurgeonsGuild/LaunchPad/blob/e5d30f78e28db5214ee19f3d3b0be9556e3a4161/src/Sdk/Sdk/Rocket.Surgery.LaunchPad.OpenApiClients.targets#L404-L408)),
  but the guide's table stops at `LPSDK0106`
  ([guide](https://github.com/RocketSurgeonsGuild/LaunchPad/blob/e5d30f78e28db5214ee19f3d3b0be9556e3a4161/docs/src/content/docs/guides/openapi-clients.md)).
  `CHIRO107` is documented.
- **`DovetailSdkImplicitPackageReference` is Dovetail's** mechanism
  ([Dovetail `ImplicitPackageReferences.targets`](https://github.com/RocketSurgeonsGuild/Dovetail/blob/main/src/Joinery.Dovetail.Sdk/Sdk/ImplicitPackageReferences.targets)).
  Chirograph gets its own `_ChirographImplicitPackageReference` with the same rule: a package the project references itself wins. It
  is expanded at evaluation, in `Sdk.targets`.
- **`GeneratedCode` version.** The PR's templates hard-code `"1.0.0"`. Chirograph writes `ChirographVersion` into `Packages.props` at
  pack time and stamps the real version.

---

## Target repository tree

The tree mirrors Dovetail's layout: `src/` and `test/` each with their own `Directory.Build.*`, a singular `sample/`, `build/`, and
Starlight `docs/`. `artifacts/` is git-ignored.

```text
Chirograph/
├── .agents/skills/                     # APM-managed (from Dovetail via apm install, not hand-copied)
├── .apm/                               # APM-managed
├── .claude/{agents,commands,rules,skills}/   # APM-managed
├── .config/
│   ├── hk.pkl
│   ├── mise.toml
│   └── mise.ci.toml
├── .github/
│   ├── actionlint.yaml
│   ├── copilot-instructions.md
│   ├── labels.yml
│   ├── release.yml
│   ├── renovate.json
│   ├── agents/ instructions/ prompts/  # APM-managed
│   └── workflows/
│       ├── build.yml
│       ├── close-milestone.yml
│       ├── dependabot-merge.yml
│       ├── docs-preview-cleanup.yml
│       ├── draft-release.yml
│       ├── lint.yml
│       ├── publish-results.yml
│       ├── samples.yml
│       ├── sync-labels.yml
│       └── update-milestone.yml
├── .vscode/{extensions,launch,settings,tasks}.json
├── build/
│   ├── Build.cs                        # ModularPipelines file-based app
│   └── appsettings.yaml
├── docs/                               # Astro + Starlight, npm workspace
│   ├── astro.config.mjs
│   ├── package.json
│   ├── tsconfig.json
│   └── src/content/docs/
│       ├── index.mdx
│       ├── getting-started.md
│       ├── concepts/{sources,generators,registration,document-cache}.md
│       ├── guides/{file-source,server-project-source,url-source,dovetail,migrating-from-openapireference,migrating-from-launchpad}.md
│       └── reference/{openapiclient-item,properties,diagnostics,generated-code}.md
├── icons/                              # chirograph-icon(.svg/.png/-invert), chirograph-wordmark(.svg/-invert)
├── plans/
│   └── chirograph.md                   # this file
├── sample/
│   └── ChirographSample/               # standalone solution on local packages (modelled on Dovetail's sample/SdkSample)
│       ├── build/pack.proj
│       ├── ChirographSample.slnx
│       ├── Directory.Build.props  Directory.Build.targets  Directory.Packages.props  global.json  NuGet.config  README.md
│       ├── Rockets.Api/                # server project (Microsoft.Extensions.ApiDescription.Server)
│       ├── Rockets.Client.Kiota/       # OpenApiClient from the server project
│       ├── Rockets.Client.Refitter/    # OpenApiClient from a committed file
│       └── Rockets.Console/            # uses both, no Dovetail
├── src/
│   ├── Directory.Build.props
│   ├── Directory.Build.targets
│   └── Chirograph/
│       ├── Chirograph.csproj           # PackageType MSBuildSdk, NoBuildOutput, packs Sdk/**
│       ├── Chirograph.Pack.targets     # writes Sdk/Packages.props at pack time
│       └── Sdk/
│           ├── Sdk.props
│           ├── Sdk.targets
│           ├── Chirograph.ImplicitPackageReferences.targets
│           ├── Chirograph.OpenApiClients.targets
│           └── templates/
│               ├── KiotaRegistration.cs.template
│               ├── RefitterRegistration.cs.template
│               └── OpenApiClientJoint.cs.template   # expanded by Dovetail (DovetailTemplate), not by Chirograph
├── test/
│   ├── Directory.Build.props
│   ├── Directory.Build.targets
│   ├── coverage.runsettings
│   └── Chirograph.Tests/
│       ├── Chirograph.Tests.csproj     # packs src/** before Build, like Joinery.Dovetail.Sdk.Tests
│       ├── ModuleInitializer.cs
│       ├── Config.cs
│       ├── TargetFrameworksAttribute.cs
│       ├── SdkTestProject.cs           # Dovetail's scaffolding, adapted
│       ├── Scaffolding/RocketsApi.cs   # from the PR
│       ├── OpenApiClientTests.cs
│       ├── IncrementalTests.cs
│       ├── DocumentCacheTests.cs
│       ├── DiagnosticsTests.cs
│       ├── DesignTimeBuildTests.cs
│       ├── DovetailJointTests.cs
│       ├── PackageTests.cs
│       └── snapshots/
├── .codecov.yml  .editorconfig  .gitattributes  .gitignore  .npmrc  .oxfmtrc.json
├── AGENTS.md  CLAUDE.md  CHANGELOG.md  GLOSSARY.md  LICENSE  Readme.md
├── Chirograph.slnx
├── Directory.Build.props  Directory.Build.targets  Directory.Packages.props
├── GitReleaseManager.yaml  GitVersion.yml  NuGet.config  global.json
├── Skillfile  apm.yml  apm.lock.yaml  biome.json  package.json  package-lock.json
```

The PR used built-in MSBuild tasks only (`DownloadFile`, `GetFileHash`, `Exec`, `WriteLinesToFile`). Writing a client's registration
from its template and the schema of its configuration section outgrew property functions, so they moved into
`src/Chirograph.Build.Tasks`, after Dovetail's pattern: a netstandard2.0 assembly that compiles against MSBuild only, is not a package
of its own, and ships inside `Chirograph` at `Sdk/tasks/netstandard2.0`, where `Sdk/Chirograph.Tasks.targets` registers its tasks.
Everything else still uses MSBuild's own tasks.

---

## Files brought across from Dovetail

All sources are paths in [RocketSurgeonsGuild/Dovetail](https://github.com/RocketSurgeonsGuild/Dovetail) at `main`
([`e35038ab3c`](https://github.com/RocketSurgeonsGuild/Dovetail/commit/e35038ab3ca5afe138e0a5cece72cbcf96878608)). "Copy" means
byte-for-byte, "Adapt" means copy and then change only what the note lists, and "Rewrite" means take only the shape.

| Dovetail file                                                                                                                                        | Action    | Notes                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| ---------------------------------------------------------------------------------------------------------------------------------------------------- | --------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `.codecov.yml`                                                                                                                                       | Copy      |                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| `.editorconfig`                                                                                                                                      | Copy      |                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| `.gitattributes`, `.gitignore`, `.npmrc`, `.oxfmtrc.json`                                                                                            | Copy      | `.gitmodules` is empty in Dovetail. Leave it out.                                                                                                                                                                                                                                                                                                                                                                                                                    |
| `.config/hk.pkl`                                                                                                                                     | Adapt     | Solution name and sample excludes.                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| `.config/mise.toml`                                                                                                                                  | Adapt     | Drop the extra workloads from postinstall (Chirograph needs no MAUI/WASM). Keep the dotnet SDK 11 and .NET 10 runtime: the tools need .NET 10. Replace `sample:sdk` with `sample:pack` for `sample/ChirographSample/build/pack.proj`.                                                                                                                                                                                                                                |
| `.config/mise.ci.toml`                                                                                                                               | Copy      |                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| `.github/actionlint.yaml`                                                                                                                            | Copy      |                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| `.github/copilot-instructions.md`                                                                                                                    | Copy      |                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| `.github/labels.yml`, `.github/release.yml`                                                                                                          | Copy      |                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| `.github/renovate.json`                                                                                                                              | Adapt     | Keep the shared preset, mise regex managers and the 10.x hold. Replace the `Joinery.Dovetail*` sample rule with `Chirograph` in `sample/**` (local `0.0.0-sample`). Ignore `sample/ChirographSample/global.json`. Add a group, "openapi generators", for `Microsoft.OpenApi.Kiota`, `Microsoft.Kiota.Bundle`, `Refitter` and `Refit.HttpClientFactory`, so generator and runtime versions move together. Drop the `*.Analyzers.supports` and MSBuildTasks rules.     |
| `.github/workflows/build.yml`                                                                                                                        | Adapt     | Names only. Keep the docs job (gh-pages, PR previews, `v*/`, `versions.json`) and the trusted-publishing publish job on `v*` tags.                                                                                                                                                                                                                                                                                                                                   |
| `.github/workflows/{close-milestone,update-milestone,draft-release,lint,publish-results,sync-labels,dependabot-merge,docs-preview-cleanup}.yml`      | Copy      |                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| `.github/workflows/samples.yml`                                                                                                                      | Rewrite   | One job, `mise run sample:pack` and then `dotnet build` of `sample/ChirographSample` on ubuntu, windows and macOS. No workload matrix.                                                                                                                                                                                                                                                                                                                               |
| `.github/mcp.json`                                                                                                                                   | Leave out | Dovetail-specific.                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| `.vscode/*`                                                                                                                                          | Adapt     | Solution and launch names.                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| `apm.yml`, `apm.lock.yaml`, `Skillfile`                                                                                                              | Adapt     | Name. Then `apm install` generates `.apm/`, `.agents/`, `.claude/` and `.github/{agents,instructions,prompts}`. Don't hand-copy the 274 skill files.                                                                                                                                                                                                                                                                                                                 |
| `AGENTS.md`                                                                                                                                          | Rewrite   | Chirograph constitution: MSBuild-only package, no compiled code, behaviour pinned by end-to-end tests, CHIRO codes are API, telemetry stays off. Keep Dovetail's workflow section (mise, TUnit, Verify, hk).                                                                                                                                                                                                                                                         |
| `CLAUDE.md`                                                                                                                                          | Rewrite   | Generated from `AGENTS.md` by APM (Dovetail's is 0 bytes).                                                                                                                                                                                                                                                                                                                                                                                                           |
| `CHANGELOG.md`                                                                                                                                       | New       | Dovetail has none.                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| `Readme.md`                                                                                                                                          | Rewrite   | Headings: Chirograph (what and why, the name) / Quick start / Sources / Generators / Registration / Status / Contributing.                                                                                                                                                                                                                                                                                                                                           |
| `LICENSE`                                                                                                                                            | Copy      | MIT. Copyright line unchanged.                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| `Directory.Build.props`                                                                                                                              | Adapt     | `PackageProjectUrl`, description and tags. Keep CPM, `WarningsAsErrors=RS0017`, analysis settings and NuGet audit.                                                                                                                                                                                                                                                                                                                                                   |
| `Directory.Build.targets`                                                                                                                            | Adapt     | Drop the runtime-async and browser-wasm blocks (no runtime code).                                                                                                                                                                                                                                                                                                                                                                                                    |
| `Directory.Packages.props`                                                                                                                           | Adapt     | Keep the `GlobalPackageReference` set. Pins: `Microsoft.OpenApi.Kiota`, `Microsoft.Kiota.Bundle`, `Refitter`, `Refit.HttpClientFactory`, `Microsoft.Extensions.Http` and `Microsoft.Extensions.ApiDescription.Server`/`Microsoft.AspNetCore.OpenApi` (10.x and 11.x, for tests), plus the test stack: TUnit, Verify.TUnit, `Rocket.Surgery.Extensions.Testing.TUnit`, MSBuild.ProjectCreation, Microsoft.Build*, MSBuild.StructuredLogger.Utils and NuGet.Packaging. |
| `Directory.Packages.MSBuildTasks.props`                                                                                                              | Leave out | No tasks.                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| `GitReleaseManager.yaml`, `GitVersion.yml`                                                                                                           | Copy      |                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| `NuGet.config`                                                                                                                                       | Copy      |                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| `global.json`                                                                                                                                        | Copy      | The same SDK pin as Dovetail at the time of copying.                                                                                                                                                                                                                                                                                                                                                                                                                 |
| `Dovetail.slnx`                                                                                                                                      | Rewrite   | `Chirograph.slnx`, without the dead entries Dovetail still lists (`build.cmd`, `build.ps1`, `build.sh`, `dotnet-tools.json`, `.husky/*`).                                                                                                                                                                                                                                                                                                                            |
| `biome.json`, `package.json`, `package-lock.json`                                                                                                    | Adapt     | Workspace name. Regenerate the lock file.                                                                                                                                                                                                                                                                                                                                                                                                                            |
| `build/Build.cs`, `build/appsettings.yaml`                                                                                                           | Adapt     | Solution name. The same ModularPipelines modules (build, test, pack, docs).                                                                                                                                                                                                                                                                                                                                                                                          |
| `docs/` (astro.config.mjs, package.json, tsconfig.json, `src/` skeleton)                                                                             | Adapt     | Site title, sidebar and content ([Docs outline](#docs-outline)). Keep the API-reference data step only if it has anything to show (there are no assemblies).                                                                                                                                                                                                                                                                                                         |
| `src/Directory.Build.props`, `src/Directory.Build.targets`                                                                                           | Adapt     | Only what an MSBuild SDK project needs.                                                                                                                                                                                                                                                                                                                                                                                                                              |
| `src/Joinery.Dovetail.Sdk/Joinery.Dovetail.Sdk.csproj`                                                                                               | Adapt     | Becomes `src/Chirograph/Chirograph.csproj`. Its `Packages.props` generation moves into `Chirograph.Pack.targets`.                                                                                                                                                                                                                                                                                                                                                    |
| `test/Directory.Build.props`, `test/Directory.Build.targets`, `test/coverage.runsettings`                                                            | Adapt     |                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| `test/Joinery.Dovetail.Sdk.Tests/{Joinery.Dovetail.Sdk.Tests.csproj, SdkTestProject.cs, Config.cs, TargetFrameworksAttribute.cs}`                    | Adapt     | Become `test/Chirograph.Tests/…`. Keep the pack-before-build target and the MSBuild.ProjectCreation scaffolding.                                                                                                                                                                                                                                                                                                                                                     |
| `sample/SdkSample/{build/pack.proj, Directory.Build.props, Directory.Build.targets, Directory.Packages.props, global.json, NuGet.config, README.md}` | Adapt     | Become `sample/ChirographSample/…`.                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| `icons/*`                                                                                                                                            | Rewrite   | New Chirograph art, with the same file set and names.                                                                                                                                                                                                                                                                                                                                                                                                                |
| `LEAN-CTX.md`, `benchmarks/`, `.idea/`                                                                                                               | Leave out |                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |

---

## Extraction map: PR 3238 to Chirograph

Each row is a file the PR adds or changes, from `git diff --stat origin/main...claude/3218-sdk-openapi-clients-5b8493` (20 files).

| PR file                                                                                                                                                                                                                                  | Chirograph file                                                                                              | Changes                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| [`src/Sdk/Sdk/Rocket.Surgery.LaunchPad.OpenApiClients.targets`](https://github.com/RocketSurgeonsGuild/LaunchPad/blob/e5d30f78e28db5214ee19f3d3b0be9556e3a4161/src/Sdk/Sdk/Rocket.Surgery.LaunchPad.OpenApiClients.targets)              | `src/Chirograph/Sdk/Chirograph.OpenApiClients.targets`                                                       | Rename `LaunchPad*` to `Chirograph*` and `LPSDK01xx` to `CHIRO1xx`. Replace `DovetailSdkImplicitPackageReference` with `_ChirographImplicitPackageReference`. Write `<ClientName>ServiceCollectionExtensions.g.cs` from the registration templates instead of `<ClientName>Joint.g.cs` (and `JointFile` becomes `RegistrationFile`). Fix the Kiota environment variables. Declare `DovetailTemplate` items, gated on D1's capability property. The rest of the logic is kept line for line: source-kind inference, `ProjectReference` to the server, `GenerateOpenApiDocuments;OpenApiGetDocuments`, `DocumentName` selection, URL download and cache, hash-guarded copies, the settings file, the `Inputs`/`Outputs` stamp and the design-time condition. |
| [`src/Sdk/Sdk/OpenApiClients/KiotaJoint.cs.template`](https://github.com/RocketSurgeonsGuild/LaunchPad/blob/e5d30f78e28db5214ee19f3d3b0be9556e3a4161/src/Sdk/Sdk/OpenApiClients/KiotaJoint.cs.template)                                  | `src/Chirograph/Sdk/templates/KiotaRegistration.cs.template`                                                 | The `Register` body becomes a `public static IHttpClientBuilder Add__ClientName__(this IServiceCollection services)` on `public static class __ClientName__ServiceCollectionExtensions`, which keeps `BaseAddressKey`. No Dovetail types. `GeneratedCode("Chirograph", "__ChirographVersion__")`.                                                                                                                                                                                                                                                                                                                                                                                                                                                          |
| [`src/Sdk/Sdk/OpenApiClients/RefitterJoint.cs.template`](https://github.com/RocketSurgeonsGuild/LaunchPad/blob/e5d30f78e28db5214ee19f3d3b0be9556e3a4161/src/Sdk/Sdk/OpenApiClients/RefitterJoint.cs.template)                            | `src/Chirograph/Sdk/templates/RefitterRegistration.cs.template`                                              | The same reshaping. Keeps `AddRefitGeneratedClient<I__ClientName__>()`.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| (new)                                                                                                                                                                                                                                    | `src/Chirograph/Sdk/templates/OpenApiClientJoint.cs.template`                                                | The only Dovetail-shaped code, expanded by Dovetail: `[DovetailExport] internal sealed class __ClientName__Joint : IServiceJoint` whose `Register` calls `global::__Namespace__.__ClientName__ServiceCollectionExtensions.Add__ClientName__(services)`. One template for both generators.                                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| [`src/Sdk/Sdk/Rocket.Surgery.LaunchPad.Detection.targets`](https://github.com/RocketSurgeonsGuild/LaunchPad/blob/e5d30f78e28db5214ee19f3d3b0be9556e3a4161/src/Sdk/Sdk/Rocket.Surgery.LaunchPad.Detection.targets) (+3 lines: the import) | `src/Chirograph/Sdk/Sdk.targets`                                                                             | `Sdk.targets` imports `Chirograph.ImplicitPackageReferences.targets` after `Chirograph.OpenApiClients.targets`, at the end of evaluation, so they see the project's items. That is where the PR's detection import placed them. In LaunchPad, the import line is replaced by the SDK chain in [§6](#6-how-launchpad-consumes-chirograph).                                                                                                                                                                                                                                                                                                                                                                                                                  |
| [`src/Sdk/Rocket.Surgery.LaunchPad.Sdk.Pack.targets`](https://github.com/RocketSurgeonsGuild/LaunchPad/blob/e5d30f78e28db5214ee19f3d3b0be9556e3a4161/src/Sdk/Rocket.Surgery.LaunchPad.Sdk.Pack.targets) (+16 lines)                      | `src/Chirograph/Chirograph.Pack.targets`                                                                     | The five `_LaunchPadOpenApiClientPackage` entries and their `Error`, renamed. Adds `ChirographVersion`. Writes `Sdk/Packages.props`, which `Sdk.props` imports. In-repo use falls back to `Directory.Packages.props` pins, as the PR's `_LaunchPadFindOpenApiTools` does (L330–336).                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| [`Directory.Packages.props`](https://github.com/RocketSurgeonsGuild/LaunchPad/blob/e5d30f78e28db5214ee19f3d3b0be9556e3a4161/Directory.Packages.props) (+7)                                                                               | `Directory.Packages.props`                                                                                   | Pins carried over: `Microsoft.OpenApi.Kiota` 1.38.0, `Microsoft.Kiota.Bundle` 2.1.6, `Refitter` 2.3.1, `Refit.HttpClientFactory` 16.4.0, `Microsoft.Extensions.ApiDescription.Server` 10.0.15/11.0.0. Renovate keeps them current from then on.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| [`docs/src/content/docs/guides/openapi-clients.md`](https://github.com/RocketSurgeonsGuild/LaunchPad/blob/e5d30f78e28db5214ee19f3d3b0be9556e3a4161/docs/src/content/docs/guides/openapi-clients.md)                                      | split across `docs/src/content/docs/**` ([Docs outline](#docs-outline))                                      | Remove LaunchPad and `ConfigureDovetail()` from the main path. Add `CHIRO107`. Add the registration extension. Dovetail gets its own guide.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| [`test/Sdk.Tests/SdkOpenApiClientTests.cs`](https://github.com/RocketSurgeonsGuild/LaunchPad/blob/e5d30f78e28db5214ee19f3d3b0be9556e3a4161/test/Sdk.Tests/SdkOpenApiClientTests.cs)                                                      | `test/Chirograph.Tests/{OpenApiClientTests,IncrementalTests,DocumentCacheTests,DiagnosticsTests}.cs`         | Split by concern. The scaffolding moves from LaunchPad's `SdkTree`/`PackedSdk`/`HostModelShape` to Dovetail's `SdkTestProject`. The apps call `services.Add<ClientName>()` on a plain `Host.CreateApplicationBuilder`, not `ConfigureDovetail()`. The server project calls `AddOpenApi()` itself: it no longer references Dovetail, so the XML-comment generator failure the PR worked around goes away (PR description, "Found, not fixed here").                                                                                                                                                                                                                                                                                                         |
| [`test/Sdk.Tests/Scaffolding/RocketsApi.cs`](https://github.com/RocketSurgeonsGuild/LaunchPad/blob/e5d30f78e28db5214ee19f3d3b0be9556e3a4161/test/Sdk.Tests/Scaffolding/RocketsApi.cs)                                                    | `test/Chirograph.Tests/Scaffolding/RocketsApi.cs`                                                            | Namespace only. It keeps its loopback `HttpListener` and the port retry.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| [`test/Sdk.Tests/Scaffolding/SdkTree.cs`](https://github.com/RocketSurgeonsGuild/LaunchPad/blob/e5d30f78e28db5214ee19f3d3b0be9556e3a4161/test/Sdk.Tests/Scaffolding/SdkTree.cs) (+environment parameter on `Run`)                        | `test/Chirograph.Tests/SdkTestProject.cs`                                                                    | The ability to pass environment variables to the app run (for `Clients__RocketsClient__BaseAddress`) is added to the adapted Dovetail scaffolding.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         |
| `test/Sdk.Tests/snapshots/SdkOpenApiClientTests.Should_Generate_A_Registered_Client_{Kiota,Refitter}_{File,Project,Url}.verified.txt` (6)                                                                                                | `test/Chirograph.Tests/snapshots/OpenApiClientTests.Should_Generate_A_Registered_Client_{…}.verified.txt`    | The content is unchanged (`RocketsClient: 6f9619ff-… FALCON-9` / `IRocketsClient: …`). Re-accepted under the new class name.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| `test/Sdk.Tests/snapshots/Sdk{Console,Package}Tests.Should_Generate_The_Package_Versions_When_Packed.verified.xml` (2)                                                                                                                   | `test/Chirograph.Tests/snapshots/PackageTests.Should_Generate_The_Package_Versions_When_Packed.verified.xml` | One package now. Properties renamed, plus `ChirographVersion`.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| `test/Sdk.Tests/snapshots/Sdk{Console,Package}Tests.Should_Ship_Only_Msbuild_Files.verified.txt` (2)                                                                                                                                     | `test/Chirograph.Tests/snapshots/PackageTests.Should_Ship_Only_Msbuild_Files.verified.txt`                   | Lists `Sdk/*.props`, `Sdk/*.targets` and `Sdk/templates/*.cs.template`.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    |

What is **not** extracted: LaunchPad's host-model detection, `HostModel`/`HostModelShape`, `Repository`, `ThirdParty` and `PackedSdk`.
Those are LaunchPad scaffolding, and Dovetail's `SdkTestProject` covers what Chirograph needs.

---

## Public surface

The item, properties, target hooks, diagnostic codes and the shape of the generated registration are all public API under
semantic versioning. Private names start with `_Chirograph`.

### `OpenApiClient` item

Carried over from the PR's guide
([metadata table](https://github.com/RocketSurgeonsGuild/LaunchPad/blob/e5d30f78e28db5214ee19f3d3b0be9556e3a4161/docs/src/content/docs/guides/openapi-clients.md)).

| Metadata              | Required | Default                                   | Meaning                                                                                                                                                                                                              |
| --------------------- | -------- | ----------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Include`             | yes      |                                           | The source. A `.csproj`/`.fsproj`/`.vbproj` is a server project. `http://` or `https://` is a URL. Anything else is a file (`.json`, `.yaml`, `.yml`), relative to the project.                                      |
| `Generator`           | yes      |                                           | `Kiota` or `Refitter` (`CHIRO100`).                                                                                                                                                                                  |
| `ClientName`          | yes      |                                           | A C# identifier (`CHIRO101`). Kiota's client class is called this, Refitter's interface is `I<ClientName>`, the registration is `Add<ClientName>()` and the configuration key is `Clients:<ClientName>:BaseAddress`. |
| `Namespace`           | no       | `$(RootNamespace)`                        | The namespace of everything generated for the client.                                                                                                                                                                |
| `DocumentName`        | no       | the only document, or `v1`                | For a server project that writes several documents.                                                                                                                                                                  |
| `DocumentCache`       | no       | `obj/…/openapi/<ClientName>/openapi.json` | URL source only: a project-relative file to keep the download in. Commit it to build offline.                                                                                                                        |
| `UpdateDocumentCache` | no       | `$(ChirographUpdateOpenApiDocumentCache)` | URL source only: `false` uses the cache whenever it exists.                                                                                                                                                          |
| `Options`             | no       |                                           | Extra arguments for `kiota generate`, or for `refitter` after the settings file.                                                                                                                                     |

### Properties

| Property                                   | Default                             | Meaning (PR name in brackets)                                                                                                                                                                                                                                                                             |
| ------------------------------------------ | ----------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `ChirographUpdateOpenApiDocumentCache`     | `true`                              | Default of `UpdateDocumentCache` (`LaunchPadUpdateOpenApiDocumentCache`).                                                                                                                                                                                                                                 |
| `ChirographOpenApiClientsDirectory`        | `$(IntermediateOutputPath)openapi/` | Where each client's folder goes (`LaunchPadOpenApiClientsDirectory`).                                                                                                                                                                                                                                     |
| `ChirographKiotaVersion`                   | packed pin (1.38.0)                 | `Microsoft.OpenApi.Kiota` tool version (`LaunchPadKiotaVersion`).                                                                                                                                                                                                                                         |
| `ChirographRefitterVersion`                | packed pin (2.3.1)                  | `Refitter` tool version (`LaunchPadRefitterVersion`).                                                                                                                                                                                                                                                     |
| `ChirographKiotaBundleVersion`             | packed pin (2.1.6)                  | `Microsoft.Kiota.Bundle` version added for Kiota clients (`LaunchPadKiotaBundleVersion`).                                                                                                                                                                                                                 |
| `ChirographRefitVersion`                   | packed pin (16.4.0)                 | `Refit.HttpClientFactory` version added for Refitter clients (`LaunchPadRefitVersion`).                                                                                                                                                                                                                   |
| `ChirographMicrosoftExtensionsHttpVersion` | packed pin (10.0.x)                 | `Microsoft.Extensions.Http` version added for Kiota clients (`LaunchPadMicrosoftExtensionsHttpVersion`), and **not added at all when the project has a Refitter client**: `Refit.HttpClientFactory` brings a newer one, and the packed pin beside it is an NU1605 downgrade on net11.0 (found in PR #17). |
| `ChirographGenerateDovetailJoint`          | `true`                              | New. `false` declares no `DovetailTemplate` items.                                                                                                                                                                                                                                                        |
| `ChirographVersion`                        | the SDK's version                   | New. Stamped into `GeneratedCode`. Read-only in practice.                                                                                                                                                                                                                                                 |

**Target hooks:** `ChirographGenerateOpenApiClients` (renamed from `LaunchPadGenerateOpenApiClients`), which runs
`AfterTargets="ResolveProjectReferences"` and `BeforeTargets="CoreCompile;BeforeCompile"`. It is the only target documented as a hook.

### Diagnostics

| Code       | Was         | Severity | Meaning                                                                               |
| ---------- | ----------- | -------- | ------------------------------------------------------------------------------------- |
| `CHIRO100` | `LPSDK0100` | error    | `Generator` is missing or is not `Kiota`/`Refitter`.                                  |
| `CHIRO101` | `LPSDK0101` | error    | `ClientName` is missing or is not a C# identifier.                                    |
| `CHIRO102` | `LPSDK0102` | error    | The document file does not exist.                                                     |
| `CHIRO103` | `LPSDK0103` | error    | The server project wrote several documents, or none matched `DocumentName`.           |
| `CHIRO104` | `LPSDK0104` | warning  | The URL could not be reached. The cached document is used.                            |
| `CHIRO105` | `LPSDK0105` | error    | The URL could not be reached, and there is no cached document.                        |
| `CHIRO106` | `LPSDK0106` | error    | The generator tool is not in the packages folders. Restore the project.               |
| `CHIRO107` | `LPSDK0107` | error    | The generator exited with an error. Its output is included. (Undocumented in the PR.) |
| `CHIRO108` | new         | error    | Two `OpenApiClient` items have the same `ClientName` (open question 1, resolved).     |

### Generated code

For a client `RocketsClient` in namespace `MyApp.Rockets`, everything is under `obj/<config>/<tfm>/openapi/RocketsClient/`, added to
`Compile` and `FileWrites`, and removed by `dotnet clean`:

- `generated/**` holds the generator's output: Kiota's `RocketsClient` and request builders, or Refitter's `IRocketsClient` and its
  contracts. It also holds `kiota-lock.json`, which stays in `obj`.
- `RocketsClientServiceCollectionExtensions.g.cs` holds the registration:

    ```csharp
    namespace MyApp.Rockets;

    [GeneratedCode("Chirograph", "1.0.0")]
    public static class RocketsClientServiceCollectionExtensions
    {
        public const string BaseAddressKey = "Clients:RocketsClient:BaseAddress";

        // Kiota: AddHttpClient("RocketsClient") + ConfigureHttpClient(base address) + AddTypedClient<RocketsClient>(HttpClientRequestAdapter)
        // Refitter: AddRefitGeneratedClient<IRocketsClient>() + ConfigureHttpClient(base address)
        public static IHttpClientBuilder AddRocketsClient(this IServiceCollection services) { … }
    }
    ```

- `openapi.json` or `openapi.yaml` (the document copy), `settings.txt`, `generated.stamp`, `<ClientName>.refitter` (Refitter only)
  and `download/` (URL only) are bookkeeping, as in the PR
  ([L143–150](https://github.com/RocketSurgeonsGuild/LaunchPad/blob/e5d30f78e28db5214ee19f3d3b0be9556e3a4161/src/Sdk/Sdk/Rocket.Surgery.LaunchPad.OpenApiClients.targets#L143-L150)).
- With Dovetail and D1, Dovetail writes `Chirograph.RocketsClient.g.cs` from `OpenApiClientJoint.cs.template`: an internal
  `[DovetailExport] RocketsClientJoint : IServiceJoint` that calls `AddRocketsClient(services)`. The declaration looks like this:

    ```xml
    <DovetailTemplate Include="RocketsClient"
                      File="$(MSBuildThisFileDirectory)templates/OpenApiClientJoint.cs.template"
                      OutputPrefix="Chirograph"
                      ClientName="RocketsClient"
                      Namespace="MyApp.Rockets"
                      ChirographVersion="$(ChirographVersion)" />
    ```

    This assumes `Include` must be unique across packages; open question 3 asks whether it needs a prefix.

---

## Test strategy

**Framework:** TUnit on Microsoft.Testing.Platform, TUnit's Should assertions, and Verify snapshots in `snapshots/`, as in Dovetail.
Following the generator-snapshot feedback for these repositories: snapshot the observable result (an app's output, a package's file
list, a `Packages.props`) rather than asserting on fragments of it.

**Shape:** a single test project, `test/Chirograph.Tests`, built like `Joinery.Dovetail.Sdk.Tests`:

- a `PackSourcePackages` target packs `src/**` into `artifacts/nuget-local` before `Build`;
- `SdkTestProject` scaffolds throwaway projects that use `<Sdk Name="Chirograph" Version="…-local" />`, restore from there, and run
  `dotnet build`/`dotnet run` out of process;
- `[TargetFrameworks]` covers net10.0 and net11.0.

**Two seams, both on scaffolded consumer projects (decided by the owner on 2026-11-15):**

1. **Evaluation:** the consumer is evaluated in-process with MSBuild.ProjectCreation, as many of Dovetail's SDK tests do, and the
   resulting items and properties are snapshotted. It covers what evaluation alone decides, without running a generator:
    - source-kind inference;
    - the injected server `ProjectReference` and its metadata;
    - `PackageDownload` items per generator;
    - implicit package references, and the rule that the project's own reference wins;
    - the `DovetailTemplate` declarations and their gating;
    - property defaults.
2. **Build:** real `dotnet build`/`dotnet run` out of process. It covers everything that needs the targets to run: generation, the
   registration, sources, the cache, incrementality, design-time builds and the diagnostics.

Package-content snapshots of the packed `.nupkg` sit alongside both. No test calls an individual target directly.

**How the PR's tests move.** Every PR test survives under a new class, and none is dropped.

| PR test (in `SdkOpenApiClientTests`)                                                | Chirograph test                                                                                                                         |
| ----------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------- |
| `Should_Generate_A_Registered_Client` × {Kiota, Refitter} × {File, Project, Url}    | `OpenApiClientTests.Should_Generate_A_Registered_Client`, the same matrix. The app uses `services.AddRocketsClient()` without Dovetail. |
| `Should_Only_Generate_A_Client_Again_When_Its_Document_Changes` × {Kiota, Refitter} | `IncrementalTests`, unchanged.                                                                                                          |
| `Should_Use_The_Last_Download_When_The_Url_Cannot_Be_Reached`                       | `DocumentCacheTests`, expecting `CHIRO104`.                                                                                             |
| `Should_Generate_From_The_Document_Cache_When_The_Url_Cannot_Be_Reached`            | `DocumentCacheTests`.                                                                                                                   |
| `Should_Use_The_Document_Cache_Without_Downloading_When_Not_Updating_It`            | `DocumentCacheTests`.                                                                                                                   |
| `Should_Fail_When_A_Client_Has_No_Generator_Or_Name`                                | `DiagnosticsTests`, expecting `CHIRO100`/`CHIRO101`.                                                                                    |
| `SdkPackageTests`/`SdkConsoleTests` package snapshots                               | `PackageTests`.                                                                                                                         |

**New tests**, for behaviour the PR has but doesn't test, or that this plan adds:

- `DiagnosticsTests`: `CHIRO102` (missing file), `CHIRO103` (server with two documents and no `DocumentName`), `CHIRO105` (unreachable
  URL with no cache), `CHIRO106` (tool absent: the version set to one that isn't in the packages folder, restore skipped), and
  `CHIRO107` (an invalid document makes the generator fail).
- `DesignTimeBuildTests`: a build with `DesignTimeBuild=true` runs no generator, and still adds the code an earlier build generated.
- `OpenApiClientTests.Should_Run_The_Generators_Without_Telemetry`: a stub tool records its environment, which pins
  `KIOTA_CLI_TELEMETRY_OPTOUT`, `KIOTA_OFFLINE_ENABLED` and `DOTNET_CLI_TELEMETRY_OPTOUT`. Alternatively, assert on the `Exec` task's
  parameters in a binlog via MSBuild.StructuredLogger.
- `OpenApiClientTests.Should_Leave_A_Package_The_Project_References_Alone`: a project that references `Microsoft.Kiota.Bundle` itself
  keeps its version.
- `OpenApiClientTests.Should_Register_A_Client_From_A_Library`: a library generates the client, and an app that doesn't use Dovetail
  calls the public `Add<ClientName>()`.
- `DovetailJointTests` (milestone 6): with a Dovetail release that has D1, an app that runs `ConfigureDovetail()` resolves the client
  with no registration call. With `ChirographGenerateDovetailJoint=false` it has no joint. With an older Dovetail, no
  `DovetailTemplate` is declared and the build succeeds.

**Network:** none beyond NuGet restores. `RocketsApi` serves the document and the API on loopback, as in the PR.

**Sample:** `sample/ChirographSample` builds in CI (`samples.yml`) against freshly packed `0.0.0-sample` packages. It proves
`<Sdk Name="Chirograph">` from a separate solution, which `SdkTestProject` can't.

---

## Docs outline

The docs site is Astro + Starlight, as in Dovetail, published to gh-pages by `build.yml`.

- **Home** (`index.mdx`): the one-item pitch, the chirograph metaphor, and three code tabs (file, server project, URL).
- **Getting started**: add `<Sdk Name="Chirograph" Version="…" />` (or `global.json` `msbuild-sdks`), declare a client, call
  `services.AddRocketsClient()`, set `Clients:RocketsClient:BaseAddress`.
- **Concepts**:
    - _Sources_: inference rules.
    - _Generators_: Kiota compared with Refitter, what each emits, and the packages added.
    - _Registration_: the extension, the configuration key, and chaining on `IHttpClientBuilder`.
    - _Document cache_: download, fallback, committing it, `UpdateDocumentCache`.
    - _How generation runs_: `PackageDownload`, `obj`, incrementality, design-time builds, telemetry off. Carried over from the PR
      guide's "How generation runs" section.
- **Guides**:
    - From a file.
    - From a server project (with `Microsoft.Extensions.ApiDescription.Server`, multi-targeted servers and `DocumentName`).
    - From a URL (CI without network, pinning with `UpdateDocumentCache`).
    - With Dovetail (joints through `DovetailTemplate`, the minimum Dovetail version, turning it off).
    - Migrating from `OpenApiReference` (`Microsoft.Extensions.ApiDescription.Client`).
    - Migrating from LaunchPad's preview (renames table: properties and `LPSDK` to `CHIRO`).
- **Reference**: the `OpenApiClient` item, properties, diagnostics (one anchor per code, so the build messages can link to it), and
  generated code.
- **Positioning**: a short "Compared with" page built from [Positioning](#positioning).

As in Dovetail's `DovetailItemDocumentationTests`, a test checks that the reference pages list exactly the metadata, properties and
codes that the targets read and raise.

---

## CI, release and NuGet publishing

These are the same mechanics as Dovetail, from the files listed above.

- **Build:** `.github/workflows/build.yml` runs `mise run build`, which runs `dotnet run build/Build.cs` (ModularPipelines). That builds,
  runs the tests (which pack `src/**` themselves), packs into `artifacts/nuget`, and uploads the `nuget` and docs artifacts. The docs
  job deploys to gh-pages, with PR previews and versioned `v*/` folders.
- **Lint:** `lint.yml` runs `hk check`/`hk fix` (`.config/hk.pkl`: dotnet-format, biome, oxfmt, tombi, actionlint, xmllint and the
  rest), with autofix.ci.
- **Samples:** `samples.yml` packs into `0.0.0-sample` and builds `sample/ChirographSample` on three operating systems.
- **Versioning:** GitVersion (`GitVersion.yml`: GitHubFlow, `main` labelled `beta`, `next` increments the major).
- **Releases:** GitReleaseManager drafts releases and milestones (`draft-release.yml`, `update-milestone.yml`, `close-milestone.yml`,
  `.github/release.yml` categories).
- **Publishing:** pushing a `v*` tag runs the publish job, which uses NuGet **trusted publishing** (OIDC): `environment: nuget`,
  `id-token: write`, `NuGet/login@v1` with `secrets.NUGET_USER`, then `dotnet nuget push nuget/*.nupkg --skip-duplicate`. This is
  as in [Dovetail's `build.yml`](https://github.com/RocketSurgeonsGuild/Dovetail/blob/main/.github/workflows/build.yml).
- **Reserving the ID (milestone 0, by hand, owner):**
    1. Pack a `Chirograph` 0.0.1 placeholder (`PackageType` `MSBuildSdk`, a README saying it is a placeholder, no files under `Sdk/`)
       and push it from the nuget.org account that will own the package.
    2. Unlist it.
    3. On nuget.org, add a trusted publishing policy for `RocketSurgeonsGuild/Chirograph`, workflow `build.yml`, environment `nuget`.
    4. In the GitHub repository, create the `nuget` environment and the `NUGET_USER` secret.
    5. Optionally, ask nuget.org for an ID prefix reservation of `Chirograph`.
- **Renovate:** `.github/renovate.json` as adapted above. The "openapi generators" group moves the tool and runtime pins together, and
  each such PR re-runs the full end-to-end matrix.

---

## Milestones

Each milestone is one pull request on `RocketSurgeonsGuild/Chirograph`, except where a row names another repository. Every
Chirograph PR passes `hk check`, `mise run build` and the tests that exist at that point.

**Tracking.** The epic is [RocketSurgeonsGuild/Chirograph#1](https://github.com/RocketSurgeonsGuild/Chirograph/issues/1). Its
sub-issues, the "blocked by" links between them, and the Dovetail tickets are all on the org project
[Chirograph](https://github.com/orgs/RocketSurgeonsGuild/projects/4).

| Milestone | Issue                                                                          |
| --------- | ------------------------------------------------------------------------------ |
| 0         | [Chirograph#2](https://github.com/RocketSurgeonsGuild/Chirograph/issues/2)     |
| 1         | [Chirograph#3](https://github.com/RocketSurgeonsGuild/Chirograph/issues/3)     |
| 2         | [Chirograph#4](https://github.com/RocketSurgeonsGuild/Chirograph/issues/4)     |
| 3         | [Chirograph#5](https://github.com/RocketSurgeonsGuild/Chirograph/issues/5)     |
| 4         | [Chirograph#6](https://github.com/RocketSurgeonsGuild/Chirograph/issues/6)     |
| 5         | [Chirograph#7](https://github.com/RocketSurgeonsGuild/Chirograph/issues/7)     |
| 6         | [Chirograph#8](https://github.com/RocketSurgeonsGuild/Chirograph/issues/8)     |
| 7         | [Chirograph#9](https://github.com/RocketSurgeonsGuild/Chirograph/issues/9)     |
| 8         | [Chirograph#10](https://github.com/RocketSurgeonsGuild/Chirograph/issues/10)   |
| 9         | [Chirograph#11](https://github.com/RocketSurgeonsGuild/Chirograph/issues/11)   |
| 10        | [Chirograph#12](https://github.com/RocketSurgeonsGuild/Chirograph/issues/12)   |
| 11        | [LaunchPad#3249](https://github.com/RocketSurgeonsGuild/LaunchPad/issues/3249) |
| 12        | [LaunchPad#3250](https://github.com/RocketSurgeonsGuild/LaunchPad/issues/3250) |
| D1        | [Dovetail#413](https://github.com/RocketSurgeonsGuild/Dovetail/issues/413)     |
| D2        | [Dovetail#414](https://github.com/RocketSurgeonsGuild/Dovetail/issues/414)     |

| #   | Milestone                                                                                                                     | Depends on      | Acceptance criteria                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| --- | ----------------------------------------------------------------------------------------------------------------------------- | --------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 0   | **Reserve the ID** (manual, owner)                                                                                            | —               | `Chirograph` 0.0.1 on nuget.org, unlisted. The trusted publishing policy is registered. The `nuget` environment and the `NUGET_USER` secret exist.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| 1   | **Repository scaffold from Dovetail**                                                                                         | —               | Every file in [Files brought across](#files-brought-across-from-dovetail) is present, with no `src/` or `test/` projects yet. `Chirograph.slnx` builds empty. `hk check` passes. `build.yml` is green. The docs site deploys a placeholder home page. `apm install` has populated the agent folders. `GLOSSARY.md` holds the [Glossary](#glossary). The rewritten `AGENTS.md`, `CLAUDE.md`, `Readme.md` and `CHANGELOG.md` are in.                                                                                                                                                                                                                                                        |
| 2   | **Empty SDK package and test harness**                                                                                        | 1               | `src/Chirograph` packs a `Chirograph` MSBuildSdk with `Sdk/Sdk.props`, `Sdk/Sdk.targets`, `Sdk/Packages.props` (generated: `ChirographVersion` and the five package versions) and `Chirograph.ImplicitPackageReferences.targets`. `test/Chirograph.Tests` packs `src/**` and scaffolds a consumer with `<Sdk Name="Chirograph" />`. `PackageTests` snapshots the file list and `Packages.props`. A consumer with no `OpenApiClient` items builds, and gains no package references or downloads.                                                                                                                                                                                           |
| 3   | **File source, both generators, registration**                                                                                | 2               | The targets are ported for the file source: `CHIRO100`–`102`, `106` and `107`, incrementality, the design-time condition, and `PackageDownload` of both tools. Registration templates write `Add<ClientName>()`, and the Kiota environment variables are fixed. Tests: `Should_Generate_A_Registered_Client` {Kiota, Refitter} × File, `IncrementalTests`, `DiagnosticsTests` for those codes, `DesignTimeBuildTests`, the telemetry test, the project's-own-reference-wins test and the library test, all on net10.0 and net11.0. Check that `Microsoft.Extensions.Http` brings `IConfiguration` (`Microsoft.Extensions.Configuration.Abstractions`) to Kiota clients ([Risks](#risks)). |
| 4   | **Server-project source**                                                                                                     | 3               | The `ProjectReference` injection (`ReferenceOutputAssembly=false`, `SkipGetTargetFrameworkProperties`), `GenerateOpenApiDocuments;OpenApiGetDocuments`, `DocumentName` and `CHIRO103`. Tests: {Kiota, Refitter} × Project, `CHIRO103`, and a multi-targeted server.                                                                                                                                                                                                                                                                                                                                                                                                                       |
| 5   | **URL source and document cache**                                                                                             | 3               | Download, `CHIRO104`/`105`, `DocumentCache`, `UpdateDocumentCache` and `ChirographUpdateOpenApiDocumentCache`. Tests: {Kiota, Refitter} × Url, and the three `DocumentCacheTests`.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| 6   | **Docs**                                                                                                                      | 3, 4, 5         | The full [Docs outline](#docs-outline), including the reference-page drift test. Getting started works from a clean clone.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| 7   | **Sample and samples CI**                                                                                                     | 4, 5            | `sample/ChirographSample` (server project, Kiota client from the project, Refitter client from a committed file, console app without Dovetail) builds in `samples.yml` on three operating systems.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| 8   | **1.0.0-beta.1**                                                                                                              | 6, 7            | Tag `v1.0.0-beta.1`. Trusted publishing pushes it. A clean project outside the repository uses `<Sdk Name="Chirograph" Version="1.0.0-beta.1" />`.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| D1  | **Dovetail: `DovetailTemplate` metadata tokens** ([Dovetail#413](https://github.com/RocketSurgeonsGuild/Dovetail/issues/413)) | —               | See [D1](#d1-dovetailtemplate-substitutes-every-metadata-value). Released in a Dovetail version.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          |
| 9   | **Dovetail joints**                                                                                                           | 8, D1 released  | `OpenApiClientJoint.cs.template` and the `DovetailTemplate` declarations, gated on D1's capability property and on `ChirographGenerateDovetailJoint`. `DovetailJointTests` pass (with D1, with the switch off, and with an older Dovetail). The Dovetail guide is published.                                                                                                                                                                                                                                                                                                                                                                                                              |
| 10  | **1.0.0**                                                                                                                     | 9               | Tag `v1.0.0`. The release notes include the [Positioning](#positioning) paragraph and the LaunchPad rename table.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         |
| 11  | **LaunchPad chains Chirograph** (in LaunchPad)                                                                                | 10, D1 released | A new LaunchPad PR imports `Sdk="Chirograph" Version="$(LaunchPadChirographVersion)"` in its `Sdk.props`/`Sdk.targets` and writes `LaunchPadChirographVersion` in its pack targets. It deletes nothing that PR 3238 would have added, because 3238 is never merged. LaunchPad's SDK tests carry one smoke test, a Kiota file client resolved from `ConfigureDovetail()`. PR 3238 is closed with a link.                                                                                                                                                                                                                                                                                   |
| 12  | **Rebase the dogfooding** (in LaunchPad)                                                                                      | 11              | [#3241](https://github.com/RocketSurgeonsGuild/LaunchPad/pull/3241) is rebased onto milestone 11 with the codes renamed. The functional tests and the re-enabled sample tests pass, and NSwag is gone, as [#3218](https://github.com/RocketSurgeonsGuild/LaunchPad/issues/3218)'s acceptance criteria require.                                                                                                                                                                                                                                                                                                                                                                            |
| D2  | **Dovetail: type-checked templates** ([Dovetail#414](https://github.com/RocketSurgeonsGuild/Dovetail/issues/414), later)      | D1              | See [D2](#d2-templates-that-compile-and-type-check-but-are-not-compiled-into-the-project). It blocks nothing in this plan.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |

Milestones 4 and 5 can be worked in parallel once 3 lands, and D1 can start at any time.

---

## Dovetail tickets (drafts)

Opened on 2026-11-15 as [RocketSurgeonsGuild/Dovetail#413](https://github.com/RocketSurgeonsGuild/Dovetail/issues/413) (D1) and
[RocketSurgeonsGuild/Dovetail#414](https://github.com/RocketSurgeonsGuild/Dovetail/issues/414) (D2). The issues are the source of
truth from here on; the drafts below are kept for the plan's history.

### D1: `DovetailTemplate` substitutes every metadata value

> **Title:** DovetailTemplate: substitute every item metadata value, as `{Name}` and `__Name__`
>
> `DovetailTemplate` ([Shipping Templates](https://github.com/RocketSurgeonsGuild/Dovetail/blob/main/docs/src/content/docs/guides/shipping-templates.md),
> [reference](https://github.com/RocketSurgeonsGuild/Dovetail/blob/main/docs/src/content/docs/reference/dovetail-template.md)) only
> replaces `{HostName}` and `{HostType}` (open question 2 of
> [`plans/template-extension-point.md`](https://github.com/RocketSurgeonsGuild/Dovetail/blob/main/plans/template-extension-point.md)).
> Chirograph (an MSBuild SDK that generates OpenAPI clients) needs one joint per client, from one template, with the client's name
> and namespace in it. It can't reference Dovetail, so it can't write the joint itself.
>
> **Proposal**
>
> - Every metadata value of a `DovetailTemplate` item, custom metadata included, replaces both `{Name}` and `__Name__` in the
>   template. `__Name__` keeps a template a valid C# file (identifiers), which D2 builds on.
> - Well-known metadata (`File`, `Package`, `DirectReferenceOnly`, `References`, `Hosts` and `OutputPrefix`) are substituted too.
> - `{HostName}`/`{HostType}` keep working unchanged.
> - Substituted values are escaped for `WriteLinesToFile` as today. The no-backslashes rule still applies to values.
> - A capability property, for example `DovetailTemplateMetadataTokens=true`, set in `Joinery.Dovetail`'s build props, so a package
>   can declare token-bearing templates only for a Dovetail that expands them.
>
> **Acceptance**
>
> - `TemplateExtensionPointTests` covers a custom metadata value in both forms.
> - The same `File` declared twice under different `Include`s with different metadata produces two outputs.
> - Unknown tokens are left as they are.
> - The reference page lists the rule, and `DovetailItemDocumentationTests` passes.
>
> **First user:** Chirograph's `OpenApiClientJoint.cs.template`.

### D2: Templates that compile and type-check, but are not compiled into the project

> **Title:** DovetailTemplate: let a template be a real C# file that is type-checked but not compiled into the consumer
>
> Templates today are text with tokens, so their mistakes show up only in a consumer's build. With `__Name__` tokens (D1), a template
> can be a valid C# file. Proposal: a template can live in a project as `*.cs` with `__Name__` placeholders standing in for real
> types, for example through `using` aliases or stub types in the shipping project. The shipping project compiles it to type-check it
> but leaves it out of its assembly, and packs it as a template. Dovetail could provide the MSBuild glue (an item or metadata that
> marks a `Compile` item as a template). To be designed. It blocks nothing in Chirograph.

---

## Risks

| Risk                                                                                                                                                                                                                                                                                                           | Likelihood       | Impact                                | Mitigation                                                                                                                                                                                                  |
| -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------- | ------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **An SDK element is less familiar than a `PackageReference`.** Some users won't know `<Sdk Name>` or `msbuild-sdks`.                                                                                                                                                                                           | medium           | adoption                              | Getting started leads with the one-line `<Sdk Name>` form. The [Packaging](#1-packaging-an-msbuild-sdk-only) decision records why a `PackageReference` can't work.                                          |
| **Tool path layout changes** (`tools/net10.0/any/kiota.dll`, `refitter.dll`, [L341–348](https://github.com/RocketSurgeonsGuild/LaunchPad/blob/e5d30f78e28db5214ee19f3d3b0be9556e3a4161/src/Sdk/Sdk/Rocket.Surgery.LaunchPad.OpenApiClients.targets#L341-L348)) when a tool drops `net10.0` or renames its DLL. | medium           | `CHIRO106` after a Renovate bump      | The Renovate group runs the end-to-end matrix. If a tool moves to `net11.0` only, raise the TFM folder to a property and probe the newest.                                                                  |
| **The tools' runtime.** The tools need a .NET 10+ runtime on the build machine (roll-forward Major).                                                                                                                                                                                                           | low              | build failure on SDK-9-only machines  | The documented minimum is SDK 10.0.100.                                                                                                                                                                     |
| **Kiota or Refitter CLI flags change** (`--exclude-backward-compatible`, `--simple-output`, `--no-banner`).                                                                                                                                                                                                    | medium           | `CHIRO107`                            | The same Renovate gate, with `CHIRO107` showing the tool's output.                                                                                                                                          |
| **`Microsoft.Extensions.Http` doesn't bring `IConfiguration`** to Kiota clients. The registration reads `IConfiguration` through `GetService`. The PR's apps always had hosting, so it was never tested bare.                                                                                                  | low              | compile error in a bare library       | Milestone 3 checks a library with only `Microsoft.Kiota.Bundle` and `Microsoft.Extensions.Http`. If the type is missing, add `Microsoft.Extensions.Configuration.Abstractions` as a fifth implicit package. |
| **A Dovetail user before D1 ships** gets no joint.                                                                                                                                                                                                                                                             | certain until D1 | must call `Add<ClientName>()` by hand | Documented in the Dovetail guide. LaunchPad waits for D1 (milestone 11).                                                                                                                                    |
| **A tool ID from PR 3238 lingers.** LaunchPad users may still have `LPSDK` codes in `NoWarn`, or `LaunchPad*` properties set.                                                                                                                                                                                  | low              | silently ignored settings             | Only preview builds of LaunchPad had them. The migration guide's rename table covers them.                                                                                                                  |
| **Dovetail's XML-doc failure** (`AddOpenApi()` in a server that references Joinery.Dovetail; PR description, "Found, not fixed here").                                                                                                                                                                         | known            | LaunchPad servers only                | Not Chirograph's to fix. Chirograph's tests use servers without Dovetail. File it with D1 or separately.                                                                                                    |
| **The placeholder package** confuses people browsing nuget.org.                                                                                                                                                                                                                                                | low              | —                                     | Unlist it, and give it a README that names the real release.                                                                                                                                                |
| **Port race in the test listener.** The PR saw `Address already in use` once and fixed it with a retry.                                                                                                                                                                                                        | low              | flaky CI                              | Keep `RocketsApi`'s retry, and run the end-to-end classes not in parallel if it recurs.                                                                                                                     |

---

## Open questions

These were not settled in the session. None of them blocks milestones 0–5.

1. ~~**Generated namespace collisions.**~~ **Resolved: `CHIRO108`.** Two clients with the same `ClientName` in one project fail the
   build with `CHIRO108`, which names both items, before either is generated (`DiagnosticsTests.Should_Fail_When_Two_Clients_Have_The_Same_Name`).
2. **The server project lacks `Microsoft.Extensions.ApiDescription.Server`.** Today the result is `CHIRO103` listing no documents.
   Recommendation: give it a dedicated message (or a `CHIRO109`) that names the missing package.
3. **`DovetailTemplate` `Include` uniqueness.** Dovetail requires the name to be unique across packages. Is the bare `ClientName`
   enough, or should Chirograph prefix it (`Chirograph.RocketsClient`)? Decide with D1.
4. **Prefix reservation.** Request one for `Chirograph` once 1.0 ships, or leave it at the placeholder?
5. **YAML from a server project** is always `.json` in the PR
   ([L140–141](https://github.com/RocketSurgeonsGuild/LaunchPad/blob/e5d30f78e28db5214ee19f3d3b0be9556e3a4161/src/Sdk/Sdk/Rocket.Surgery.LaunchPad.OpenApiClients.targets#L140-L141)).
   That's fine while `Microsoft.Extensions.ApiDescription.Server` writes JSON. Revisit if it starts writing YAML.

---

## Verification of this plan

Done before finishing, on 2026-11-15:

- **PR paths:** every `PR:` file linked above exists on `claude/3218-sdk-openapi-clients-5b8493` at `e5d30f78e`, checked with
  `git cat-file -e`.
- **Dovetail paths:** every Dovetail source path in [Files brought across](#files-brought-across-from-dovetail) exists at
  `e35038ab3c`, checked with `git ls-files`.
- **Links:** every external link in this file returned HTTP 200 (after redirects) with `curl -sIL`. The deprecation page redirects to
  `learn.microsoft.com/en-us/aspnet/core/breaking-changes/10/apidescription-client-deprecated`.
- **NuGet IDs:** `Chirograph`, `Chirograph.Sdk` and `Chirograph.Dovetail` returned 404 on nuget.org (available).
- **The packaging claim:** the throwaway probe described in [§1](#1-packaging-an-msbuild-sdk-only), run in the session's scratch
  directory. Nothing was created in any repository.
