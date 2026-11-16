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
