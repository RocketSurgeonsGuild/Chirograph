---
title: Reference
description: The OpenApiClient item, the properties and target hook, the diagnostic codes and the generated code.
tags: [msbuild]
---

Everything here is public API under semantic versioning: the item and its metadata, the properties, the target hook, the diagnostic
codes and the shape of the generated registration. Names that start with `_Chirograph` are private and can change in any release.

- [`OpenApiClient`](openapiclient/): the item, and every metadata value it reads.
- [Properties](properties/): the properties that change every client of a project, and the target hook.
- [Diagnostics](diagnostics/): each error and warning, what causes it and how to fix it. Each code has its own anchor, which the build
  message links to.
- [Generated code](generated-code/): the files in each client's folder, and the registration.

A test in the repository reads the SDK's targets and fails when these pages list different metadata, properties or codes from the
ones the targets read and raise.
