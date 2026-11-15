using NuGet.Packaging;

namespace Chirograph.Tests;

/// <summary>The Chirograph package that Chirograph.Tests.csproj packs before it builds.</summary>
public class PackageTests
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

        foreach (var packageId in Config.PinnedPackageProperties.Values)
        {
            text = text.Replace($">{Config.CentralPackageVersion(packageId, "netstandard2.0")}<", $">{Config.PinPlaceholder(packageId)}<", StringComparison.Ordinal);
        }

        await Verify(text, "xml").ScrubLinesWithReplace(z => z.Replace(Config.ChirographPackageVersion, "{chirograph-version}", StringComparison.Ordinal));
    }
}
