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
