using Microsoft.Data.SqlClient;
using PetraERP.Application.Common;
using PetraERP.Infrastructure.Persistence.Registry;

namespace PetraERP.Infrastructure.Workspaces;

public sealed partial class SqlCompanyProvisioningService
{
    private static string DatabaseIdentifier(string value)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(value, "^[A-Za-z][A-Za-z0-9_]{0,127}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)
            || value.Equals("master", StringComparison.OrdinalIgnoreCase) || value.Equals("model", StringComparison.OrdinalIgnoreCase)
            || value.Equals("msdb", StringComparison.OrdinalIgnoreCase) || value.Equals("tempdb", StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("workspace.database_name_invalid", "Use a database identifier of 1–128 ASCII letters, digits or underscores, beginning with a letter.");
        return value;
    }

    private async Task EnsureOwnedDatabaseAsync(FiscalDatabaseRegistryRow registry, ProvisioningOperationRow operation, CancellationToken token)
    {
        var name = DatabaseIdentifier(registry.DatabaseName);
        using var master = new SqlConnection(new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = "master" }.ConnectionString);
        await master.OpenAsync(token).ConfigureAwait(false);
        var resource = "PetraERP.Provision:" + name.ToUpperInvariant();
        using var gate = master.CreateCommand();
        gate.CommandText = "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@resource,@LockMode=N'Exclusive',@LockOwner=N'Session',@LockTimeout=30000; IF @r<0 THROW 51032,N'Database provisioning is busy.',1;";
        gate.Parameters.AddWithValue("@resource", resource);
        await gate.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        try
        {
            using var exists = master.CreateCommand();
            exists.CommandText = "SELECT DB_ID(@name);";
            exists.Parameters.AddWithValue("@name", name);
            var missing = await exists.ExecuteScalarAsync(token).ConfigureAwait(false) is DBNull or null;
            if (missing)
            {
                using var create = master.CreateCommand();
                using var quotes = new SqlCommandBuilder();
                create.CommandText = "CREATE DATABASE " + quotes.QuoteIdentifier(name) + ";";
                await create.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
            using var annual = new SqlConnection(new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = name }.ConnectionString);
            await annual.OpenAsync(token).ConfigureAwait(false);
            using var marker = annual.CreateCommand();
            marker.CommandText = missing
                ? "EXEC sys.sp_addextendedproperty @name=N'PetraERP.ProvisioningOperationId',@value=@operation; SELECT @operation;"
                : "SELECT CAST([value] AS nvarchar(36)) FROM sys.extended_properties WHERE [class]=0 AND [name]=N'PetraERP.ProvisioningOperationId';";
            marker.Parameters.AddWithValue("@operation", operation.Id.ToString("D"));
            var owner = await marker.ExecuteScalarAsync(token).ConfigureAwait(false) as string;
            if (!string.Equals(owner, operation.Id.ToString("D"), StringComparison.OrdinalIgnoreCase))
                throw new BusinessRuleException("workspace.database_name_conflict", "The database already exists and is not owned by this provisioning operation. It will not be adopted or overwritten.");
        }
        finally
        {
            using var release = master.CreateCommand();
            release.CommandText = "EXEC sys.sp_releaseapplock @Resource=@resource,@LockOwner=N'Session';";
            release.Parameters.AddWithValue("@resource", resource);
            await release.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}
