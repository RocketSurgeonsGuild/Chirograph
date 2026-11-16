// Written by Chirograph because the file was missing. It is the project's own: edit it, Chirograph never writes it again.
#nullable enable

namespace Rockets.Client.Kiota;

static partial class RocketsClientServiceCollectionExtensions
{
    /// <summary>
    ///     Required: the request adapter of <see cref="RocketsClient" />. <paramref name="adapter" /> has the authentication provider
    ///     from the container (keyed "RocketsClient", else unkeyed, else anonymous): return it, or change or replace it.
    /// </summary>
    private static partial global::Microsoft.Kiota.Abstractions.IRequestAdapter CreateRequestAdapter(
        global::System.IServiceProvider services,
        global::Microsoft.Kiota.Http.HttpClientLibrary.HttpClientRequestAdapter adapter
    ) => adapter;

    /// <summary>
    ///     Optional: called at the end of <c>AddRocketsClient()</c>. Add handlers, resilience and the like to the client's builder here.
    /// </summary>
    static partial void ConfigureHttpClientBuilder(global::Microsoft.Extensions.DependencyInjection.IHttpClientBuilder builder)
    {
    }
}
