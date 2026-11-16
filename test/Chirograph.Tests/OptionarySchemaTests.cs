using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chirograph.Tests.Scaffolding;
using Microsoft.Build.Utilities.ProjectCreation;

namespace Chirograph.Tests;

/// <summary>
/// Each client declares the schema of its <c>Clients:&lt;ClientName&gt;</c> section to Optionary through an <c>OptionarySchemaFragment</c>
/// item, so an application that references Optionary gets the section in its <c>appsettings.schema.json</c>. Without Optionary, or with
/// one that doesn't read the item, the item is inert. <c>ChirographOptionarySchema=false</c> turns it off.
/// </summary>
[TargetFrameworks]
public class OptionarySchemaTests(string targetFramework) : LoggerTest<TestRecord>(TestRecord.Create())
{
    /// <summary>
    /// Evaluation seam: one item per client with a valid name, its section <c>Clients:&lt;ClientName&gt;</c> and the schema file of its
    /// generator, unless <c>ChirographOptionarySchema=false</c>.
    /// </summary>
    [Test]
    [Arguments("")]
    [Arguments("false")]
    public async Task Should_Declare_A_Schema_Fragment_Per_Client(string optionarySchema)
    {
        using var project = new SdkTestProject(targetFramework);
        project.AddProject("app/app.csproj", ClientsApp(project, optionaryVersion: null, optionarySchema));

        await project.VerifyResult(
            project.EvaluatedItems("app/app.csproj", "OptionarySchemaFragment"),
            optionarySchema == "" ? "Declared" : "TurnedOff"
        );
    }

    /// <summary>
    /// Build seam: an application with a Kiota and a Refitter client that references Optionary merges both sections into its
    /// <c>appsettings.schema.json</c>, with <c>BaseAddress</c> required only for the Refitter client.
    /// </summary>
    [Test]
    public async Task Should_Merge_The_Clients_Into_The_Application_Schema()
    {
        using var project = new SdkTestProject(targetFramework);
        AddClientsApp(project, Config.OptionaryVersion);

        await project.Build("app");

        var schema = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(project.Directory, "app", "obj", "appsettings.schema.json")))!;
        var clients = new JsonObject(
            schema["definitions"]!
               .AsObject()
               .Where(z => z.Key == "Clients" || z.Key.StartsWith("Clients:", StringComparison.Ordinal))
               .Select(z => KeyValuePair.Create(z.Key, z.Value?.DeepClone()))
        );

        await project.VerifyResult(clients.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }

    /// <summary>
    /// Build seam: without Optionary, with a released Optionary that doesn't read <c>OptionarySchemaFragment</c>, and with
    /// <c>ChirographOptionarySchema=false</c>, the application builds and no schema it writes has a <c>Clients</c> section. The result lists
    /// each schema file under <c>obj</c> with its sections.
    /// </summary>
    [Test]
    [Arguments("", "")]
    [Arguments(Config.OptionaryVersionWithoutSchemaFragments, "")]
    [Arguments(Config.OptionaryVersion, "false")]
    public async Task Should_Contribute_No_Schema(string optionaryVersion, string optionarySchema)
    {
        using var project = new SdkTestProject(targetFramework);
        AddClientsApp(project, optionaryVersion == "" ? null : optionaryVersion, optionarySchema);

        await project.Build("app");

        var obj = Path.Combine(project.Directory, "app", "obj");
        var schemas = Directory
                     .EnumerateFiles(obj, "*.json", SearchOption.AllDirectories)
                     .Where(z => Path.GetFileName(z) is "appsettings.schema.json" or "ConfigurationSchema.json")
                     .Order(StringComparer.Ordinal)
                     .ToDictionary(
                          z => Path.GetRelativePath(obj, z).Replace('\\', '/'),
                          z => JsonNode.Parse(File.ReadAllText(z))!["definitions"]
                                      ?.AsObject()
                                       .Select(d => d.Key)
                                       .Where(d => d != "SchemaStoreAppSettings")
                                       .Order(StringComparer.Ordinal)
                                       .ToArray()
                            ?? []
                      );

        await project.VerifyResult(
            schemas,
            (optionaryVersion, optionarySchema) switch
            {
                ("", _) => "NoOptionary",
                (_, "false") => "TurnedOff",
                _ => "OlderOptionary",
            }
        );
    }

    private static void AddClientsApp(SdkTestProject project, string? optionaryVersion, string optionarySchema = "") =>
        project
           .AddProject("app/app.csproj", ClientsApp(project, optionaryVersion, optionarySchema))
           .AddFile("app/rockets.json", RocketsApi.Document)
           .AddFile("app/Program.cs", "System.Console.WriteLine();\n");

    // An application with a Kiota client (KiotaClient) and a Refitter client (RefitterClient), referencing Optionary at
    // optionaryVersion (none when null).
    private static ProjectCreator ClientsApp(SdkTestProject project, string? optionaryVersion, string optionarySchema)
    {
        var app = project
                 .CreateConsumer()
                 .Property("ChirographOptionarySchema", optionarySchema == "" ? null : optionarySchema)
                 .OpenApiClient(Generator.Kiota, "rockets.json", "KiotaClient")
                 .OpenApiClient(Generator.Refitter, "rockets.json", "RefitterClient");
        return optionaryVersion is null ? app : app.ItemPackageReference("Optionary", optionaryVersion);
    }
}
