---
title: Migrating from LaunchPad's preview
description: The renames from the OpenAPI clients in LaunchPad's MSBuild SDK preview to Chirograph, with the behaviour that changed.
tags: [msbuild]
sidebar:
    order: 6
---

Chirograph is extracted from the OpenAPI clients of LaunchPad's MSBuild SDK, which were never released
([LaunchPad#3238](https://github.com/RocketSurgeonsGuild/LaunchPad/pull/3238)). If you tried that preview, this page is the list of
what to rename. LaunchPad's SDK will chain Chirograph rather than carry its own copy, so a LaunchPad application gets Chirograph
through LaunchPad.

## What stays the same

- The `OpenApiClient` item and all its metadata: `Generator`, `ClientName`, `Namespace`, `DocumentName`, `DocumentCache`,
  `UpdateDocumentCache` and `Options`.
- The three sources, and how they are inferred from `Include`.
- The configuration key, `Clients:<ClientName>:BaseAddress`.
- Where the code goes: `obj/<configuration>/<framework>/openapi/<ClientName>/`.

## Renames

Properties and the target hook lose the `LaunchPad` prefix for `Chirograph`. Diagnostic codes keep their last digits, so the search
and replace is mechanical.

| LaunchPad preview                         | Chirograph                                 |
| ----------------------------------------- | ------------------------------------------ |
| `LaunchPadUpdateOpenApiDocumentCache`     | `ChirographUpdateOpenApiDocumentCache`     |
| `LaunchPadOpenApiClientsDirectory`        | `ChirographOpenApiClientsDirectory`        |
| `LaunchPadKiotaVersion`                   | `ChirographKiotaVersion`                   |
| `LaunchPadRefitterVersion`                | `ChirographRefitterVersion`                |
| `LaunchPadKiotaBundleVersion`             | `ChirographKiotaBundleVersion`             |
| `LaunchPadRefitVersion`                   | `ChirographRefitVersion`                   |
| `LaunchPadMicrosoftExtensionsHttpVersion` | `ChirographMicrosoftExtensionsHttpVersion` |
| `LaunchPadGenerateOpenApiClients` target  | `ChirographGenerateOpenApiClients` target  |
| `LPSDK0100`                               | `CHIRO100`                                 |
| `LPSDK0101`                               | `CHIRO101`                                 |
| `LPSDK0102`                               | `CHIRO102`                                 |
| `LPSDK0103`                               | `CHIRO103`                                 |
| `LPSDK0104`                               | `CHIRO104`                                 |
| `LPSDK0105`                               | `CHIRO105`                                 |
| `LPSDK0106`                               | `CHIRO106`                                 |
| `LPSDK0107`                               | `CHIRO107`                                 |

`CHIRO107` (the generator failed) existed in the preview as `LPSDK0107`, but wasn't documented.

## What changed

- **Registration.** The preview generated a joint for each client, which needed Dovetail. Chirograph generates a public
  `Add<ClientName>()` extension on `IServiceCollection` instead, which works without Dovetail, and with a recent Dovetail declares
  the joint that calls it: see [With Dovetail](../with-dovetail/).
- **`Microsoft.Extensions.Http`** is added for Kiota clients only, and not at all in a project that also has a Refitter client.
  `Refit.HttpClientFactory` brings its own, and the packed version could be older than that one (NU1605, a downgrade). The preview
  added it for both.
- **Telemetry.** The preview set environment variables Kiota doesn't read. Chirograph sets `KIOTA_CLI_TELEMETRY_OPTOUT`,
  `KIOTA_OFFLINE_ENABLED` and `DOTNET_CLI_TELEMETRY_OPTOUT`, so Kiota's telemetry and update check are really off.
- **`GeneratedCode`.** The registration is stamped with Chirograph's version, not a fixed `1.0.0`.
- **A server with several target frameworks** is built as a whole and writes its document from its own outer build, rather than from
  its first target framework, so it works whichever frameworks the client targets.
- **`dotnet clean`** removes the generated code and the stamp that marks it up to date, so the next build generates it again.
