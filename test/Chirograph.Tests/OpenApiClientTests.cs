using Chirograph.Tests.Scaffolding;

namespace Chirograph.Tests;

/// <summary>
/// An <c>OpenApiClient</c> item generates an HTTP client from an OpenAPI document with Kiota or Refitter, and a public
/// <c>Add&lt;ClientName&gt;()</c> extension that registers it with <c>IHttpClientFactory</c>, its base address read from
/// <c>Clients:&lt;ClientName&gt;:BaseAddress</c>. Each throwaway application calls <see cref="RocketsApi" />, which the test serves on
/// the loopback interface.
/// </summary>
[TargetFrameworks]
public class OpenApiClientTests(string targetFramework) : LoggerTest<TestRecord>(TestRecord.Create())
{
    /// <summary>
    /// Build seam: each generator generates a client from each kind of source, and the application registers it with
    /// <c>AddRocketsClient()</c> and calls the API at the configured base address.
    /// </summary>
    [Test]
    [Arguments(Generator.Kiota, Source.File)]
    [Arguments(Generator.Kiota, Source.Project)]
    [Arguments(Generator.Kiota, Source.Url)]
    [Arguments(Generator.Refitter, Source.File)]
    [Arguments(Generator.Refitter, Source.Project)]
    [Arguments(Generator.Refitter, Source.Url)]
    public async Task Should_Generate_A_Registered_Client(Generator generator, Source source)
    {
        await using var api = RocketsApi.Start();
        using var project = new SdkTestProject(targetFramework);
        switch (source)
        {
            case Source.File:
                project.AddRocketsApp(targetFramework, generator);
                break;
            case Source.Project:
                project.AddRocketsServer([targetFramework]).AddRocketsApp(targetFramework, generator, RocketsServer.Include);
                break;
            case Source.Url:
                project.AddRocketsApp(targetFramework, generator, api.DocumentAddress.ToString());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(source), source, null);
        }

        await project.Build("app");
        var output = await project.Run("app", RocketsApp.Environment(api));

