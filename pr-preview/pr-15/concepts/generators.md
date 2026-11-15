# Generators

A generator is the tool that turns a document into client code. Chirograph runs one of two, chosen per client with `Generator`. Neither
is a Roslyn source generator: each is a .NET tool that runs as a process during the build. See
[How generation runs](../how-generation-runs/).

|                        | Kiota                                                            | Refitter                                                                                     |
| ---------------------- | ---------------------------------------------------------------- | -------------------------------------------------------------------------------------------- |
| What it writes         | A client class, `<ClientName>`, with request builders and models | A [Refit](https://github.com/reactiveui/refit) interface, `I<ClientName>`, and its contracts |
| A call looks like      | `await client.Rockets[id].GetAsync()`                            | `await client.GetRocket(id)`                                                                 |
| Package added          | `Microsoft.Kiota.Bundle`, and `Microsoft.Extensions.Http`        | `Refit.HttpClientFactory`                                                                    |
| Without a base address | Uses the first server of the document                            | Needs one                                                                                    |
| Tool package           | `Microsoft.OpenApi.Kiota`                                        | `Refitter`                                                                                   |

Choose Kiota for a large API, or one whose shape changes often: its request builders follow the paths. Choose Refitter for a smaller
API where one method per operation reads better, or when you already use Refit.

## The packages added to the project

The generated code needs a runtime library, which Chirograph adds as an implicit `PackageReference` when at least one client uses the
generator:

- **Kiota**: `Microsoft.Kiota.Bundle`, and `Microsoft.Extensions.Http` for the registration. A project that also has a Refitter client
  doesn't get `Microsoft.Extensions.Http`: `Refit.HttpClientFactory` brings it, possibly at a newer version than the one Chirograph
  was packed with, and adding the older one would be a downgrade (NU1605).
- **Refitter**: `Refit.HttpClientFactory`, which brings Refit, its source generator and `Microsoft.Extensions.Http`.

A package the project references itself wins: its own `PackageReference` and its version are left alone, and Chirograph adds nothing.
So reference a package yourself to choose its version, or set the matching [property](../../reference/properties/).

## What Chirograph passes

Kiota runs as:

```sh
kiota generate --language CSharp --openapi <document> --class-name <ClientName> --namespace-name <Namespace> \
  --output <client folder>/generated --clean-output --exclude-backward-compatible --log-level Warning <Options>
```

Refitter runs with a settings file Chirograph writes, `<ClientName>.refitter`, which sets the document, the namespace, the interface
name (`useOpenApiTitle` off) and cancellation tokens on every method:

```sh
refitter --settings-file <client folder>/<ClientName>.refitter --output <client folder>/generated/<ClientName>.cs \
  --no-logging --no-banner --simple-output <Options>
```

`Options` goes last, so you can add to what Chirograph passes. Don't change the output, the class or interface name or the namespace
there: the registration expects them.

## Versions

Each Chirograph release is packed with a version of each tool and of each package it adds. Set `ChirographKiotaVersion` or
`ChirographRefitterVersion` to run another version of a tool; the [properties](../../reference/properties/) list them all. A different
tool version generates every client of that generator again.