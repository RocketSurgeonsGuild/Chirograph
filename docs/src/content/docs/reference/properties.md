---
title: Properties
description: The MSBuild properties that change every client of a project, their defaults, and the target hooks before and after generation.
tags: [msbuild]
sidebar:
    order: 2
---

Set a property in the project file, a `Directory.Build.props`, or on the command line (`-p:Name=value`). The version properties are
set when the SDK is imported, each only when the project hasn't set it already, so a value in `Directory.Build.props` or the project
wins.

## Properties

| Property                                       | Default                             | Meaning                                                                                                                                                                                                                                              |
| ---------------------------------------------- | ----------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `ChirographUpdateOpenApiDocumentCache`         | `true`                              | The default of every client's `UpdateDocumentCache`: whether a URL is downloaded again when its document is cached. See [Document cache](../../concepts/document-cache/#updating).                                                                   |
| `ChirographGenerateDovetailJoint`              | `true`                              | Whether each client gets a Dovetail joint, with a Dovetail that can write it. See [With Dovetail](../../guides/with-dovetail/).                                                                                                                      |
| `ChirographServiceDiscovery`                   | `true`                              | The default of every client's `ServiceDiscovery`: `false` leaves service discovery out of the registrations when the project references Microsoft.Extensions.ServiceDiscovery. See [Service discovery](../../concepts/registration/#turning-it-off). |
| `ChirographOptionarySchema`                    | `true`                              | Whether each client declares the schema of its `Clients:<ClientName>` section to Optionary. See [With Optionary](../../guides/with-optionary/).                                                                                                      |
| `ChirographOpenApiClientsDirectory`            | `$(IntermediateOutputPath)openapi/` | The folder each client's folder goes in, relative to the project. A folder inside the project is left out of its default items, so the code is compiled once.                                                                                        |
| `ChirographKiotaVersion`                       | the packed version                  | The version of the `Microsoft.OpenApi.Kiota` tool.                                                                                                                                                                                                   |
| `ChirographRefitterVersion`                    | the packed version                  | The version of the `Refitter` tool.                                                                                                                                                                                                                  |
| `ChirographKiotaBundleVersion`                 | the packed version                  | The version of `Microsoft.Kiota.Bundle` added to a project with a Kiota client.                                                                                                                                                                      |
| `ChirographRefitVersion`                       | the packed version                  | The version of `Refit.HttpClientFactory` added to a project with a Refitter client.                                                                                                                                                                  |
| `ChirographMicrosoftExtensionsHttpVersion`     | the packed version                  | The version of `Microsoft.Extensions.Http` added to a project with a Kiota client and no Refitter client.                                                                                                                                            |
| `ChirographDuendeAccessTokenManagementVersion` | the packed version                  | The version of `Duende.AccessTokenManagement` added to a project with a client whose `Authentication` declares `ClientCredentials`. Only 4.x works with the generated code.                                                                          |
| `ChirographAzureIdentityVersion`               | the packed version                  | The version of `Azure.Identity` added to a project with a client whose `Authentication` declares `Azure`.                                                                                                                                            |
| `ChirographVersion`                            | the SDK's version                   | Stamped into the registration's `[GeneratedCode]`. Read it; don't set it.                                                                                                                                                                            |
| `ChirographGenerateOpenApiClientsDependsOn`    | empty                               | Targets to run before the documents are read, after `ChirographBeforeGenerateOpenApiClients`. Add yours: `$(ChirographGenerateOpenApiClientsDependsOn);MyTarget`. See [Targets](#targets).                                                           |

The packed versions are the ones Chirograph's own repository pinned when the SDK was packed: they are in the package's
`Sdk/Packages.props`. A package the project references itself is never added, whatever these say: see
[Generators](../../concepts/generators/#the-packages-added-to-the-project).

A tool version that isn't in the packages folder fails the build with [`CHIRO106`](../diagnostics/#chiro106) until the project is
restored.

## Targets

| Target                                   | Runs                                                                            | Meaning                                                                                                                                                                                                                               |
| ---------------------------------------- | ------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `ChirographGenerateOpenApiClients`       | before `BeforeCompile` and `CoreCompile`, once the references are resolved      | Generates every client that is out of date and adds the generated code to the compilation. A design-time build only adds what an earlier build generated. Hook `AfterTargets` on it to run something after the clients are generated. |
| `ChirographBeforeGenerateOpenApiClients` | first thing `ChirographGenerateOpenApiClients` does, not in a design-time build | Empty. Hook `BeforeTargets` on it to run something before any document is checked, read or downloaded: a target that writes the document a client is generated from, say.                                                             |

A target hooked `BeforeTargets="ChirographGenerateOpenApiClients"` runs too late for that: MSBuild runs a target's `DependsOnTargets`,
where Chirograph does its work, before the targets that are `BeforeTargets` of it. Hook `ChirographBeforeGenerateOpenApiClients`
instead, or add the target to `ChirographGenerateOpenApiClientsDependsOn`:

```xml
<Target Name="ExportApiSpec" BeforeTargets="ChirographBeforeGenerateOpenApiClients">
  <Exec Command="dotnet run --project ../Exporter -- $(MSBuildProjectDirectory)/openapi.json" />
</Target>
```

These two targets are the only public ones. The others start with `_Chirograph` and can change in any release.
