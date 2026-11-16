using System.Text.Json;
using System.Text.Json.Serialization;

namespace PetraERP.Api.Tests;

/// <summary>Client-side JSON matching the server contract: camelCase and enums as strings (integers are rejected).</summary>
internal static class ApiJson
{
    internal static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
