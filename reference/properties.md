# Properties

Set a property in the project file, a `Directory.Build.props`, or on the command line (`-p:Name=value`). The version properties are
set when the SDK is imported, each only when the project hasn't set it already, so a value in `Directory.Build.props` or the project
wins.

## Properties

| Property                                   | Default                             | Meaning                                                                                                                                                                                                                  |
| ------------------------------------------ | ----------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `ChirographUpdateOpenApiDocumentCache`     | `true`                              | The default of every client's `UpdateDocumentCache`: whether a URL is downloaded again when its document is cached. See [Document cache](../../concepts/document-cache/#updating).                                       |
| `ChirographGenerateDovetailJoint`          | `true`                              | Whether each client gets a Dovetail joint, with a Dovetail that can write it. See [With Dovetail](../../guides/with-dovetail/).                                                                                          |
| `ChirographScaffoldRegistration`           | `true`                              | Whether the build writes each client's registration file (`RegistrationFile`) when it is missing. `false` for a project that keeps it elsewhere. See [Registration](../../concepts/registration/#the-registration-file). |
| `ChirographOptionarySchema`                | `true`                              | Whether each client declares the schema of its `Clients:<ClientName>` section to Optionary. See [With Optionary](../../guides/with-optionary/).                                                                          |
| `ChirographOpenApiClientsDirectory`        | `$(IntermediateOutputPath)openapi/` | The folder each client's folder goes in, relative to the project. A folder inside the project is left out of its default items, so the code is compiled once.                                                            |
| `ChirographKiotaVersion`                   | the packed version                  | The version of the `Microsoft.OpenApi.Kiota` tool.                                                                                                                                                                       |
| `ChirographRefitterVersion`                | the packed version                  | The version of the `Refitter` tool.                                                                                                                                                                                      |
| `ChirographKiotaBundleVersion`             | the packed version                  | The version of `Microsoft.Kiota.Bundle` added to a project with a Kiota client.                                                                                                                                          |
| `ChirographRefitVersion`                   | the packed version                  | The version of `Refit.HttpClientFactory` added to a project with a Refitter client.                                                                                                                                      |
| `ChirographMicrosoftExtensionsHttpVersion` | the packed version                  | The version of `Microsoft.Extensions.Http` added to a project with a Kiota client and no Refitter client.                                                                                                                |
| `ChirographVersion`                        | the SDK's version                   | Stamped into the registration's `[GeneratedCode]`. Read it; don't set it.                                                                                                                                                |

The packed versions are the ones Chirograph's own repository pinned when the SDK was packed: they are in the package's
`Sdk/Packages.props`. A package the project references itself is never added, whatever these say: see
[Generators](../../concepts/generators/#the-packages-added-to-the-project).

A tool version that isn't in the packages folder fails the build with [`CHIRO106`](../diagnostics/#chiro106) until the project is
restored.

## Targets

| Target                             | Runs                                                                       | Meaning                                                                                                                                                                                                 |
| ---------------------------------- | -------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `ChirographGenerateOpenApiClients` | after `ResolveProjectReferences`, before `CoreCompile` and `BeforeCompile` | Generates every client that is out of date and adds the generated code to the compilation. A design-time build only adds what an earlier build generated. Hook `AfterTargets` or `BeforeTargets` on it. |

It is the only target that is public. The others start with `_Chirograph` and can change in any release.