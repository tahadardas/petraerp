using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Treasury;
using PetraERP.Domain.Patterns;
using PetraERP.Domain.FinancialValues;
using PetraERP.Domain.Primitives;
using PetraERP.Domain.Treasury;
using PetraERP.Infrastructure.Currencies;

namespace PetraERP.Infrastructure.Treasury;

public sealed partial class SqlTreasuryVoucherService
{
    public async Task<VoucherDetailResult> CreateDraftAsync(Guid companyId, Guid branchId, Guid userId, SaveVoucherCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var currencyTransaction = await PetraERP.Infrastructure.Currencies.CurrencyWriteTransaction.BeginAsync(context, companyId, cancellationToken).ConfigureAwait(false);
        await using var currencyTransactionLifetime = currencyTransaction.ConfigureAwait(false);
        await VerifyTreasuryAsync(companyId,branchId,userId,PetraERP.Application.Security.PermissionCatalog.TreasuryVouchersManage,cancellationToken).ConfigureAwait(false);
        if(AllocationInputs(command).Count>0)
            await VerifyTreasuryAsync(companyId,branchId,userId,PetraERP.Application.Security.PermissionCatalog.TreasuryVouchersAllocate,cancellationToken).ConfigureAwait(false);
        var validation = await ValidateAsync(companyId, branchId, command, cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();
        EnforceDateLock(validation.Configuration, command.VoucherDateUtc, now);
        EnforceCutoff(validation.Configuration, command.VoucherDateUtc);
        var row = new VoucherRow
        {
            Id = command.DocumentId ?? Guid.NewGuid(), CompanyId = companyId, BranchId = branchId, Kind = (byte)command.Kind,
            Status = (byte)VoucherStatus.Draft, CreatedByUserId = userId, CreatedAtUtc = now,
        };
        Apply(row, command with { CurrencyCode = validation.Currency, ExchangeRate = validation.Rate }, validation.Prepared, now);
        context.Vouchers.Add(row);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await currencyTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await GetAsync(companyId, branchId, row.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<VoucherDetailResult> UpdateDraftAsync(Guid companyId, Guid branchId, Guid voucherId, SaveVoucherCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var currencyTransaction = await PetraERP.Infrastructure.Currencies.CurrencyWriteTransaction.BeginAsync(context, companyId, cancellationToken).ConfigureAwait(false);
        await using var currencyTransactionLifetime = currencyTransaction.ConfigureAwait(false);
        if (command.ActorUserId != Guid.Empty)
        {
            await VerifyTreasuryAsync(companyId,branchId,command.ActorUserId,PetraERP.Application.Security.PermissionCatalog.TreasuryVouchersManage,cancellationToken).ConfigureAwait(false);
            if(AllocationInputs(command).Count>0)
                await VerifyTreasuryAsync(companyId,branchId,command.ActorUserId,PetraERP.Application.Security.PermissionCatalog.TreasuryVouchersAllocate,cancellationToken).ConfigureAwait(false);
        }
        var expected = command.ExpectedVersion ?? throw new RequestValidationException("treasury.version_required", "ExpectedVersion is required.", "expectedVersion");
        var row = await LoadDraftAsync(companyId, branchId, voucherId, expected, cancellationToken).ConfigureAwait(false);
        if (row.Kind != (byte)command.Kind)
            throw new BusinessRuleException("treasury.kind_immutable", "A voucher cannot change between receipt and payment.", "kind");
        var validation = await ValidateAsync(companyId, branchId, command, cancellationToken).ConfigureAwait(false);
        EnforceDateLock(validation.Configuration, command.VoucherDateUtc, row.CreatedAtUtc);
        // Neither the stored document nor its new date may sit in the frozen period.
        EnforceCutoff(validation.Configuration, row.VoucherDateUtc);
        EnforceCutoff(validation.Configuration, command.VoucherDateUtc);
        context.VoucherAllocationPlans.RemoveRange(row.AllocationPlans); row.AllocationPlans.Clear();
        row.ApprovedBy = null; row.ApprovedAtUtc = null; row.ApprovalReason = null;
        context.VoucherAllocations.RemoveRange(row.Allocations);
        context.VoucherLines.RemoveRange(row.Lines);
        row.Allocations.Clear(); row.Lines.Clear();
        Apply(row, command with { CurrencyCode = validation.Currency, ExchangeRate = validation.Rate }, validation.Prepared, timeProvider.GetUtcNow());
        await SaveVersionedAsync(cancellationToken).ConfigureAwait(false);
        await currencyTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await GetAsync(companyId, branchId, voucherId, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteDraftAsync(Guid companyId, Guid branchId, Guid voucherId, ConcurrencyToken expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedVersion);
        var row = await LoadDraftAsync(companyId, branchId, voucherId, expectedVersion, cancellationToken).ConfigureAwait(false);
        await EnforceStoredCutoffAsync(row, cancellationToken).ConfigureAwait(false);
        context.Vouchers.Remove(row);
        await SaveVersionedAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<VoucherRow> LoadDraftAsync(Guid companyId, Guid branchId, Guid voucherId, ConcurrencyToken expected,
        CancellationToken cancellationToken)
    {
        var row = await context.Vouchers.Include(v => v.Allocations).Include(v => v.Lines).Include(v => v.AllocationPlans).AsSplitQuery()
            .SingleOrDefaultAsync(v => v.Id == voucherId && v.CompanyId == companyId && v.BranchId == branchId, cancellationToken)
            .ConfigureAwait(false) ?? throw new ResourceNotFoundException("Voucher");
        if (!expected.Matches(row.RowVersion)) throw new ConcurrencyConflictException("voucher");
        if (row.Status != (byte)VoucherStatus.Draft || PetraERP.Domain.Documents.DocumentLifecycle.IsImmutable(PetraERP.Domain.Documents.DocumentLifecycle.From((VoucherStatus)row.Status)))
            throw new BusinessRuleException("treasury.voucher_not_draft", "Only a draft voucher can be changed or deleted.");
        context.Entry(row).Property(static r => r.RowVersion).OriginalValue = expected.ToBytes();
        return row;
    }

    private async Task SaveVersionedAsync(CancellationToken cancellationToken)
    {
        try { await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false); }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException("The voucher was changed by another operation. Reload it and try again.", exception);
        }
    }

    private static void Apply(VoucherRow row, SaveVoucherCommand command, IReadOnlyList<PreparedSettlement> prepared, DateTimeOffset now)
    {
        row.PatternRevisionId = command.PatternRevisionId;
        row.PartyId = command.PartyId;
        row.SettlementMethod = (byte)command.SettlementMethod;
        row.VoucherDateUtc = command.VoucherDateUtc.ToUniversalTime();
        // Always the resolved values (see ValidateAsync): the rate actually used is stored and never re-read from the register.
        row.CurrencyCode = command.CurrencyCode!;
        row.ExchangeRate = command.ExchangeRate!.Value;
        row.Amount = command.Amount;
        row.Notes = string.IsNullOrWhiteSpace(command.Notes) ? null : command.Notes.Trim();
        row.SourceNumber = string.IsNullOrWhiteSpace(command.SourceNumber) ? null : command.SourceNumber.Trim();
        row.SourceDate = command.SourceDate;
        row.UpdatedAtUtc = now;
        foreach (var a in AllocationInputs(command))
        {
            var prep = prepared.FirstOrDefault(p => p.Input.InvoiceId == a.InvoiceId);
            var isOpenItem = prep?.Invoice.IsOpenItem ?? false;
            row.AllocationPlans.Add(new VoucherAllocationPlanRow
            {
                Id = Guid.NewGuid(),
                VoucherId = row.Id,
                SalesInvoiceId = !isOpenItem && command.Kind == VoucherKind.Receipt ? a.InvoiceId : null,
                PurchaseInvoiceId = !isOpenItem && command.Kind == VoucherKind.Payment ? a.InvoiceId : null,
                OpenItemId = isOpenItem ? a.InvoiceId : null,
                InvoiceAmount = a.InvoiceAmount,
                SettlementAmount = a.SettlementAmount,
                CrossRate = a.CrossRate
            });
        }
        var order = 0;
        foreach (var l in command.Lines)
            row.Lines.Add(new VoucherLineRow
            {
                Id = Guid.NewGuid(), VoucherId = row.Id, PartyId = l.PartyId, AccountId = l.AccountId, CostCenterId = l.CostCenterId,
                Amount = l.Amount, Description = string.IsNullOrWhiteSpace(l.Description) ? null : l.Description.Trim(), DisplayOrder = order++,
            });
    }

    /// <summary>Server validation, independent of what the client displays; repeated under lock when posting.</summary>
    private async Task<VoucherValidation> ValidateAsync(Guid companyId, Guid branchId, SaveVoucherCommand command, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(command.Kind) || !Enum.IsDefined(command.SettlementMethod))
            throw new RequestValidationException("treasury.kind_invalid", "Voucher kind and settlement method must be valid.", "kind");
        if ((command.Allocations?.Count ?? 0) > MaxDetailRows || (command.Lines?.Count ?? 0) > MaxDetailRows)
            throw new RequestValidationException("treasury.too_many_rows", $"At most {MaxDetailRows} allocations and lines are allowed.", "lines");
        Rule(() => VoucherRules.RequireAmount(command.Amount), "amount");

        var revision = await context.PatternRevisions.AsNoTracking().Include(r => r.Pattern)
            .SingleOrDefaultAsync(r => r.Id == command.PatternRevisionId && r.Pattern!.CompanyId == companyId, cancellationToken).ConfigureAwait(false)
            ?? throw new BusinessRuleException("treasury.pattern_invalid", "The voucher pattern revision does not exist in this company.", "patternRevisionId");
        var expectedKind = command.Kind == VoucherKind.Receipt ? BusinessOperationKind.CashReceipt : BusinessOperationKind.CashPayment;
        if (revision.State != (int)PatternRevisionState.Published || !revision.Pattern!.IsActive || revision.Pattern.OperationKind != (int)expectedKind
            || !await context.PatternBranchGrants.AnyAsync(g => g.PatternId == revision.PatternId && g.BranchId == branchId, cancellationToken).ConfigureAwait(false))
            throw new BusinessRuleException("treasury.pattern_invalid", "The pattern must be a published, active voucher pattern of this kind for this branch.", "patternRevisionId");
        var config = JsonSerializer.Deserialize<PatternConfiguration>(revision.ConfigurationJson) ?? new();
        var settlement = Rule(() => PostingRuleResolver.Resolve(command.SettlementMethod == SettlementMethod.Cash ? "Cash" : "Bank", config).AccountId, "settlementMethod");

        var partyCurrency = command.PartyId is { } currencyPartyId
            ? await context.Parties.AsNoTracking().Where(p => p.Id == currencyPartyId && p.CompanyId == companyId).Select(p => p.DefaultCurrencyCode)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;
        var currency = await CompanyCurrencies.ResolveAsync(context, companyId, "treasury", command.CurrencyCode, command.ExchangeRate,
            DateOnly.FromDateTime(command.VoucherDateUtc.UtcDateTime), partyCurrency, PatternCurrencyDefaults.DefaultCurrency(revision.ConfigurationJson),
            cancellationToken).ConfigureAwait(false);

        var allocations = command.Allocations ?? [];
        var lines = command.Lines ?? [];
        var instructions = AllocationInputs(command);
        if (command.Kind == VoucherKind.Payment && allocations.Count > 0)
            throw SettlementError("payment_allocations", "سند الصرف لا يخصص لفواتير البيع", "A payment voucher cannot allocate sales invoices.");
        if (command.Kind == VoucherKind.Receipt || command.PartyId.HasValue && lines.Count == 0)
        {
            if (lines.Count > 0) throw SettlementError("receipt_lines", "سند الطرف لا يقبل توزيعاً مع تخصيص الفواتير", "A party voucher cannot combine distribution and invoice allocations.");
            var party = await context.Parties.AsNoTracking().SingleOrDefaultAsync(p => p.Id == command.PartyId && p.CompanyId == companyId, cancellationToken).ConfigureAwait(false);
            if (party is null || !party.IsActive || party.IsCashCustomer || (command.Kind == VoucherKind.Receipt ? party.Role is not (1 or 3) : party.Role is not (2 or 3)))
                throw SettlementError("party_invalid", "اختر طرفاً فعالاً بالدور المناسب؛ الزبون النقدي لا يحمل ذمماً", "Choose an active party with the appropriate role; the walk-in cash customer has no receivables.");
            var target = Rule(() => PostingRuleResolver.Resolve(command.Kind == VoucherKind.Receipt ? "Receivable" : "Payable", config,
                partyContractAccountId: command.Kind == VoucherKind.Receipt ? party.ReceivableAccountId : party.PayableAccountId).AccountId, "partyId");
            var prepared = await PrepareAsync(companyId,branchId,command.Kind,party.Id,currency.CurrencyCode,currency.Rate,command.Amount,
                DocumentCurrencyPolicy.ToBase(command.Amount,currency.Rate),command.VoucherDateUtc,instructions,cancellationToken).ConfigureAwait(false);
            return new(currency.BaseCurrencyCode,settlement,target,command.Amount-prepared.Sum(p => p.Input.SettlementAmount),[])
            { Currency=currency.CurrencyCode,Rate=currency.Rate,Configuration=config,Prepared=prepared };
        }
        if (instructions.Count > 0) throw SettlementError("payment_allocations", "حدد المورد في رأس السند وامسح التوزيع لتخصيص الفواتير", "Select the header supplier and clear distribution lines to allocate invoices.");
        if (command.PartyId is { } payee && !await context.Parties.AnyAsync(p => p.Id == payee && p.CompanyId == companyId && p.IsActive, cancellationToken).ConfigureAwait(false))
            throw new BusinessRuleException("treasury.party_invalid", "The payee must be an active party of this company.", "partyId");
        Rule(() => VoucherRules.RequireBalancedLines(command.Amount, lines.Select(l => l.Amount).ToList()), "lines");
        var targets = new List<Guid>();
        var lookup = VoucherPatternRules.Effective(config).Lookup;
        HashSet<Guid>? eligibleAccounts = null;
        if (lookup.RestrictsAccounts && lines.Any(l => l.AccountId.HasValue))
        {
            var chart = await context.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId)
                .Select(a => new { a.Id, a.ParentId }).ToListAsync(cancellationToken).ConfigureAwait(false);
            eligibleAccounts = VoucherPatternRules.EligibleAccounts(lookup, chart.ToDictionary(a => a.Id, a => a.ParentId));
        }
        foreach (var line in lines)
        {
            if (line.PartyId.HasValue == line.AccountId.HasValue)
                throw new BusinessRuleException("treasury.line_target_invalid", "Each payment line targets exactly one supplier or account.", "lines");
            if (VoucherPatternRules.CheckLineFields(config, line.CostCenterId.HasValue, !string.IsNullOrWhiteSpace(line.Description)) is { } fieldViolation)
                throw new BusinessRuleException("treasury." + fieldViolation.Code,
                    $"The pattern's rules for the line field '{fieldViolation.Field}' are not met ({fieldViolation.Code}).", "lines");
            if (line.AccountId is { } restrictedAccount && eligibleAccounts is not null && !eligibleAccounts.Contains(restrictedAccount))
                throw new BusinessRuleException("treasury.account_not_eligible", "The pattern does not allow this account on its lines.", "lines");
            if (line.CostCenterId is { } restrictedCenter && lookup.RestrictsCostCenters && !lookup.EligibleCostCenterIds.Contains(restrictedCenter))
                throw new BusinessRuleException("treasury.cost_center_not_eligible", "The pattern does not allow this cost center on its lines.", "lines");
            if (line.PartyId is { } supplierId)
            {
                var supplier = await context.Parties.AsNoTracking().SingleOrDefaultAsync(p => p.Id == supplierId && p.CompanyId == companyId, cancellationToken).ConfigureAwait(false);
                if (supplier is null || !supplier.IsActive || supplier.Role is not (2 or 3))
                    throw new BusinessRuleException("treasury.supplier_invalid", "Payment lines may only target active suppliers of this company.", "lines");
                targets.Add(Rule(() => PostingRuleResolver.Resolve("Payable", config, partyContractAccountId: supplier.PayableAccountId).AccountId, "lines"));
            }
            else
            {
                var account = await context.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == line.AccountId && a.CompanyId == companyId, cancellationToken).ConfigureAwait(false);
                if (account is null || !account.IsActive || !account.PostingAllowed)
                    throw new BusinessRuleException("treasury.account_invalid", "Payment lines may only target active posting accounts of this company.", "lines");
                targets.Add(account.Id);
            }
            if (line.CostCenterId is { } center && !await context.CostCenters.AnyAsync(c => c.Id == center && c.CompanyId == companyId, cancellationToken).ConfigureAwait(false))
                throw new BusinessRuleException("treasury.cost_center_invalid", "The cost center does not belong to this company.", "lines");
        }
        return new(currency.BaseCurrencyCode, settlement, null, 0m, targets) { Currency = currency.CurrencyCode, Rate = currency.Rate, Configuration = config };
    }

