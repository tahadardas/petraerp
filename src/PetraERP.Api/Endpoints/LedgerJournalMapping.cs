using System.Globalization;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger;
using PetraERP.Contracts.Financial;
using PetraERP.Contracts.Ledger;
using DomainJournalState = PetraERP.Domain.GeneralLedger.JournalEntryState;

namespace PetraERP.Api.Endpoints;

/// <summary>
/// Explicit mapping between Application General Ledger models and Contracts responses/commands.
/// </summary>
internal static class LedgerJournalMapping
{
    /// <summary>The single persisted/Domain → transport journal-state mapping. Unknown values fail loudly.</summary>
    internal static JournalState ToContractState(byte persisted) => (DomainJournalState)persisted switch
    {
        DomainJournalState.Draft => JournalState.Draft,
        DomainJournalState.Validated => JournalState.Validated,
        DomainJournalState.Approved => JournalState.Approved,
        DomainJournalState.Posted => JournalState.Posted,
        DomainJournalState.Reversed => JournalState.Reversed,
        _ => throw new InvalidOperationException("Unknown persisted journal state."),
    };

    /// <summary>Transport → Domain persisted value, used for server-side state filters.</summary>
    internal static byte ToPersistedState(JournalState state) => state switch
    {
        JournalState.Draft => (byte)DomainJournalState.Draft,
        JournalState.Validated => (byte)DomainJournalState.Validated,
        JournalState.Approved => (byte)DomainJournalState.Approved,
        JournalState.Posted => (byte)DomainJournalState.Posted,
        JournalState.Reversed => (byte)DomainJournalState.Reversed,
        _ => throw new RequestValidationException("ledger.journal_state_invalid", "Unknown journal state.", "state"),
    };

    internal static JournalDraftLineInput ToInput(this JournalLineDraftDto dto) => new()
    {
        AccountId = dto.AccountId,
        CostCenterId = dto.CostCenterId,
        TransactionCurrencyCode = dto.TransactionCurrencyCode,
        TransactionDebit = decimal.Parse(dto.TransactionDebit, CultureInfo.InvariantCulture),
        TransactionCredit = decimal.Parse(dto.TransactionCredit, CultureInfo.InvariantCulture),
        RateValue = string.IsNullOrWhiteSpace(dto.RateValue) ? 1.0m : decimal.Parse(dto.RateValue, CultureInfo.InvariantCulture),
        RateSourceCurrencyCode = dto.RateSourceCurrencyCode,
        RateTargetCurrencyCode = dto.RateTargetCurrencyCode,
        BaseDebit = string.IsNullOrWhiteSpace(dto.BaseDebit) ? null : decimal.Parse(dto.BaseDebit, CultureInfo.InvariantCulture),
        BaseCredit = string.IsNullOrWhiteSpace(dto.BaseCredit) ? null : decimal.Parse(dto.BaseCredit, CultureInfo.InvariantCulture),
        Rounding = (MidpointRounding)dto.Rounding,
        Description = dto.Description,
    };

    internal static JournalDetailsResponse ToResponse(this JournalDetailsDto dto) => new()
    {
        Id = dto.Id,
        CompanyId = dto.CompanyId,
        BranchId = dto.BranchId,
        AccountingDate = dto.AccountingDate,
        Description = dto.Description,
        State = ToContractState(dto.State),
        StateName = dto.StateName,
        Revision = dto.Revision,
        Version = dto.VersionBase64,
        PostingSequence = dto.PostingSequence,
        PostingFiscalYearId = dto.PostingFiscalYearId,
        PostingPeriodId = dto.PostingPeriodId,
        ReversesEntryId = dto.ReversesEntryId,
        ReversedByEntryId = dto.ReversedByEntryId,
        CorrectsEntryId = dto.CorrectsEntryId,
        TotalDebit = FinancialDecimalText.Format(dto.TotalDebit, FinancialDecimalText.MoneyScale),
        TotalCredit = FinancialDecimalText.Format(dto.TotalCredit, FinancialDecimalText.MoneyScale),
        BaseCurrencyCode = dto.BaseCurrencyCode,
        CreatedAtUtc = dto.CreatedAtUtc,
        UpdatedAtUtc = dto.UpdatedAtUtc,
        Lines = dto.Lines.Select(ToResponse).ToArray(),
        AuditTrail = dto.AuditTrail.Select(ToResponse).ToArray(),
    };

