# OpenApiClient

An `OpenApiClient` item declares one client: a document, the generator that turns it into code, and the name of what is generated.

```xml
<ItemGroup>
  <OpenApiClient Include="openapi/rockets.yaml" Generator="Refitter" ClientName="RocketsClient" Namespace="MyApp.Rockets" />
</ItemGroup>
```

Declare it in the project file, or in a `Directory.Build.targets`. The SDK reads the items when the project is evaluated (to add
packages, tool downloads and server references) and again when it builds.

## Metadata

Metadata the item doesn't read is ignored without a warning, so a misspelt name falls back to its default.

| Metadata              | Required | Default                                                    | Meaning                                                                                                                                                                                                                                                                                                      |
| --------------------- | -------- | ---------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Include`             | yes      |                                                            | The [source](../../concepts/sources/). A `.csproj`, `.fsproj` or `.vbproj` is a server project. `http://` or `https://` is a URL. Anything else is a file (`.json`, `.yaml`, `.yml`), relative to the project.                                                                                               |
| `Generator`           | yes      |                                                            | `Kiota` or `Refitter` ([`CHIRO100`](../diagnostics/#chiro100)).                                                                                                                                                                                                                                              |
| `ClientName`          | yes      |                                                            | A C# identifier ([`CHIRO101`](../diagnostics/#chiro101)), unique within the project ([`CHIRO108`](../diagnostics/#chiro108)). Kiota's client class is called this, Refitter's interface is `I<ClientName>`, the registration is `Add<ClientName>()` and its configuration section is `Clients:<ClientName>`. |
| `Namespace`           | no       | `$(RootNamespace)`                                         | The namespace of everything generated for the client. Give each client its own when a project has several.                                                                                                                                                                                                   |
| `DocumentName`        | no       | the only document                                          | Server project only: the document to generate from, when the server writes several. `v1` is ASP.NET Core's default ([`CHIRO103`](../diagnostics/#chiro103)).                                                                                                                                                 |
| `DocumentCache`       | no       | the document's copy in `obj`                               | URL only: a file, relative to the project, to keep the last download in. Commit it to build without the URL. See [Document cache](../../concepts/document-cache/).                                                                                                                                           |
| `UpdateDocumentCache` | no       | `$(ChirographUpdateOpenApiDocumentCache)`, which is `true` | URL only: `false` uses the cache whenever it exists instead of downloading. Wins over the property.                                                                                                                                                                                                          |
| `Options`             | no       |                                                            | More arguments for the generator: after `kiota generate …`, or after Refitter's settings file. See [Generators](../../concepts/generators/#what-chirograph-passes).                                                                                                                                          |

`DocumentName` is ignored for a file or URL, and `DocumentCache` and `UpdateDocumentCache` for a file or server project.

## What changes when

- Changing `Generator`, `ClientName`, `Namespace` or `Options` generates the client again on the next build.
- Changing `Include` to another document of the same content changes nothing: the client is generated from the content, not the
  path.
- Removing an item leaves its folder in `obj`, but its code is no longer compiled.