using System.Runtime.CompilerServices;
using DiffEngine;

namespace Chirograph.Tests;

public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        DiffRunner.Disabled = true;
        // Every snapshot lives in snapshots/, named after its test class and method.
        DerivePathInfo((sourceFile, _, type, method) => new(Path.Combine(Path.GetDirectoryName(sourceFile)!, "snapshots"), type.Name, method.Name));
        VerifierSettings.SortPropertiesAlphabetically();
    }
}
