using Chirograph.Tests.Scaffolding;

namespace Chirograph.Tests;

/// <summary>
/// Build seam: the build writes the project's own part of the registration class, <c>&lt;ClientName&gt;ServiceCollectionExtensions.cs</c>
/// in the project (or <c>RegistrationFile</c>), when it is missing, and never again. It implements the required hook by returning what
/// it is given, and has an empty <c>ConfigureHttpClientBuilder</c>, which <c>Add&lt;ClientName&gt;()</c> calls last.
/// </summary>
[TargetFrameworks]
public class RegistrationScaffoldTests(string targetFramework) : LoggerTest<TestRecord>(TestRecord.Create())
{
    /// <summary>A new client gets the file on its first build, and the application compiles and calls the API.</summary>
    [Test]
    [Arguments(Generator.Kiota)]
    [Arguments(Generator.Refitter)]
    public async Task Should_Scaffold_The_Registration_File(Generator generator)
    {
        await using var api = RocketsApi.Start();
        using var project = new SdkTestProject(targetFramework);
        project.AddRocketsApp(targetFramework, generator);

        await project.Build("app");
        var output = await project.Run("app", RocketsApp.Environment(api));

        await project.VerifyResult(new { Output = output, File = File.ReadAllText(Path.Combine(project.Directory, "app", DefaultFile)) }, generator.ToString());
    }

    /// <summary>A file the project has is never written over.</summary>
    [Test]
    [Arguments(Generator.Kiota)]
    [Arguments(Generator.Refitter)]
    public async Task Should_Leave_An_Existing_File_Alone(Generator generator)
    {
        using var project = new SdkTestProject(targetFramework);
        var edited = "// edited by the project\n" + Implementation(generator, "");
        project.AddRocketsApp(targetFramework, generator).AddFile($"app/{DefaultFile}", edited);

        await project.Build("app");

        await project.VerifyResult(File.ReadAllText(Path.Combine(project.Directory, "app", DefaultFile)), generator.ToString());
    }

    /// <summary>
    /// The file is the project's: <c>dotnet clean</c> keeps it, a design-time build doesn't write it, and
    /// <c>ChirographScaffoldRegistration=false</c> doesn't either.
    /// </summary>
    [Test]
    public async Task Should_Keep_The_File_Out_Of_Clean_And_Design_Time_Builds()
    {
        using var project = new SdkTestProject(targetFramework);
        project.AddRocketsApp(targetFramework, Generator.Kiota);
        var file = Path.Combine(project.Directory, "app", DefaultFile);

        await project.Build("app", "-t:Compile -p:DesignTimeBuild=true -p:SkipCompilerExecution=true -p:ProvideCommandLineArgs=true");
        var afterADesignTimeBuild = File.Exists(file);
        await project.BuildDiagnostics("app", "-p:ChirographScaffoldRegistration=false");
        var withTheSwitchOff = File.Exists(file);
        await project.Build("app");
        var afterABuild = File.Exists(file);
        await project.Dotnet("app", "clean");
        var afterAClean = File.Exists(file);

        await project.VerifyResult(new { afterADesignTimeBuild, withTheSwitchOff, afterABuild, afterAClean });
    }

    /// <summary><c>RegistrationFile</c> puts the file where it says, and the build compiles it from there.</summary>
    [Test]
    [Arguments(Generator.Kiota)]
    [Arguments(Generator.Refitter)]
    public async Task Should_Write_The_File_Where_RegistrationFile_Says(Generator generator)
    {
        await using var api = RocketsApi.Start();
        using var project = new SdkTestProject(targetFramework);
        project.AddRocketsApp(targetFramework, generator, metadata: new Dictionary<string, string?> { ["RegistrationFile"] = "../shared/Rockets.Registration.cs" });

        await project.Build("app");
        var output = await project.Run("app", RocketsApp.Environment(api));

        await project.VerifyResult(
            new
            {
                Output = output,
                Moved = File.Exists(Path.Combine(project.Directory, "shared", "Rockets.Registration.cs")),
                Default = File.Exists(Path.Combine(project.Directory, "app", DefaultFile)),
            },
            generator.ToString()
        );
    }

    /// <summary>A handler <c>ConfigureHttpClientBuilder</c> adds reaches the API: it sends <c>X-Serial-Number</c>, which the API echoes.</summary>
    [Test]
    [Arguments(Generator.Kiota)]
    [Arguments(Generator.Refitter)]
    public async Task Should_Call_ConfigureHttpClientBuilder(Generator generator)
    {
        await using var api = RocketsApi.Start();
        using var project = new SdkTestProject(targetFramework);
        project
           .AddRocketsApp(targetFramework, generator)
           .AddFile(
                $"app/{DefaultFile}",
                Implementation(
                    generator,
                    """
                        static partial void ConfigureHttpClientBuilder(global::Microsoft.Extensions.DependencyInjection.IHttpClientBuilder builder) =>
                            global::Microsoft.Extensions.DependencyInjection.HttpClientBuilderExtensions.AddHttpMessageHandler(builder, () => new SerialNumberHandler());

                        private sealed class SerialNumberHandler : global::System.Net.Http.DelegatingHandler
                        {
                            protected override global::System.Threading.Tasks.Task<global::System.Net.Http.HttpResponseMessage> SendAsync(
                                global::System.Net.Http.HttpRequestMessage request,
                                global::System.Threading.CancellationToken cancellationToken
                            )
                            {
                                request.Headers.TryAddWithoutValidation("X-Serial-Number", "BUILDER");
                                return base.SendAsync(request, cancellationToken);
                            }
                        }
                    """
                )
            );

        await project.Build("app");
        var output = await project.Run("app", RocketsApp.Environment(api));

        await project.VerifyResult(output, generator.ToString());
    }

    /// <summary>Without the file, and with the scaffold turned off, the project fails to compile with CS8795.</summary>
    [Test]
    [Arguments(Generator.Kiota)]
    [Arguments(Generator.Refitter)]
    public async Task Should_Require_The_File_Once_Deleted(Generator generator)
    {
        using var project = new SdkTestProject(targetFramework);
        project.AddRocketsApp(targetFramework, generator);
        await project.Build("app");
        File.Delete(Path.Combine(project.Directory, "app", DefaultFile));

        var diagnostics = await project.BuildDiagnostics("app", "-p:ChirographScaffoldRegistration=false", codePrefix: "CS8795");

        await project.VerifyResult(diagnostics, generator.ToString());
    }

    private const string DefaultFile = "RocketsClientServiceCollectionExtensions.cs";

    private static string Implementation(Generator generator, string more) => $$"""
        namespace Throwaway.Clients;

        static partial class RocketsClientServiceCollectionExtensions
        {
            {{RequiredHook(generator)}}
        {{more}}
        }
        """;

    private static string RequiredHook(Generator generator) => generator switch
    {
        Generator.Kiota =>
            "private static partial global::Microsoft.Kiota.Abstractions.IRequestAdapter CreateRequestAdapter(global::System.IServiceProvider services, global::Microsoft.Kiota.Http.HttpClientLibrary.HttpClientRequestAdapter adapter) => adapter;",
        Generator.Refitter =>
            "private static partial global::Refit.RefitSettings? CreateRefitSettings(global::System.IServiceProvider services, global::Refit.RefitSettings? settings) => settings;",
        _ => throw new ArgumentOutOfRangeException(nameof(generator), generator, null),
    };
}
