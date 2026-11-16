using System.Text.Json;
using System.Text.Json.Serialization;

namespace PetraERP.ApiClient;

/// <summary>
/// Hand-written partial of the generated client. The server contract transmits enums as their names
/// (integers are rejected) and omits absent optional members, so the client serializes the same way.
/// </summary>
public partial class PetraApiClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
        settings.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        settings.NumberHandling = JsonNumberHandling.Strict;
    }
}
