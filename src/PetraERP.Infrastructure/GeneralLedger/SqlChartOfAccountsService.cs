using System.Data;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Domain.FinancialValues;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Primitives;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.GeneralLedger;

/// <summary>Company-scoped chart of accounts and cost center management. Domain owns hierarchy/history rules.</summary>
public sealed partial class SqlChartOfAccountsService : IChartOfAccountsService
{
    private readonly FinancialDbContext context;
    private readonly TimeProvider time;

    public SqlChartOfAccountsService(FinancialDbContext context, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(time);
        this.context = context;
        this.time = time;
    }

    public async Task<IReadOnlyList<AccountDto>> GetAccountTreeAsync(LedgerMasterScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        // Every customer/supplier owns a sub-account (Al-Ameen): link any party that predates that rule (or was imported)
        // so the chart always lists them under الزبائن / الموردين.
        await new Parties.SqlPartyService(context).SyncMissingPartyAccountsAsync(scope.CompanyId, cancellationToken).ConfigureAwait(false);
        var rows = await context.Accounts.AsNoTracking().Where(a => a.CompanyId == scope.CompanyId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var used = await UsedAccountIdsAsync(scope.CompanyId, cancellationToken).ConfigureAwait(false);
        var children = rows.Where(r => r.ParentId is not null).ToLookup(r => r.ParentId!.Value);
        var result = new List<AccountDto>(rows.Count);
        void Visit(AccountRow row, int level)
        {
            result.Add(Map(row, level, children.Contains(row.Id), used.Contains(row.Id)));
            foreach (var child in children[row.Id].OrderBy(c => c.Code, StringComparer.Ordinal))
            {
                Visit(child, level + 1);
            }
        }

        foreach (var root in rows.Where(r => r.ParentId is null).OrderBy(r => r.Code, StringComparer.Ordinal))
        {
            Visit(root, 0);
        }

        return result.AsReadOnly();
    }

    public async Task<PagedResult<AccountDto>> SearchAccountsAsync(LedgerMasterScope scope, AccountQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);
        var accounts = context.Accounts.AsNoTracking().Where(a => a.CompanyId == scope.CompanyId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var text = query.Search.Trim();
            accounts = accounts.Where(a => a.Code.Contains(text) || a.NameArabic.Contains(text) || (a.NameEnglish != null && a.NameEnglish.Contains(text)));
        }

        if (query.AccountType is { } type) accounts = accounts.Where(a => a.AccountType == (byte)type);
        if (query.IsActive is { } active) accounts = accounts.Where(a => a.IsActive == active);
        if (query.PostingAllowed is { } posting) accounts = accounts.Where(a => a.PostingAllowed == posting);
        if (query.ParentId is { } parent) accounts = accounts.Where(a => a.ParentId == parent);

        var total = await accounts.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var page = await accounts.OrderBy(a => a.Code).Skip(query.Page.Skip).Take(query.Page.PageSize)
            .Select(a => new
            {
                Row = a,
                HasChildren = context.Accounts.Any(c => c.ParentId == a.Id),
                Used = context.JournalLines.Any(l => l.AccountId == a.Id),
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var parents = await ParentMapAsync(scope.CompanyId, cancellationToken).ConfigureAwait(false);
        return new PagedResult<AccountDto>(
            page.Select(p => Map(p.Row, Ancestors(parents, p.Row.ParentId).Count, p.HasChildren, p.Used)).ToList().AsReadOnly(),
            query.Page.Page, query.Page.PageSize, total);
    }

    public async Task<AccountDto> GetAccountAsync(LedgerMasterScope scope, Guid accountId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var row = await context.Accounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accountId && a.CompanyId == scope.CompanyId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("account");
        return await DescribeAsync(row, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AccountDto> CreateAccountAsync(LedgerMasterScope scope, AccountInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(input);
        var company = await LedgerRowMapping.LoadCompanyAsync(context, scope.CompanyId, cancellationToken).ConfigureAwait(false);
        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var parent = input.ParentId is { } parentId ? await LockParentAsync(scope, parentId, cancellationToken).ConfigureAwait(false) : null;
            var account = Account.Create(company, LedgerRowMapping.Code(input.Code), LedgerRowMapping.Name(input.NameArabic, input.NameEnglish),
                (AccountType)input.AccountType, input.PostingAllowed, (CostCenterPolicy)input.CostCenterPolicy, Policy(input), parent);
            var row = new AccountRow
            {
                Id = account.Id,
                CompanyId = account.CompanyId,
                Code = account.Code.Value,
                NameArabic = account.Name.Arabic,
                NameEnglish = account.Name.English,
            };
            Apply(row, account);
            context.Accounts.Add(row);
            AuditAccount(scope, row.Id, AccountAuditActionCode.Created, null, State(row));
            await LedgerRowMapping.SaveAsync(context, LedgerMasterErrorCodes.AccountCodeTaken, "account", cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return await GetAccountAsync(scope, account.Id, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<AccountDto> UpdateAccountAsync(LedgerMasterScope scope, Guid accountId, AccountInput input, ConcurrencyToken expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(expectedVersion);
        var company = await LedgerRowMapping.LoadCompanyAsync(context, scope.CompanyId, cancellationToken).ConfigureAwait(false);
        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var row = await LockAccountAsync(scope, accountId, cancellationToken, tracked: true).ConfigureAwait(false);
            LedgerRowMapping.VerifyVersion(row.RowVersion, expectedVersion, "account");
            var before = State(row);
            var parents = await ParentMapAsync(scope.CompanyId, cancellationToken).ConfigureAwait(false);
            var parent = input.ParentId is { } parentId
                ? await LockParentAsync(scope, parentId, cancellationToken).ConfigureAwait(false)
                : null;
            var revised = LedgerRowMapping.ToDomain(row).Revise(company, LedgerRowMapping.Code(input.Code),
                LedgerRowMapping.Name(input.NameArabic, input.NameEnglish), (AccountType)input.AccountType, input.PostingAllowed,
                (CostCenterPolicy)input.CostCenterPolicy, Policy(input), parent, Ancestors(parents, input.ParentId),
                await UsageAsync(row.Id, cancellationToken).ConfigureAwait(false));
            row.Code = revised.Code.Value;
            row.NameArabic = revised.Name.Arabic;
            row.NameEnglish = revised.Name.English;
            Apply(row, revised);
            var after = State(row);
            if (before != after) AuditAccount(scope, row.Id, AccountAuditActionCode.Updated, before, after);
            await LedgerRowMapping.SaveAsync(context, LedgerMasterErrorCodes.AccountCodeTaken, "account", cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return await GetAccountAsync(scope, accountId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AccountDto> SetAccountActiveAsync(LedgerMasterScope scope, Guid accountId, bool isActive, ConcurrencyToken expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(expectedVersion);
        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var row = await LockAccountAsync(scope, accountId, cancellationToken, tracked: true).ConfigureAwait(false);
            LedgerRowMapping.VerifyVersion(row.RowVersion, expectedVersion, "account");
            var before = State(row);
            var parent = row.ParentId is { } parentId
                ? LedgerRowMapping.ToDomain(await LockAccountAsync(scope, parentId, cancellationToken).ConfigureAwait(false))
                : null;
            row.IsActive = LedgerRowMapping.ToDomain(row).WithActivation(isActive, parent,
                await UsageAsync(row.Id, cancellationToken).ConfigureAwait(false)).IsActive;
            if (before.IsActive != row.IsActive)
                AuditAccount(scope, row.Id, row.IsActive ? AccountAuditActionCode.Activated : AccountAuditActionCode.Deactivated, before, State(row));
            await LedgerRowMapping.SaveAsync(context, LedgerMasterErrorCodes.AccountCodeTaken, "account", cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return await GetAccountAsync(scope, accountId, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAccountAsync(LedgerMasterScope scope, Guid accountId, ConcurrencyToken expectedVersion, string reason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(expectedVersion);
        var auditReason = AuditReason.Create(reason);
        // SERIALIZABLE plus the row lock keeps a concurrent child insert or journal line from slipping in between the
        // usage check and the delete; the journal-line and parent foreign keys remain the last line of defense.
        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var row = await LockAccountAsync(scope, accountId, cancellationToken, tracked: true).ConfigureAwait(false);
            LedgerRowMapping.VerifyVersion(row.RowVersion, expectedVersion, "account");
            Account.EnsureDeletable(await UsageAsync(row.Id, cancellationToken).ConfigureAwait(false));
            AuditAccount(scope, row.Id, AccountAuditActionCode.Deleted, State(row), null, auditReason.Value);
            var profile = await context.AccountProfiles.FirstOrDefaultAsync(p => p.AccountId == row.Id, cancellationToken).ConfigureAwait(false);
            if (profile is not null) context.AccountProfiles.Remove(profile);
            context.Accounts.Remove(row);
            try
            {
                await LedgerRowMapping.SaveAsync(context, RuleViolation.AccountInUse, "account", cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException exception) when (exception.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 547 })
            {
                // A reference the usage check could not see (foreign key) still means "in use": nothing is deleted.
                context.ChangeTracker.Clear();
                throw RuleViolation.Create(RuleViolation.AccountInUse, "The account is still referenced; deactivate it instead.");
            }
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<AccountDto?> NavigateAccountAsync(LedgerMasterScope scope, AccountNavigationCode direction, Guid? fromAccountId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var accounts = context.Accounts.AsNoTracking().Where(a => a.CompanyId == scope.CompanyId);
        string? from = null;
        if (direction is AccountNavigationCode.Previous or AccountNavigationCode.Next)
        {
            if (fromAccountId is not { } id)
                throw new RequestValidationException("request.invalid", "Previous and next need the current account.", "fromAccountId");
            from = await accounts.Where(a => a.Id == id).Select(a => a.Code).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new ResourceNotFoundException("account");
        }

        // The code column uses a binary (ordinal) collation, so SQL ordering and comparison match the tree's ordinal order.
        // string.Compare is translated to a SQL comparison on that column; it never runs as a .NET culture comparison.
#pragma warning disable CA1309
        var row = direction switch
        {
            AccountNavigationCode.First => await accounts.OrderBy(a => a.Code).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false),
            AccountNavigationCode.Last => await accounts.OrderByDescending(a => a.Code).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false),
            AccountNavigationCode.Next => await accounts.Where(a => string.Compare(a.Code, from) > 0).OrderBy(a => a.Code)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false),
            AccountNavigationCode.Previous => await accounts.Where(a => string.Compare(a.Code, from) < 0).OrderByDescending(a => a.Code)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false),
            _ => throw new RequestValidationException("request.invalid", "Unknown navigation direction.", "direction"),
        };
#pragma warning restore CA1309
        return row is null ? null : await DescribeAsync(row, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AccountAuditDto>> GetDeletedAccountsAuditAsync(LedgerMasterScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var rows = await context.AccountAudit.AsNoTracking().Where(a => a.CompanyId == scope.CompanyId && a.Action == AccountAuditRow.Deleted)
            .OrderByDescending(a => a.OccurredAtUtc).ThenByDescending(a => a.EventId).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(a => new AccountAuditDto
        {
            EventId = a.EventId,
            AccountId = a.AccountId,
            Code = a.Code,
            NameArabic = a.NameArabic,
            NameEnglish = a.NameEnglish,
            AccountType = (AccountTypeCode)a.AccountType,
            ParentId = a.ParentId,
            ActorUserId = a.ActorUserId,
            Reason = a.Reason!,
            OccurredAtUtc = a.OccurredAtUtc,
        }).ToList().AsReadOnly();
    }

    private static AccountCurrencyPolicy Policy(AccountInput input) => input.CurrencyMode switch
    {
        CurrencyModeCode.BaseCurrencyOnly when input.FixedCurrencyCode is null => AccountCurrencyPolicy.BaseOnly(),
        CurrencyModeCode.AnyEnabledCurrency when input.FixedCurrencyCode is null => AccountCurrencyPolicy.AnyEnabled(),
        CurrencyModeCode.FixedCurrency when input.FixedCurrencyCode is { Length: 3 } fixedCode && fixedCode.All(char.IsAsciiLetterUpper) =>
            AccountCurrencyPolicy.Fixed(CurrencyCode.Create(fixedCode)),
        _ => throw new RequestValidationException(PetraERP.Domain.Primitives.RuleViolation.CurrencyInvalid,
            "A fixed currency code is required only for the fixed-currency mode.", "fixedCurrencyCode"),
    };

    private static void Apply(AccountRow row, Account account)
    {
        row.AccountType = (byte)account.Type;
        row.PostingAllowed = account.PostingAllowed;
        row.CostCenterPolicy = (byte)account.CostCenterPolicy;
        row.CurrencyMode = (byte)account.CurrencyPolicy.Mode;
        row.FixedCurrencyCode = account.CurrencyPolicy.FixedCurrency?.Value;
        row.ParentId = account.ParentId;
        row.IsActive = account.IsActive;
    }

    private async Task<AccountRow> LockAccountAsync(LedgerMasterScope scope, Guid accountId, CancellationToken cancellationToken, bool tracked = false)
    {
        var query = context.Accounts.FromSqlInterpolated(
            $"SELECT * FROM [ledger].[Accounts] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {accountId} AND [CompanyId] = {scope.CompanyId}");
        if (!tracked) query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
               ?? throw new ResourceNotFoundException("account");
    }

    private async Task<Account> LockParentAsync(LedgerMasterScope scope, Guid parentId, CancellationToken cancellationToken)
    {
        try
        {
            return LedgerRowMapping.ToDomain(await LockAccountAsync(scope, parentId, cancellationToken).ConfigureAwait(false));
        }
        catch (ResourceNotFoundException)
        {
            throw PetraERP.Domain.Primitives.RuleViolation.Create(PetraERP.Domain.Primitives.RuleViolation.AccountParentInvalid,
                "The parent account does not exist in this company.");
        }
    }

    private async Task<AccountUsage> UsageAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var facts = await context.Accounts.AsNoTracking().Where(a => a.Id == accountId).Select(a => new
        {
            Used = context.JournalLines.Any(l => l.AccountId == a.Id),
            Children = context.Accounts.Any(c => c.ParentId == a.Id),
            ActiveChildren = context.Accounts.Any(c => c.ParentId == a.Id && c.IsActive),
        }).SingleAsync(cancellationToken).ConfigureAwait(false);
        return new AccountUsage(facts.Used, facts.Children, facts.ActiveChildren);
    }

    private async Task<HashSet<Guid>> UsedAccountIdsAsync(Guid companyId, CancellationToken cancellationToken) =>
        (await context.JournalLines.AsNoTracking().Where(l => l.CompanyId == companyId).Select(l => l.AccountId).Distinct()
            .ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet();

    private async Task<Dictionary<Guid, Guid?>> ParentMapAsync(Guid companyId, CancellationToken cancellationToken) =>
        await context.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId)
            .ToDictionaryAsync(a => a.Id, a => a.ParentId, cancellationToken).ConfigureAwait(false);

    /// <summary>Ancestors of <paramref name="start"/> including itself, nearest first; stops on a persisted cycle.</summary>
    private static List<Guid> Ancestors(Dictionary<Guid, Guid?> parents, Guid? start)
    {
        var result = new List<Guid>();
        var current = start;
        while (current is { } id && !result.Contains(id))
        {
            result.Add(id);
            current = parents.TryGetValue(id, out var next) ? next : null;
        }

        return result;
    }

    private async Task<AccountDto> DescribeAsync(AccountRow row, CancellationToken cancellationToken)
    {
        var usage = await UsageAsync(row.Id, cancellationToken).ConfigureAwait(false);
        var path = await ReadAccountPathAsync(row.CompanyId, row.Id, cancellationToken).ConfigureAwait(false);
        return Map(row, path.Count - 1, usage.HasChildren, usage.HasJournalLines);
    }

    private static AccountDto Map(AccountRow row, int level, bool hasChildren, bool used) => new()
    {
        Id = row.Id,
        CompanyId = row.CompanyId,
        Code = row.Code,
        NameArabic = row.NameArabic,
        NameEnglish = row.NameEnglish,
        AccountType = (AccountTypeCode)row.AccountType,
        PostingAllowed = row.PostingAllowed,
        CostCenterPolicy = (CostCenterPolicyCode)row.CostCenterPolicy,
        CurrencyMode = (CurrencyModeCode)row.CurrencyMode,
        FixedCurrencyCode = row.FixedCurrencyCode,
        ParentId = row.ParentId,
        Level = level,
        HasChildren = hasChildren,
        HasJournalLines = used,
        IsActive = row.IsActive,
        VersionBase64 = Convert.ToBase64String(row.RowVersion),
    };
}
