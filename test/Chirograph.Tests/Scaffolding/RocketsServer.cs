using Microsoft.Build.Utilities.ProjectCreation;

namespace Chirograph.Tests.Scaffolding;

/// <summary>
/// The throwaway server of the OpenAPI client tests: an ASP.NET Core application that serves the API <see cref="RocketsApi" />
/// describes, registers its documents with <c>AddOpenApi()</c> itself (no Dovetail), and writes them when it builds with
/// Microsoft.Extensions.ApiDescription.Server.
/// </summary>
internal static class RocketsServer
{
    /// <summary>The include of the server, relative to the application.</summary>
    public const string Include = "../server/server.csproj";

    /// <summary>The include of the F# server (<see cref="AddRocketsFSharpServer" />), relative to the application.</summary>
    public const string FSharpInclude = "../fsserver/fsserver.fsproj";

    /// <summary>
    /// Adds <c>fsserver/fsserver.fsproj</c>, the same server in F#, on <paramref name="targetFramework" />, with ASP.NET Core's default
    /// document.
    /// </summary>
    public static SdkTestProject AddRocketsFSharpServer(this SdkTestProject project, string targetFramework)
    {
        var server = ProjectCreator.Templates.SdkCsproj(sdk: "Microsoft.NET.Sdk.Web", targetFramework: targetFramework, outputType: "Exe")
                                   .ItemCompile("Program.fs")
                                   .ItemPackageReference("Microsoft.AspNetCore.OpenApi", Config.CentralPackageVersion("Microsoft.AspNetCore.OpenApi", targetFramework))
                                   .ItemPackageReference(
                                        "Microsoft.Extensions.ApiDescription.Server",
                                        Config.CentralPackageVersion("Microsoft.Extensions.ApiDescription.Server", targetFramework),
                                        privateAssets: "all"
                                    );
        return project
              .AddProject("fsserver/fsserver.fsproj", server)
              .AddFile(
                   "fsserver/Program.fs",
                   """
                   open System
                   open Microsoft.AspNetCore.Builder
                   open Microsoft.Extensions.DependencyInjection

                   type Rocket = { Id: Guid; SerialNumber: string }

                   [<EntryPoint>]
                   let main args =
                       let builder = WebApplication.CreateBuilder(args)
                       builder.Services.AddOpenApi() |> ignore
                       let app = builder.Build()
                       app.MapGet("/rockets/{id}", Func<Guid, Rocket>(fun id -> { Id = id; SerialNumber = "FALCON-9" })).WithName("GetRocket") |> ignore
                       app.MapOpenApi() |> ignore
                       app.Run()
                       0
                   """
               );
    }

    /// <summary>
    /// Adds <c>server/server.csproj</c> on <paramref name="targetFrameworks" /> (several make it multi-targeted), with one OpenAPI
    /// document per name in <paramref name="documents" /> (ASP.NET Core's default, <c>v1</c>, when there is none).
    /// </summary>
    public static SdkTestProject AddRocketsServer(this SdkTestProject project, string[] targetFrameworks, params string[] documents)
    {
        var server = ProjectCreator.Templates.SdkCsproj(sdk: "Microsoft.NET.Sdk.Web", targetFramework: null, outputType: "Exe");
        server.Property(targetFrameworks.Length == 1 ? "TargetFramework" : "TargetFrameworks", string.Join(';', targetFrameworks));
        foreach (var targetFramework in targetFrameworks)
        {
            var condition = targetFrameworks.Length == 1 ? null : $"'$(TargetFramework)' == '{targetFramework}'";
            server
               .ItemPackageReference("Microsoft.AspNetCore.OpenApi", Config.CentralPackageVersion("Microsoft.AspNetCore.OpenApi", targetFramework), condition: condition)
               .ItemPackageReference(
                    "Microsoft.Extensions.ApiDescription.Server",
                    Config.CentralPackageVersion("Microsoft.Extensions.ApiDescription.Server", targetFramework),
                    privateAssets: "all",
                    condition: condition
                );
        }

        var addOpenApi = documents.Length == 0
            ? "builder.Services.AddOpenApi();"
            : string.Join("\n", documents.Select(z => $"builder.Services.AddOpenApi(\"{z}\");"));
        return project
              .AddProject("server/server.csproj", server)
              .AddFile(
                   "server/Program.cs",
                   $$"""
                   using System;
                   using Microsoft.AspNetCore.Builder;
                   using Microsoft.Extensions.DependencyInjection;

                   var builder = WebApplication.CreateBuilder(args);
                   {{addOpenApi}}
                   var app = builder.Build();
                   app.MapGet("/rockets/{id}", (Guid id) => new Throwaway.Server.Rocket(id, "FALCON-9")).WithName("GetRocket");
                   app.MapOpenApi();
                   app.Run();

                   namespace Throwaway.Server
                   {
                       public sealed record Rocket(Guid Id, string SerialNumber);
                   }
                   """
               );
    }
}