        await project.VerifyResult(output, $"{generator}_{source}");
    }

    /// <summary>
    /// Build seam: a server with several target frameworks writes its document from its outer build, once, and the client is generated
    /// from it whatever the target framework of the application.
    /// </summary>
    [Test]
    public async Task Should_Generate_A_Client_From_A_Multi_Targeted_Server()
    {
        await using var api = RocketsApi.Start();
        using var project = new SdkTestProject(targetFramework);
        project.AddRocketsServer(["net10.0", "net11.0"]).AddRocketsApp(targetFramework, Generator.Kiota, RocketsServer.Include);

        await project.Build("app");
        var output = await project.Run("app", RocketsApp.Environment(api));

        await project.VerifyResult(output);
    }

    /// <summary>
    /// Build seam: <c>DocumentName</c> picks the document a client is generated from when the server writes several: ASP.NET Core's
    /// default, <c>v1</c>, or another by its name. The client's copy of the document shows which one (its title names it).
    /// </summary>
    [Test]
    [Arguments("v1")]
    [Arguments("v2")]
    public async Task Should_Generate_A_Client_From_The_Named_Document(string documentName)
    {
        await using var api = RocketsApi.Start();
        using var project = new SdkTestProject(targetFramework);
        project
           .AddRocketsServer([targetFramework], "v1", "v2")
           .AddRocketsApp(targetFramework, Generator.Kiota, RocketsServer.Include, new Dictionary<string, string?> { ["DocumentName"] = documentName });

        await project.Build("app");
        var output = await project.Run("app", RocketsApp.Environment(api));
        // the title only: the rest of the document differs between ASP.NET Core releases (its OpenAPI version)
        var document = System.Text.Json.Nodes.JsonNode.Parse(
            await File.ReadAllTextAsync(Path.Combine(project.Directory, "app", "obj", "Debug", targetFramework, "openapi", "RocketsClient", "openapi.json"))
        )!;

        await project.VerifyResult(new { Output = output, Document = document["info"]!["title"]!.GetValue<string>() }, documentName);
    }

    /// <summary>
    /// Build seam: a library generates the client, and an application that doesn't use Chirograph registers it through the library's
    /// public <c>AddRocketsClient()</c>. The library has only the packages Chirograph adds, so it also proves they are enough to compile
    /// the registration.
    /// </summary>
    [Test]
    [Arguments(Generator.Kiota)]
    [Arguments(Generator.Refitter)]
    public async Task Should_Register_A_Client_From_A_Library(Generator generator)
    {
        await using var api = RocketsApi.Start();
        using var project = new SdkTestProject(targetFramework);
        project
           .AddProject("lib/lib.csproj", project.CreateConsumer("Library").OpenApiClient(generator, "rockets.json"))
           .AddFile("lib/rockets.json", RocketsApi.Document)
           .AddProject("app/app.csproj", project.CreateProject().Hosting(targetFramework).ItemProjectReference("../lib/lib.csproj"))
           .AddFile("app/Program.cs", RocketsApp.Program(generator));

        await project.Build("app");
        var output = await project.Run("app", RocketsApp.Environment(api));

        await project.VerifyResult(output, generator.ToString());
    }

    /// <summary>
    /// Build seam: the registration Chirograph writes for each generator, a public <c>Add&lt;ClientName&gt;()</c> that returns the
    /// <c>IHttpClientBuilder</c>, stamped with the version of Chirograph.
    /// </summary>
    [Test]
    [Arguments(Generator.Kiota)]
    [Arguments(Generator.Refitter)]
    public async Task Should_Write_The_Registration(Generator generator)
    {
        using var project = new SdkTestProject(targetFramework);
        project
           .AddProject("lib/lib.csproj", project.CreateConsumer("Library").OpenApiClient(generator, "rockets.json"))
           .AddFile("lib/rockets.json", RocketsApi.Document);

        await project.Build("lib");
        var registration = await File.ReadAllTextAsync(
            Path.Combine(project.Directory, "lib", "obj", "Debug", targetFramework, "openapi", "RocketsClient", "RocketsClientServiceCollectionExtensions.g.cs")
        );

        await project.VerifyResult(registration, generator.ToString());
    }

    /// <summary>
    /// Build seam: the generators run with their telemetry and update checks off. The variables are the ones Kiota reads
    /// (<c>KIOTA_CLI_TELEMETRY_OPTOUT</c>, and <c>KIOTA_OFFLINE_ENABLED</c> for the update check) and the .NET CLI's.
    /// </summary>
    [Test]
    public async Task Should_Run_The_Generators_Without_Telemetry()
    {
        using var project = new SdkTestProject(targetFramework);
        project
           .AddProject(
                "app/app.csproj",
                project
                   .CreateConsumer("Library")
                   .OpenApiClient(Generator.Kiota, "rockets.json", "KiotaClient")
                   .OpenApiClient(Generator.Refitter, "rockets.json", "RefitterClient")
            )
           .AddFile("app/rockets.json", RocketsApi.Document);

        var build = await project.Build("app");

        await project.VerifyResult(
            build.TaskParameter("Exec", "EnvironmentVariables", z => z.ParameterValues("Command").Any(c => c.Contains("exec --roll-forward", StringComparison.Ordinal)))
        );
    }

    /// <summary>
    /// Evaluation seam: each generator a project's clients use is downloaded (<c>PackageDownload</c>), and the packages its code needs are
    /// referenced: <c>Microsoft.Kiota.Bundle</c> and <c>Microsoft.Extensions.Http</c> for Kiota, <c>Refit.HttpClientFactory</c> for
    /// Refitter.
    /// </summary>
    [Test]
    [Arguments("Kiota")]
    [Arguments("Refitter")]
    [Arguments("Kiota,Refitter")]
    public async Task Should_Add_The_Generators_The_Clients_Use(string generators)
    {
        using var project = new SdkTestProject(targetFramework);
        var consumer = project.CreateConsumer();
        foreach (var generator in generators.Split(','))
        {
            consumer.OpenApiClient(Enum.Parse<Generator>(generator), "rockets.json", $"{generator}Client");
        }

        project.AddProject("app/app.csproj", consumer);

        await project.VerifyEvaluation("app/app.csproj", generators.Replace(',', '_'));
    }

    /// <summary>
    /// Evaluation seam: the source of a client is inferred from its <c>Include</c>. A project (<c>.csproj</c>, <c>.fsproj</c>,
    /// <c>.vbproj</c>) becomes a reference that is built first but not referenced by the assembly; a file (JSON or YAML) or a URL adds no
    /// reference, even a URL that ends in <c>.csproj</c>.
    /// </summary>
    [Test]
    public async Task Should_Infer_The_Source_From_The_Include()
    {
        using var project = new SdkTestProject(targetFramework);
        project.AddProject(
            "app/app.csproj",
            project
               .CreateConsumer()
               .OpenApiClient(Generator.Kiota, "rockets.json", "JsonClient")
               .OpenApiClient(Generator.Kiota, "openapi/rockets.yaml", "YamlClient")
               .OpenApiClient(Generator.Kiota, "../server/server.csproj", "ServerClient")
               .OpenApiClient(Generator.Kiota, "https://example.com/openapi.json", "UrlClient")
               .OpenApiClient(Generator.Kiota, "../fsserver/fsserver.fsproj", "FSharpServerClient")
               .OpenApiClient(Generator.Kiota, "../vbserver/vbserver.vbproj", "VisualBasicServerClient")
               // a URL first: one that ends in a project's extension is still a URL
               .OpenApiClient(Generator.Kiota, "https://example.com/server.csproj", "UrlProjectClient")
        );

        await project.VerifyEvaluation("app/app.csproj");
    }

    /// <summary>
    /// Evaluation seam: what Chirograph knows of each client before it builds, its source, its namespace (<c>$(RootNamespace)</c> when
    /// not set) and whether its <c>ClientName</c> is a C# identifier, so a file imported after Chirograph's can declare items from the
    /// clients at evaluation (ticket #11's Dovetail joints). A build raises the errors.
    /// </summary>
    [Test]
    public async Task Should_Know_Each_Client_At_Evaluation()
    {
        using var project = new SdkTestProject(targetFramework);
        project.AddProject(
            "app/app.csproj",
            project
               .CreateConsumer()
               .OpenApiClient(Generator.Kiota, "rockets.json", "NamedClient")
               .ItemInclude("OpenApiClient", "https://example.com/openapi.json", metadata: new Dictionary<string, string?> { ["Generator"] = "Refitter", ["ClientName"] = "DefaultClient" })
               .ItemInclude("OpenApiClient", "../server/server.csproj", metadata: new Dictionary<string, string?> { ["Generator"] = "Kiota", ["ClientName"] = "Server.Client" })
               // the scheme of a URL in any case
               .OpenApiClient(Generator.Kiota, "HTTPS://EXAMPLE.COM/OPENAPI.JSON", "UpperCaseUrlClient")
        );

        await project.VerifyResult(project.EvaluatedItems("app/app.csproj", "_ChirographOpenApiClient"));
    }

    /// <summary>
    /// Evaluation seam: a package Chirograph adds for a client is left alone when the project references it itself: the project's own
    /// reference and version win.
    /// </summary>
    [Test]
    public async Task Should_Leave_A_Package_The_Project_References_Alone()
    {
        using var project = new SdkTestProject(targetFramework);
        project.AddProject(
            "app/app.csproj",
            project
               .CreateConsumer()
               .ItemPackageReference("Microsoft.Kiota.Bundle", version: "2.0.0")
               .OpenApiClient(Generator.Kiota, "rockets.json")
        );

        await project.VerifyEvaluation("app/app.csproj");
    }

    /// <summary>
    /// Build seam: each generator generates a client from a YAML document, <c>.yaml</c> or <c>.yml</c>. The client's copy of the document
    /// keeps the extension, which tells the generator JSON from YAML.
    /// </summary>
    [Test]
    [Arguments(Generator.Kiota, ".yaml")]
    [Arguments(Generator.Kiota, ".yml")]
    [Arguments(Generator.Refitter, ".yaml")]
    [Arguments(Generator.Refitter, ".yml")]
    public async Task Should_Generate_A_Client_From_A_Yaml_Document(Generator generator, string extension)
    {
        await using var api = RocketsApi.Start();
        using var project = new SdkTestProject(targetFramework);
        project.AddRocketsApp(targetFramework, generator, $"rockets{extension}").AddFile($"app/rockets{extension}", RocketsApi.YamlDocument);

        await project.Build("app");
        var output = await project.Run("app", RocketsApp.Environment(api));
        var client = Path.Combine(project.Directory, "app", "obj", "Debug", targetFramework, "openapi", "RocketsClient");

        await project.VerifyResult(
            new { Output = output, Document = Directory.EnumerateFiles(client, "openapi.*").Select(Path.GetFileName).ToArray() },
            $"{generator}_{extension.TrimStart('.')}"
        );
    }

    /// <summary>
    /// Build seam: <c>Options</c> goes to Kiota's command line, here <c>--backing-store</c>, which makes every model an
    /// <c>IBackedModel</c>. Without it, the model is not.
    /// </summary>
    [Test]
    [Arguments("")]
    [Arguments("--backing-store")]
    public async Task Should_Pass_The_Options_To_Kiota(string options)
    {
        using var project = new SdkTestProject(targetFramework);
        project
           .AddProject(
                "lib/lib.csproj",
                project
                   .CreateConsumer("Library")
                   .OpenApiClient(Generator.Kiota, "rockets.json", metadata: new Dictionary<string, string?> { ["Options"] = options == "" ? null : options })
            )
           .AddFile("lib/rockets.json", RocketsApi.Document);

        await project.Build("lib");
        var model = await File.ReadAllLinesAsync(
            Path.Combine(project.Directory, "lib", "obj", "Debug", targetFramework, "openapi", "RocketsClient", "generated", "Models", "Rocket.cs")
        );

        // the declaration of the model, which names what it implements
        await project.VerifyResult(model.Single(z => z.Contains("partial class Rocket", StringComparison.Ordinal)).Trim(), options == "" ? "None" : "BackingStore");
    }

    /// <summary>
    /// Build seam: a client without <c>Namespace</c> is generated, and registered, in <c>$(RootNamespace)</c>.
    /// </summary>
    [Test]
    [Arguments(Generator.Kiota)]
    [Arguments(Generator.Refitter)]
    public async Task Should_Generate_A_Client_In_The_Root_Namespace_By_Default(Generator generator)
    {
        await using var api = RocketsApi.Start();
        using var project = new SdkTestProject(targetFramework);
        project
           .AddProject(
                "app/app.csproj",
                project
                   .CreateConsumer()
                   .Property("RootNamespace", "Throwaway.Root")
                   .Hosting(targetFramework)
                   .OpenApiClient(generator, "rockets.json", metadata: new Dictionary<string, string?> { ["Namespace"] = null })
            )
           .AddFile("app/rockets.json", RocketsApi.Document)
           .AddFile("app/Program.cs", RocketsApp.Program(generator, "Throwaway.Root"));

        await project.Build("app");
        var output = await project.Run("app", RocketsApp.Environment(api));

        await project.VerifyResult(output, generator.ToString());
    }

    /// <summary>
    /// Build seam: one project with a Kiota and a Refitter client, generated from the same document into namespaces of their own, each
    /// registered and called at its own base address. It restores without a package downgrade (NU1605, an error) on every target
    /// framework: <c>Microsoft.Extensions.Http</c> comes with <c>Refit.HttpClientFactory</c>, not at the version Chirograph packs.
    /// </summary>
    [Test]
    public async Task Should_Generate_Several_Clients_In_One_Project()
    {
        await using var api = RocketsApi.Start();
        using var project = new SdkTestProject(targetFramework);
        project
           .AddProject(
                "app/app.csproj",
                project
                   .CreateConsumer()
                   .Hosting(targetFramework)
                   .OpenApiClient(Generator.Kiota, "rockets.json", "KiotaRocketsClient", new Dictionary<string, string?> { ["Namespace"] = "Throwaway.Kiota" })
                   .OpenApiClient(Generator.Refitter, "rockets.json", "RefitterRocketsClient", new Dictionary<string, string?> { ["Namespace"] = "Throwaway.Refitter" })
            )
           .AddFile("app/rockets.json", RocketsApi.Document)
           .AddFile(
                "app/Program.cs",
                $$"""
                using System;
                using Microsoft.Extensions.DependencyInjection;
                using Microsoft.Extensions.Hosting;
                using Microsoft.Extensions.Logging;
                using Throwaway.Kiota;
                using Throwaway.Refitter;

                var builder = Host.CreateApplicationBuilder(args);
                builder.Logging.ClearProviders();
                builder.Services.AddKiotaRocketsClient();
                builder.Services.AddRefitterRocketsClient();
                using var host = builder.Build();
                var id = Guid.Parse("{{RocketsApi.RocketId}}");
                var kiota = await host.Services.GetRequiredService<KiotaRocketsClient>().Rockets[id].GetAsync();
                Console.WriteLine($"{nameof(KiotaRocketsClient)}: {kiota!.Id} {kiota.SerialNumber}");
                var refitter = await host.Services.GetRequiredService<IRefitterRocketsClient>().GetRocket(id);
                Console.WriteLine($"{nameof(IRefitterRocketsClient)}: {refitter.Id} {refitter.SerialNumber}");
                """
            );

        await project.Build("app");
        var output = await project.Run(
            "app",
            new Dictionary<string, string>
            {
                ["Clients__KiotaRocketsClient__BaseAddress"] = api.BaseAddress.ToString(),
                ["Clients__RefitterRocketsClient__BaseAddress"] = api.BaseAddress.ToString(),
            }
        );

        await project.VerifyResult(output);
    }

    /// <summary>
    /// Build seam: <c>ChirographKiotaVersion</c> and <c>ChirographRefitterVersion</c> make restore download another version of the tool,
    /// and the build generate with it: the generated code names the version that wrote it.
    /// </summary>
    [Test]
    [Arguments(Generator.Kiota, "1.37.0")]
    [Arguments(Generator.Refitter, "2.3.0")]
    public async Task Should_Generate_With_Another_Tool_Version(Generator generator, string version)
    {
        using var project = new SdkTestProject(targetFramework);
        project
           .AddProject(
                "lib/lib.csproj",
                project.CreateConsumer("Library").Property($"Chirograph{generator}Version", version).OpenApiClient(generator, "rockets.json")
            )
           .AddFile("lib/rockets.json", RocketsApi.Document);

        await project.Build("lib");
        var generated = Path.Combine(project.Directory, "lib", "obj", "Debug", targetFramework, "openapi", "RocketsClient", "generated");
        var stamp = generator switch
        {
            // Kiota writes its version into kiota-lock.json
            Generator.Kiota => System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(generated, "kiota-lock.json")))!["kiotaVersion"]!.GetValue<string>(),
            // Refitter into the header of the code
            Generator.Refitter => ( await File.ReadAllLinesAsync(Path.Combine(generated, "RocketsClient.cs")) ).First(z => z.Contains("generated by Refitter", StringComparison.Ordinal)).Trim(),
            _ => throw new ArgumentOutOfRangeException(nameof(generator), generator, null),
        };

        await project.VerifyResult(stamp, generator.ToString());
    }

    /// <summary>
    /// Evaluation seam: a version property the project sets wins over the one Chirograph was packed with, for the tool it downloads and
    /// for the packages it adds.
    /// </summary>
    [Test]
    [Arguments(Generator.Kiota)]
    [Arguments(Generator.Refitter)]
    public async Task Should_Use_The_Versions_The_Project_Sets(Generator generator)
    {
        using var project = new SdkTestProject(targetFramework);
        var consumer = project.CreateConsumer().OpenApiClient(generator, "rockets.json");
        if (generator == Generator.Kiota)
        {
            consumer.Property("ChirographKiotaVersion", "1.37.0").Property("ChirographKiotaBundleVersion", "2.0.0").Property("ChirographMicrosoftExtensionsHttpVersion", "10.0.0");
        }
        else
        {
            consumer.Property("ChirographRefitterVersion", "2.3.0").Property("ChirographRefitVersion", "15.0.0");
        }

        project.AddProject("app/app.csproj", consumer);

        await project.VerifyEvaluation("app/app.csproj", generator.ToString());
    }

    /// <summary>
    /// Build seam: <c>ChirographOpenApiClientsDirectory</c> moves each client's folder, here into the project, and the client still
    /// builds, again, and runs.
    /// </summary>
    [Test]
    public async Task Should_Generate_Into_The_Clients_Directory()
    {
        await using var api = RocketsApi.Start();
        using var project = new SdkTestProject(targetFramework);
        project
           .AddProject(
                "app/app.csproj",
                project.CreateConsumer().Property("ChirographOpenApiClientsDirectory", "clients/").Hosting(targetFramework).OpenApiClient(Generator.Kiota, "rockets.json")
            )
           .AddFile("app/rockets.json", RocketsApi.Document)
           .AddFile("app/Program.cs", RocketsApp.Program(Generator.Kiota));

        await project.Build("app");
        // again, now that evaluation finds the generated code in the project's folder
        var again = await project.Build("app");
        var output = await project.Run("app", RocketsApp.Environment(api));
        var client = Path.Combine(project.Directory, "app", "clients", "RocketsClient");

        await project.VerifyResult(
            new
            {
                Folder = Directory.EnumerateFileSystemEntries(client).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray(),
                Warnings = again.FindChildrenRecursive<Microsoft.Build.Logging.StructuredLogger.Warning>().Select(z => $"{z.Code}: {z.Text}").Distinct().ToArray(),
                Output = output,
            }
        );
    }

    /// <summary>
    /// Build seam: a target of the project that runs <c>AfterTargets="ChirographGenerateOpenApiClients"</c> sees the generated code in
    /// <c>Compile</c>.
    /// </summary>
    [Test]
    public async Task Should_Run_A_Target_Hooked_After_The_Clients_Are_Generated()
    {
        using var project = new SdkTestProject(targetFramework);
        project
           .AddProject(
                "lib/lib.csproj",
                project
                   .CreateConsumer("Library")
                   .OpenApiClient(Generator.Kiota, "rockets.json")
                   .Target("ListClientCode", afterTargets: "ChirographGenerateOpenApiClients")
                   .TaskMessage(
                        "Client code: %(Compile.Filename)%(Compile.Extension)",
                        Microsoft.Build.Framework.MessageImportance.High,
                        condition: "$([System.String]::Copy('%(Compile.FullPath)').Contains('openapi'))"
                    )
            )
           .AddFile("lib/rockets.json", RocketsApi.Document);

        var build = await project.Build("lib");

        await project.VerifyResult(
            build.FindChildrenRecursive<Microsoft.Build.Logging.StructuredLogger.Message>(z => z.Text.StartsWith("Client code: ", StringComparison.Ordinal))
                 .Select(z => z.Text)
                 .Distinct(StringComparer.Ordinal)
                 .Order(StringComparer.Ordinal)
                 .ToArray()
        );
    }

    /// <summary>
    /// Build seam: without a configured base address, a Kiota client calls the first server of its document.
    /// </summary>
    [Test]
    public async Task Should_Call_The_First_Server_Of_The_Document_Without_A_Base_Address()
    {
        await using var api = RocketsApi.Start();
        using var project = new SdkTestProject(targetFramework);
        project.AddRocketsApp(targetFramework, Generator.Kiota).AddFile("app/rockets.json", api.DocumentWithServer);

        await project.Build("app");
        var output = await project.Run("app");

        await project.VerifyResult(output);
    }

    /// <summary>
    /// Build seam: without a configured base address, a Refit client, which has no server to fall back to, fails when it is called, with
    /// an exception that says the base address is missing.
    /// </summary>
    [Test]
    public async Task Should_Fail_To_Call_A_Refit_Client_Without_A_Base_Address()
    {
        using var project = new SdkTestProject(targetFramework);
        project
           .AddRocketsApp(targetFramework, Generator.Refitter)
           .AddFile(
                "app/Program.cs",
                $$"""
                using System;
                using Microsoft.Extensions.DependencyInjection;
                using Microsoft.Extensions.Hosting;
                using Microsoft.Extensions.Logging;
                using Throwaway.Clients;

                var builder = Host.CreateApplicationBuilder(args);
                builder.Logging.ClearProviders();
                builder.Services.AddRocketsClient();
                using var host = builder.Build();
                var client = host.Services.GetRequiredService<IRocketsClient>();
                try
                {
                    await client.GetRocket(Guid.Parse("{{RocketsApi.RocketId}}"));
                    Console.WriteLine("called");
                }
                catch (Exception e)
                {
                    Console.WriteLine($"{e.GetType().FullName}: {e.Message}");
                }
                """
            );

        await project.Build("app");
        var output = await project.Run("app");

        await project.VerifyResult(output);
    }

    /// <summary>
    /// Build seam: a library with several target frameworks generates its client for each, and an application on either registers it
    /// through the library's <c>AddRocketsClient()</c>.
    /// </summary>
    [Test]
    [Arguments(Generator.Kiota)]
    [Arguments(Generator.Refitter)]
    public async Task Should_Register_A_Client_From_A_Multi_Targeted_Library(Generator generator)
    {
        await using var api = RocketsApi.Start();
        using var project = new SdkTestProject(targetFramework);
        project
           .AddProject(
                "lib/lib.csproj",
                Microsoft.Build.Utilities.ProjectCreation.ProjectCreator.Templates
                         .SdkCsproj(targetFrameworks: ["net10.0", "net11.0"], outputType: "Library")
                         .Sdk("Chirograph")
                         .OpenApiClient(generator, "rockets.json")
            )
           .AddFile("lib/rockets.json", RocketsApi.Document)
           .AddProject("app/app.csproj", project.CreateProject().Hosting(targetFramework).ItemProjectReference("../lib/lib.csproj"))
           .AddFile("app/Program.cs", RocketsApp.Program(generator));

        // the library on its own first, which builds both of its target frameworks
        await project.Build("lib");
        await project.Build("app");
        var output = await project.Run("app", RocketsApp.Environment(api));

        await project.VerifyResult(output, generator.ToString());
    }

    /// <summary>
    /// Build seam: an F# server project (<c>.fsproj</c>) is a source like a C# one: the client is generated from the document it writes.
    /// </summary>
    [Test]
    public async Task Should_Generate_A_Client_From_An_FSharp_Server()
    {
        await using var api = RocketsApi.Start();
        using var project = new SdkTestProject(targetFramework);
        project.AddRocketsFSharpServer(targetFramework).AddRocketsApp(targetFramework, Generator.Kiota, RocketsServer.FSharpInclude);

        await project.Build("app");
        var output = await project.Run("app", RocketsApp.Environment(api));

        await project.VerifyResult(output);
    }

    /// <summary>
    /// <c>dotnet publish</c> (Release) generates the client too, and the published application calls it.
    /// </summary>
    [Test]
    public async Task Should_Publish_An_Application_With_A_Client()
    {
        await using var api = RocketsApi.Start();
        using var project = new SdkTestProject(targetFramework);
        project.AddRocketsApp(targetFramework, Generator.Kiota);

        await project.Dotnet("app", "publish -o publish");
        var output = await project.Dotnet(Path.Combine("app", "publish"), "app.dll", RocketsApp.Environment(api));

        await project.VerifyResult(output);
    }

    /// <summary>Where a document comes from.</summary>
    public enum Source
    {
        /// <summary>A file in the project.</summary>
        File,

        /// <summary>A server project, which writes its document when it builds.</summary>
        Project,

        /// <summary>A URL, which <see cref="RocketsApi" /> serves.</summary>
        Url,
    }
}
