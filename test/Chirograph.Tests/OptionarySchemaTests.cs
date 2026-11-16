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
    // What an IDE runs: a design-time build up to the compiler, which only gives its command line.
    private const string DesignTime = "-t:Compile -p:DesignTimeBuild=true -p:SkipCompilerExecution=true -p:ProvideCommandLineArgs=true";
    // the clients of the application ClientsApp declares
    private static readonly string[] ClientNames = ["KiotaClient", "RefitterClient"];

    /// <summary>
    /// Evaluation seam: one item per client with a valid name, its section <c>Clients:&lt;ClientName&gt;</c> and the client's schema file in
    /// <c>obj</c> (relative to the project), unless <c>ChirographOptionarySchema=false</c>.
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
    /// Build seam: each client's schema is written into its folder in <c>obj</c>, with or without Optionary: the keys of the section,
    /// <c>Authentication</c> as an enum of the methods the client allows, and a rule per method that requires its keys and forbids the
    /// keys of the other methods. The result is each client's schema, by client. A method the item's <c>Authentication</c> metadata
    /// declares (<c>ClientCredentials</c>, written in any case, or <c>Azure</c> and its four methods) adds its values and its keys; without
    /// it, they are not there.
    /// </summary>
    [Test]
    [Arguments(null, null)]
    [Arguments(" clientcredentials ", "ClientCredentials")]
    [Arguments("Azure", "Azure")]
    public async Task Should_Write_The_Schema_Of_Each_Client(string? authentication, string? variant)
    {
        using var project = new SdkTestProject(targetFramework);
        AddClientsApp(project, optionaryVersion: null, authentication: authentication);

        await project.Build("app");

        var clients = new JsonObject(
            ClientNames.Select(
                name => KeyValuePair.Create(
                    name,
                    JsonNode.Parse(File.ReadAllText(Path.Combine(project.Directory, "app", "obj", "Debug", targetFramework, "openapi", name, $"{name}.schema.json")))
                )
            )
        );

        await project.VerifyResult(
            clients.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
            variant
        );
    }

    /// <summary>
    /// Build seam: a design-time build before any build (what an IDE runs when it opens the project) writes the schemas too, since
    /// Optionary reads the items in that build and fails on a file that doesn't exist. The result is Optionary's errors, and the schema
    /// files that exist.
    /// </summary>
    [Test]
    public async Task Should_Write_The_Schemas_In_A_Design_Time_Build()
    {
        using var project = new SdkTestProject(targetFramework);
        AddClientsApp(project, Config.OptionaryVersion);

        var diagnostics = await project.BuildDiagnostics("app", DesignTime, codePrefix: "OPT");
        var clients = Path.Combine(project.Directory, "app", "obj", "Debug", targetFramework, "openapi");

        await project.VerifyResult(
            new
            {
                Diagnostics = diagnostics,
                Schemas = Directory.EnumerateFiles(clients, "*.schema.json", SearchOption.AllDirectories)
                                   .Select(z => Path.GetRelativePath(clients, z).Replace('\\', '/'))
                                   .Order(StringComparer.Ordinal)
                                   .ToArray(),
            }
        );
    }

    /// <summary>
    /// Build seam: an application with a Kiota and a Refitter client that references Optionary merges both generated sections into its
    /// <c>appsettings.schema.json</c>.
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

    private static void AddClientsApp(SdkTestProject project, string? optionaryVersion, string optionarySchema = "", string? authentication = null) =>
        project
           .AddProject("app/app.csproj", ClientsApp(project, optionaryVersion, optionarySchema, authentication))
           .AddFile("app/rockets.json", RocketsApi.Document)
           .AddFile("app/Program.cs", "System.Console.WriteLine();\n");

    // An application with a Kiota client (KiotaClient) and a Refitter client (RefitterClient), referencing Optionary at
    // optionaryVersion (none when null), both with the Authentication metadata authentication.
    private static ProjectCreator ClientsApp(SdkTestProject project, string? optionaryVersion, string optionarySchema, string? authentication = null)
    {
        var metadata = new Dictionary<string, string?> { ["Authentication"] = authentication };
        var app = project
                 .CreateConsumer()
                 .Property("ChirographOptionarySchema", optionarySchema == "" ? null : optionarySchema)
                 .OpenApiClient(Generator.Kiota, "rockets.json", "KiotaClient", metadata)
                 .OpenApiClient(Generator.Refitter, "rockets.json", "RefitterClient", metadata);
        return optionaryVersion is null ? app : app.ItemPackageReference("Optionary", optionaryVersion);
    }
}
