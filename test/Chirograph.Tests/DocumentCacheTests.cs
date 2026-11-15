using Chirograph.Tests.Scaffolding;

namespace Chirograph.Tests;

/// <summary>
/// Build seam: a client generated from a URL downloads its document on every build into a cache, the client's folder in <c>obj</c>, or
/// <c>DocumentCache</c>, a file of the project that can be committed. The cache is updated only when the download differs from it, it
/// stands in for a download that fails (<c>CHIRO104</c>), and <c>UpdateDocumentCache</c> set to <c>false</c> uses it without
/// downloading.
/// </summary>
[TargetFrameworks]
public class DocumentCacheTests(string targetFramework) : LoggerTest<TestRecord>(TestRecord.Create())
{
    /// <summary>
    /// Two builds download the document twice but generate the client once. When the URL then answers with an error, the build uses the last download,
    /// with <c>CHIRO104</c>, and generates nothing.
    /// </summary>
    [Test]
    public async Task Should_Use_The_Last_Download_When_The_Url_Cannot_Be_Reached()
    {
        await using var api = RocketsApi.Start();
        using var project = new SdkTestProject(targetFramework);
        project.AddRocketsApp(targetFramework, Generator.Kiota, api.DocumentAddress.ToString());

        var first = await project.Build("app");
        var again = await project.Build("app");
        var documentRequests = api.DocumentRequests;
        api.DocumentUnavailable = true;
        var offline = await project.Build("app");

        await project.VerifyResult(
            new
            {
                First = first.GeneratedClients(),
                Again = again.GeneratedClients(),
                DocumentRequests = documentRequests,
                Offline = new { Generated = offline.GeneratedClients(), Diagnostics = project.Diagnostics(offline) },
            }
        );
    }

    /// <summary>
    /// <c>DocumentCache</c> keeps the download in a file of the project: a fresh clone (no <c>obj</c> or <c>bin</c>) that cannot reach the
    /// URL generates the client from the committed file, with <c>CHIRO104</c>.
    /// </summary>
    [Test]
    public async Task Should_Generate_From_The_Document_Cache_When_The_Url_Cannot_Be_Reached()
    {
        await using var api = RocketsApi.Start();
        using var project = new SdkTestProject(targetFramework);
        project.AddRocketsApp(
            targetFramework,
            Generator.Kiota,
            api.DocumentAddress.ToString(),
            new Dictionary<string, string?> { ["DocumentCache"] = "openapi/rockets.json" }
        );

        await project.Build("app");
        var cache = await File.ReadAllTextAsync(Path.Combine(project.Directory, "app", "openapi", "rockets.json"));
        Directory.Delete(Path.Combine(project.Directory, "app", "obj"), recursive: true);
        Directory.Delete(Path.Combine(project.Directory, "app", "bin"), recursive: true);
        api.DocumentUnavailable = true;
        var offline = await project.Build("app");

        await project.VerifyResult(
            new
            {
                Cache = cache,
                Offline = new { Generated = offline.GeneratedClients(), Diagnostics = project.Diagnostics(offline) },
            }
        );
    }

    /// <summary>
    /// <c>UpdateDocumentCache</c> set to <c>false</c>, on the client or for the whole project with
    /// <c>ChirographUpdateOpenApiDocumentCache</c>, generates the client from a committed cache without a request to the URL.
    /// </summary>
    [Test]
    [Arguments("UpdateDocumentCache")]
    [Arguments("ChirographUpdateOpenApiDocumentCache")]
    public async Task Should_Use_The_Document_Cache_Without_Downloading_When_Not_Updating_It(string setting)
    {
        await using var api = RocketsApi.Start();
        using var project = new SdkTestProject(targetFramework);
        var metadata = new Dictionary<string, string?> { ["DocumentCache"] = "openapi/rockets.json" };
        if (setting == "UpdateDocumentCache")
        {
            metadata["UpdateDocumentCache"] = "false";
        }
        else
        {
            project.AddFile(
                "app/Directory.Build.props",
                "<Project><PropertyGroup><ChirographUpdateOpenApiDocumentCache>false</ChirographUpdateOpenApiDocumentCache></PropertyGroup></Project>"
            );
        }

        project
           .AddRocketsApp(targetFramework, Generator.Kiota, api.DocumentAddress.ToString(), metadata)
           // the document an earlier build cached and the repository committed
           .AddFile("app/openapi/rockets.json", RocketsApi.Document);

        var build = await project.Build("app");
        var output = await project.Run("app", RocketsApp.Environment(api));

        // no request is the result, so show the 0 rather than leave it out
        await project
             .VerifyResult(new { Generated = build.GeneratedClients(), api.DocumentRequests, Output = output })
             .AddExtraSettings(z => z.DefaultValueHandling = Argon.DefaultValueHandling.Include);
    }
}
