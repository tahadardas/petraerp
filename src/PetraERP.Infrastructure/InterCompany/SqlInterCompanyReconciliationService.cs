using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.InterCompany;
using PetraERP.Application.Security;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Persistence.Registry;

namespace PetraERP.Infrastructure.InterCompany;

/// <summary>
/// Inter-company links (central master) and their reconciliation. Each company keeps its own database per fiscal year, so the
/// two balances are read from the registered database that covers the date and compared in the link's currency. Reading
/// another company's ledger needs the report permission on every branch of that company (no silent narrowing).
/// </summary>
public sealed class SqlInterCompanyReconciliationService(CentralDataContext central, IAccessEvaluator access, TimeProvider? clock = null) : IInterCompanyReconciliationService
{
    private const int RecentMovementCount = 50;
    internal const int MatchingLineCap = 2000;
    private TimeProvider Clock => clock ?? TimeProvider.System;

    public async Task<IReadOnlyList<InterCompanyLinkResult>> ListLinksAsync(Guid companyId, bool includeInactive, CancellationToken cancellationToken = default)
    {
        var rows = await central.Set<InterCompanyLinkRow>().AsNoTracking().Where(l => l.CompanyId == companyId && (includeInactive || l.IsActive))
            .OrderBy(l => l.AccountCode).ThenBy(l => l.CurrencyCode).ToListAsync(cancellationToken).ConfigureAwait(false);
        var names = await NamesAsync(rows.Select(r => r.CounterpartyCompanyId).Distinct().ToList(), cancellationToken).ConfigureAwait(false);
        return rows.Select(r => Map(r, names)).ToList();
    }

    public async Task<InterCompanyLinkResult> SaveLinkAsync(Guid companyId, Guid userId, SaveInterCompanyLinkCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.CounterpartyCompanyId == Guid.Empty || command.CounterpartyCompanyId == companyId)
            throw new BusinessRuleException("intercompany.counterparty_invalid", "الشركة المقابلة يجب أن تكون شركة أخرى / The counterparty must be another company.", "counterpartyCompanyId");
        var code = Code(command.AccountCode, "accountCode");
        var other = Code(command.CounterpartyAccountCode, "counterpartyAccountCode");
        var currency = command.CurrencyCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(currency) || currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z'))
            throw new BusinessRuleException("intercompany.currency_invalid", "رمز العملة من ثلاثة أحرف / A three-letter currency code is required.", "currencyCode");
        var note = string.IsNullOrWhiteSpace(command.Note) ? null : command.Note.Trim();
        if (note is { Length: > 500 }) throw new BusinessRuleException("intercompany.note_too_long", "الملاحظة حتى 500 محرف / Note is at most 500 characters.", "note");
        if (!await central.Companies.AnyAsync(c => c.Id == command.CounterpartyCompanyId, cancellationToken).ConfigureAwait(false))
            throw new ResourceNotFoundException("Company");