    /// <summary>
    /// A pattern that fixes the date allows only the day the voucher was added. Clients send the local calendar day, so one day either
    /// side absorbs the time zone without letting an arbitrary date through.
    /// </summary>
    private static void EnforceDateLock(PatternConfiguration configuration, DateTimeOffset voucherDate, DateTimeOffset addedAt)
    {
        if (!configuration.EntryPolicy.LockDocumentDate) return;
        if (Math.Abs((voucherDate.UtcDateTime.Date - addedAt.UtcDateTime.Date).Days) > 1)
            throw new BusinessRuleException("treasury.document_date_locked", "The pattern fixes the voucher date to the day it is added.", "voucherDateUtc");
    }

    /// <summary>
    /// A pattern that froze a period ("تاريخ تثبيت السندات") refuses to create, edit, delete, post or reverse a voucher dated on or before
    /// the cut-off day. This is not <see cref="EnforceDateLock"/>, which only fixes a NEW voucher's date to the day it is added.
    /// </summary>
    private static void EnforceCutoff(PatternConfiguration configuration, DateTimeOffset voucherDate)
    {
        if (VoucherPatternRules.IsFrozen(configuration, DateOnly.FromDateTime(voucherDate.UtcDateTime.Date)))
            throw new BusinessRuleException("treasury.voucher_date_cutoff", "The pattern froze vouchers dated on or before its cut-off date.", "voucherDateUtc");
    }

