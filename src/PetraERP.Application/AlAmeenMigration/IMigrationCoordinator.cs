namespace PetraERP.Application.AlAmeenMigration;

public interface IMigrationCoordinator
{
    Task<MigrationPreviewResponse> PreviewAsync(
        MigrationPreviewRequest request,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<MigrationExecuteResponse> ExecuteAsync(
        MigrationExecuteRequest request,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<MigrationReconciliationResponse> ReconcileAsync(
        Guid migrationRunId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<MigrationActivationResponse> ActivateAsync(
        MigrationActivationRequest request,
        Guid userId,
        CancellationToken cancellationToken = default);
}
