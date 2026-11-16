---
title: Getting started
description: Add the Chirograph SDK to a project, declare a client from an OpenAPI document, register it and call it.
tags: [getting-started, msbuild]
sidebar:
    order: 0
---

This page takes a console application from nothing to a call through a generated client. The client comes from an OpenAPI file in
the project. The [guides](#next-steps) cover the other sources.

You need the .NET SDK 10.0.100 or later: the generators run on .NET 10. The generated code is tested on `net10.0` and `net11.0`.
Other target frameworks are not blocked, but they are untested.

## 1. Add the SDK

Chirograph is an MSBuild SDK. Add it next to your project's own SDK:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Sdk Name="Chirograph" Version="1.0.0" />

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
```

To pin the version once for a whole repository, put it in `global.json` and leave `Version` out of the project:

```json
{
    "msbuild-sdks": {
        "Chirograph": "1.0.0"
    }
}
```

```xml
<Sdk Name="Chirograph" />
```

> [!NOTE]
> Chirograph has no release on nuget.org yet. Until the first beta ships, [pack it from a clone](#before-the-first-release) and use
> that version.

## 2. Declare a client

Put the API's OpenAPI document in the project, for example `openapi/rockets.json`, and add an `OpenApiClient` item:

```xml
<ItemGroup>
  <OpenApiClient Include="openapi/rockets.json" Generator="Kiota" ClientName="RocketsClient" Namespace="MyApp.Rockets" />
</ItemGroup>
```

- `Include` is the [source](../concepts/sources/): a file here. A project path or a URL works the same way.
- `Generator` is `Kiota` or `Refitter`. See [Generators](../concepts/generators/) to choose.
- `ClientName` names the client. Kiota's client class is `RocketsClient`, Refitter's interface would be `IRocketsClient`, and the
  registration is `AddRocketsClient()`.
- `Namespace` is optional. Without it, the code is in the project's `RootNamespace`.

Build the project. The first build restores the generator, generates the client into `obj` and compiles it. Nothing is written into
your project folder.

## 3. Register the client

The build also writes `AddRocketsClient()`, an extension on `IServiceCollection`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MyApp.Rockets;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddRocketsClient();
using var host = builder.Build();

var rockets = host.Services.GetRequiredService<RocketsClient>();
var rocket = await rockets.Rockets[Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff")].GetAsync();
```

`Host.CreateApplicationBuilder` comes from `Microsoft.Extensions.Hosting`, which a console project references itself. A web project
already has it.

## 4. Set the base address

The registration reads the client's base address from `Clients:<ClientName>:BaseAddress`, for example in `appsettings.json`:

```json
{
    "Clients": {
        "RocketsClient": {
            "BaseAddress": "https://rockets.example.com/"
        }
    }
}
```

Any configuration source works, so an environment variable `Clients__RocketsClient__BaseAddress` does too. Without a base address, a
Kiota client uses the first server of its document. A Refit client needs one. The same section also takes a `Timeout`, default
request headers and resilience: see [Configuration](../concepts/registration/#configuration).

## Before the first release

Until Chirograph is on nuget.org, pack it from a clone into a local folder and add that folder as a package source.

```sh
git clone https://github.com/RocketSurgeonsGuild/Chirograph.git
cd Chirograph
mise install
dotnet pack src/Chirograph/Chirograph.csproj -o ~/chirograph-packages -p:Version=1.0.0-local
```

[mise](https://mise.jdx.dev/) installs the .NET SDK the repository builds with. Without it, use a .NET 11 SDK. Then, next to your
solution, add a `nuget.config` with the folder (NuGet does not expand `~`, so give the full path):

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="chirograph-local" value="/absolute/path/to/chirograph-packages" />
  </packageSources>
</configuration>
```

Use `Version="1.0.0-local"` in the steps above. NuGet keeps the first package it sees for a version in its global packages folder,
so when you pack again, either use a new version or delete `~/.nuget/packages/chirograph/1.0.0-local`.

## Next steps

- [From a file](from-a-file/): a document kept in the project, JSON or YAML.
- [From a server project](from-a-server-project/): an ASP.NET Core project in the same build.
- [From a URL](from-a-url/): a document downloaded on build, with a cache you can commit.
- [With Dovetail](with-dovetail/): the client registered by a joint.
- [With Optionary](with-optionary/): `appsettings.json` IntelliSense and validation for each client's section.
- Coming from somewhere else: [`OpenApiReference`](migrating-from-openapireference/) or
  [LaunchPad's preview](migrating-from-launchpad/).
