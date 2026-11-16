using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.AlAmeenMigration;
using PetraERP.Application.AlAmeenMigration.Mapping;
using PetraERP.Application.AlAmeenMigration.Reconciliation;
using PetraERP.Infrastructure.AlAmeenMigration.Mapping;
using PetraERP.Application.Common;
using PetraERP.Application.Security;
using PetraERP.Application.Workspaces;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Security;
using PetraERP.Infrastructure.Workspaces;

namespace PetraERP.Infrastructure.AlAmeenMigration;

/// <summary>
/// Orchestrates an Al-Ameen migration. The source is captured once and frozen; every later step works from that capture
/// and from identities resolved against it, and the source connection string is never retained after the capture.
/// Writing and activation remain blocked until the owner's independent acceptance evidence exists.
/// </summary>
public sealed class AlAmeenMigrationCoordinator(
    SystemDbContext systemContext,
    IAccountingDbContextFactory contextFactory,
    ICompanyProvisioningService provisioningService,
    IAdministratorConfirmation confirmation,
    TimeProvider timeProvider,
    AlAmeenMigrationOptions? options = null) : IMigrationCoordinator
{
    private readonly AlAmeenMigrationOptions _options = options ?? new AlAmeenMigrationOptions();
    private readonly SystemDbContext _systemContext = systemContext ?? throw new ArgumentNullException(nameof(systemContext));
    private readonly IAccountingDbContextFactory _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    private readonly ICompanyProvisioningService _provisioningService = provisioningService ?? throw new ArgumentNullException(nameof(provisioningService));
    private readonly IAdministratorConfirmation _confirmation = confirmation ?? throw new ArgumentNullException(nameof(confirmation));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async Task<MigrationPreviewResponse> PreviewAsync(
        MigrationPreviewRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        EnsureMigrationReadsEnabled();

        var validation = await AlAmeenSourceSchemaValidator.ValidateSchemaAsync(request.SourceConnectionString, cancellationToken).ConfigureAwait(false);
        var issues = new List<string>();

        if (!validation.IsValid)
        {
            issues.AddRange(validation.MissingTables.Select(t => $"الجدول المطلوب '{t}' غير موجود في قاعدة بيانات المصدر."));
            if (validation.Error is not null)
                issues.Add($"خطأ الاتصال بقاعدة البيانات: {validation.Error}");

            return FailedPreview(issues);
        }

        issues.AddRange(validation.Warnings);

        var cutoff = request.CutoffDate ?? request.TargetFiscalYearEnd;
        if (cutoff < request.TargetFiscalYearStart || cutoff > request.TargetFiscalYearEnd)
            throw new BusinessRuleException("migration.invalid_cutoff", "تاريخ القطع يجب أن يقع داخل السنة المالية المستهدفة.");

        var snapshot = await new AlAmeenSourceSnapshotReader(request.SourceConnectionString, _timeProvider, _options.BaseCurrencyCode)
            .CaptureAsync(cutoff, cancellationToken).ConfigureAwait(false);

        var branchId = await ResolveDefaultBranchAsync(request.TargetCompanyId, cancellationToken).ConfigureAwait(false);
        var identities = await ResolveIdentitiesAsync(snapshot, request.TargetCompanyId, fiscalYearId: Guid.Empty, branchId, persistBindings: false, cancellationToken).ConfigureAwait(false);

        var runId = Guid.NewGuid();
        _systemContext.MigrationRuns.Add(new PetraERP.Infrastructure.Persistence.Registry.MigrationRunRow
        {
            Id = runId,
            CompanyId = request.TargetCompanyId,
            FiscalYearId = null,
            BranchId = branchId,
            Mode = (int)request.Mode,
            State = (int)MigrationState.Validated,
            CutoffDate = cutoff,
            SourceFingerprint = snapshot.SourceFingerprint,
            SourceDatabaseName = SourceDatabaseName(request.SourceConnectionString),
            IdentityIssueCount = identities.Issues.Count,
            CreatedByUserId = userId,
        });
        await _systemContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        CaptureCache.Remember(runId, request, snapshot, identities, userId);

        var debit = snapshot.Vouchers.SelectMany(v => v.Lines).Sum(l => l.Debit);
        var credit = snapshot.Vouchers.SelectMany(v => v.Lines).Sum(l => l.Credit);
        var receivable = PartyNet(snapshot, identities, customer: true);
        var payable = PartyNet(snapshot, identities, customer: false);

        return new MigrationPreviewResponse(
            runId,
            MigrationState.Validated,
            snapshot.Accounts.Count,
            snapshot.Parties.Count,
            snapshot.Items.Count,
            snapshot.Warehouses.Count,
            snapshot.Currencies.Count,
            Money(debit),
            Money(credit),
            snapshot.Parties.Count(p => p.IsCustomer),
            Money(receivable),
            snapshot.Parties.Count(p => p.IsSupplier),
            Money(payable),
            snapshot.Stock.Count,
            Money(snapshot.Stock.Sum(s => s.TotalValue)),
            issues);
    }

    public async Task<MigrationExecuteResponse> ExecuteAsync(
        MigrationExecuteRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        EnsureMigrationWritesEnabled();

        if (string.IsNullOrWhiteSpace(request.AdministratorPassword))
            throw new BusinessRuleException("migration.password_required", "كلمة مرور المسؤول مطلوبة لتنفيذ الترحيل.");

        await _confirmation.ConfirmAsync(userId, request.AdministratorPassword, cancellationToken).ConfigureAwait(false);
        var stored = await RequireRunRowAsync(request.MigrationRunId, userId, cancellationToken).ConfigureAwait(false);
        if (stored.State != (int)MigrationState.Validated)
            throw new BusinessRuleException("migration.state_invalid", $"حالة الجلسة {stored.State} لا تسمح بالتنفيذ؛ المطلوب جلسة معاينة مُتحقق منها.");
        var capture = CaptureCache.Require(request.MigrationRunId, userId);
        var request2 = capture.Request;

        var company = await _systemContext.CompanyDatabases.FirstOrDefaultAsync(c => c.CompanyId == request2.TargetCompanyId, cancellationToken).ConfigureAwait(false)
            ?? throw new BusinessRuleException("migration.company_not_found", "الشركة المحددة للترحيل غير موجودة.");

        var provisionResult = await _provisioningService.ProvisionFiscalYearDatabaseAsync(
            new ProvisionFiscalYearInput(
                request2.TargetCompanyId,
                request2.TargetFiscalYearStart.Year,
                request2.TargetFiscalYearCode,
                request2.TargetFiscalYearStart,
                request2.TargetFiscalYearEnd,
                CopyChartOfAccounts: false,
                CopyWarehouses: false,
                CopyPatterns: false),
            userId,
            cancellationToken).ConfigureAwait(false);

        if (!provisionResult.Succeeded)
            throw new BusinessRuleException("migration.provision_failed", $"فشل تهيئة قاعدة بيانات السنة المالية: {provisionResult.Error}");

        var fiscalYearId = provisionResult.FiscalYearId;

        // Master data first, then identities against the rows that now exist, then the financial effect.
        await new AlAmeenMasterDataProvisioner(_contextFactory, _timeProvider).ProvisionAsync(
            capture.Snapshot, request2.TargetCompanyId, fiscalYearId, stored.BranchId!.Value, userId, cancellationToken).ConfigureAwait(false);

        var identities = await ResolveIdentitiesAsync(
            capture.Snapshot, request2.TargetCompanyId, fiscalYearId, stored.BranchId!.Value, persistBindings: true, cancellationToken).ConfigureAwait(false);

        stored.State = (int)MigrationState.ImportedToIsolatedTarget;
        stored.FiscalYearId = fiscalYearId;
        stored.TargetDatabaseName = provisionResult.DatabaseName;
        stored.IdentityIssueCount = identities.Issues.Count;
        await _systemContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var cutover = new AlAmeenFastCutoverService(_contextFactory, _timeProvider);
        var result = await cutover.ExecuteAsync(
            capture.Snapshot, identities, request2.TargetCompanyId, stored.BranchId!.Value, fiscalYearId, userId, request2.TargetBaseCurrencyCode,
            includeAccountBalances: request2.Mode != MigrationMode.FullHistorical, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (request2.Mode == MigrationMode.FullHistorical)
        {
            // Replay is journal-only for now: Al-Ameen invoices, receipts and settlements are not yet read per document,
            // so a historical run is reported as partial and can never be activated as if it were a full history.
            var history = await new AlAmeenHistoricalMigrator(_contextFactory, _systemContext, _timeProvider).MigrateHistoryAsync(
                capture.Snapshot, identities, request2.TargetCompanyId, stored.BranchId!.Value, fiscalYearId, userId,
                dryRun: false, cancellationToken: cancellationToken).ConfigureAwait(false);
            result = result with { Message = result.Message + " " + history.Message, Success = result.Success && history.Success };
        }

        var reconciliation = await ReconcileInternalAsync(capture.Snapshot, identities, stored.BranchId!.Value, stored.CutoffDate,
            request2.Mode, request.MigrationRunId, request2.TargetCompanyId, fiscalYearId, cancellationToken).ConfigureAwait(false);

        RecordReconciliation(stored, reconciliation);
        await _systemContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new MigrationExecuteResponse(
            request.MigrationRunId,
            MigrationState.AwaitingApproval,
            provisionResult.DatabaseName,
            fiscalYearId,
            _timeProvider.GetUtcNow(),
            reconciliation.Passed,
            result.Success,
            result.Message);
    }

    public async Task<MigrationReconciliationResponse> ReconcileAsync(
        Guid migrationRunId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        EnsureMigrationReadsEnabled();

        var stored = await RequireRunRowAsync(migrationRunId, userId, cancellationToken).ConfigureAwait(false);
        if (stored.FiscalYearId is not Guid fiscalYearId || stored.BranchId is not Guid rowBranch)
            throw new BusinessRuleException("migration.not_executed", "لم يُنفَّذ الترحيل لهذه الجلسة بعد؛ لا توجد بيانات هدف للمطابقة.");

        var capture = CaptureCache.Require(migrationRunId, userId);
        var identities = await ResolveIdentitiesAsync(capture.Snapshot, stored.CompanyId, fiscalYearId, rowBranch, persistBindings: false, cancellationToken).ConfigureAwait(false);
        var reconciliation = await ReconcileInternalAsync(capture.Snapshot, identities, rowBranch, stored.CutoffDate,
            (MigrationMode)stored.Mode, migrationRunId, stored.CompanyId, fiscalYearId, cancellationToken).ConfigureAwait(false);

        RecordReconciliation(stored, reconciliation);
        await _systemContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return reconciliation;
    }

    public async Task<MigrationActivationResponse> ActivateAsync(
        MigrationActivationRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        EnsureMigrationWritesEnabled();

        if (string.IsNullOrWhiteSpace(request.AdministratorPassword))
            throw new BusinessRuleException("migration.password_required", "كلمة مرور المسؤول مطلوبة لاعتماد الترحيل وتنشيط بيئة العمل.");

        await _confirmation.ConfirmAsync(userId, request.AdministratorPassword, cancellationToken).ConfigureAwait(false);
        var stored = await RequireRunRowAsync(request.MigrationRunId, userId, cancellationToken).ConfigureAwait(false);

        // Activation is a financial decision. It is proved against reconciliation that was persisted for this same run,
        // with the same captured source fingerprint - not against an in-memory object a caller could have replaced.
        if (stored.State != (int)MigrationState.AwaitingApproval)
            throw new BusinessRuleException("migration.state_invalid", "التنشيط يتطلب جلسة منفذة ومطابَقة تنتظر الاعتماد.");
        if (stored.ReconciliationPassed is not true || stored.FiscalYearId is not Guid runFiscalYearId)
            throw new BusinessRuleException("migration.reconciliation_failed", "لا يوجد إقفال مطابقة ناجح محفوظ لهذه الجلسة؛ التنشيط مرفوض.");
        var capture = CaptureCache.Require(request.MigrationRunId, userId);
        if (!string.Equals(stored.SourceFingerprint, capture.Snapshot.SourceFingerprint, StringComparison.Ordinal))
            throw new BusinessRuleException("migration.fingerprint_mismatch", "بصمة اللقطة الحالية لا تطابق البصمة المطابَقة؛ أعد المطابقة قبل التنشيط.");

        var fiscalRecord = await _systemContext.FiscalDatabases
            .FirstOrDefaultAsync(f => f.CompanyId == stored.CompanyId && f.FiscalYearId == runFiscalYearId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new BusinessRuleException("migration.fiscal_not_found", "سجل السنة المالية غير موجود في السجل المركزي.");

        stored.State = (int)MigrationState.Activated;
        if (!string.Equals(fiscalRecord.State, "Active", StringComparison.Ordinal))
            fiscalRecord.State = "Active";
        await _systemContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new MigrationActivationResponse(
            request.MigrationRunId,
            stored.CompanyId,
            runFiscalYearId,
            _timeProvider.GetUtcNow(),
            true,
            "تم تنشيط السنة المالية بعد مطابقة محفوظة بلا فروق.");
    }

    private static void RecordReconciliation(PetraERP.Infrastructure.Persistence.Registry.MigrationRunRow stored, MigrationReconciliationResponse reconciliation)
    {
        stored.State = (int)MigrationState.AwaitingApproval;
        stored.ReconciledAtUtc = reconciliation.ReconciledAtUtc;
        stored.ReconciliationPassed = reconciliation.Passed;
        stored.ReconciliationJson = string.Join(
            ";",
            reconciliation.Checks.Select(c => $"{c.CheckName}={(c.Passed ? "PASS" : "FAIL")} س={c.SourceValue} هـ={c.TargetValue} ف={c.Difference}"));
        if (stored.ReconciliationJson.Length > 4000)
            stored.ReconciliationJson = stored.ReconciliationJson[..4000];
    }

    private async Task<PetraERP.Infrastructure.Persistence.Registry.MigrationRunRow> RequireRunRowAsync(
        Guid runId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var stored = await _systemContext.MigrationRuns.FirstOrDefaultAsync(r => r.Id == runId, cancellationToken).ConfigureAwait(false)
            ?? throw new BusinessRuleException("migration.run_not_found", "جلسة الترحيل غير موجودة في السجل المركزي؛ أعد المعاينة.");
        if (stored.CreatedByUserId != userId)
            throw new BusinessRuleException("migration.run_forbidden", "جلسة الترحيل تخص مستخدماً آخر.");
        return stored;
    }

    private async Task<MigrationReconciliationResponse> ReconcileInternalAsync(
        SourceSnapshot snapshot,
        IdentityResolution identities,
        Guid branchId,
        DateOnly cutoffDate,
        MigrationMode mode,
        Guid runId,
        Guid companyId,
        Guid fiscalYearId,
        CancellationToken cancellationToken)
    {
        var scoped = identities with { CompanyId = companyId, FiscalYearId = fiscalYearId, BranchId = branchId };
        return await new AlAmeenReconciliationRunner(_contextFactory, _timeProvider).ReconcileAsync(
            snapshot, scoped, branchId, cutoffDate,
            mode == MigrationMode.FullHistorical ? AccountComparisonMode.FullHistory : AccountComparisonMode.Cutover,
            runId, cancellationToken).ConfigureAwait(false);
    }

    private static string SourceDatabaseName(string connectionString)
    {
        try
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
            return string.IsNullOrWhiteSpace(builder.InitialCatalog) ? "unknown" : builder.InitialCatalog;
        }
        catch (ArgumentException)
        {
            return "invalid";
        }
    }

    private async Task<IdentityResolution> ResolveIdentitiesAsync(
        SourceSnapshot snapshot,
        Guid companyId,
        Guid fiscalYearId,
        Guid branchId,
        bool persistBindings,
        CancellationToken cancellationToken)
    {
        var yearId = fiscalYearId == Guid.Empty
            ? await ResolveCurrentFiscalYearAsync(companyId, cancellationToken).ConfigureAwait(false)
            : fiscalYearId;
        return await new AlAmeenIdentityResolver(_contextFactory, _systemContext, _timeProvider)
            .ResolveAsync(snapshot, companyId, yearId, branchId, persistBindings, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Guid> ResolveCurrentFiscalYearAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var record = await _systemContext.FiscalDatabases
            .Where(f => f.CompanyId == companyId)
            .OrderByDescending(f => f.Id)
            .Select(f => f.FiscalYearId)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (record == Guid.Empty)
            throw new BusinessRuleException("migration.fiscal_not_found", "لا توجد سنة مالية مسجلة للشركة؛ نفّذ المعاينة بعد تهيئة السنة.");
        return record;
    }

    private async Task<Guid> ResolveDefaultBranchAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var record = await _systemContext.Branches
            .Where(b => b.CompanyId == companyId)
            .OrderBy(b => b.Id)
            .Select(b => b.Id)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (record == Guid.Empty)
            throw new BusinessRuleException("migration.branch_required", "الشركة بلا فرع مسجل؛ لا يمكن إسناد أرصدة مستوردة.");
        return record;
    }

    private static decimal PartyNet(SourceSnapshot snapshot, IdentityResolution identities, bool customer)
    {
        if (!identities.IsComplete)
            return 0m;
        try
        {
            var balances = AlAmeenSourceBalanceAdapter.FromSnapshot(snapshot, identities);
            var rows = customer ? balances.Receivables : balances.Payables;
            return rows.Sum(r => r.Outstanding);
        }
        catch (BusinessRuleException)
        {
            return 0m;
        }
    }

    private static string Money(decimal value) => value.ToString("F4", CultureInfo.InvariantCulture);

    private static MigrationPreviewResponse FailedPreview(IReadOnlyList<string> issues) => new(
        Guid.NewGuid(),
        MigrationState.FailedRecoverable,
        0, 0, 0, 0, 0,
        Money(0m),
        Money(0m),
        0,
        Money(0m),
        0,
        Money(0m),
        0,
        Money(0m),
        issues);

    private void EnsureMigrationWritesEnabled()
    {
        if (_options.WritesEnabled)
            return;

        throw new BusinessRuleException(
            "migration.write_blocked",
            "تنفيذ استيراد الأمين وتنشيطه متوقفان لحماية البيانات حتى اكتمال الإصلاحات والمطابقة المستقلة واختبارات القبول.");
    }

    private void EnsureMigrationReadsEnabled()
    {
        if (_options.ReadsEnabled)
            return;

        throw new BusinessRuleException(
            "migration.read_blocked",
            "معاينة استيراد الأمين ومطابقته متوقفتان حتى توفير مصدر ثابت للقراءة فقط ومقارنات مالية مستقلة موثوقة.");
    }
}
