# Sources

A source is where a client's document comes from. You never declare the kind: Chirograph reads it from the item's `Include`.

| `Include`                                      | Source             | The document is                                                                                  |
| ---------------------------------------------- | ------------------ | ------------------------------------------------------------------------------------------------ |
| ends in `.csproj`, `.fsproj` or `.vbproj`      | **server project** | the one the project writes when it builds, through `Microsoft.Extensions.ApiDescription.Server`. |
| starts with `http://` or `https://` (any case) | **URL**            | downloaded on build, into the [document cache](../document-cache/).                              |
| anything else                                  | **file**           | that file, relative to the project.                                                              |

The rules are checked in that order, so a URL that ends in `.csproj` is still a URL.

## The document's copy

Whatever the source, Chirograph copies the document into the client's folder, `obj/<configuration>/<framework>/openapi/<ClientName>/`,
and generates from the copy. The copy is only written when its content differs, which is what makes generation incremental: a server
that writes the same document on every build, or a download that returns what the cache holds, regenerates nothing.

The copy keeps its source's extension, which tells the generators JSON from YAML: `.yaml` and `.yml` for a file or URL that ends in
them, and `.json` otherwise. A server project's document is always JSON.

## Choosing a source

- **A file** when the document is owned elsewhere and you take its changes on purpose. The build never needs the network.
- **A server project** when the API is in the same repository. The client always matches the server you are building.
- **A URL** when the document is published by a running service. Commit its cache to build offline, and turn updating off to take its
  changes on purpose.

The guides cover each: [from a file](../../guides/from-a-file/), [from a server project](../../guides/from-a-server-project/) and
[from a URL](../../guides/from-a-url/).