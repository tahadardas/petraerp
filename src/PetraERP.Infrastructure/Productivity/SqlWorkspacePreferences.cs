using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Productivity;

namespace PetraERP.Infrastructure.Productivity;

public sealed partial class SqlWorkspaceRepository
{
    public async Task<StoredWorkspace> LoadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var row = await central.Set<WorkspacePreferenceRow>().AsNoTracking().SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken).ConfigureAwait(false);
        if (row is null) return new StoredWorkspace(WorkspacePreferences.Default, [], string.Empty);
        var value = JsonSerializer.Deserialize<StoredWorkspace>(row.Document) ?? throw new InvalidOperationException("Invalid workspace preferences.");
        return value with { Version = Convert.ToBase64String(row.RowVersion) };
    }
    public async Task<StoredWorkspace> SaveAsync(Guid userId, StoredWorkspace value, CancellationToken cancellationToken)
    {
        var document = JsonSerializer.Serialize(value with { Version = string.Empty });
        if (document.Length > 32768) throw new RequestValidationException("workspace.preferences_too_large", "Workspace preferences exceed their size limit.", null);
        var row = await central.Set<WorkspacePreferenceRow>().SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            if (value.Version.Length != 0) throw new ConcurrencyConflictException("Workspace preferences changed.");
            row = new WorkspacePreferenceRow { UserId = userId, Document = document };
            central.Add(row);
        }
        else
        {
            if (Convert.ToBase64String(row.RowVersion) != value.Version) throw new ConcurrencyConflictException("Workspace preferences changed.");
            row.Document = document;
        }
        try { await central.SaveChangesAsync(cancellationToken).ConfigureAwait(false); }
        catch (DbUpdateConcurrencyException exception) { throw new ConcurrencyConflictException("Workspace preferences changed.", exception); }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        { throw new ConcurrencyConflictException("Workspace preferences changed.", exception); }
        return value with { Version = Convert.ToBase64String(row.RowVersion) };
    }
}
