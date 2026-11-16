using Chirograph.Tests.Scaffolding;

namespace Chirograph.Tests;

/// <summary>
/// Build seam: a client is generated on the first build, and after that only when its document changes. Not on the next build, and not
/// when the document is written again with the same content.
/// </summary>
[TargetFrameworks]
public class IncrementalTests(string targetFramework) : LoggerTest<TestRecord>(TestRecord.Create())
{
    [Test]
    [Arguments(Generator.Kiota)]
    [Arguments(Generator.Refitter)]
    public async Task Should_Only_Generate_A_Client_Again_When_Its_Document_Changes(Generator generator)
    {
        using var project = new SdkTestProject(targetFramework);
        project.AddRocketsApp(targetFramework, generator);
        var document = Path.Combine(project.Directory, "app", "rockets.json");

        var first = await project.Build("app");
        var unchanged = await project.Build("app");
        await File.WriteAllTextAsync(document, RocketsApi.Document);
        File.SetLastWriteTimeUtc(document, DateTime.UtcNow.AddMinutes(1));
        var rewritten = await project.Build("app");
        await File.WriteAllTextAsync(
            document,
            RocketsApi.Document.Replace("\"serialNumber\"", "\"name\": { \"type\": \"string\" }, \"serialNumber\"", StringComparison.Ordinal)
        );
        var changed = await project.Build("app");

        await project.VerifyResult(
            new
            {
                First = first.GeneratedClients(),
                Unchanged = unchanged.GeneratedClients(),
                Rewritten = rewritten.GeneratedClients(),
                Changed = changed.GeneratedClients(),
            },
            generator.ToString()
        );
    }

    /// <summary>
    /// A client is generated again when one of its settings changes: its generator, its namespace, its options or the version of its tool.
    /// Its <c>Include</c> naming another document of the same content changes nothing: the client is generated from the content.
    /// </summary>
    [Test]
    [Arguments("Generator")]
    [Arguments("Namespace")]
    [Arguments("Options")]
    [Arguments("ToolVersion")]
    [Arguments("Include")]
    public async Task Should_Generate_A_Client_Again_Only_When_Its_Settings_Change(string setting)
    {
        using var project = new SdkTestProject(targetFramework);
        project
           .AddProject("lib/lib.csproj", project.CreateConsumer("Library").OpenApiClient(Generator.Kiota, "rockets.json"))
           .AddFile("lib/rockets.json", RocketsApi.Document)
           .AddFile("lib/copy.json", RocketsApi.Document);

        var first = await project.Build("lib");
        var (generator, include, metadata, arguments) = setting switch
        {
            "Generator" => (Generator.Refitter, "rockets.json", null, ""),
            "Namespace" => (Generator.Kiota, "rockets.json", new Dictionary<string, string?> { ["Namespace"] = "Throwaway.Other" }, ""),
            "Options" => (Generator.Kiota, "rockets.json", new Dictionary<string, string?> { ["Options"] = "--backing-store" }, ""),
            "ToolVersion" => (Generator.Kiota, "rockets.json", null, "-p:ChirographKiotaVersion=1.37.0"),
            "Include" => (Generator.Kiota, "copy.json", null, ""),
            _ => throw new ArgumentOutOfRangeException(nameof(setting), setting, null),
        };
        project.AddProject("lib/lib.csproj", project.CreateConsumer("Library").OpenApiClient(generator, include, metadata: metadata));
        var changed = await project.Build("lib", arguments);

        await project.VerifyResult(new { First = first.GeneratedClients(), Changed = changed.GeneratedClients() }, setting);
    }

    /// <summary>
    /// A client removed from the project is no longer compiled. Its folder is left in <c>obj</c>, as it was.
    /// </summary>
    [Test]
    public async Task Should_Not_Compile_A_Removed_Client()
    {
        using var project = new SdkTestProject(targetFramework);
        project
           .AddProject(
                "lib/lib.csproj",
                project
                   .CreateConsumer("Library")
                   .OpenApiClient(Generator.Refitter, "rockets.json", "KeptClient", new Dictionary<string, string?> { ["Namespace"] = "Throwaway.Kept" })
                   .OpenApiClient(Generator.Refitter, "rockets.json", "RemovedClient", new Dictionary<string, string?> { ["Namespace"] = "Throwaway.Removed" })
            )
           .AddFile("lib/rockets.json", RocketsApi.Document);

        var both = await project.Build("lib");
        project.AddProject(
            "lib/lib.csproj",
            project.CreateConsumer("Library").OpenApiClient(Generator.Refitter, "rockets.json", "KeptClient", new Dictionary<string, string?> { ["Namespace"] = "Throwaway.Kept" })
        );
        var afterRemoving = await project.Build("lib");

        await project.VerifyResult(
            new
            {
                Both = both.CompiledClientFiles(),
                AfterRemoving = afterRemoving.CompiledClientFiles(),
                RemovedClientFolderLeft = Directory.Exists(Path.Combine(project.Directory, "lib", "obj", "Debug", targetFramework, "openapi", "RemovedClient")),
            }
        );
    }

    /// <summary><c>dotnet clean</c> removes the generated code, so the next build generates the client again instead of failing to compile.</summary>
    [Test]
    public async Task Should_Generate_A_Client_Again_After_A_Clean()
    {
        using var project = new SdkTestProject(targetFramework);
        project.AddRocketsApp(targetFramework, Generator.Kiota);

        var first = await project.Build("app");
        await project.Build("app", "-t:Clean");
        var afterClean = await project.Build("app");

        await project.VerifyResult(new { First = first.GeneratedClients(), AfterClean = afterClean.GeneratedClients() });
    }

    /// <summary>
    /// <c>dotnet clean</c> removes every file the build wrote for a client in <c>obj</c> (the copy of the document, the download, the
    /// settings and the code), but not its <c>DocumentCache</c>, a file of the project kept for a build that cannot reach the URL.
    /// </summary>
    [Test]
    public async Task Should_Remove_What_The_Build_Wrote_On_Clean_But_Not_The_Document_Cache()
    {
        await using var api = RocketsApi.Start();
        using var project = new SdkTestProject(targetFramework);
        project.AddRocketsApp(
            targetFramework,
            Generator.Refitter,
            api.DocumentAddress.ToString(),
            new Dictionary<string, string?> { ["DocumentCache"] = "openapi/rockets.json" }
        );
        var app = Path.Combine(project.Directory, "app");
        var clients = Path.Combine(app, "obj", "Debug", targetFramework, "openapi");

        await project.Build("app");
        await project.Build("app", "-t:Clean");

        await project.VerifyResult(
            new
            {
                LeftInTheClientsFolder = Directory.EnumerateFiles(clients, "*", SearchOption.AllDirectories)
                                                  .Select(z => Path.GetRelativePath(clients, z).Replace('\\', '/'))
                                                  .Order(StringComparer.Ordinal)
                                                  .ToArray(),
                LeftInTheProject = Directory.EnumerateFiles(Path.Combine(app, "openapi")).Select(Path.GetFileName).ToArray(),
            }
        );
    }
}
