---
title: With Dovetail
description: Chirograph declares a Dovetail joint for every client through DovetailTemplate, so a host built with ConfigureDovetail() resolves it with no registration call.
tags: [dovetail]
sidebar:
    order: 4
---

[Dovetail](https://rocketsurgeonsguild.github.io/Dovetail/) registers services through joints: classes the host finds at build time
and runs when `ConfigureDovetail()` builds it. Chirograph never references Dovetail. Every client gets a plain
[`Add<ClientName>()` registration](../../concepts/registration/), and a joint is one more caller of it.

## The joint Chirograph declares

> [!NOTE]
> This needs **Dovetail TBD (Dovetail#426)** or later: the first release in which `DovetailTemplate` replaces every metadata value
> as a token ([Dovetail#413](https://github.com/RocketSurgeonsGuild/Dovetail/issues/413)). With an older Dovetail, Chirograph declares
> nothing, the build still succeeds, and you register the client [yourself](#a-joint-of-your-own).

Dovetail has an extension point for packages that ship C#: a [`DovetailTemplate`](https://rocketsurgeonsguild.github.io/Dovetail/reference/dovetail-template/)
item, which Dovetail expands into a file only in projects that run its source generator. Chirograph declares one per client. For the
client `RocketsClient` in `MyApp.Rockets` it is:

```xml
<DovetailTemplate Include="Chirograph.RocketsClient"
                  File="…/Sdk/templates/OpenApiClientJoint.cs.template"
                  ClientName="RocketsClient"
                  Namespace="MyApp.Rockets"
                  ChirographVersion="…" />
```

Dovetail writes it as `obj/…/DovetailTemplate.Chirograph.RocketsClient.g.cs`, with each `__Name__` token replaced by the metadata of the
same name:

```csharp
namespace MyApp.Rockets;

[DovetailExport]
internal sealed class RocketsClientJoint : IServiceJoint
{
    public void Register(IDovetailContext context, IServiceCollection services) =>
        global::MyApp.Rockets.RocketsClientServiceCollectionExtensions.AddRocketsClient(services);
}
```

So the client resolves from a host built with `ConfigureDovetail()`, with no registration call of your own:

```csharp
using var host = await Host.CreateApplicationBuilder(args).ConfigureDovetail();
var client = host.Services.GetRequiredService<RocketsClient>();
```

- The items are declared only when the Dovetail in the build sets `DovetailTemplateMetadataTokens`, the capability property that
  release added. An older Dovetail never gets a template it can't expand.
- The `Include` is prefixed with `Chirograph.`, so it can't clash with another package's template for a type of the same name.
- Dovetail writes the joint only into a project that runs its source generator. A library that generates a client and references
  Dovetail exports the joint to every application that references it, as any exported joint is.
- `ChirographGenerateDovetailJoint=false` turns the joints off for the project. See [Properties](../../reference/properties/).

## A joint of your own

Turn the generated joints off and write your own when you want to configure the client further, on the builder the registration
returns, or when your Dovetail is older than the release above:

```csharp
[DovetailExport]
public sealed class RocketsClientSetup : IServiceJoint
{
    public void Register(IDovetailContext context, IServiceCollection services) =>
        services.AddRocketsClient().AddStandardResilienceHandler();
}
```

`AddStandardResilienceHandler()` comes from `Microsoft.Extensions.Http.Resilience`, as an example of chaining on the
`IHttpClientBuilder`. Don't name your joint `<ClientName>Joint` in the client's namespace while the generated joints are on: that is
the name of the generated one.
