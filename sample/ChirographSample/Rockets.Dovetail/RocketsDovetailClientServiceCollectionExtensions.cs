using System;
using Refit;

namespace Rockets.Dovetail.Client;

public static partial class RocketsDovetailClientServiceCollectionExtensions
{
    // the RefitSettings from the application's container (keyed "RocketsDovetailClient", else unkeyed, else none)
    private static partial RefitSettings? CreateRefitSettings(IServiceProvider services, RefitSettings? settings) => settings;
}
