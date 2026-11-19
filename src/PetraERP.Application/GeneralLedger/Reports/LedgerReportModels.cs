using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;

namespace PetraERP.Application.GeneralLedger.Reports;

/// <summary>
/// Report scope. <see cref="BranchId"/> is the authorized working branch from the route. When
/// <see cref="Consolidated"/> is set, all branches of the company are included and the service verifies that the
/// actor holds a scope grant for every branch of the company; otherwise only the working branch is read.
/// </summary>
public sealed record LedgerReportScope
{
    private LedgerReportScope(Guid companyId, Guid branchId, Guid actorUserId, bool consolidated)
    {
        CompanyId = companyId;
        BranchId = branchId;
        ActorUserId = actorUserId;
        Consolidated = consolidated;
    }

    public Guid CompanyId { get; }
    public Guid BranchId { get; }
    public Guid ActorUserId { get; }
    public bool Consolidated { get; }

    public static LedgerReportScope Create(Guid companyId, Guid branchId, Guid actorUserId, bool consolidated)
    {
        if (companyId == Guid.Empty || branchId == Guid.Empty || actorUserId == Guid.Empty)
        {
            throw new ArgumentException("Company, branch and actor are required.");
        }

        return new LedgerReportScope(companyId, branchId, actorUserId, consolidated);
    }
}

/// <summary>The actor lacks a grant needed for the requested report scope (HTTP 403).</summary>
public sealed class ReportScopeDeniedException : Exception
{
    /// <summary>Canonical permission code that was missing; null when the denial is about branch scope rather than a named grant.</summary>
    public string? Permission { get; }

    public ReportScopeDeniedException()
        : base("Consolidated reports require a scope grant for every branch of the company.")
    {
    }

    public ReportScopeDeniedException(string message)
        : base(message)
    {
    }

    public ReportScopeDeniedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ReportScopeDeniedException(string permission, string message)
        : base(message) => Permission = permission;
}

public sealed record CurrencyDto(string Code, string NameArabic, string? NameEnglish);

public sealed record CompanyCurrencyContextDto(Guid CompanyId, CurrencyDto BaseCurrency, IReadOnlyList<CurrencyDto> AdditionalCurrencies);

public sealed record GeneralJournalQuery(DateOnly? From, DateOnly? To, Guid? AccountId, Guid? CostCenterId, IReadOnlyList<byte>? States,
    string? Search, PageRequest Page);

public sealed record GeneralJournalLineDto
{
    public required Guid JournalId { get; init; }
    public required Guid LineId { get; init; }
    public required Guid BranchId { get; init; }
    public required DateOnly AccountingDate { get; init; }
    public long? PostingSequence { get; init; }
    public required byte State { get; init; }
    public required string JournalDescription { get; init; }
    public required string LineDescription { get; init; }
    public required Guid AccountId { get; init; }
    public required string AccountCode { get; init; }
    public required string AccountNameArabic { get; init; }
    public string? AccountNameEnglish { get; init; }
    public Guid? CostCenterId { get; init; }
    public string? CostCenterCode { get; init; }
    public required string TransactionCurrencyCode { get; init; }
    public required decimal TransactionDebit { get; init; }
    public required decimal TransactionCredit { get; init; }
    public required decimal BaseDebit { get; init; }
    public required decimal BaseCredit { get; init; }
    public Guid? ReversesEntryId { get; init; }
}

public sealed record GeneralJournalDto(IReadOnlyList<GeneralJournalLineDto> Lines, int Page, int PageSize, long TotalCount,
    decimal TotalBaseDebit, decimal TotalBaseCredit, string BaseCurrencyCode);

/// <summary>
/// <paramref name="Cursor"/> (from <see cref="AccountLedgerDto.NextCursor"/>) selects keyset paging: the next page is found by seeking
/// past the last row of the previous one, so every page costs the same however deep it is. Without a cursor the first page is
/// returned (or an offset page when <c>Page</c> &gt; 1, kept for random access).
/// </summary>
public sealed record AccountLedgerQuery(Guid AccountId, DateOnly? From, DateOnly? To, PageRequest Page, string? Cursor = null)
{
    /// <summary>Optional document-source restriction (see <see cref="TrialBalanceSources"/>); null means every source.</summary>
    public IReadOnlyList<string>? Sources { get; init; }
}

public sealed record AccountLedgerLineDto
{
    public required Guid JournalId { get; init; }
    public required Guid LineId { get; init; }
    public required Guid BranchId { get; init; }
    public required DateOnly AccountingDate { get; init; }
    public long? PostingSequence { get; init; }
    public required string JournalDescription { get; init; }
    public required string LineDescription { get; init; }
    public required string AccountCode { get; init; }
    public string? CostCenterCode { get; init; }
    public required string TransactionCurrencyCode { get; init; }
    public required decimal TransactionDebit { get; init; }
    public required decimal TransactionCredit { get; init; }
    public required decimal BaseDebit { get; init; }
    public required decimal BaseCredit { get; init; }
    /// <summary>Opening balance plus every posted line up to and including this one, debit positive.</summary>
    public required decimal RunningBalance { get; init; }
    public Guid? ReversesEntryId { get; init; }
}

