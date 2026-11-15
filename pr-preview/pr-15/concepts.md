# Concepts

A **client** is one `OpenApiClient` item and everything generated for it. It has one **document**, an OpenAPI description, which comes
from a **source**: a file, a server project or a URL. A **generator**, Kiota or Refitter, turns the document into code, and a
**registration**, `Add<ClientName>()`, puts the client into `IHttpClientFactory` with its **base address** read from configuration.

- [Sources](sources/): how the kind of source is inferred from `Include`.
- [Generators](generators/): Kiota and Refitter, what each writes, and the packages each adds.
- [Registration](registration/): the extension method, the configuration key and chaining on `IHttpClientBuilder`.
- [Document cache](document-cache/): what happens to a URL's document between builds.
- [How generation runs](how-generation-runs/): tools, `obj`, incrementality, design-time builds and telemetry.
- [Compared with](compared-with/): where Chirograph sits among the other ways to get a client.

The [glossary](https://github.com/RocketSurgeonsGuild/Chirograph/blob/main/GLOSSARY.md) defines each of these words once.