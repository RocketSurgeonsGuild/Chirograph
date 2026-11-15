using System.Text.RegularExpressions;
using NuGet.Packaging;

namespace Chirograph.Tests;

/// <summary>The Chirograph package that Chirograph.Tests.csproj packs before it builds.</summary>
public partial class PackageTests() : LoggerTest<TestRecord>(TestRecord.Create())
{
    [Test]
    public async Task Should_Ship_Only_Msbuild_Files()
    {
        using var reader = new PackageArchiveReader(Config.ChirographPackage);
        var files = reader.GetFiles()
                          // The zip package's own bookkeeping, which pack writes for every package.
                          .Where(
                               z => !z.StartsWith("_rels/", StringComparison.Ordinal)
                                && !z.StartsWith("package/services/metadata/", StringComparison.Ordinal)
                                && z != "[Content_Types].xml"
                           )
                          .Order(StringComparer.Ordinal)
                          .ToArray();

        await Verify(new { reader.GetIdentity().Id, PackageTypes = reader.GetPackageTypes().Select(z => z.Name), Files = files });
    }

    [Test]
    public async Task Should_Generate_The_Package_Versions_When_Packed()
    {
        using var reader = new PackageArchiveReader(Config.ChirographPackage);
        using var stream = new StreamReader(reader.GetStream("Sdk/Packages.props"));
        var text = await stream.ReadToEndAsync();

        // each version property's value, <ChirographKiotaVersion …>1.2.3</ChirographKiotaVersion>, with its pin scrubbed
        text = VersionProperty().Replace(
            text,
            z => $"{z.Groups["start"].Value}{Config.ScrubPin(Config.PinnedPackageProperties.GetValueOrDefault(z.Groups["name"].Value), z.Groups["value"].Value)}</"
        );

        await Verify(text, "xml").ScrubLinesWithReplace(z => z.Replace(Config.ChirographPackageVersion, "{chirograph-version}", StringComparison.Ordinal));
    }

    [GeneratedRegex(@"(?<start><(?<name>Chirograph\w+Version)\b[^>]*>)(?<value>[^<]*)</")]
    private static partial Regex VersionProperty();
}
