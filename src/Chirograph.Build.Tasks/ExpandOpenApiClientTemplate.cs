using System.Text.RegularExpressions;
using Microsoft.Build.Framework;

namespace Chirograph.Build.Tasks;

/// <summary>
///     Writes one of a client's files (its registration or its options) from a template in <c>Sdk/templates</c>.
/// </summary>
/// <remarks>
///     A template's code between <c>//#if &lt;Block&gt;</c> and <c>//#endif</c> is kept, without those two lines, only when the block
///     is one of <see cref="Blocks" />; otherwise it is cut out. <c>//#if A|B</c> keeps the code when either block is on. Blocks don't
///     nest, and their names compare case-insensitively. Then <c>__Namespace__</c>, <c>__ClientName__</c> and
///     <c>__ChirographVersion__</c> are replaced.
/// </remarks>
[PublicAPI]
public sealed class ExpandOpenApiClientTemplate : Microsoft.Build.Utilities.Task
{
    private static readonly Regex Block = new(
        @"[ \t]*//#if (?<blocks>[A-Za-z|]+)\r?\n.*?//#endif\r?\n",
        RegexOptions.Singleline | RegexOptions.CultureInvariant
    );

    private static readonly Regex Marker = new(@"[ \t]*//#(if [A-Za-z|]+|endif)\r?\n", RegexOptions.CultureInvariant);

    /// <summary>The template file to read.</summary>
    [Required]
    public string Template { get; set; } = "";

    /// <summary>The file to write.</summary>
    [Required]
    public string OutputFile { get; set; } = "";

    /// <summary>The blocks that are on for the client; empty values are ignored.</summary>
    public string[] Blocks { get; set; } = [];

    /// <summary>The namespace of the client's code.</summary>
    public string Namespace { get; set; } = "";

    /// <summary>The name of the client.</summary>
    [Required]
    public string ClientName { get; set; } = "";

    /// <summary>The version of Chirograph, written into the generated code.</summary>
    public string ChirographVersion { get; set; } = "";

    /// <inheritdoc />
    public override bool Execute()
    {
        var on = new HashSet<string>(
            Blocks.Select(z => z.Trim()).Where(z => z.Length > 0),
            StringComparer.OrdinalIgnoreCase
        );

        var text = Block.Replace(
            File.ReadAllText(Template),
            match => match.Groups["blocks"].Value.Split('|').Any(on.Contains) ? match.Value : ""
        );
        text = Marker.Replace(text, "")
                     .Replace("__Namespace__", Namespace)
                     .Replace("__ClientName__", ClientName)
                     .Replace("__ChirographVersion__", ChirographVersion);

        GeneratedFile.Write(OutputFile, text);
        return !Log.HasLoggedErrors;
    }
}
