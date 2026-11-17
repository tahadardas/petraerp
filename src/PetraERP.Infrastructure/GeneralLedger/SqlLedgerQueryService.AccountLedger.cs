using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.GeneralLedger.Reports;

namespace PetraERP.Infrastructure.GeneralLedger;

/// <summary>
/// Account ledger with keyset paging. Order is the fixed key (AccountingDate, PostingSequence, JournalId, LineId); a page is found
/// by seeking past the previous page's last key, so page 1 and page 1,000 cost the same and nothing is loaded that is not shown.
/// The cursor carries the running balance at its row plus the opening and period totals, so later pages run one seek query and
/// no aggregates. The first page fixes a posting-sequence watermark that every later page honours, which makes the whole chain a
/// consistent snapshot even while other users post.
/// </summary>
public sealed partial class SqlLedgerQueryService
{
    private static readonly TimeSpan CursorLifetime = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions CursorJson = new(JsonSerializerDefaults.Web);

    private sealed record LedgerCursorState(
        int Version, long IssuedAtUnixSeconds, string Workspace, Guid ActorUserId, Guid CompanyId, Guid BranchId, bool Consolidated, Guid AccountId, DateOnly? From, DateOnly? To,
        long Watermark, DateOnly LastDate, long LastSequence, Guid LastJournalId, Guid LastLineId,
        decimal Running, decimal Opening, decimal PeriodDebit, decimal PeriodCredit, long TotalCount, int IncludedAccounts);

