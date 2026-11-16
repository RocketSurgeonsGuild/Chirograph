using System.Text.RegularExpressions;

namespace Chirograph.Tests;

/// <summary>
/// The reference pages of the docs site list the <c>OpenApiClient</c> metadata, the properties, the target hooks and the diagnostic codes
/// by hand. These tests read the SDK's files and fail when a page lists something the SDK doesn't read or raise, or misses something it
/// does, as Dovetail's <c>DovetailItemDocumentationTests</c> does for its items.
/// </summary>
public partial class ReferenceDocumentationTests() : LoggerTest<TestRecord>(TestRecord.Create())
{
    private const string OpenApiClientsTargets = "src/Chirograph/Sdk/Chirograph.OpenApiClients.targets";
    private const string ItemPage = "docs/src/content/docs/reference/openapiclient.md";
    private const string PropertiesPage = "docs/src/content/docs/reference/properties.md";
    private const string DiagnosticsPage = "docs/src/content/docs/reference/diagnostics.md";
    private const string OptionaryPage = "docs/src/content/docs/guides/with-optionary.md";
    private const string OptionsTemplate = "src/Chirograph/Sdk/templates/OpenApiClientOptions.cs.template";

    /// <summary>
    /// The metadata a project can set: every metadata the targets read from <c>OpenApiClient</c> or its private copy
    /// <c>_ChirographOpenApiClient</c>, less the well-known metadata and the metadata the targets work out themselves (an element that
    /// isn't only a default for an empty value). The comment at the top of the targets and the page's table both list them.
    /// </summary>
    [Test]
    public async Task Should_List_Exactly_The_Metadata_The_Targets_Read()
    {
        var targets = ReadFile(OpenApiClientsTargets);

        var computed = ComputedMetadata()
                      .Matches(targets)
                      .Where(z => !z.Groups["condition"].Value.Contains($"'%(_ChirographOpenApiClient.{z.Groups["name"].Value})' == ''", StringComparison.Ordinal))
                      .Select(z => z.Groups["name"].Value)
                      .ToHashSet(StringComparer.Ordinal);
        string[] wellKnown = ["Identity", "FullPath", "Extension", "Filename"];
        var read = MetadataRead()
                  .Matches(targets)
                  .Select(z => z.Groups["name"].Value)
                  .Concat(AnyHaveMetadataValue().Matches(targets).Select(z => z.Groups["name"].Value))
                  .Where(z => !wellKnown.Contains(z) && !computed.Contains(z))
                  .Distinct()
                  .Order(StringComparer.Ordinal)
                  .ToArray();

        var header = Regex.Match(targets, "<!--(?<comment>.*?)-->", RegexOptions.Singleline).Groups["comment"].Value;
        var documentedInHeader = HeaderEntry().Matches(header).Select(z => z.Groups["name"].Value).Distinct().Order(StringComparer.Ordinal).ToArray();
        // Include is the source, and isn't read as metadata.
        var documentedInTable = TableRows(ItemPage, "Metadata").Where(z => z != "Include").Order(StringComparer.Ordinal).ToArray();

        await read.Should().NotBeEmpty();
        await documentedInHeader.Should().BeEquivalentTo(read, CollectionOrdering.Matching).Because($"the comment at the top of {OpenApiClientsTargets}");
        await documentedInTable.Should().BeEquivalentTo(read, CollectionOrdering.Matching).Because($"the metadata table of {ItemPage}");
    }