public sealed record AccountLedgerDto
{
    public required AccountDto Account { get; init; }
    public required string BaseCurrencyCode { get; init; }
    public required decimal OpeningBalance { get; init; }
    public required decimal PeriodDebit { get; init; }
    public required decimal PeriodCredit { get; init; }
    public required decimal ClosingBalance { get; init; }
    public required IReadOnlyList<AccountLedgerLineDto> Lines { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required long TotalCount { get; init; }
    /// <summary>Accounts whose lines are included: the account itself, or every posting descendant of a control account.</summary>
    public required int IncludedAccountCount { get; init; }

    /// <summary>Signed, expiring token for the page that follows this one; null on the last page.</summary>
    public string? NextCursor { get; init; }

    /// <summary>
    /// Highest posting sequence included. The first page fixes it and every later page of the same cursor chain honours it, so
    /// entries posted while the user is paging neither shift rows between pages nor change the opening, totals or balances.
    /// </summary>
    public long SnapshotPostingSequence { get; init; }
}

/// <summary>Signs and verifies the opaque paging tokens; implemented with a server-side secret.</summary>
public interface ILedgerCursorSigner
{
    string Protect(ReadOnlySpan<byte> payload);

    bool TryUnprotect(string token, out byte[] payload);
}

public sealed record TrialBalanceQuery(DateOnly? From, DateOnly To, Guid? PeriodId, AccountTypeCode? AccountType, bool IncludeZeroRows, int? MaximumLevel)
{
    /// <summary>Al-Ameen report-request parameters; null is the plain trial balance.</summary>
    public TrialBalanceOptions? Options { get; init; }
}

/// <summary>Which accounts the trial balance lists (totals always come from posting accounts).</summary>
public enum TrialBalanceAccountScope
{
    All = 0,
    /// <summary>Only accounts that accept postings (الحسابات الفرعية).</summary>
    PostingOnly = 1,
    /// <summary>Only control accounts (الحسابات الرئيسية).</summary>
    ParentsOnly = 2,
}

/// <summary>Where a posted journal came from; used to restrict a trial balance to some document sources.</summary>
public static class TrialBalanceSources
{
    public const string Journal = "JOURNAL";
    public const string SalesInvoice = "SALES_INVOICE";
    public const string PurchaseInvoice = "PURCHASE_INVOICE";
    public const string Voucher = "VOUCHER";
    public static readonly IReadOnlyList<string> All = [Journal, SalesInvoice, PurchaseInvoice, Voucher];
}

/// <summary>Optional trial-balance parameters (Al-Ameen "ميزان المراجعة" request window).</summary>
public sealed record TrialBalanceOptions
{
    /// <summary>Restrict to this account and every account under it.</summary>
    public Guid? AccountId { get; init; }
    public Guid? CostCenterId { get; init; }
    public TrialBalanceAccountScope AccountScope { get; init; }
    /// <summary>Posting numbers of the journals to include (inclusive range).</summary>
    public long? FromJournalNumber { get; init; }
    public long? ToJournalNumber { get; init; }
    /// <summary>Document sources to include; null or all four means no restriction.</summary>
    public IReadOnlyList<string>? Sources { get; init; }
    /// <summary>Report in another currency: every amount is divided by <see cref="DisplayRate"/> (units of base currency per unit).</summary>
    public string? DisplayCurrencyCode { get; init; }
    public decimal? DisplayRate { get; init; }
    /// <summary>Add the date of the last debit and last credit movement of each account.</summary>
    public bool ShowLastMovement { get; init; }
}

public sealed record TrialBalanceRowDto
{
    public required Guid AccountId { get; init; }
    public Guid? ParentId { get; init; }
    public required string Code { get; init; }
    public required string NameArabic { get; init; }
    public string? NameEnglish { get; init; }
    public required AccountTypeCode AccountType { get; init; }
    public required bool PostingAllowed { get; init; }
    public required int Level { get; init; }
    public required decimal OpeningDebit { get; init; }
    public required decimal OpeningCredit { get; init; }
    public required decimal PeriodDebit { get; init; }
    public required decimal PeriodCredit { get; init; }
    public required decimal EndingDebit { get; init; }
    public required decimal EndingCredit { get; init; }
    public DateOnly? LastDebitDate { get; init; }
    public DateOnly? LastCreditDate { get; init; }
}

public sealed record TrialBalanceDto
{
    public required DateOnly? From { get; init; }
    public required DateOnly To { get; init; }
    public required string BaseCurrencyCode { get; init; }
    public required IReadOnlyList<TrialBalanceRowDto> Rows { get; init; }
    /// <summary>Totals over posting (leaf) accounts only, so parents never double count.</summary>
    public required decimal TotalOpeningDebit { get; init; }
    public required decimal TotalOpeningCredit { get; init; }
    public required decimal TotalPeriodDebit { get; init; }
    public required decimal TotalPeriodCredit { get; init; }
    public required decimal TotalEndingDebit { get; init; }
    public required decimal TotalEndingCredit { get; init; }
    public required bool IsBalanced { get; init; }
    /// <summary>Currency the amounts are shown in; the base currency (rate 1) unless a display currency was requested.</summary>
    public string? DisplayCurrencyCode { get; init; }
    public decimal DisplayRate { get; init; } = 1m;
}

/// <summary>Server-authoritative ledger read models. Only Posted and Reversed journals (both posted) are reported.</summary>
public interface ILedgerQueryService
{
    Task<CompanyCurrencyContextDto> GetCurrencyContextAsync(Guid companyId, CancellationToken cancellationToken);
    Task<GeneralJournalDto> GetGeneralJournalAsync(LedgerReportScope scope, GeneralJournalQuery query, CancellationToken cancellationToken);
    Task<AccountLedgerDto> GetAccountLedgerAsync(LedgerReportScope scope, AccountLedgerQuery query, CancellationToken cancellationToken);
    Task<TrialBalanceDto> GetTrialBalanceAsync(LedgerReportScope scope, TrialBalanceQuery query, CancellationToken cancellationToken);
}
