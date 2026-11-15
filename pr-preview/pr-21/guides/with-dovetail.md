# With Dovetail

[Dovetail](https://rocketsurgeonsguild.github.io/Dovetail/) registers services through joints: classes the host finds at build time
and runs when `ConfigureDovetail()` builds it. Chirograph never references Dovetail. Every client gets a plain
[`Add<ClientName>()` registration](../../concepts/registration/), and a joint is one more caller of it.

## Today: a joint of your own

Call the registration from a service joint. Configure the client further on the builder it returns:

```csharp
[DovetailExport]
public sealed class RocketsClientJoint : IServiceJoint
{
    public void Register(IDovetailContext context, IServiceCollection services) =>
        services.AddRocketsClient().AddStandardResilienceHandler();
}
```

`AddStandardResilienceHandler()` comes from `Microsoft.Extensions.Http.Resilience`, as an example of chaining on the
`IHttpClientBuilder`. A joint in a library is exported to every application that references the library, as any exported joint is,
so a library can generate a client and register it for its consumers.

## Coming: joints Chirograph declares

> [!NOTE]
> This is planned for after 1.0.0-beta.1 ([Chirograph#11](https://github.com/RocketSurgeonsGuild/Chirograph/issues/11)). It needs a
> Dovetail release in which `DovetailTemplate` substitutes every metadata value
> ([Dovetail#413](https://github.com/RocketSurgeonsGuild/Dovetail/issues/413)). Nothing below works yet.

Dovetail has an extension point for packages that ship C#: a [`DovetailTemplate`](https://rocketsurgeonsguild.github.io/Dovetail/reference/dovetail-template/)
item, which Dovetail expands into a file only in projects that run its source generator. Chirograph will declare one template per
client:

```xml
<DovetailTemplate Include="RocketsClient"
                  File="…/templates/OpenApiClientJoint.cs.template"
                  OutputPrefix="Chirograph"
                  ClientName="RocketsClient"
                  Namespace="MyApp.Rockets" />
```

Dovetail then writes an internal `[DovetailExport] RocketsClientJoint : IServiceJoint` that calls `AddRocketsClient(services)`, so the
client resolves from a host built with `ConfigureDovetail()` with no registration call of your own.

- The templates are declared only when the Dovetail in the build can expand them, so an older Dovetail gets nothing and the build
  still succeeds.
- `ChirographGenerateDovetailJoint=false` will turn them off, for a project that keeps its own joint.

Once it ships, delete a joint like the one above that does nothing but call `Add<ClientName>()`, or turn the generated ones off.