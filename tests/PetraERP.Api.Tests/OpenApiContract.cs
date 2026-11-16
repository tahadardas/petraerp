using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PetraERP.Api.Tests;

/// <summary>Locates and normalizes the committed OpenAPI contract used to generate the desktop client.</summary>
internal static class OpenApiContract
{
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PetraERP.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root (PetraERP.slnx) was not found.");
    }

    internal static string CommittedPath() =>
        Path.Combine(RepositoryRoot(), "src", "PetraERP.ApiClient", "openapi", "petraerp-v1.json");

    /// <summary>Re-serializes with stable indentation and LF endings so comparisons ignore formatting only.</summary>
    internal static string Normalize(string json)
    {
        var node = JsonNode.Parse(json) ?? throw new InvalidOperationException("The OpenAPI document is empty.");
        return node.ToJsonString(Indented).ReplaceLineEndings("\n") + "\n";
    }
}
