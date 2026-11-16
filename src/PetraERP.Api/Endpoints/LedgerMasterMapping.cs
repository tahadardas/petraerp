using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.Calendars;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.GeneralLedger.Reports;
using PetraERP.Contracts.Financial;
using PetraERP.Contracts.Ledger;

namespace PetraERP.Api.Endpoints;

/// <summary>Explicit Application ↔ transport mapping for ledger master data, calendars and reports.</summary>
internal static class LedgerMasterMapping
{
    internal static string Money(decimal value) => FinancialDecimalText.Format(value, FinancialDecimalText.MoneyScale);

    internal static AccountInput ToInput(this AccountRequest request) => new()
    {
        Code = request.Code,
        NameArabic = request.NameArabic,
        NameEnglish = request.NameEnglish,
        AccountType = (AccountTypeCode)request.AccountType,
        PostingAllowed = request.PostingAllowed,
        CostCenterPolicy = (CostCenterPolicyCode)request.CostCenterPolicy,
        CurrencyMode = (CurrencyModeCode)request.CurrencyMode,
        FixedCurrencyCode = request.FixedCurrencyCode,
        ParentId = request.ParentId,
    };

    internal static AccountResponse ToResponse(this AccountDto dto) => new()
    {
        Id = dto.Id,
        CompanyId = dto.CompanyId,
        Code = dto.Code,
        NameArabic = dto.NameArabic,
        NameEnglish = dto.NameEnglish,
        AccountType = (AccountType)dto.AccountType,
        PostingAllowed = dto.PostingAllowed,
        CostCenterPolicy = (CostCenterPolicy)dto.CostCenterPolicy,
        CurrencyMode = (AccountCurrencyMode)dto.CurrencyMode,
        FixedCurrencyCode = dto.FixedCurrencyCode,
        ParentId = dto.ParentId,
        Level = dto.Level,
        HasChildren = dto.HasChildren,
        HasJournalLines = dto.HasJournalLines,
        IsActive = dto.IsActive,
        Version = dto.VersionBase64,
    };

    internal static AccountAuditResponse ToResponse(this AccountAuditDto dto) => new()
    {
        EventId = dto.EventId,
        AccountId = dto.AccountId,
        Code = dto.Code,
        NameArabic = dto.NameArabic,
        NameEnglish = dto.NameEnglish,
        AccountType = (AccountType)dto.AccountType,
        ParentId = dto.ParentId,
        ActorUserId = dto.ActorUserId,
        Reason = dto.Reason,
        OccurredAtUtc = dto.OccurredAtUtc,
    };

    internal static AccountPageResponse ToResponse(this PagedResult<AccountDto> page) => new()
    {
        Items = page.Items.Select(ToResponse).ToArray(),
        Page = page.Page,
        PageSize = page.PageSize,
        TotalCount = page.TotalCount,
    };

    internal static CostCenterInput ToInput(this CostCenterRequest request) => new()
    {
        Code = request.Code,
        NameArabic = request.NameArabic,
        NameEnglish = request.NameEnglish,
        ParentId = request.ParentId,
        CenterType = request.CenterType,
        Description = request.Description,
        StartDate = request.StartDate,
        EndDate = request.EndDate,
        ResponsiblePerson = request.ResponsiblePerson
    };

    internal static CostCenterResponse ToResponse(this CostCenterDto dto) => new()
    {
        Id = dto.Id,
        CompanyId = dto.CompanyId,
        Code = dto.Code,
        NameArabic = dto.NameArabic,
        NameEnglish = dto.NameEnglish,
        ParentId = dto.ParentId,
        CenterType = dto.CenterType,
        Description = dto.Description,
        StartDate = dto.StartDate,
        EndDate = dto.EndDate,
        ResponsiblePerson = dto.ResponsiblePerson,
        IsActive = dto.IsActive,
        HasJournalLines = dto.HasJournalLines,
        Version = dto.VersionBase64,
        ChildrenCount = dto.ChildrenCount
    };

    internal static CostCenterAllocationResponse ToResponse(this CostCenterAllocationDto dto) => new()
    {
        Id = dto.Id,
        SourceCostCenterId = dto.SourceCostCenterId,
        TargetCostCenterId = dto.TargetCostCenterId,
        TargetCode = dto.TargetCode,
        TargetNameArabic = dto.TargetNameArabic,
        Percentage = dto.Percentage.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
    };

    internal static CostCenterPageResponse ToResponse(this PagedResult<CostCenterDto> page) => new()
    {
        Items = page.Items.Select(ToResponse).ToArray(),
        Page = page.Page,
        PageSize = page.PageSize,
        TotalCount = page.TotalCount,
    };

    internal static FiscalYearResponse ToResponse(this FiscalYearDto dto) => new()
    {
        Id = dto.Id,
        CompanyId = dto.CompanyId,
        Code = dto.Code,
        RangeStart = dto.RangeStart,
        RangeEnd = dto.RangeEnd,
        Periods = dto.Periods.Select(ToResponse).ToArray(),
        Version = dto.VersionBase64,
    };

    internal static AccountingPeriodResponse ToResponse(this AccountingPeriodDto dto) => new()
    {
        Id = dto.Id,
        FiscalYearId = dto.FiscalYearId,
        Code = dto.Code,
        RangeStart = dto.RangeStart,
        RangeEnd = dto.RangeEnd,
        State = (AccountingPeriodState)dto.State,
        Version = dto.VersionBase64,
    };

