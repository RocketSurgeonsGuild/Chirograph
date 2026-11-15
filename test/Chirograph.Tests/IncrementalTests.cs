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
}
