using Microsoft.AspNetCore.Mvc;
using PetraERP.Api.Errors;
using PetraERP.Api.Security;
using PetraERP.Application.Administration;
using PetraERP.Application.Security;
using PetraERP.Contracts.Administration;

namespace PetraERP.Api.Endpoints;

internal static class BackupRestoreEndpoints
{
    internal static void MapBackupRestoreEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/administration/backups")
            .WithTags("System Administration: Backup & Restore");

        var manage = PetraPolicies.Permission(PermissionCatalog.DatabaseManage);

        group.MapPost("", BackupAsync)
            .WithName("BackupDatabase")
            .WithSummary("Creates a native SQL Server backup with CHECKSUM and records it in central BackupCatalog.")
            .RequireAuthorization(manage)
            .Validate<BackupDatabaseRequest>()
            .Produces<BackupOperationResponse>(StatusCodes.Status201Created)
            .ProducesStandardProblems();

        group.MapPost("/verify", VerifyAsync)
            .WithName("VerifyBackup")
            .WithSummary("Verifies an existing backup using RESTORE VERIFYONLY.")
            .RequireAuthorization(manage)
            .Validate<VerifyBackupRequest>()
            .Produces<BackupVerificationResponse>()
            .ProducesStandardProblems();

        group.MapPost("/test-restore", TestRestoreAsync)
            .WithName("TestRestoreBackup")
            .WithSummary("Restores backup into disposable test database and executes DBCC CHECKDB.")
            .RequireAuthorization(manage)
            .Validate<TestRestoreRequest>()
            .Produces<RestoreValidationResponse>()
            .ProducesStandardProblems();

        group.MapGet("", ListAsync)
            .WithName("ListBackups")
            .WithSummary("Lists registered database backups from BackupCatalog.")
            .RequireAuthorization(manage)
            .Produces<IReadOnlyList<BackupCatalogItemResponse>>()
            .ProducesStandardProblems();
    }

    private static async Task<IResult> BackupAsync(
        [FromBody] BackupDatabaseRequest request,
        IBackupRestoreCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        var result = await coordinator.BackupDatabaseAsync(request.DatabaseName, request.BackupDirectory, request.Password, cancellationToken).ConfigureAwait(false);
        var response = new BackupOperationResponse
        {
            BackupCatalogId = result.BackupCatalogId,
            DatabaseName = result.DatabaseName,
            BackupFilePath = result.BackupFilePath,
            ChecksumSha256 = result.ChecksumSha256,
            FileSizeBytes = result.FileSizeBytes,
            CreatedAtUtc = result.CreatedAtUtc,
            IsVerified = result.IsVerified,
            IsEncrypted = result.IsEncrypted,
            OriginalSizeBytes = result.OriginalSizeBytes
        };
        var location = $"/api/v1/administration/backups";
        return TypedResults.Created(location, response);
    }

    private static async Task<IResult> VerifyAsync(
        [FromBody] VerifyBackupRequest request,
        IBackupRestoreCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        var result = await coordinator.VerifyBackupAsync(request.BackupCatalogId, request.Password, cancellationToken).ConfigureAwait(false);
        var response = new BackupVerificationResponse
        {
            BackupCatalogId = result.BackupCatalogId,
            DatabaseName = result.DatabaseName,
            BackupFilePath = result.BackupFilePath,
            IsReadSuccess = result.IsReadSuccess,
            VerifiedAtUtc = result.VerifiedAtUtc,
            ErrorDetails = result.ErrorDetails
        };
        return TypedResults.Ok(response);
    }

    private static async Task<IResult> TestRestoreAsync(
        [FromBody] TestRestoreRequest request,
        IBackupRestoreCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        var result = await coordinator.TestRestoreInDisposableDbAsync(request.BackupCatalogId, request.Password, cancellationToken).ConfigureAwait(false);
        var response = new RestoreValidationResponse
        {
            BackupCatalogId = result.BackupCatalogId,
            DisposableDatabaseName = result.DisposableDatabaseName,
            RestoreSuccess = result.RestoreSuccess,
            DbccCheckdbPass = result.DbccCheckdbPass,
            TestedAtUtc = result.TestedAtUtc,
            Message = result.Message
        };
        return TypedResults.Ok(response);
    }

    private static async Task<IResult> ListAsync(
        [FromQuery] string? databaseName,
        IBackupRestoreCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        var list = await coordinator.ListBackupsAsync(databaseName, cancellationToken).ConfigureAwait(false);
        var response = list.Select(b => new BackupCatalogItemResponse
        {
            Id = b.Id,
            DatabaseName = b.DatabaseName,
            BackupType = b.BackupType,
            BackupFilePath = b.BackupFilePath,
            ChecksumSha256 = b.ChecksumSha256,
            CreatedAtUtc = b.CreatedAtUtc,
            VerifiedAtUtc = b.VerifiedAtUtc,
            IsCompressed = b.IsCompressed,
            IsEncrypted = b.IsEncrypted
        }).ToList();
        return TypedResults.Ok(response);
    }
}