    internal static JournalLineResponse ToResponse(this JournalLineDto dto) => new()
    {
        Id = dto.Id,
        AccountId = dto.AccountId,
        AccountCode = dto.AccountCode,
        AccountNameArabic = dto.AccountNameArabic,
        AccountNameEnglish = dto.AccountNameEnglish,
        CostCenterId = dto.CostCenterId,
        CostCenterCode = dto.CostCenterCode,
        CostCenterNameArabic = dto.CostCenterNameArabic,
        CostCenterNameEnglish = dto.CostCenterNameEnglish,
        TransactionCurrencyCode = dto.TransactionCurrencyCode,
        TransactionDebit = FinancialDecimalText.Format(dto.TransactionDebit, FinancialDecimalText.MoneyScale),
        TransactionCredit = FinancialDecimalText.Format(dto.TransactionCredit, FinancialDecimalText.MoneyScale),
        BaseCurrencyCode = dto.BaseCurrencyCode,
        BaseDebit = FinancialDecimalText.Format(dto.BaseDebit, FinancialDecimalText.MoneyScale),
        BaseCredit = FinancialDecimalText.Format(dto.BaseCredit, FinancialDecimalText.MoneyScale),
        RateSourceCurrencyCode = dto.RateSourceCurrencyCode,
        RateTargetCurrencyCode = dto.RateTargetCurrencyCode,
        RateValue = FinancialDecimalText.Format(dto.RateValue, FinancialDecimalText.RateScale),
        Rounding = dto.Rounding,
        Description = dto.Description,
        ReversesLineId = dto.ReversesLineId,
    };

    internal static JournalAuditResponse ToResponse(this JournalAuditDto dto) => new()
    {
        EventId = dto.EventId,
        Revision = dto.Revision,
        Action = dto.Action,
        ActionName = dto.ActionName,
        PreviousState = dto.PreviousState is { } previous ? ToContractState(previous) : null,
        NewState = ToContractState(dto.NewState),
        ActorUserId = dto.ActorUserId,
        ActorNameArabic = dto.ActorNameArabic,
        ActorNameEnglish = dto.ActorNameEnglish,
        Reason = dto.Reason,
        OccurredAtUtc = dto.OccurredAtUtc,
        RelatedEntryId = dto.RelatedEntryId,
    };

    internal static JournalSummaryItemResponse ToResponse(this JournalSummaryDto dto) => new()
    {
        Id = dto.Id,
        AccountingDate = dto.AccountingDate,
        Description = dto.Description,
        State = ToContractState(dto.State),
        StateName = dto.StateName,
        PostingSequence = dto.PostingSequence,
        TotalDebit = FinancialDecimalText.Format(dto.TotalDebit, FinancialDecimalText.MoneyScale),
        TotalCredit = FinancialDecimalText.Format(dto.TotalCredit, FinancialDecimalText.MoneyScale),
        BaseCurrencyCode = dto.BaseCurrencyCode,
        LineCount = dto.LineCount,
        Version = dto.VersionBase64,
    };

    internal static JournalPagedResponse ToResponse(this PagedResult<JournalSummaryDto> paged) => new()
    {
        Items = paged.Items.Select(ToResponse).ToArray(),
        Page = paged.Page,
        PageSize = paged.PageSize,
        TotalCount = paged.TotalCount,
    };

    internal static AccountLookupResponse ToResponse(this AccountLookupDto dto) => new()
    {
        Id = dto.Id,
        CompanyId = dto.CompanyId,
        Code = dto.Code,
        NameArabic = dto.NameArabic,
        NameEnglish = dto.NameEnglish,
        AccountType = dto.AccountType,
        AccountTypeName = dto.AccountTypeName,
        PostingAllowed = dto.PostingAllowed,
        CostCenterPolicy = dto.CostCenterPolicy,
        CurrencyMode = dto.CurrencyMode,
        FixedCurrencyCode = dto.FixedCurrencyCode,
        ParentId = dto.ParentId,
        IsActive = dto.IsActive,
    };

    internal static CostCenterLookupResponse ToResponse(this CostCenterLookupDto dto) => new()
    {
        Id = dto.Id,
        CompanyId = dto.CompanyId,
        Code = dto.Code,
        NameArabic = dto.NameArabic,
        NameEnglish = dto.NameEnglish,
        IsActive = dto.IsActive,
    };

    internal static JournalValidationResponse ToResponse(this JournalDraftValidationResult dto) => new()
    {
        IsValid = dto.IsValid,
        Errors = dto.Errors,
        TotalDebit = FinancialDecimalText.Format(dto.TotalDebit, FinancialDecimalText.MoneyScale),
        TotalCredit = FinancialDecimalText.Format(dto.TotalCredit, FinancialDecimalText.MoneyScale),
        Difference = FinancialDecimalText.Format(dto.Difference, FinancialDecimalText.MoneyScale),
        IsBalanced = dto.IsBalanced,
    };
}
