using System;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace Rockets.Client.Url;

public static partial class RocketsUrlClientServiceCollectionExtensions
{
    // the adapter as built, with the authentication provider from the application's container (keyed "RocketsUrlClient", else unkeyed,
    // else anonymous)
    private static partial IRequestAdapter CreateRequestAdapter(IServiceProvider services, HttpClientRequestAdapter adapter) => adapter;
}
