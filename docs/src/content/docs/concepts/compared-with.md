---
title: Compared with
description: Where Chirograph sits among OpenApiReference, Kiota and Refitter's own build integrations, hand-written targets, and generators that ship their own code generation.
sidebar:
    order: 6
---

Chirograph is the MSBuild layer for the two generators .NET teams already choose: Kiota, and [Refit](https://github.com/reactiveui/refit)
through Refitter. It generates no code of its own, so it inherits each generator's fidelity and keeps up with their releases by
changing a version. What it adds:

- one item per client;
- three sources, including a sibling server project and a URL with a committable cache;
- incremental, offline-capable, telemetry-free builds;
- an `IHttpClientFactory` registration, and with it a Dovetail joint ([planned](../../guides/with-dovetail/)).

## `OpenApiReference` and `dotnet openapi`

`Microsoft.Extensions.ApiDescription.Client` is deprecated as of .NET 10
([breaking change](https://learn.microsoft.com/en-us/dotnet/core/compatibility/aspnet-core/10/apidescription-client-deprecated),
[dotnet/aspnetcore#59208](https://github.com/dotnet/aspnetcore/issues/59208)). Microsoft now points at each generator's own tooling,
and at committing the generated code or running generation in a custom pre-build step. Chirograph is that pre-build step, written
once. [Migrating from OpenApiReference](../../guides/migrating-from-openapireference/) maps one item onto the other.

## Kiota with a hand-written target

Kiota dropped its own MSBuild integration for lack of use, and points people at an `Exec` task
([microsoft/kiota#3005](https://github.com/microsoft/kiota/issues/3005)). Blog posts show that target for one server project and one
tool manifest ([Meziantou](https://www.meziantou.net/generate-a-kiota-client-at-build-time-from-an-asp-net-core-openapi-file.htm)).
Chirograph is that target for any number of clients and all three sources, with the incrementality, design-time behaviour and
telemetry settings already worked out, and without a tool manifest.

## Refitter.MSBuild

[Refitter.MSBuild](https://www.nuget.org/packages/Refitter.MSBuild) runs Refitter from the build. It finds `.refitter` files by
scanning the project folder, and runs on every build: its target has no inputs or outputs and no design-time guard. It writes into the
project folder by default, has telemetry on by default, and is Refit-only.

Chirograph drives the Refitter CLI instead. It writes the `.refitter` file into `obj` itself, from the item, so each Refitter client
gets the same incrementality and design-time rules as a Kiota client.

## Generators with their own code generation

[AutoSDK](https://github.com/tryAGI/AutoSDK) and
[OpenApiWeaver](https://dev.to/polymind/openapiweaver-generate-type-safe-c-clients-from-openapi-at-compile-time-with-source-generators-5e79)
also offer one item per client, but generate the client with their own code generators. AutoSDK is still before 1.0. OpenApiWeaver's
item, `OpenApiWeaverDocument`, is close to Chirograph's.

Pick one of them if you want their client. Pick Chirograph if you want Kiota's or Refit's.

## Summary

|                     | Chirograph        | `OpenApiReference` | Hand-written `Exec` | Refitter.MSBuild   |
| ------------------- | ----------------- | ------------------ | ------------------- | ------------------ |
| Generators          | Kiota, Refitter   | NSwag by default   | Whatever you call   | Refitter           |
| Status              | Active            | Deprecated         | Yours to maintain   | Active             |
| Declared with       | One item          | One item           | A target            | A `.refitter` file |
| Generated code goes | `obj`             | `obj`              | Your choice         | The project folder |
| Generates when      | Something changed | —                  | Your choice         | Every build        |
| Generator telemetry | Off               | —                  | Your choice         | On by default      |
