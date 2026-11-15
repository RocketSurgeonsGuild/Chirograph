using Chirograph.Tests.Scaffolding;

namespace Chirograph.Tests;

/// <summary>
/// With a Dovetail whose <c>DovetailTemplate</c> replaces every metadata value as a token (<c>DovetailTemplateMetadataTokens</c>),
/// Chirograph declares one template per client, which Dovetail writes as a joint that calls <c>Add&lt;ClientName&gt;()</c>.
/// <c>ChirographGenerateDovetailJoint=false</c> turns the joints off, and an older Dovetail gets no templates.
/// </summary>
[TargetFrameworks]
public class DovetailJointTests(string targetFramework)
{
    /// <summary>
    /// Build seam: an application that builds its host with <c>ConfigureDovetail()</c> resolves the client with no registration call of
    /// its own, and the client calls the API at the configured base address.
    /// </summary>
    [Test]
    [Arguments(Generator.Kiota)]
    [Arguments(Generator.Refitter)]
    public async Task Should_Register_The_Client_Through_A_Joint(Generator generator)
    {
        await using var api = RocketsApi.Start();
        using var project = new SdkTestProject(targetFramework);
        project.AddDovetailApp(targetFramework, generator, Config.DovetailVersion);

        await project.Build("app");
        var output = await project.Run("app", RocketsApp.Environment(api));

        await project.VerifyResult(output, generator.ToString());
    }

    /// <summary>
    /// Build seam: with <c>ChirographGenerateDovetailJoint=false</c>, or with a released Dovetail that can't expand the template's tokens,
    /// the project builds and Dovetail writes no joint.
    /// </summary>
    [Test]
    [Arguments(Config.DovetailVersion, "false")]
    [Arguments(Config.DovetailVersionWithoutMetadataTokens, "")]
    public async Task Should_Write_No_Joint(string dovetailVersion, string generateDovetailJoint)
    {
        using var project = new SdkTestProject(targetFramework);
        project.AddDovetailApp(
            targetFramework,
            Generator.Kiota,
            dovetailVersion,
            generateDovetailJoint == "" ? null : generateDovetailJoint
        );

        await project.Build("app");

        await project.VerifyResult(JointFiles(project), dovetailVersion == Config.DovetailVersion ? "TurnedOff" : "OlderDovetail");
    }

    /// <summary>
    /// Evaluation seam: one <c>DovetailTemplate</c> per client, named <c>Chirograph.&lt;ClientName&gt;</c> so it can't clash with
    /// another package's, when the Dovetail in the build advertises <c>DovetailTemplateMetadataTokens</c> (set here as its props would)
    /// and the joints aren't turned off. The namespace falls back to the root namespace.
    /// </summary>
    [Test]
    [Arguments("true", "")]
    [Arguments("true", "false")]
    [Arguments("", "")]
    public async Task Should_Declare_A_Template_Per_Client(string metadataTokens, string generateDovetailJoint)
    {
        using var project = new SdkTestProject(targetFramework);
        var consumer = project
                      .CreateConsumer()
                      .Property("DovetailTemplateMetadataTokens", metadataTokens == "" ? null : metadataTokens)
                      .Property("ChirographGenerateDovetailJoint", generateDovetailJoint == "" ? null : generateDovetailJoint)
                      .OpenApiClient(Generator.Kiota, "rockets.json")
                      .ItemInclude(
                           "OpenApiClient",
                           "launches.yaml",
                           metadata: new Dictionary<string, string?> { ["Generator"] = "Refitter", ["ClientName"] = "LaunchesClient" }
                       );
        project.AddProject("app/app.csproj", consumer);

        await project.VerifyEvaluation(
            "app/app.csproj",
            (metadataTokens, generateDovetailJoint) switch
            {
                ("true", "") => "Declared",
                ("true", "false") => "TurnedOff",
                _ => "OlderDovetail",
            }
        );
    }

    // The joints Dovetail wrote into the application's intermediate folder (DovetailTemplate.Chirograph.<ClientName>.g.cs).
    private static string[] JointFiles(SdkTestProject project) =>
        [.. Directory
           .EnumerateFiles(Path.Combine(project.Directory, "app", "obj"), "*.Chirograph.*.g.cs", SearchOption.AllDirectories)
           .Select(Path.GetFileName)
           .Order(StringComparer.Ordinal)!];
}

/// <summary>
/// The throwaway Dovetail application of the joint tests: <c>app/app.csproj</c> generates <c>RocketsClient</c> and references
/// Joinery.Dovetail.Hosting, and its <c>Program.cs</c> builds the host with <c>ConfigureDovetail()</c>, resolves the client and asks
/// <see cref="RocketsApi" /> for one rocket.
/// </summary>
internal static class DovetailApp
{
    public static SdkTestProject AddDovetailApp(
        this SdkTestProject project,
        string targetFramework,
        Generator generator,
        string dovetailVersion,
        string? generateDovetailJoint = null
    ) =>
        project
           .AddProject(
                "app/app.csproj",
                project
                   .CreateConsumer()
                   .Property("ChirographGenerateDovetailJoint", generateDovetailJoint)
                   .Hosting(targetFramework)
                   .ItemPackageReference("Joinery.Dovetail.Hosting", dovetailVersion)
                   // The generic host, as Joinery.Dovetail.Sdk's host detection declares it, so Dovetail writes ConfigureDovetail().
                   .ItemInclude(
                        "DovetailHost",
                        "HostApplication",
                        metadata: new Dictionary<string, string?>
                        {
                            ["Namespace"] = "Throwaway",
                            ["HostBuilderType"] = "global::Microsoft.Extensions.Hosting.HostApplicationBuilder",
                            ["HostBuilderName"] = "builder",
                            ["HostType"] = "global::Microsoft.Extensions.Hosting.IHost",
                            ["HelperName"] = "global::Joinery.Dovetail.DovetailHostApplicationHelpers",
                        }
                    )
                   .OpenApiClient(generator, "rockets.json")
            )
           .AddFile("app/rockets.json", RocketsApi.Document)
           .AddFile("app/Program.cs", Program(generator));

    private static string Program(Generator generator) => $$"""
        using System;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Hosting;
        using Microsoft.Extensions.Logging;
        using Throwaway;
        using Throwaway.Clients;

        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        using var host = await builder.ConfigureDovetail();
        {{Call(generator)}}
        """;

    // Resolves the client the joint registered and writes the rocket it gets.
    private static string Call(Generator generator) => generator switch
    {
        Generator.Kiota => $$"""
            var client = host.Services.GetRequiredService<RocketsClient>();
            var rocket = await client.Rockets[Guid.Parse("{{RocketsApi.RocketId}}")].GetAsync();
            Console.WriteLine($"{client.GetType().Name}: {rocket!.Id} {rocket.SerialNumber}");
            """,
        Generator.Refitter => $$"""
            var client = host.Services.GetRequiredService<IRocketsClient>();
            var rocket = await client.GetRocket(Guid.Parse("{{RocketsApi.RocketId}}"));
            Console.WriteLine($"{nameof(IRocketsClient)}: {rocket.Id} {rocket.SerialNumber}");
            """,
        _ => throw new ArgumentOutOfRangeException(nameof(generator), generator, null),
    };
}
