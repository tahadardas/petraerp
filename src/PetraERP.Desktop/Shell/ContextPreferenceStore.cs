using System.IO;
using System.Text.Json;

namespace PetraERP.Desktop.Shell;

/// <summary>
/// A reopenable document: a route, an account card or a saved journal, by server id only. Unsaved input, new drafts and
/// financial content are never stored; restoring re-reads everything through the normal authorized open path.
/// </summary>
public sealed record WorkspaceDocumentReference(string Kind, string? Route, Guid? Id, bool Detached, bool Active,
    DetachedWindowPlacement? Placement = null);

/// <summary>Last company/branch/fiscal-year of a user and the documents that were open in that scope.</summary>
public sealed record ContextPreference(Guid CompanyId, Guid BranchId, IReadOnlyList<WorkspaceDocumentReference>? Documents = null, Guid? FiscalYearId = null, int? YearNumber = null)
{
    public const int MaximumDocuments = 20;
}
public interface IContextPreferenceStore
{
    ContextPreference? Read(Guid userId);
    void Save(Guid userId, ContextPreference preference);
}
public sealed class ContextPreferenceStore(string directory) : IContextPreferenceStore
{
    private string PathFor(Guid userId) => Path.Combine(directory, userId.ToString("N") + ".json");
    public ContextPreference? Read(Guid userId)
    {
        try { var path = PathFor(userId); return File.Exists(path) && new FileInfo(path).Length <= 16 * 1024 ? JsonSerializer.Deserialize<ContextPreference>(File.ReadAllText(path)) : null; }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }
    public void Save(Guid userId, ContextPreference preference)
    {
        Directory.CreateDirectory(directory); var path = PathFor(userId); var temporary = path + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(preference)); File.Move(temporary, path, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
