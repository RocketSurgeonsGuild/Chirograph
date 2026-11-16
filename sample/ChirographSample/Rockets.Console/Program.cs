using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rockets.Client.Kiota;
using Rockets.Client.Refitter;
using Rockets.Client.Url;

// Registers the three clients with their generated Add<ClientName>() extensions, without Dovetail. Each client reads its section,
// Clients:<ClientName>: appsettings.json sets a Timeout and a default request header, and the base address comes from the environment,
// for example Clients__RocketsClient__BaseAddress=http://localhost:5179/ (mise run sample:run sets all of them). Each lists the rockets of a running Rockets.Api and gets the second one; the app exits
// non-zero when a call fails or the clients disagree.
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Services.AddRocketsClient();
builder.Services.AddRocketsRefitClient();
builder.Services.AddRocketsUrlClient();

using var host = builder.Build();
var kiota = host.Services.GetRequiredService<RocketsClient>();
var refitter = host.Services.GetRequiredService<IRocketsRefitClient>();
var url = host.Services.GetRequiredService<RocketsUrlClient>();

try
{
    string[][] results =
    [
        await Print(
            "RocketsClient (Kiota, generated from the Rockets.Api project)",
            async () => [.. (await kiota.Rockets.GetAsync() ?? []).Select(z => Describe(z.Id, z.SerialNumber, z.Type))],
            async id => await kiota.Rockets[id].GetAsync() is { } z ? Describe(z.Id, z.SerialNumber, z.Type) : "nothing"
        ),
        await Print(
            "IRocketsRefitClient (Refitter, generated from rockets.json)",
            async () => [.. (await refitter.ListRockets()).Select(z => Describe(z.Id, z.SerialNumber, z.Type))],
            async id => await refitter.GetRocket(id) is { } z ? Describe(z.Id, z.SerialNumber, z.Type) : "nothing"
        ),
        await Print(
            "RocketsUrlClient (Kiota, generated from http://localhost:5179/openapi/v1.json, cached in rockets.openapi.json)",
            async () => [.. (await url.Rockets.GetAsync() ?? []).Select(z => Describe(z.Id, z.SerialNumber, z.Type))],
            async id => await url.Rockets[id].GetAsync() is { } z ? Describe(z.Id, z.SerialNumber, z.Type) : "nothing"
        ),
    ];

    if (results.Skip(1).Any(z => !z.SequenceEqual(results[0])))
    {
        Console.Error.WriteLine("The clients disagree.");
        return 1;
    }

    Console.WriteLine("The three clients agree.");
    return 0;
}
catch (Exception e)
{
    Console.Error.WriteLine($"A call failed: {e.GetType().Name}: {e.Message}");
    return 1;
}

// Lists the rockets, gets the second one by its id, and prints both under the client's title. Returns what it printed.
static async Task<string[]> Print(string title, Func<Task<string[]>> list, Func<Guid, Task<string>> get)
{
    var listed = await list();
    if (listed.Length < 2)
    {
        throw new InvalidOperationException($"{title} listed {listed.Length} rockets, not 2.");
    }

    var got = await get(Guid.Parse(listed[1].Split(' ')[0]));
    string[] lines = [.. listed.Select(z => $"  listed {z}"), $"  got    {got}"];
    Console.WriteLine(title);
    foreach (var line in lines)
    {
        Console.WriteLine(line);
    }

    return lines;
}

static string Describe(Guid? id, string? serialNumber, object? type) => $"{id} {serialNumber} {type}";
