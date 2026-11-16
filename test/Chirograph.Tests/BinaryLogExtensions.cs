using Microsoft.Build.Logging.StructuredLogger;

namespace Chirograph.Tests;

/// <summary>What a build's binary log (<see cref="SdkTestProject.Build" />) says the build did.</summary>
internal static class BinaryLogExtensions
{
    /// <summary>The clients the build generated, as the message Chirograph logs for each: <c>Generating the OpenAPI client …</c>.</summary>
    public static string[] GeneratedClients(this Build build) =>
        [.. build.FindChildrenRecursive<Message>(z => z.Text.StartsWith("Generating the OpenAPI client ", StringComparison.Ordinal)).Select(z => z.Text)];

    /// <summary>
    /// The client code Chirograph added to the compilation, relative to the folder of the project's clients (<c>…/openapi/</c>), sorted.
    /// Read from the target that adds it: the compiler itself doesn't run again when it is up to date.
    /// </summary>
    public static string[] CompiledClientFiles(this Build build) =>
        [.. build
           .AddedItems("_ChirographCompileOpenApiClients", "Compile")
           .Select(z => z.Replace('\\', '/'))
           .Where(z => z.Contains("/openapi/", StringComparison.Ordinal))
           .Select(z => z[( z.IndexOf("/openapi/", StringComparison.Ordinal) + "/openapi/".Length )..])
           .Distinct(StringComparer.Ordinal)
           .Order(StringComparer.Ordinal)];

    /// <summary>The items of type <paramref name="itemType" /> the target <paramref name="targetName" /> added, in every run of it.</summary>
    public static string[] AddedItems(this Build build, string targetName, string itemType) =>
        [.. build.FindChildrenRecursive<Microsoft.Build.Logging.StructuredLogger.Target>(z => z.Name == targetName)
             .SelectMany(z => z.FindChildrenRecursive<AddItem>(item => item.Name == itemType))
             .SelectMany(z => z.Children.OfType<Item>().Select(item => item.Text))];

    /// <summary>
    /// The values of the parameter <paramref name="parameterName" /> of every run of the task <paramref name="taskName" /> whose
    /// parameters satisfy <paramref name="predicate" />.
    /// </summary>
    public static string[][] TaskParameter(this Build build, string taskName, string parameterName, Func<Microsoft.Build.Logging.StructuredLogger.Task, bool>? predicate = null) =>
        [.. build.FindChildrenRecursive<Microsoft.Build.Logging.StructuredLogger.Task>(z => z.Name == taskName && ( predicate?.Invoke(z) ?? true )).Select(z => z.ParameterValues(parameterName))];

    /// <summary>The values a task was given for <paramref name="parameterName" />: one for a scalar parameter, one per item for a list.</summary>
    public static string[] ParameterValues(this Microsoft.Build.Logging.StructuredLogger.Task task, string parameterName)
    {
        var parameters = task.FindChild<Folder>("Parameters");
        return parameters is null
            ? []
            : [.. parameters
              .Children
              .SelectMany(
                   z => z switch
                   {
                       Property property when property.Name == parameterName => [property.Value],
                       NamedNode node when node.Name == parameterName => node.Children.OfType<Item>().Select(item => item.Text),
                       _ => [],
                   }
               )];
    }
}
