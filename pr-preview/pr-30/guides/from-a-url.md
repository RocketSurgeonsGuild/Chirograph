# From a URL

A URL source downloads the document when the project builds. The last download is kept in a [document cache](../../concepts/document-cache/),
so the build still works when the URL can't be reached.

## Declare the client

```xml
<OpenApiClient Include="https://rockets.example.com/openapi/v1.json" Generator="Kiota" ClientName="RocketsClient" />
```

Any `Include` that starts with `http://` or `https://` is a URL. A URL that ends in `.yaml` or `.yml` is read as YAML, and anything
else as JSON.

Every build downloads the document, and the client is generated again only when the download differs from the cache. When the
download fails:

- with a cached document, the build goes on with it and warns with [`CHIRO104`](../../reference/diagnostics/#chiro104);
- without one, the build fails with [`CHIRO105`](../../reference/diagnostics/#chiro105).

## Commit the cache

By default, the cache is the copy of the document in `obj`, so a fresh clone or a clean CI agent has none. Name a file in the project
with `DocumentCache` and commit it:

```xml
<OpenApiClient
  Include="https://rockets.example.com/openapi/v1.json"
  Generator="Kiota"
  ClientName="RocketsClient"
  DocumentCache="openapi/rockets.json"
/>
```

Now a CI agent without access to the URL builds from the committed document, with `CHIRO104`. When the API changes and the URL is
reachable, the build overwrites the file, and the change shows up in your diff.

## Pin the document

Overwriting the cache on every build means a breaking change of the API reaches your client the next time anyone builds. To decide when
that happens, stop updating the cache:

```xml
<PropertyGroup>
  <ChirographUpdateOpenApiDocumentCache>false</ChirographUpdateOpenApiDocumentCache>
</PropertyGroup>
```

With `false`, a client whose cache exists is generated from it without downloading anything. A client without a cache still downloads
once to fill it. Then, to take the API's changes on purpose:

```sh
dotnet build -p:ChirographUpdateOpenApiDocumentCache=true
```

and review the diff of the cache.

The `UpdateDocumentCache` metadata sets the same thing for one client. It wins over the property, so a client that sets
`UpdateDocumentCache="false"` is not updated by `-p:ChirographUpdateOpenApiDocumentCache=true`: delete its cache file to update it.
Setting the property, as above, keeps the command-line switch working for every client.

## CI without network

Commit the cache, and either:

- leave updating on: the build tries the URL, warns with `CHIRO104`, and uses the cache. Treating warnings as errors fails such a
  build, so add `CHIRO104` to `WarningsNotAsErrors` (or `NoWarn`) when you do that;
- or turn updating off for CI, so the build never tries the URL:

    ```sh
    dotnet build -p:ChirographUpdateOpenApiDocumentCache=false
    ```