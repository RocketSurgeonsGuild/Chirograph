using Microsoft.AspNetCore.Http.HttpResults;
using Rockets.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();

var app = builder.Build();
app.MapOpenApi();

// A request that carries an API key must carry the right one, Rockets:ApiKey (sample-key by default); a request without one is anonymous.
var apiKey = app.Configuration["Rockets:ApiKey"] ?? "sample-key";
app.Use(
    async (context, next) =>
    {
        if (context.Request.Headers.TryGetValue("X-Api-Key", out var key) && key != apiKey)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }
);

Rocket[] rockets =
[
    new(Guid.Parse("4c1e2a8e-5c0b-4c1f-9a43-1f4a9a2f0d11"), "FALCON-9", RocketType.Falcon9),
    new(Guid.Parse("7b2d3f5a-9e1c-4d2b-8f6a-2e5b8c3d1a22"), "FALCON-HEAVY", RocketType.FalconHeavy),
];

app.MapGet("/rockets", () => rockets).WithName("ListRockets");
app.MapGet(
        "/rockets/{id:guid}",
        Results<Ok<Rocket>, NotFound> (Guid id) => rockets.FirstOrDefault(z => z.Id == id) is { } rocket ? TypedResults.Ok(rocket) : TypedResults.NotFound()
    )
   .WithName("GetRocket");

app.Run();
