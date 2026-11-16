using System.Text;

namespace Chirograph.Build.Tasks;

/// <summary>
///     Writes what a task generates the way <c>WriteLinesToFile</c> with <c>WriteOnlyWhenDifferent</c> did before the tasks existed:
///     UTF-8 without a BOM, trimmed, with a trailing new line, and only when the content changed, so the file's time stamp tells when it
///     did and an incremental build compiles nothing again.
/// </summary>
internal static class GeneratedFile
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    ///     Writes <paramref name="content" /> to <paramref name="path" />, creating its folder, unless the file already holds it.
    /// </summary>
    /// <returns>Whether the file was written.</returns>
    public static bool Write(string path, string content)
    {
        content = content.Trim() + Environment.NewLine;
        if (File.Exists(path) && File.ReadAllText(path, Utf8) == content)
        {
            return false;
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content, Utf8);
        return true;
    }
}
