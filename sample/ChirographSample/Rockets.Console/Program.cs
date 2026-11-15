using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rockets.Client.Kiota;
using Rockets.Client.Refitter;

// Each client reads its base address from Clients:<ClientName>:BaseAddress, for example the environment variable
// Clients__RocketsClient__BaseAddress=http://localhost:5000/. The sample only resolves the clients, so it calls no server.
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Services.AddRocketsClient();
builder.Services.AddRocketsRefitClient();

using var host = builder.Build();
var kiota = host.Services.GetRequiredService<RocketsClient>();
var refitter = host.Services.GetRequiredService<IRocketsRefitClient>();

Console.WriteLine($"Kiota:    {kiota.GetType().FullName}");
Console.WriteLine($"Refitter: {nameof(IRocketsRefitClient)} as {refitter.GetType().FullName}");

// With a running Rockets.Api and the base addresses set, the calls look like this:
//     var rockets = await kiota.Rockets.GetAsync();
//     var rocket = await refitter.GetRocket(rockets![0].Id!.Value);
