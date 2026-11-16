using PetraERP.ApiClient;

namespace PetraERP.Desktop.Connection;

public interface IAlAmeenMigrationClient
{
    Task<MigrationPreviewResponse> PreviewAlAmeenMigrationAsync(MigrationPreviewRequest request, CancellationToken token = default);
    Task<MigrationExecuteResponse> ExecuteAlAmeenMigrationAsync(MigrationExecuteRequest request, CancellationToken token = default);
    Task<MigrationReconciliationResponse> ReconcileAlAmeenMigrationAsync(Guid migrationRunId, CancellationToken token = default);
    Task<MigrationActivationResponse> ActivateAlAmeenMigrationAsync(MigrationActivationRequest request, CancellationToken token = default);
}

public sealed partial class DesktopClient : IAlAmeenMigrationClient
{
    public Task<MigrationPreviewResponse> PreviewAlAmeenMigrationAsync(MigrationPreviewRequest request, CancellationToken token = default) =>
        session.Api.PreviewAlAmeenMigrationAsync(request, token);

    public Task<MigrationExecuteResponse> ExecuteAlAmeenMigrationAsync(MigrationExecuteRequest request, CancellationToken token = default) =>
        session.Api.ExecuteAlAmeenMigrationAsync(request, token);

    public Task<MigrationReconciliationResponse> ReconcileAlAmeenMigrationAsync(Guid migrationRunId, CancellationToken token = default) =>
        session.Api.ReconcileAlAmeenMigrationAsync(migrationRunId, token);

    public Task<MigrationActivationResponse> ActivateAlAmeenMigrationAsync(MigrationActivationRequest request, CancellationToken token = default) =>
        session.Api.ActivateAlAmeenMigrationAsync(request, token);
}
