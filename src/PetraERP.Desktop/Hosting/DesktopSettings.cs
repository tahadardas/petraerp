using System.IO;
using System.Text.Json;

namespace PetraERP.Desktop.Hosting;

public sealed record DesktopSettings(Uri ApiBaseUri)
{
    public static DesktopSettings Load(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("apiBaseUri", out var property) ||
            !Uri.TryCreate(property.GetString(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || !uri.AbsolutePath.EndsWith('/'))
            throw new InvalidDataException("Invalid HTTPS API configuration.");
        return new DesktopSettings(uri);
    }
}
