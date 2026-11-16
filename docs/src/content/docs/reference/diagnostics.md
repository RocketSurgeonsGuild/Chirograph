---
title: Diagnostics
description: Every error and warning Chirograph raises, CHIRO100 to CHIRO109, with the message, the cause and the fix.
tags: [msbuild]
sidebar:
    order: 3
---

Each diagnostic links here from the build output: an IDE shows the code as a link to its heading below. A release of Chirograph links
to this page in the docs of that release, and a prerelease to the latest docs. Codes keep their last digits
from LaunchPad's preview (`LPSDK0100` is `CHIRO100`); see [Migrating from LaunchPad's preview](../../guides/migrating-from-launchpad/).

## Codes

| Code                    | Severity | Meaning                                                                     |
| ----------------------- | -------- | --------------------------------------------------------------------------- |
| [`CHIRO100`](#chiro100) | error    | `Generator` is missing or is not `Kiota` or `Refitter`.                     |
| [`CHIRO101`](#chiro101) | error    | `ClientName` is missing or is not a C# identifier.                          |
| [`CHIRO102`](#chiro102) | error    | The document file does not exist.                                           |
| [`CHIRO103`](#chiro103) | error    | The server project wrote several documents, or none matched `DocumentName`. |
| [`CHIRO104`](#chiro104) | warning  | The URL could not be reached. The cached document is used.                  |
| [`CHIRO105`](#chiro105) | error    | The URL could not be reached, and there is no cached document.              |
| [`CHIRO106`](#chiro106) | error    | The generator tool is not in the packages folders. Restore the project.     |
| [`CHIRO107`](#chiro107) | error    | The generator exited with an error. Its output is included.                 |
| [`CHIRO108`](#chiro108) | error    | Two clients have the same `ClientName`.                                     |
| [`CHIRO109`](#chiro109) | error    | `Authentication` lists a value Chirograph doesn't know.                     |

## CHIRO100

> The OpenApiClient 'openapi/rockets.json' has Generator 'NSwag': set it to Kiota or Refitter.

The item's `Generator` is empty, or names a generator Chirograph doesn't run. The comparison is case-sensitive. Set it to `Kiota` or
`Refitter`; see [Generators](../../concepts/generators/) to choose.

## CHIRO101

> The OpenApiClient 'openapi/rockets.json' has ClientName 'Rockets.Client': set it to the name of the client, a C# identifier.

`ClientName` is empty, or is not a C# identifier: letters, digits and underscores, not starting with a digit. It names a class, an
interface, a method and a configuration key, so it can't contain dots or dashes. Put the namespace in `Namespace` instead.

## CHIRO102

> The OpenAPI document 'openapi/rockets.json' of the OpenApiClient 'RocketsClient' does not exist.

A [file source](../../guides/from-a-file/) points at a file that isn't there. The path is relative to the project, not to the solution
or the current folder. An `Include` meant as a URL has to start with `http://` or `https://`.

## CHIRO103

> The project '../Rockets.Api/Rockets.Api.csproj' of the OpenApiClient 'RocketsClient' wrote the documents '…/Rockets.Api.json;…/Rockets.Api_v2.json': set DocumentName to the one the client is generated from.

A [server project](../../guides/from-a-server-project/) wrote more than one document and the client has no `DocumentName`, or none of
the documents matches `DocumentName`. The message lists what the server wrote. `<Project>.json` is `v1`, and `<Project>_<name>.json` is
`<name>`. When the list is empty, the server wrote no document: check that it references `Microsoft.Extensions.ApiDescription.Server`
and calls `AddOpenApi()`.

## CHIRO104

> The OpenAPI document of the OpenApiClient 'RocketsClient' could not be downloaded from 'https://rockets.example.com/openapi/v1.json': the client is generated from the cached copy, '…/openapi/rockets.json'.

A warning. The [URL](../../guides/from-a-url/) couldn't be downloaded, so the client is generated from the
[document cache](../../concepts/document-cache/). The build succeeds with the last document it saw. Fix the URL or the network, or turn
updating off so the build doesn't try (`ChirographUpdateOpenApiDocumentCache=false`). A build that treats warnings as errors fails on it
unless `CHIRO104` is in `WarningsNotAsErrors`.

## CHIRO105

> The OpenAPI document of the OpenApiClient 'RocketsClient' could not be downloaded from 'https://rockets.example.com/openapi/v1.json', and there is no cached copy.

The URL couldn't be downloaded, and there is nothing to fall back on: a fresh clone or a clean agent with a cache in `obj`. Build once
where the URL can be reached, or set `DocumentCache` to a file in the project and commit it.

## CHIRO106

> The Kiota tool the OpenApiClient 'RocketsClient' is generated with was not found in the packages folders (…): restore the project.

The generator's tool package isn't in any of the packages folders restore uses. Restore the project (`dotnet restore`, or a build
without `--no-restore`). After changing `ChirographKiotaVersion` or `ChirographRefitterVersion`, the new version has to be restored
too, and has to exist on your package sources.

## CHIRO107

> Kiota could not generate the OpenAPI client RocketsClient (exit code 1):
> …

The generator ran and failed. The message ends with everything it wrote. The usual causes are a document that isn't valid OpenAPI, or
an `Options` value the generator doesn't accept. Run the generator by hand on the client's copy of the document, in
`obj/<configuration>/<framework>/openapi/<ClientName>/`, to see the problem outside the build.

## CHIRO108

> The OpenApiClient items 'openapi/rockets.json, openapi/rockets-v2.json' have the same ClientName 'RocketsClient': give each client a name of its own.

Two `OpenApiClient` items in one project have the same `ClientName`. They would be generated into the same folder, with the same
types and the same `Add<ClientName>()`, so the build stops before it generates either. Give each client its own `ClientName`; two
clients can still be generated from the same document.

## CHIRO109

> The OpenApiClient 'RocketsClient' has Authentication values Chirograph doesn't know (Bearer, Kerberos): set it to a ;-separated list of ClientCredentials, Azure, or leave it out.

The item's `Authentication` metadata lists the methods that need a package, which the item must declare: `ClientCredentials` (which
allows `PrivateKeyJwt` too) and `Azure` (the four Azure methods). The
comparison ignores case. `None`, `ApiKey`, `Bearer` and `Basic` need no package and are always available, so they don't go in the
metadata: configuration chooses among them with `Clients:<ClientName>:Authentication`. Remove the unknown values; see
[Authentication](../openapiclient/#metadata).
