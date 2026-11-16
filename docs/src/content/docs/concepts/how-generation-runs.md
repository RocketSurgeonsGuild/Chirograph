---
title: How generation runs
description: How Chirograph fetches the generators with PackageDownload, generates into obj, stays incremental, behaves in design-time builds and keeps telemetry off.
tags: [msbuild]
sidebar:
    order: 5
---

## The tools come from the packages folder

Kiota and Refitter are .NET tools. Chirograph declares each one a client uses as a `PackageDownload`, so NuGet restores it with the
project's other packages, and the build runs it from the packages folder with `dotnet exec`. Nothing is installed globally or in a
tool manifest, and once the packages are restored, a file or server-project source needs no network.

That is why Chirograph is an MSBuild SDK rather than an ordinary package: NuGet restore never sees a `PackageDownload` or
`PackageReference` that a package's own `build/` targets declare.

The tools run on .NET 10, rolling forward to a newer runtime when .NET 10 isn't installed, so a build needs the .NET SDK 10.0.100 or
later. A tool that isn't in the packages folder fails the build with [`CHIRO106`](../../reference/diagnostics/#chiro106): restore the
project.

## When it runs

The target `ChirographGenerateOpenApiClients` runs after the project's references are resolved, and before compilation. For a server
project source, that means the server has been built. The target is the hook to run something before or after the clients are
generated:

```xml
<Target Name="ListClients" AfterTargets="ChirographGenerateOpenApiClients">
  <Message Importance="high" Text="Generated: @(OpenApiClient->'%(ClientName)')" />
</Target>
```

A project without `OpenApiClient` items doesn't run it, and gets no packages or downloads.

## Everything stays in `obj`

Each client has a folder, `obj/<configuration>/<framework>/openapi/<ClientName>/` by default
(`ChirographOpenApiClientsDirectory` moves the parent). The generated code and the registration are added to `Compile` there, and
never written into the project. `dotnet clean` removes them. [Generated code](../../reference/generated-code/) lists the folder's
files.

## Incremental

A client is generated again only when something it depends on changed:

1. The document is copied into the client's folder only when its content differs from the copy there.
2. The client's settings (the generator, the tool's version, `ClientName`, `Namespace` and `Options`) are written to `settings.txt`
   only when they differ.
3. The generator runs only when the copy or `settings.txt` is newer than the client's last generation.

So a build with nothing changed runs no generator, and neither does a server that writes the same document again. A change to one
client regenerates only that client.

## Design-time builds

An IDE runs design-time builds to learn a project's compile items. Those never run a generator: they only add the code an earlier
build generated, so IntelliSense sees the client without the IDE downloading or generating anything. They check nothing either: a
client with a mistake loads without an error and without creating a folder, and the next build reports it. After you change a
client, build once to update it.

## No telemetry

A build is no place to call home. Chirograph runs the tools with:

- `KIOTA_CLI_TELEMETRY_OPTOUT=true`, which turns Kiota's telemetry off;
- `KIOTA_OFFLINE_ENABLED=true`, which turns Kiota's update check off;
- `DOTNET_CLI_TELEMETRY_OPTOUT=1`;
- Refitter's `--no-logging`, which turns its telemetry off.

## Output and failures

What a tool writes is logged at low importance, and not parsed for warnings (Kiota warns about every document without a server).
When a tool exits with an error, the build fails with [`CHIRO107`](../../reference/diagnostics/#chiro107), which includes what the
tool wrote. Run the build with `-v:n` to see the tools' output on success.