    public async Task<AccountLedgerDto> GetAccountLedgerAsync(LedgerReportScope scope, AccountLedgerQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);
        EnsureRange(query.From, query.To);
        await EnsureScopeAsync(scope, cancellationToken).ConfigureAwait(false);
        var chart = await context.Accounts.AsNoTracking().Where(a => a.CompanyId == scope.CompanyId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var account = chart.FirstOrDefault(a => a.Id == query.AccountId) ?? throw new ResourceNotFoundException("account");
        var included = Descendants(chart, account.Id).Append(account.Id).ToArray();
        var cursor = query.Cursor is { Length: > 0 } token ? ReadCursor(token, scope, query) : null;

        var posted = ScopedLines(scope).Where(l => PostedStates.Contains(l.Journal!.State) && included.Contains(l.AccountId));
        var watermark = cursor?.Watermark
            ?? await LatestPostingSequenceAsync(scope, cancellationToken).ConfigureAwait(false)
            ?? 0L;
        var snapshot = posted.Where(l => (l.Journal!.PostingSequence ?? 0L) <= watermark);

        var period = snapshot;
        if (query.From is { } start) period = period.Where(l => l.Journal!.AccountingDate >= start);
        if (query.To is { } end) period = period.Where(l => l.Journal!.AccountingDate <= end);

        decimal opening, periodDebit, periodCredit;
        long totalCount;
        if (cursor is null)
        {
            opening = query.From is { } from
                ? await snapshot.Where(l => l.Journal!.AccountingDate < from).SumAsync(l => l.BaseDebit - l.BaseCredit, cancellationToken).ConfigureAwait(false)
                : 0m;
            var totals = await period.GroupBy(static _ => 1)
                .Select(g => new { Count = g.LongCount(), Debit = g.Sum(l => l.BaseDebit), Credit = g.Sum(l => l.BaseCredit) })
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            totalCount = totals?.Count ?? 0;
            periodDebit = totals?.Debit ?? 0m;
            periodCredit = totals?.Credit ?? 0m;
        }
        else
        {
            (opening, periodDebit, periodCredit, totalCount) = (cursor.Opening, cursor.PeriodDebit, cursor.PeriodCredit, cursor.TotalCount);
        }

        var ordered = period.OrderBy(l => l.Journal!.AccountingDate).ThenBy(l => l.Journal!.PostingSequence ?? 0L)
            .ThenBy(l => l.JournalId).ThenBy(l => l.Id);
        decimal running;
        IQueryable<JournalLineRow> pageQuery;
        if (cursor is not null)
        {
            var (lastDate, lastSequence, lastJournal, lastLine) = (cursor.LastDate, cursor.LastSequence, cursor.LastJournalId, cursor.LastLineId);
            // GUID comparison runs in SQL Server, whose uniqueidentifier ordering is the same one ORDER BY uses.
            pageQuery = ordered.Where(l => l.Journal!.AccountingDate > lastDate
                || (l.Journal.AccountingDate == lastDate && ((l.Journal.PostingSequence ?? 0L) > lastSequence
                    || ((l.Journal.PostingSequence ?? 0L) == lastSequence && (l.JournalId.CompareTo(lastJournal) > 0
                        || (l.JournalId == lastJournal && l.Id.CompareTo(lastLine) > 0))))));
            running = cursor.Running;
        }
        else
        {
            // Random access by page number is still supported; it costs one prefix sum, exactly as before.
            var beforePage = query.Page.Skip == 0 ? 0m
                : await ordered.Take(query.Page.Skip).SumAsync(l => l.BaseDebit - l.BaseCredit, cancellationToken).ConfigureAwait(false);
            pageQuery = query.Page.Skip == 0 ? ordered : ordered.Skip(query.Page.Skip);
            running = opening + beforePage;
        }

        var rows = await pageQuery.Take(query.Page.PageSize + 1).Select(l => new
            {
                l.JournalId, l.Id, l.Journal!.BranchId, l.Journal.AccountingDate, l.Journal.PostingSequence,
                JournalDescription = l.Journal.Description, l.Description, l.AccountId,
                CostCenterCode = context.CostCenters.Where(c => c.Id == l.CostCenterId).Select(c => c.Code).FirstOrDefault(),
                l.TransactionCurrencyCode, l.TransactionDebit, l.TransactionCredit, l.BaseDebit, l.BaseCredit, l.Journal.ReversesEntryId,
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var hasMore = rows.Count > query.Page.PageSize;
        if (hasMore) rows.RemoveAt(rows.Count - 1);

        var codes = chart.ToDictionary(a => a.Id, a => a.Code);
        var lines = new List<AccountLedgerLineDto>(rows.Count);
        foreach (var row in rows)
        {
            running += row.BaseDebit - row.BaseCredit;
            lines.Add(new AccountLedgerLineDto
            {
                JournalId = row.JournalId, LineId = row.Id, BranchId = row.BranchId, AccountingDate = row.AccountingDate,
                PostingSequence = row.PostingSequence, JournalDescription = row.JournalDescription, LineDescription = row.Description,
                AccountCode = codes[row.AccountId], CostCenterCode = row.CostCenterCode, TransactionCurrencyCode = row.TransactionCurrencyCode,
                TransactionDebit = row.TransactionDebit, TransactionCredit = row.TransactionCredit, BaseDebit = row.BaseDebit,
                BaseCredit = row.BaseCredit, RunningBalance = running, ReversesEntryId = row.ReversesEntryId,
            });
        }

        string? next = null;
        if (hasMore && cursorSigner is not null && rows.Count > 0)
        {
            var last = rows[^1];
            var state = new LedgerCursorState(2, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), WorkspaceKey(), scope.ActorUserId, scope.CompanyId, scope.BranchId, scope.Consolidated,
                account.Id, query.From, query.To, watermark, last.AccountingDate, last.PostingSequence ?? 0L, last.JournalId, last.Id,
                running, opening, periodDebit, periodCredit, totalCount, included.Length);
            next = cursorSigner.Protect(JsonSerializer.SerializeToUtf8Bytes(state, CursorJson));
        }

        var parents = chart.ToDictionary(a => a.Id, a => a.ParentId);
        return new AccountLedgerDto
        {
            Account = new AccountDto
            {
                Id = account.Id, CompanyId = account.CompanyId, Code = account.Code, NameArabic = account.NameArabic, NameEnglish = account.NameEnglish,
                AccountType = (AccountTypeCode)account.AccountType, PostingAllowed = account.PostingAllowed,
                CostCenterPolicy = (CostCenterPolicyCode)account.CostCenterPolicy, CurrencyMode = (CurrencyModeCode)account.CurrencyMode,
                FixedCurrencyCode = account.FixedCurrencyCode, ParentId = account.ParentId, Level = Depth(parents, account.Id),
                HasChildren = chart.Any(a => a.ParentId == account.Id), HasJournalLines = totalCount > 0 || opening != 0m,
                IsActive = account.IsActive, VersionBase64 = Convert.ToBase64String(account.RowVersion),
            },
            BaseCurrencyCode = await BaseCurrencyAsync(scope.CompanyId, cancellationToken).ConfigureAwait(false),
            OpeningBalance = opening, PeriodDebit = periodDebit, PeriodCredit = periodCredit, ClosingBalance = opening + periodDebit - periodCredit,
            Lines = lines.AsReadOnly(), Page = query.Page.Page, PageSize = query.Page.PageSize, TotalCount = totalCount,
            IncludedAccountCount = cursor?.IncludedAccounts ?? included.Length, NextCursor = next, SnapshotPostingSequence = watermark,
        };
    }

    /// <summary>
    /// The snapshot watermark: the highest posting sequence of a posted journal in scope. It reads the journal headers only (a posted
    /// journal always has lines), so it no longer joins the whole line table of the company just to find one maximum.
    /// </summary>
    private Task<long?> LatestPostingSequenceAsync(LedgerReportScope scope, CancellationToken cancellationToken)
    {
        var journals = context.JournalEntries.AsNoTracking().Where(j => j.CompanyId == scope.CompanyId && PostedStates.Contains(j.State));
        if (!scope.Consolidated) journals = journals.Where(j => j.BranchId == scope.BranchId);
        return journals.MaxAsync(j => j.PostingSequence, cancellationToken);
    }

    /// <summary>Identity of the database this service reads (one fiscal year each); part of every cursor.</summary>
    private string WorkspaceKey()
    {
        var connection = context.Database.GetDbConnection();
        return string.Concat(connection.DataSource, "/", connection.Database);
    }

    /// <summary>
    /// A cursor is only honoured for the exact company, branch, account, range and consolidation it was issued for, must verify
    /// against the server secret, and expires; anything else is a client error (restart from the first page), never a guess.
    /// </summary>
    private LedgerCursorState ReadCursor(string token, LedgerReportScope scope, AccountLedgerQuery query)
    {
        if (cursorSigner is null || !cursorSigner.TryUnprotect(token, out var payload))
            throw new RequestValidationException("ledger.cursor_invalid", "The paging cursor is not valid; reload the ledger from its first page.", "cursor");
        LedgerCursorState? state;
        try
        {
            state = JsonSerializer.Deserialize<LedgerCursorState>(payload, CursorJson);
        }
        catch (JsonException)
        {
            state = null;
        }

        if (state is not { Version: 2 })
            throw new RequestValidationException("ledger.cursor_invalid", "The paging cursor is not valid; reload the ledger from its first page.", "cursor");
        if (DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(state.IssuedAtUnixSeconds) > CursorLifetime)
            throw new RequestValidationException("ledger.cursor_expired", "The paging cursor expired; reload the ledger from its first page.", "cursor");
        // The running balance and totals inside a cursor belong to ONE workspace (fiscal-year database) and ONE actor. Account ids can be
        // identical across years, so company/account equality alone would let a cursor from year A page year B with year A's balances.
        if (!string.Equals(state.Workspace, WorkspaceKey(), StringComparison.Ordinal) || state.ActorUserId != scope.ActorUserId)
            throw new RequestValidationException("ledger.cursor_mismatch", "The paging cursor belongs to a different workspace or user.", "cursor");
        if (state.CompanyId != scope.CompanyId || state.BranchId != scope.BranchId || state.Consolidated != scope.Consolidated
            || state.AccountId != query.AccountId || state.From != query.From || state.To != query.To)
            throw new RequestValidationException("ledger.cursor_mismatch", "The paging cursor belongs to a different ledger request.", "cursor");
        return state;
    }
}