    /// <summary>Same rule for an existing voucher, read through the revision it was saved with.</summary>
    private async Task EnforceStoredCutoffAsync(VoucherRow voucher, CancellationToken cancellationToken)
    {
        var json = await context.PatternRevisions.AsNoTracking().Where(r => r.Id == voucher.PatternRevisionId)
            .Select(r => r.ConfigurationJson).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (json is null) return;
        EnforceCutoff(JsonSerializer.Deserialize<PatternConfiguration>(json) ?? new(), voucher.VoucherDateUtc);
    }

    private static void Rule(Action action, string field) => Rule(() => { action(); return 0; }, field);

    /// <summary>Domain rule violations surface as 422 business-rule problems carrying their stable code.</summary>
    private static T Rule<T>(Func<T> action, string field)
    {
        try { return action(); }
        catch (InvalidOperationException exception) when (RuleViolation.CodeOf(exception) is { } code)
        {
            throw new BusinessRuleException(code, exception.Message, field);
        }
    }

    /// <summary>Accounts resolved by validation. Targets align with payment lines in order.</summary>
    private sealed record VoucherValidation(string BaseCurrency, Guid SettlementAccountId, Guid? ReceivableAccountId, decimal Advance, IReadOnlyList<Guid> LineTargets)
    {
        /// <summary>The resolved voucher currency and the rate actually used.</summary>
        public IReadOnlyList<PreparedSettlement> Prepared { get; init; } = [];
        public string Currency { get; init; } = string.Empty;
        public decimal Rate { get; init; } = 1m;
        public PatternConfiguration Configuration { get; init; } = new();
    }
}
