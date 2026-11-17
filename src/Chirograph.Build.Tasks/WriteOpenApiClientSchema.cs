using System.Text.RegularExpressions;
using Microsoft.Build.Framework;

namespace Chirograph.Build.Tasks;

/// <summary>
///     Writes the JSON Schema of one client's configuration section, <c>Clients:&lt;ClientName&gt;</c>, from the authentication methods
///     and the keys <c>Chirograph.OpenApiClientSchemas.targets</c> declares (see that file for their metadata).
/// </summary>
/// <remarks>
///     The schema has the keys of the client's generator, and of the project's service discovery and resilience, less the keys only methods the
///     client doesn't allow use. <c>Authentication</c> is an enum of the client's methods, and each method gets an if/then: when
///     Authentication is that method, its keys are required and the keys of the other methods are not allowed.
/// </remarks>
[PublicAPI]
public sealed class WriteOpenApiClientSchema : Microsoft.Build.Utilities.Task
{
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant);

    /// <summary>The file to write.</summary>
    [Required]
    public string OutputFile { get; set; } = "";

    /// <summary>The name of the client.</summary>
    [Required]
    public string ClientName { get; set; } = "";

    /// <summary>The client's generator, Kiota or Refitter.</summary>
    [Required]
    public string Generator { get; set; } = "";

    /// <summary>The client's Authentication metadata: the features (a ;-list) that allow more methods.</summary>
    public string Authentication { get; set; } = "";

    /// <summary>Whether the project references Microsoft.Extensions.ServiceDiscovery.</summary>
    public bool ServiceDiscovery { get; set; }

    /// <summary>Whether the project references Microsoft.Extensions.Http.Resilience.</summary>
    public bool Resilience { get; set; }

    /// <summary>The authentication methods, in order, with their <c>Feature</c> and <c>Requires</c>.</summary>
    [Required]
    public ITaskItem[] Methods { get; set; } = [];

    /// <summary>
    ///     The keys of the section, in order, with their <c>Schema</c>, <c>Description</c>, <c>Generator</c>, <c>ServiceDiscovery</c>,
    ///     <c>Resilience</c>, <c>Methods</c> and <c>Secret</c>.
    /// </summary>
    [Required]
    public ITaskItem[] Keys { get; set; } = [];

    /// <inheritdoc />
    public override bool Execute()
    {
        var features = new HashSet<string>(Whitespace.Replace(Authentication, "").Split(';'), StringComparer.OrdinalIgnoreCase);
        var methods = Methods.Where(z => z.GetMetadata("Feature") is not { Length: > 0 } feature || features.Contains(feature)).ToArray();

        var keys = Keys.Where(z => Matches(z.GetMetadata("Generator"), Generator)
                                && Matches(z.GetMetadata("ServiceDiscovery"), ServiceDiscovery ? "true" : "false")
                                && Matches(z.GetMetadata("Resilience"), Resilience ? "true" : "false"))
                       .ToList();
        // a key only some methods use goes when none of the client's methods uses it (every key of that name: Remove matched on it)
        var unused = new HashSet<string>(
            keys.Where(z => MethodsOf(z) is { Count: > 0 } keyMethods && !methods.Any(m => keyMethods.Contains(m.ItemSpec)))
                .Select(z => z.ItemSpec),
            StringComparer.OrdinalIgnoreCase
        );
        keys.RemoveAll(z => unused.Contains(z.ItemSpec));
        var methodKeys = keys.Where(z => MethodsOf(z).Count > 0).ToArray();

        var properties = keys.Select(
            z =>
            {
                var schema = z.GetMetadata("Schema") is { Length: > 0 } value ? value : "\"type\": \"string\"";
                var description = z.GetMetadata("Description");
                if (string.Equals(z.GetMetadata("Secret"), "true", StringComparison.OrdinalIgnoreCase))
                {
                    description += " It is a secret: keep it out of appsettings.json, in user secrets, an environment variable or a vault, say.";
                }

                return $"\"{z.ItemSpec}\": {{ {schema}, \"description\": \"{description}\" }}";
            }
        );

        // with no key of a method, there is no rule
        var rules = methods.Where(_ => methodKeys.Length > 0).Select(
                m =>
                {
                    var forbids = string.Join(", ", methodKeys.Where(k => !MethodsOf(k).Contains(m.ItemSpec)).Select(k => $"\"{k.ItemSpec}\": false"));
                    var then = $"\"properties\": {{ {forbids} }}";
                    if (m.GetMetadata("Requires") is { Length: > 0 } requires)
                    {
                        then = $"\"allOf\": [{Requirements(requires)}], {then}";
                    }

                    return $"{{ \"if\": {{ \"properties\": {{ \"Authentication\": {{ \"const\": \"{m.ItemSpec}\" }} }}, \"required\": [\"Authentication\"] }}, \"then\": {{ {then} }} }}";
                }
            );

        const string Separator = ",\n    ";
        var text = $$"""
                     {
                       "type": "object",
                       "description": "__ClientName__, a {{Generator}} client Chirograph generates: its Add__ClientName__() registration reads this section to configure the client's HttpClient.",
                       "properties": {
                         {{string.Join(Separator, properties)}}
                       },
                       "allOf": [
                         {{string.Join(Separator, rules)}}
                       ]
                     }
                     """;
        text = text.Replace("__Methods__", string.Join(", ", methods.Select(z => $"\"{z.ItemSpec}\"")))
                   .Replace("__ClientName__", ClientName);

        GeneratedFile.Write(OutputFile, text);
        return !Log.HasLoggedErrors;
    }

    // a metadata that is empty matches anything; MSBuild compares strings case-insensitively
    private static bool Matches(string metadata, string value) => metadata.Length == 0 || string.Equals(metadata, value, StringComparison.OrdinalIgnoreCase);

    private static HashSet<string> MethodsOf(ITaskItem key) => new(
        key.GetMetadata("Methods").Split([';'], StringSplitOptions.RemoveEmptyEntries).Select(z => z.Trim()),
        StringComparer.Ordinal
    );

    // A;B|C: { "required": ["A"] }, { "anyOf": [{ "required": ["B"] }, { "required": ["C"] }] }
    private static string Requirements(string requires) => string.Join(
        ", ",
        requires.Split([';'], StringSplitOptions.RemoveEmptyEntries)
                .Select(
                     z => z.Contains("|")
                         ? $"{{ \"anyOf\": [{string.Join(", ", z.Split('|').Select(Required))}] }}"
                         : Required(z)
                 )
    );

    private static string Required(string key) => $"{{ \"required\": [\"{key}\"] }}";
}
