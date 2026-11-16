using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.AlAmeenMigration.Mapping;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger;
using PetraERP.Application.GeneralLedger.Posting;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Primitives;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Persistence.Actors;
using PetraERP.Infrastructure.Persistence.Registry;
using PetraERP.Infrastructure.Workspaces;

namespace PetraERP.Infrastructure.AlAmeenMigration;

public enum HistoricalDocumentOutcome
{
    Imported = 1,
    AlreadyImported = 2,
    Blocked = 3,
    SkippedWithReason = 4,
}

public sealed record HistoricalDocumentResult(
    string SourceNumber,
    Guid SourceId,
    HistoricalDocumentOutcome Outcome,
    Guid? TargetJournalId,
    string? Reason);

public sealed record HistoricalMigrationResult(
    int VouchersProcessed,
    int VouchersImported,
    int VouchersSkippedExisting,
    int VouchersBlocked,
    decimal TotalDebits,
    decimal TotalCredits,
    IReadOnlyList<HistoricalDocumentResult> Documents,
    bool Success,
    string Message);

/// <summary>
/// Replays Al-Ameen vouchers into the target through the ordinary journal and posting gate. It never writes a journal
/// row directly, never assigns a posting sequence, never drops an unmappable line, and identifies each document by the
/// source capture plus its source id rather than by description text.
/// </summary>
/// <summary>
/// Source-document claims are recorded in the central global-identity registry, so an import can prove what it already
/// brought in without a description-text match. The registry is the approved central table; a UNIQUE index on the
/// source side would need an owner-approved annual schema change and is tracked as an open item.
/// </summary>
public sealed class AlAmeenHistoricalMigrator(
    IAccountingDbContextFactory contextFactory,
    SystemDbContext systemContext,
    TimeProvider timeProvider)
{
    private const string SourceSystem = "ALAMEEN";
    private const string VoucherEntityType = "ameen.doc.voucher";

    private readonly IAccountingDbContextFactory _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    private readonly SystemDbContext _systemContext = systemContext ?? throw new ArgumentNullException(nameof(systemContext));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async Task<HistoricalMigrationResult> MigrateHistoryAsync(
        SourceSnapshot snapshot,
        IdentityResolution identities,
        Guid companyId,
        Guid branchId,
        Guid fiscalYearId,
        Guid actorUserId,
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(identities);

        if (!identities.IsComplete)
            throw new BusinessRuleException("migration.identity_incomplete", "مواءمة هويات المصدر غير مكتملة؛ لا يُعاد تنفيذ التاريخ.");
        if (identities.CompanyId != companyId || identities.FiscalYearId != fiscalYearId || identities.BranchId != branchId)
            throw new BusinessRuleException("migration.identity_scope_mismatch", "الهويات المبدولة لا تنتمي إلى الشركة والسنة والفرع المطلوب ترحيلهم.");

        var results = new List<HistoricalDocumentResult>();
        var inRange = snapshot.Vouchers
            .Where(v => !fromDate.HasValue || DateOnly.FromDateTime(v.Date) >= fromDate.Value)
            .Where(v => !toDate.HasValue || DateOnly.FromDateTime(v.Date) <= toDate.Value)
            .ToList();

        var baseCode = snapshot.Currencies.FirstOrDefault(c => c.EquivalentRate == 1m)?.Code ?? string.Empty;
        var claimed = await LoadClaimsAsync(companyId, fiscalYearId, cancellationToken).ConfigureAwait(false);
        var existing = claimed.Keys.ToHashSet(StringComparer.Ordinal);

        foreach (var voucher in inRange)
        {
            var sourceId = voucher.Id.ToString("N");
            if (existing.Contains(sourceId))
            {
                results.Add(new HistoricalDocumentResult(voucher.Number, voucher.Id, HistoricalDocumentOutcome.AlreadyImported, null, "مستند مصدر مطبوع سابقاً في هذه القاعدة."));
                continue;
            }

            var classification = Classify(voucher, identities, baseCode);
            if (classification is not null)
            {
                results.Add(new HistoricalDocumentResult(voucher.Number, voucher.Id, HistoricalDocumentOutcome.Blocked, null, classification));
                continue;
            }

            if (dryRun)
            {
                results.Add(new HistoricalDocumentResult(voucher.Number, voucher.Id, HistoricalDocumentOutcome.Imported, null, "معاينة فقط: لم تُكتب أي سجلات."));
                continue;
            }

            var journalId = await ImportAsync(voucher, identities, baseCode, companyId, branchId, fiscalYearId, actorUserId, cancellationToken).ConfigureAwait(false);
            Claim(voucher, snapshot, companyId, fiscalYearId, actorUserId, journalId, claimed);
            existing.Add(sourceId);
            results.Add(new HistoricalDocumentResult(voucher.Number, voucher.Id, HistoricalDocumentOutcome.Imported, journalId, null));
        }

        var imported = results.Count(r => r.Outcome == HistoricalDocumentOutcome.Imported && !dryRun);
        var blocked = results.Count(r => r.Outcome == HistoricalDocumentOutcome.Blocked);
        var debit = inRange.Where(v => results.Any(r => r.SourceId == v.Id && r.Outcome == HistoricalDocumentOutcome.Imported)).Sum(v => v.Lines.Sum(l => l.Debit));
        var credit = inRange.Where(v => results.Any(r => r.SourceId == v.Id && r.Outcome == HistoricalDocumentOutcome.Imported)).Sum(v => v.Lines.Sum(l => l.Credit));

        if (!dryRun && claimed.Count > 0)
            await _systemContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var blockedDetail = string.Join(" ; ", results.Where(r => r.Outcome == HistoricalDocumentOutcome.Blocked).Take(5).Select(r => $"#{r.SourceNumber}: {r.Reason}"));

        return new HistoricalMigrationResult(
            inRange.Count,
            imported,
            results.Count(r => r.Outcome == HistoricalDocumentOutcome.AlreadyImported),
            blocked,
            debit,
            credit,
            results,
            blocked == 0,
            dryRun
                ? $"معاينة: {inRange.Count} سنداً، {results.Count(r => r.Outcome == HistoricalDocumentOutcome.Imported)} صالحة للترحيل، {blocked} محجوبة."
                : $"تم ترحيل {imported} سنداً، وتخطي {results.Count(r => r.Outcome == HistoricalDocumentOutcome.AlreadyImported)} مطبوعة سابقاً، وحجب {blocked} بمبرر مسجل." + (blocked > 0 ? " " + blockedDetail : string.Empty));
    }

    /// <summary>Returns the blocking reason, or null when every line can be mapped and the voucher balances.</summary>
    private static string? Classify(SourceVoucher voucher, IdentityResolution identities, string baseCode)
    {
        if (voucher.Lines.Count == 0)
            return "سند مصدر بلا أسطر.";

        var debit = voucher.Lines.Sum(l => Math.Round(l.Debit, 4, MidpointRounding.ToEven));
        var credit = voucher.Lines.Sum(l => Math.Round(l.Credit, 4, MidpointRounding.ToEven));
        if (Math.Abs(debit - credit) > BalanceTolerance || debit == 0m)
            return $"سند غير متوازن: مدينة {debit} مقابل دائنة {credit}.";

        foreach (var line in voucher.Lines)
        {
            if (!identities.BindingsOf(MigrationEntityKind.Account).ContainsKey(line.AccountId))
                return $"سطر بحساب مصدر {line.AccountId:N} غير مبدول إلى حساب هدف.";
            var currency = line.CurrencyId == Guid.Empty ? voucher.CurrencyId : line.CurrencyId;
            if (!identities.CurrencyCodes.ContainsKey(currency))
                return $"سطر بعملة مصدر {currency:N} غير مبدولة إلى رمز عملة هدف.";
        }

        return Adjust(voucher, identities, baseCode, out var failure) is null ? failure : null;
    }

    private const decimal BalanceTolerance = 0.01m;

    internal sealed record AdjustedLine(Guid SourceAccountId, string Currency, decimal Debit, decimal Credit, decimal Rate, string? Description);

    internal static decimal DerivedBase(AdjustedLine line) => Math.Round((line.Debit + line.Credit) * line.Rate, 4, MidpointRounding.ToEven);

    /// <summary>
    /// Al-Ameen stores the base-currency amount on each line plus the rate; PetraERP stores the transaction amount and derives
    /// the base from it. A foreign line therefore carries base / rate as its transaction amount with the effective rate that
    /// reproduces the source base amount, and any sub-cent rounding residue left on the voucher is absorbed by its largest
    /// base-currency line. A voucher that cannot be balanced within the tolerance is reported as blocked, never forced.
    /// </summary>
    internal static List<AdjustedLine>? Adjust(SourceVoucher voucher, IdentityResolution identities, string baseCode, out string? failure)
    {
        failure = null;
        var lines = new List<AdjustedLine>();
        foreach (var l in voucher.Lines)
        {
            // Al-Ameen keeps unrounded floating-point amounts; the ledger stores four decimals.
            var debitIn = Math.Round(l.Debit, 4, MidpointRounding.ToEven);
            var creditIn = Math.Round(l.Credit, 4, MidpointRounding.ToEven);
            if (debitIn > 0m && creditIn > 0m)
            {
                failure = "A line carries both a debit and a credit.";
                return null;
            }

            var currency = RequireCurrency(identities, l.CurrencyId, voucher.CurrencyId);
            var rate = l.Rate > 0m ? l.Rate : 1m;
            var baseAmount = debitIn + creditIn;
            if (!string.Equals(currency, baseCode, StringComparison.Ordinal) && rate != 1m)
            {
                var foreign = Math.Round(baseAmount / rate, 4, MidpointRounding.ToEven);
                if (foreign > 0m)
                {
                    var effective = Math.Round(baseAmount / foreign, 8, MidpointRounding.ToEven);
                    lines.Add(new AdjustedLine(l.AccountId, currency, debitIn > 0m ? foreign : 0m, creditIn > 0m ? foreign : 0m, effective, l.Description));
                    continue;
                }

                currency = baseCode; // too small to be carried in the foreign currency
            }

            lines.Add(new AdjustedLine(l.AccountId, currency, debitIn, creditIn, 1m, l.Description));
        }

        var residue = lines.Where(x => x.Debit > 0m).Sum(DerivedBase) - lines.Where(x => x.Credit > 0m).Sum(DerivedBase);
        if (residue == 0m)
            return lines;
        if (Math.Abs(residue) > BalanceTolerance)
        {
            failure = $"The voucher does not balance after conversion (difference {residue}).";
            return null;
        }

        var index = -1;
        var best = 0m;
        for (var i = 0; i < lines.Count; i++)
        {
            var candidate = lines[i];
            if (!string.Equals(candidate.Currency, baseCode, StringComparison.Ordinal) || candidate.Rate != 1m)
                continue;
            var amount = candidate.Debit + candidate.Credit;
            var adjusted = candidate.Debit > 0m == residue > 0m ? amount - Math.Abs(residue) : amount + Math.Abs(residue);
            if (adjusted > 0m && amount > best)
            {
                index = i;
                best = amount;
            }
        }

        if (index < 0)
        {
            // A voucher made only of foreign-currency lines has no base line to carry the residue: the largest foreign line
            // takes it through its effective rate (kept to eight places, and only when it reproduces the base exactly).
            var rateIndex = -1;
            var rateValue = 0m;
            var largest = 0m;
            for (var i = 0; i < lines.Count; i++)
            {
                var candidate = lines[i];
                var amount = candidate.Debit + candidate.Credit;
                if (candidate.Rate == 1m || amount <= largest)
                    continue;
                var derived = DerivedBase(candidate);
                var wanted = candidate.Debit > 0m == residue > 0m ? derived - Math.Abs(residue) : derived + Math.Abs(residue);
                var newRate = Math.Round(wanted / amount, 8, MidpointRounding.ToEven);
                if (newRate > 0m && Math.Round(amount * newRate, 4, MidpointRounding.ToEven) == wanted)
                {
                    rateIndex = i;
                    rateValue = newRate;
                    largest = amount;
                }
            }

            if (rateIndex < 0)
            {
                failure = "The conversion residue cannot be absorbed by a base-currency line or a foreign rate.";
                return null;
            }

            lines[rateIndex] = lines[rateIndex] with { Rate = rateValue };
            return lines;
        }

        var target = lines[index];
        var delta = Math.Abs(residue);
        var movesDebit = target.Debit > 0m;
        var newAmount = movesDebit == residue > 0m ? target.Debit + target.Credit - delta : target.Debit + target.Credit + delta;
        lines[index] = target with { Debit = movesDebit ? newAmount : 0m, Credit = movesDebit ? 0m : newAmount };
        return lines;
    }

    private async Task<Guid> ImportAsync(
        SourceVoucher voucher,
        IdentityResolution identities,
        string baseCode,
        Guid companyId,
        Guid branchId,
        Guid fiscalYearId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var scope = JournalEntryScope.Create(companyId, branchId, actorUserId);
        var accountMap = identities.BindingsOf(MigrationEntityKind.Account);
        var adjusted = Adjust(voucher, identities, baseCode, out var adjustFailure)
            ?? throw new BusinessRuleException("migration.voucher_unbalanced", adjustFailure ?? "The voucher cannot be converted.");
        var lines = adjusted.Select(l => new JournalDraftLineInput
        {
            AccountId = accountMap[l.SourceAccountId],
            TransactionCurrencyCode = l.Currency,
            TransactionDebit = l.Debit,
            TransactionCredit = l.Credit,
            RateValue = l.Rate,
            RateSourceCurrencyCode = l.Currency,
            Description = l.Description ?? voucher.Description ?? "Al-Ameen voucher",
        }).ToList();

        // One operation per context, exactly as the desktop and API flows do: an accounting context cannot change the
        // write identity it was bound to, so each lifecycle step gets its own authorized scope.
        JournalDetailsDto draft;
        JournalDetailsDto validated;
        using (var draftContext = await _contextFactory.CreateDbContextAsync(companyId, fiscalYearId, cancellationToken).ConfigureAwait(false))
        {
            draftContext.BindWriteScope(new AccountingWriteScope(companyId, fiscalYearId, actorUserId, AccountingOperation.ManageJournals, branchId));
            var journalService = new SqlJournalEntryService(draftContext, _timeProvider);
            draft = await journalService.CreateDraftAsync(scope, new CreateJournalDraftCommand
            {
                AccountingDate = DateOnly.FromDateTime(voucher.Date),
                Description = "سند تاريخي مستورد " + voucher.Number,
                Lines = lines,
            }, cancellationToken).ConfigureAwait(false);
            validated = await journalService.ValidateDraftAsync(scope, draft.Id, new JournalDraftActionCommand
            {
                ExpectedVersion = ConcurrencyToken.FromBytes(Convert.FromBase64String(draft.VersionBase64)),
                Reason = "تحقق من قيد تاريخي مستورد",
            }, cancellationToken).ConfigureAwait(false);
        }

        JournalDetailsDto approved;
        using (var approveContext = await _contextFactory.CreateDbContextAsync(companyId, fiscalYearId, cancellationToken).ConfigureAwait(false))
        {
            approveContext.BindWriteScope(new AccountingWriteScope(companyId, fiscalYearId, actorUserId, AccountingOperation.ApproveJournal, branchId));
            approved = await new SqlJournalEntryService(approveContext, _timeProvider).ApproveDraftAsync(scope, validated.Id, new JournalDraftActionCommand
            {
                ExpectedVersion = ConcurrencyToken.FromBytes(Convert.FromBase64String(validated.VersionBase64)),
                Reason = "اعتماد قيد تاريخي مستورد",
            }, cancellationToken).ConfigureAwait(false);
        }

        using (var postContext = await _contextFactory.CreateDbContextAsync(companyId, fiscalYearId, cancellationToken).ConfigureAwait(false))
        {
            postContext.BindWriteScope(new AccountingWriteScope(companyId, fiscalYearId, actorUserId, AccountingOperation.PostJournal, branchId));
            await new SqlPostingService(postContext).PostAsync(PostingRequest.Post(
                PostingRequestScope.Create(companyId, branchId, actorUserId),
                ImportKey(voucher.Id, fiscalYearId),
                approved.Id,
                ExpectedJournalVersion.FromBase64(approved.VersionBase64),
                AuditReason.Create("ترحيل قيد تاريخي مستورد من الأمين")), cancellationToken).ConfigureAwait(false);
        }

        return draft.Id;
    }

    private void Claim(
        SourceVoucher voucher,
        SourceSnapshot snapshot,
        Guid companyId,
        Guid fiscalYearId,
        Guid actorUserId,
        Guid targetJournalId,
        Dictionary<string, GlobalEntityMappingRow> claims)
    {
        var now = _timeProvider.GetUtcNow();
        var row = new GlobalEntityMappingRow
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            EntityType = VoucherEntityType,
            GlobalEntityId = voucher.Id,
            LocalEntityId = targetJournalId,
            FiscalYearId = fiscalYearId,
            CreatedAtUtc = now,
        };
        _systemContext.GlobalEntityMappings.Add(row);
        claims[voucher.Id.ToString("N")] = row;
        _ = snapshot;
    }

    private async Task<Dictionary<string, GlobalEntityMappingRow>> LoadClaimsAsync(Guid companyId, Guid fiscalYearId, CancellationToken cancellationToken)
    {
        var rows = await _systemContext.GlobalEntityMappings.AsNoTracking()
            .Where(m => m.CompanyId == companyId && m.EntityType == VoucherEntityType && m.FiscalYearId == fiscalYearId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.ToDictionary(m => m.GlobalEntityId.ToString("N"), StringComparer.Ordinal);
    }

    /// <summary>A deterministic key per source document and year, so a retry replays the same request, not a new posting.</summary>
    private static Guid ImportKey(Guid sourceDocumentId, Guid fiscalYearId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"ALAMEEN|{sourceDocumentId:N}|{fiscalYearId:N}"));
        var bytes = new byte[16];
        Array.Copy(hash, bytes, 16);
        return new Guid(bytes);
    }

    private static string RequireCurrency(IdentityResolution identities, Guid lineCurrencyId, Guid voucherCurrencyId)
    {
        if (identities.CurrencyCodes.TryGetValue(lineCurrencyId == Guid.Empty ? voucherCurrencyId : lineCurrencyId, out var code))
            return code;
        throw new BusinessRuleException("migration.currency_unmapped", "سطر تاريخي بعملة غير مبدولة.");
    }
}