    /// <summary>The public properties (<c>$(Chirograph…)</c>) the SDK's files read, and the targets it documents as hooks.</summary>
    [Test]
    public async Task Should_List_Exactly_The_Properties_And_Targets_The_Sdk_Reads()
    {
        var files = Directory.EnumerateFiles(Path.Combine(Config.RootDirectory, "src", "Chirograph", "Sdk"), "*.*", SearchOption.TopDirectoryOnly)
                             .Where(z => z.EndsWith(".targets", StringComparison.Ordinal) || z.EndsWith(".props", StringComparison.Ordinal))
                             .Select(File.ReadAllText)
                             .ToArray();
        var properties = files.SelectMany(z => PublicProperty().Matches(z)).Select(z => z.Groups["name"].Value).Distinct().Order(StringComparer.Ordinal).ToArray();
        var targets = files.SelectMany(z => PublicTarget().Matches(z)).Select(z => z.Groups["name"].Value).Distinct().Order(StringComparer.Ordinal).ToArray();

        await properties.Should().NotBeEmpty();
        await TableRows(PropertiesPage, "Properties").Order(StringComparer.Ordinal).ToArray().Should().BeEquivalentTo(properties, CollectionOrdering.Matching).Because($"the properties table of {PropertiesPage}");
        await TableRows(PropertiesPage, "Targets").Order(StringComparer.Ordinal).ToArray().Should().BeEquivalentTo(targets, CollectionOrdering.Matching).Because($"the targets table of {PropertiesPage}");
    }

    /// <summary>
    /// Every code the targets raise, with its severity, is a row of the page's table and a heading of its own, and the error or warning
    /// links to that heading.
    /// </summary>
    [Test]
    public async Task Should_List_Exactly_The_Codes_The_Targets_Raise()
    {
        var raised = Diagnostic()
                    .Matches(ReadFile(OpenApiClientsTargets))
                    .Select(z => (Attributes: z.Groups["attributes"].Value, Severity: z.Groups["severity"].Value.ToLowerInvariant()))
                    .Select(z => (Code: CodeAttribute().Match(z.Attributes).Groups["value"].Value, z.Severity, HelpLink: HelpLinkAttribute().Match(z.Attributes).Groups["value"].Value))
                    .Where(z => z.Code.Length > 0)
                    .OrderBy(z => z.Code, StringComparer.Ordinal)
                    .ToArray();

        var page = ReadFile(DiagnosticsPage);
        var table = Section(page, "Codes");
        var rows = DiagnosticRow().Matches(table).Select(z => $"{z.Groups["code"].Value} {z.Groups["severity"].Value}").Order(StringComparer.Ordinal).ToArray();
        var headings = CodeHeading().Matches(page).Select(z => z.Groups["code"].Value).Order(StringComparer.Ordinal).ToArray();

        await raised.Should().NotBeEmpty();
        await rows.Should().BeEquivalentTo(raised.Select(z => $"{z.Code} {z.Severity}").ToArray(), CollectionOrdering.Matching).Because($"the codes table of {DiagnosticsPage}");
        await headings.Should().BeEquivalentTo(raised.Select(z => z.Code).Distinct().ToArray(), CollectionOrdering.Matching).Because($"the headings of {DiagnosticsPage}");
        await raised.Where(z => z.HelpLink != $"$(_ChirographDiagnosticsUrl)#{z.Code.ToLowerInvariant()}").Select(z => z.Code).ToArray().Should().BeEmpty().Because("each diagnostic links to its heading");
    }

    /// <summary>
    /// The keys of a client's <c>Clients:&lt;ClientName&gt;</c> section: the keys the generated schemas describe (every client in the
    /// accepted snapshots of <see cref="OptionarySchemaTests.Should_Write_The_Schema_Of_Each_Client" />, one per variant), the keys the
    /// options class reads from the section (its <c>ObjectKeyList</c>, with each alias) and the keys the Optionary guide's table lists
    /// are the same.
    /// </summary>
    [Test]
    public async Task Should_List_Exactly_The_Keys_The_Schema_And_The_Options_Read()
    {
        var schemaKeys = Directory.EnumerateFiles(Path.Combine(Config.RootDirectory, "test", "Chirograph.Tests", "snapshots"), "OptionarySchemaTests.Should_Write_The_Schema_Of_Each_Client*.verified.txt")
                                  .SelectMany(z => System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(z))!.AsObject())
                                  .SelectMany(z => z.Value!["properties"]!.AsObject().Select(p => p.Key))
                                  .Distinct()
                                  .Order(StringComparer.Ordinal)
                                  .ToArray();
        // every key of the list, whichever //#if block it is in
        var optionsKeys = ObjectKeyList()
                         .Matches(ReadFile(OptionsTemplate))
                         .SelectMany(z => StringLiteral().Matches(z.Groups["list"].Value))
                         .SelectMany(z => KeyListEntry().Matches(z.Groups["value"].Value))
                         .SelectMany(z => z.Groups["key"].Captures.Select(c => c.Value))
                         .Where(z => z != "and")
                         .Distinct()
                         .Order(StringComparer.Ordinal)
                         .ToArray();

