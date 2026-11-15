using System.Text.Json.Serialization;

namespace Rockets.Api;

/// <summary>A rocket in the fleet.</summary>
/// <param name="Id">The rocket's identifier.</param>
/// <param name="SerialNumber">The serial number painted on its side.</param>
/// <param name="Type">The kind of rocket.</param>
public sealed record Rocket(Guid Id, string SerialNumber, RocketType Type);

/// <summary>The kinds of rocket.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RocketType>))]
public enum RocketType
{
    Falcon9,
    FalconHeavy,
}
