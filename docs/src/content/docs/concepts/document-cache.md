---
title: Document cache
description: How a URL source keeps its last download, falls back to it when the URL can't be reached, and how committing it and UpdateDocumentCache decide when an API change reaches the client.
tags: [msbuild]
sidebar:
    order: 4
---

The document cache is the last document a [URL source](../sources/) downloaded. Only URL sources have one: a file is its own document,
and a server project writes its document on every build.

## Where it lives

By default the cache is the client's copy of the document in `obj`, `obj/<configuration>/<framework>/openapi/<ClientName>/openapi.json`.
It survives builds, but not a clean clone.

`DocumentCache` names a file in the project instead, relative to the project. That file can be committed, so every clone and every CI
agent starts with a cache.

## What a build does

For each URL client, a build:

1. Decides whether to download. It downloads unless updating is off (below) and the cache exists.
2. Downloads the document into the client's `download/` folder, retrying twice.
3. If the download succeeded and differs from the cache, **updates** the cache: overwrites it with the download.
4. If the download failed, uses the cache, with warning [`CHIRO104`](../../reference/diagnostics/#chiro104). With no cache, the
   build fails with [`CHIRO105`](../../reference/diagnostics/#chiro105).
5. Generates the client from the cache, through the same copy-when-changed step as every source.

So a download that returns what the cache already holds changes no file and regenerates nothing.

## Updating

Updating is on by default: every build that reaches the URL takes the API's latest document. That suits a cache in `obj`, or a
committed cache whose diffs you review.

Turn it off to decide when a change reaches the client. With updating off and a cache present, the build doesn't download at all:

- for every client, with the property `ChirographUpdateOpenApiDocumentCache` set to `false`;
- for one client, with the metadata `UpdateDocumentCache="false"`, which wins over the property.

To take the changes later, build once with `-p:ChirographUpdateOpenApiDocumentCache=true` (which works for clients that don't set the
metadata), or delete the cache file. A client with updating off and no cache still downloads once, to fill it.

[From a URL](../../guides/from-a-url/) walks through committing the cache and building in CI without network.