    internal static PeriodAuditResponse ToResponse(this PeriodAuditDto dto) => new()
    {
        EventId = dto.EventId,
        PreviousState = (AccountingPeriodState)dto.PreviousState,
        NewState = (AccountingPeriodState)dto.NewState,
        ActorUserId = dto.ActorUserId,
        Reason = dto.Reason,
        OccurredAtUtc = dto.OccurredAtUtc,
    };

    internal static CompanyCurrencyContextResponse ToResponse(this CompanyCurrencyContextDto dto) => new()
    {
        CompanyId = dto.CompanyId,
        BaseCurrency = ToResponse(dto.BaseCurrency),
        AdditionalCurrencies = dto.AdditionalCurrencies.Select(ToResponse).ToArray(),
    };

    private static CurrencyResponse ToResponse(CurrencyDto dto) => new() { Code = dto.Code, NameArabic = dto.NameArabic, NameEnglish = dto.NameEnglish };

    internal static GeneralJournalResponse ToResponse(this GeneralJournalDto dto) => new()
    {
        Lines = dto.Lines.Select(line => new GeneralJournalLineResponse
        {
            JournalId = line.JournalId,
            LineId = line.LineId,
            BranchId = line.BranchId,
            AccountingDate = line.AccountingDate,
            PostingSequence = line.PostingSequence,
            State = LedgerJournalMapping.ToContractState(line.State),
            JournalDescription = line.JournalDescription,
            LineDescription = line.LineDescription,
            AccountId = line.AccountId,
            AccountCode = line.AccountCode,
            AccountNameArabic = line.AccountNameArabic,
            AccountNameEnglish = line.AccountNameEnglish,
            CostCenterId = line.CostCenterId,
            CostCenterCode = line.CostCenterCode,
            TransactionCurrencyCode = line.TransactionCurrencyCode,
            TransactionDebit = Money(line.TransactionDebit),
            TransactionCredit = Money(line.TransactionCredit),
            BaseDebit = Money(line.BaseDebit),
            BaseCredit = Money(line.BaseCredit),
            ReversesEntryId = line.ReversesEntryId,
        }).ToArray(),
        Page = dto.Page,
        PageSize = dto.PageSize,
        TotalCount = dto.TotalCount,
        TotalBaseDebit = Money(dto.TotalBaseDebit),
        TotalBaseCredit = Money(dto.TotalBaseCredit),
        BaseCurrencyCode = dto.BaseCurrencyCode,
    };

    internal static AccountLedgerResponse ToResponse(this AccountLedgerDto dto) => new()
    {
        Account = dto.Account.ToResponse(),
        BaseCurrencyCode = dto.BaseCurrencyCode,
        OpeningBalance = Money(dto.OpeningBalance),
        PeriodDebit = Money(dto.PeriodDebit),
        PeriodCredit = Money(dto.PeriodCredit),
        ClosingBalance = Money(dto.ClosingBalance),
        NextCursor = dto.NextCursor,
        SnapshotPostingSequence = dto.SnapshotPostingSequence,
        Lines = dto.Lines.Select(line => new AccountLedgerLineResponse
        {
            JournalId = line.JournalId,
            LineId = line.LineId,
            BranchId = line.BranchId,
            AccountingDate = line.AccountingDate,
            PostingSequence = line.PostingSequence,
            JournalDescription = line.JournalDescription,
            LineDescription = line.LineDescription,
            AccountCode = line.AccountCode,
            CostCenterCode = line.CostCenterCode,
            TransactionCurrencyCode = line.TransactionCurrencyCode,
            TransactionDebit = Money(line.TransactionDebit),
            TransactionCredit = Money(line.TransactionCredit),
            BaseDebit = Money(line.BaseDebit),
            BaseCredit = Money(line.BaseCredit),
            RunningBalance = Money(line.RunningBalance),
            ReversesEntryId = line.ReversesEntryId,
        }).ToArray(),
        Page = dto.Page,
        PageSize = dto.PageSize,
        TotalCount = dto.TotalCount,
        IncludedAccountCount = dto.IncludedAccountCount,
    };

    internal static TrialBalanceResponse ToResponse(this TrialBalanceDto dto) => new()
    {
        From = dto.From,
        To = dto.To,
        BaseCurrencyCode = dto.BaseCurrencyCode,
        Rows = dto.Rows.Select(row => new TrialBalanceRowResponse
        {
            AccountId = row.AccountId,
            ParentId = row.ParentId,
            Code = row.Code,
            NameArabic = row.NameArabic,
            NameEnglish = row.NameEnglish,
            AccountType = (AccountType)row.AccountType,
            PostingAllowed = row.PostingAllowed,
            Level = row.Level,
            OpeningDebit = Money(row.OpeningDebit),
            OpeningCredit = Money(row.OpeningCredit),
            PeriodDebit = Money(row.PeriodDebit),
            PeriodCredit = Money(row.PeriodCredit),
            EndingDebit = Money(row.EndingDebit),
            EndingCredit = Money(row.EndingCredit),
            LastDebitDate = row.LastDebitDate,
            LastCreditDate = row.LastCreditDate,
        }).ToArray(),
        TotalOpeningDebit = Money(dto.TotalOpeningDebit),
        TotalOpeningCredit = Money(dto.TotalOpeningCredit),
        TotalPeriodDebit = Money(dto.TotalPeriodDebit),
        TotalPeriodCredit = Money(dto.TotalPeriodCredit),
        TotalEndingDebit = Money(dto.TotalEndingDebit),
        TotalEndingCredit = Money(dto.TotalEndingCredit),
        IsBalanced = dto.IsBalanced,
        DisplayCurrencyCode = dto.DisplayCurrencyCode,
        DisplayRate = FinancialDecimalText.Format(dto.DisplayRate, FinancialDecimalText.RateScale),
    };
}
