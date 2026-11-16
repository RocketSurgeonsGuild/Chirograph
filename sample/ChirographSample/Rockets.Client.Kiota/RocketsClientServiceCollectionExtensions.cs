using System;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace Rockets.Client.Kiota;

public static partial class RocketsClientServiceCollectionExtensions
{
    // the adapter as built, with the authentication provider from the application's container (keyed "RocketsClient", else unkeyed,
    // else anonymous)
    private static partial IRequestAdapter CreateRequestAdapter(IServiceProvider services, HttpClientRequestAdapter adapter) => adapter;
}
