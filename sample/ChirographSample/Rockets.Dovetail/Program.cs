using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rockets.Dovetail;
using Rockets.Dovetail.Client;

// The host is built with ConfigureDovetail(), and there is no AddRocketsDovetailClient() call here: the joint Dovetail wrote for the
// client registers it. Its base address is read from Clients:RocketsDovetailClient:BaseAddress, for example the environment variable
// Clients__RocketsDovetailClient__BaseAddress=http://localhost:5179/ (mise run sample:run sets it). It lists the rockets of a running
// Rockets.Api and gets the second one; a failed call exits non-zero.
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
using var host = await builder.ConfigureDovetail();
var client = host.Services.GetRequiredService<IRocketsDovetailClient>();

try
{
    var rockets = (await client.ListRockets()).ToArray();
    if (rockets.Length < 2)
    {
        throw new InvalidOperationException($"Rockets.Api listed {rockets.Length} rockets, not 2.");
    }

    var rocket = await client.GetRocket(rockets[1].Id);

    Console.WriteLine("IRocketsDovetailClient (Refitter, generated from the Rockets.Api project, registered by its Dovetail joint)");
    foreach (var listed in rockets)
    {
        Console.WriteLine($"  listed {listed.Id} {listed.SerialNumber} {listed.Type}");
    }

    Console.WriteLine($"  got    {rocket.Id} {rocket.SerialNumber} {rocket.Type}");
    return 0;
}
catch (Exception e)
{
    Console.Error.WriteLine($"A call failed: {e.GetType().Name}: {e.Message}");
    return 1;
}
