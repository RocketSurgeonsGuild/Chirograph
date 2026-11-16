using System;
using Refit;

namespace Rockets.Client.Refitter;

public static partial class RocketsRefitClientServiceCollectionExtensions
{
    // the RefitSettings from the application's container (keyed "RocketsRefitClient", else unkeyed, else none)
    private static partial RefitSettings? CreateRefitSettings(IServiceProvider services, RefitSettings? settings) => settings;
}