        await schemaKeys.Should().NotBeEmpty();
        await optionsKeys.Should().BeEquivalentTo(schemaKeys, CollectionOrdering.Matching).Because($"the ObjectKeyList of {OptionsTemplate}");
        await TableRows(OptionaryPage, "The keys").Order(StringComparer.Ordinal).ToArray().Should().BeEquivalentTo(schemaKeys, CollectionOrdering.Matching).Because($"the keys table of {OptionaryPage}");
    }

    private static string ReadFile(string path) => File.ReadAllText(Path.Combine(Config.RootDirectory, path));

    private static string Section(string page, string heading) =>
        Regex.Match(page, $@"^## {heading}\s*$(?<section>.*?)(?=^## |\z)", RegexOptions.Multiline | RegexOptions.Singleline).Groups["section"].Value;

    private static IEnumerable<string> TableRows(string path, string heading) => TableRow().Matches(Section(ReadFile(path), heading)).Select(z => z.Groups["name"].Value);

    // the keys of the object in the options class, string literals in an array, some in //#if blocks: KeyList(new[] { "A", "B (or C)", … })
    [GeneratedRegex(@"ObjectKeyList\s*=\s*KeyList\(\s*new\[\]\s*\{(?<list>[^}]*)\}")]
    private static partial Regex ObjectKeyList();

    [GeneratedRegex(@"""(?<value>[^""]*)""")]
    private static partial Regex StringLiteral();

    // an entry of that list: a key, with its alias if it has one
    [GeneratedRegex(@"(?<key>[A-Za-z]\w*)(?: \(or (?<key>[A-Za-z]\w*)\))?")]
    private static partial Regex KeyListEntry();

    [GeneratedRegex(@"%\((?:_Chirograph)?OpenApiClient\.(?<name>[A-Za-z]\w*)\)")]
    private static partial Regex MetadataRead();

    [GeneratedRegex(@"AnyHaveMetadataValue\('(?<name>[A-Za-z]\w*)'")]
    private static partial Regex AnyHaveMetadataValue();

    // An element inside an update or include of _ChirographOpenApiClient: <Name>…</Name> or <Name Condition="…">…</Name>.
    [GeneratedRegex(@"^\s*<(?<name>[A-Z]\w*)(?:\s+Condition=""(?<condition>[^""]*)"")?>[^<]*</\k<name>>", RegexOptions.Multiline)]
    private static partial Regex ComputedMetadata();

    [GeneratedRegex(@"^ {8}- (?<name>[A-Z]\w*)[ :(]", RegexOptions.Multiline)]
    private static partial Regex HeaderEntry();

    [GeneratedRegex(@"^\|\s*`(?<name>[A-Za-z]\w*)`", RegexOptions.Multiline)]
    private static partial Regex TableRow();

    [GeneratedRegex(@"\$\((?<name>Chirograph\w*)\)")]
    private static partial Regex PublicProperty();

    [GeneratedRegex(@"<Target\s+Name=""(?<name>Chirograph\w*)""")]
    private static partial Regex PublicTarget();

    [GeneratedRegex(@"<(?<severity>Error|Warning)\b(?<attributes>(?:(?!/>).)*)/>", RegexOptions.Singleline)]
    private static partial Regex Diagnostic();

    [GeneratedRegex(@"\bCode=""(?<value>CHIRO\d+)""")]
    private static partial Regex CodeAttribute();

    [GeneratedRegex(@"\bHelpLink=""(?<value>[^""]*)""")]
    private static partial Regex HelpLinkAttribute();

    [GeneratedRegex(@"^\|\s*\[`(?<code>CHIRO\d+)`\]\(#chiro\d+\)\s*\|\s*(?<severity>error|warning)\s*\|", RegexOptions.Multiline)]
    private static partial Regex DiagnosticRow();

    [GeneratedRegex(@"^## (?<code>CHIRO\d+)\s*$", RegexOptions.Multiline)]
    private static partial Regex CodeHeading();
}