        var now = Clock.GetUtcNow();
        var row = await central.Set<InterCompanyLinkRow>().SingleOrDefaultAsync(l => l.CompanyId == companyId && l.CounterpartyCompanyId == command.CounterpartyCompanyId
            && l.AccountCode == code && l.CurrencyCode == currency, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            row = new InterCompanyLinkRow
            {
                Id = Guid.CreateVersion7(now), CompanyId = companyId, CounterpartyCompanyId = command.CounterpartyCompanyId, AccountCode = code,
                CounterpartyAccountCode = other, CurrencyCode = currency, Note = note, IsActive = true, CreatedByUserId = userId, CreatedAtUtc = now, UpdatedAtUtc = now,
            };
            central.Add(row);
        }
        else
        {
            row.CounterpartyAccountCode = other;
            row.Note = note;
            row.IsActive = true;
            row.UpdatedAtUtc = now;
        }
        await central.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var names = await NamesAsync([row.CounterpartyCompanyId], cancellationToken).ConfigureAwait(false);
        return Map(row, names);
    }

    public async Task<InterCompanyLinkResult> SetActiveAsync(Guid companyId, Guid linkId, bool isActive, CancellationToken cancellationToken = default)
    {
        var row = await central.Set<InterCompanyLinkRow>().SingleOrDefaultAsync(l => l.CompanyId == companyId && l.Id == linkId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("InterCompanyLink");
        if (row.IsActive != isActive)
        {
            row.IsActive = isActive;
            row.UpdatedAtUtc = Clock.GetUtcNow();
            await central.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        var names = await NamesAsync([row.CounterpartyCompanyId], cancellationToken).ConfigureAwait(false);
        return Map(row, names);
    }

    public async Task<InterCompanyReconciliationResult> ReconcileAsync(Guid companyId, Guid userId, Guid linkId, DateOnly asOfDate, CancellationToken cancellationToken = default)
    {
        var link = await central.Set<InterCompanyLinkRow>().AsNoTracking().SingleOrDefaultAsync(l => l.CompanyId == companyId && l.Id == linkId, cancellationToken)
            .ConfigureAwait(false) ?? throw new ResourceNotFoundException("InterCompanyLink");
        if (!link.IsActive) throw new BusinessRuleException("intercompany.link_inactive", "الربط معطّل / The link is inactive.");
        await RequireAllBranchesAsync(userId, link.CompanyId, cancellationToken).ConfigureAwait(false);
        await RequireAllBranchesAsync(userId, link.CounterpartyCompanyId, cancellationToken).ConfigureAwait(false);

        var thisSide = await ReadSideAsync(link.CompanyId, link.AccountCode, link.CurrencyCode, asOfDate, cancellationToken).ConfigureAwait(false);
        var otherSide = await ReadSideAsync(link.CounterpartyCompanyId, link.CounterpartyAccountCode, link.CurrencyCode, asOfDate, cancellationToken).ConfigureAwait(false);
        var difference = thisSide.Balance + otherSide.Balance;
        string? warning = !thisSide.AccountFound || !otherSide.AccountFound ? "intercompany.account_not_found"
            : thisSide.DatabaseName.Length == 0 || otherSide.DatabaseName.Length == 0 ? "intercompany.database_not_found" : null;
        var matching = Match(thisSide.RecentMovements, otherSide.RecentMovements);
        thisSide = thisSide with { RecentMovements = thisSide.RecentMovements.Take(RecentMovementCount).ToList() };
        otherSide = otherSide with { RecentMovements = otherSide.RecentMovements.Take(RecentMovementCount).ToList() };
        return new(link.Id, asOfDate, link.CurrencyCode, thisSide, otherSide, difference, warning is null && difference == 0m, warning, matching);
    }

    /// <summary>One-to-one pairing of equal-and-opposite amounts, preferring the nearest accounting date. Deterministic.</summary>
    internal static InterCompanyMatching Match(IReadOnlyList<InterCompanyMovement> mine, IReadOnlyList<InterCompanyMovement> theirs)
    {
        var pool = new Dictionary<decimal, List<InterCompanyMovement>>();
        foreach (var t in theirs.OrderBy(m => m.Date).ThenBy(m => m.JournalId))
        {
            if (!pool.TryGetValue(t.Amount, out var list)) pool[t.Amount] = list = [];
            list.Add(t);
        }
        var pairs = new List<InterCompanyMatchPair>();
        var unmatchedThis = new List<InterCompanyMovement>();
        foreach (var m in mine.OrderBy(x => x.Date).ThenBy(x => x.JournalId))
        {
            if (m.Amount != 0m && pool.TryGetValue(-m.Amount, out var candidates) && candidates.Count > 0)
            {
                var best = 0;
                var bestGap = long.MaxValue;
                for (var i = 0; i < candidates.Count; i++)
                {
                    var gap = Math.Abs((long)(candidates[i].Date.DayNumber - m.Date.DayNumber));
                    if (gap < bestGap) { bestGap = gap; best = i; }
                }
                pairs.Add(new(m, candidates[best]));
                candidates.RemoveAt(best);
            }
            else unmatchedThis.Add(m);
        }
        var unmatchedTheirs = pool.Values.SelectMany(v => v).OrderBy(x => x.Date).ThenBy(x => x.JournalId).ToList();
        return new(pairs, unmatchedThis, unmatchedTheirs, mine.Count >= MatchingLineCap || theirs.Count >= MatchingLineCap);
    }

    private async Task RequireAllBranchesAsync(Guid userId, Guid companyId, CancellationToken cancellationToken)
    {
        var report = PermissionCatalog.Get(PermissionCatalog.ReportsView);
        var branches = await central.Set<PetraERP.Infrastructure.Organizations.BranchRow>().AsNoTracking().Where(b => b.CompanyId == companyId && b.IsActive)
            .Select(b => b.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (branches.Count == 0) throw new ResourceNotFoundException("Branch");
        foreach (var branch in branches)
            if (await access.EvaluatePermissionAsync(userId, report, companyId, branch, cancellationToken).ConfigureAwait(false) != AccessDecision.Granted)
                throw new BusinessRuleException("intercompany.scope_denied",
                    "المطابقة بين الشركات تتطلب صلاحية التقارير على كل فروع الشركتين / Inter-company reconciliation needs the report permission on every branch of both companies.");
    }

    private async Task<InterCompanySide> ReadSideAsync(Guid companyId, string accountCode, string currency, DateOnly asOf, CancellationToken cancellationToken)
    {
        var database = await central.Set<FiscalDatabaseRegistryRow>().AsNoTracking()
            .Where(d => d.CompanyId == companyId && d.State == "Active" && d.RangeStart <= asOf && d.RangeEnd >= asOf)
            .Select(d => d.DatabaseName).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (database is null) return new(companyId, string.Empty, false, 0m, []);

        var builder = new SqlConnectionStringBuilder(central.Database.GetConnectionString()) { InitialCatalog = database };
        var connection = new SqlConnection(builder.ConnectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var asOfDate = asOf.ToDateTime(TimeOnly.MinValue);
            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                // Identifiers are fixed; every value is a parameter. States 4 and 5 are Posted and Reversed.
                command.CommandText = """
                    SELECT a.Id, COALESCE(SUM(CASE WHEN j.Id IS NULL THEN 0 ELSE l.TransactionDebit - l.TransactionCredit END), 0)
                    FROM ledger.Accounts a
                    LEFT JOIN ledger.JournalLines l ON l.AccountId = a.Id AND l.CompanyId = @company AND l.TransactionCurrencyCode = @currency
                    LEFT JOIN ledger.JournalEntries j ON j.Id = l.JournalId AND j.State IN (4, 5) AND j.AccountingDate <= @asof
                    WHERE a.CompanyId = @company AND a.Code = @code
                    GROUP BY a.Id
                    """;
                command.Parameters.Add(new SqlParameter("@company", companyId));
                command.Parameters.Add(new SqlParameter("@currency", System.Data.SqlDbType.Char, 3) { Value = currency });
                command.Parameters.Add(new SqlParameter("@asof", System.Data.SqlDbType.Date) { Value = asOfDate });
                command.Parameters.Add(new SqlParameter("@code", System.Data.SqlDbType.NVarChar, 64) { Value = accountCode });
                Guid? accountId = null;
                decimal balance = 0m;
                var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        accountId = reader.GetGuid(0);
                        balance = reader.GetDecimal(1);
                    }
                }
                if (accountId is null) return new(companyId, database, false, 0m, []);

                var movements = new List<InterCompanyMovement>();
                var list = connection.CreateCommand();
                await using (list.ConfigureAwait(false))
                {
                    list.CommandText = $"""
                        SELECT TOP ({MatchingLineCap}) j.AccountingDate, j.Id, j.Description, l.TransactionDebit - l.TransactionCredit
                        FROM ledger.JournalLines l
                        JOIN ledger.JournalEntries j ON j.Id = l.JournalId
                        WHERE l.CompanyId = @company AND l.AccountId = @account AND l.TransactionCurrencyCode = @currency AND j.State IN (4, 5) AND j.AccountingDate <= @asof
                        ORDER BY j.AccountingDate DESC, j.Id DESC
                        """;
                    list.Parameters.Add(new SqlParameter("@company", companyId));
                    list.Parameters.Add(new SqlParameter("@account", accountId.Value));
                    list.Parameters.Add(new SqlParameter("@currency", System.Data.SqlDbType.Char, 3) { Value = currency });
                    list.Parameters.Add(new SqlParameter("@asof", System.Data.SqlDbType.Date) { Value = asOfDate });
                    var rows = await list.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    await using (rows.ConfigureAwait(false))
                        while (await rows.ReadAsync(cancellationToken).ConfigureAwait(false))
                            movements.Add(new(DateOnly.FromDateTime(rows.GetDateTime(0)), rows.GetGuid(1), rows.GetString(2), rows.GetDecimal(3)));
                }
                return new(companyId, database, true, balance, movements);
            }
        }
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await central.Companies.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.NameArabic, cancellationToken).ConfigureAwait(false);

    private static InterCompanyLinkResult Map(InterCompanyLinkRow r, IReadOnlyDictionary<Guid, string> names) =>
        new(r.Id, r.CompanyId, r.CounterpartyCompanyId, names.GetValueOrDefault(r.CounterpartyCompanyId, string.Empty), r.AccountCode, r.CounterpartyAccountCode,
            r.CurrencyCode, r.IsActive, r.Note, r.UpdatedAtUtc);

    private static string Code(string? value, string field)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > 64 || text.Any(char.IsControl))
            throw new BusinessRuleException("intercompany.account_code_invalid", "رمز الحساب من 1 إلى 64 محرفاً / An account code is 1 to 64 characters.", field);
        return text;
    }
}
