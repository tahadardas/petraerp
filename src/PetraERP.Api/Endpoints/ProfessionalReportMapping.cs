using PetraERP.Contracts.Financial;
using App = PetraERP.Application.Reports.Professional;
using Wire = PetraERP.Contracts.Reports;

namespace PetraERP.Api.Endpoints;

internal static class ProfessionalReportMapping
{
    internal static App.ProfessionalReportRequest ToApplication(Wire.ProfessionalReportRequest r) => new()
    {
        Kind = (App.ProfessionalReportKind)r.Kind,
        FiscalYearId = r.FiscalYearId,
        From = r.From,
        To = r.To,
        AccountId = r.AccountId,
        PartyId = r.PartyId,
        OppositeAccountId = r.OppositeAccountId,
        CostCenterId = r.CostCenterId,
        CurrencyCode = r.CurrencyCode,
        CurrencyBasis = (App.ProfessionalCurrencyBasis)r.CurrencyBasis,
        DisplayCurrencyCode = r.DisplayCurrencyCode,
        RateDate = r.RateDate,
        Sources = r.Sources.Select(v => (App.ProfessionalReportSource)v).ToArray(),
        Fields = r.Fields.Select(v => (App.ProfessionalReportField)v).ToArray(),
        Grouping = (App.ProfessionalReportGrouping)r.Grouping,
        Order = (App.ProfessionalReportOrder)r.Order,
        BalancePresentation = (App.ProfessionalBalancePresentation)r.BalancePresentation,
        DescriptionContains = r.DescriptionContains,
        DescriptionNotContains = r.DescriptionNotContains,
        PostingFrom = r.PostingFrom,
        PostingTo = r.PostingTo,
        CreatedFrom = r.CreatedFrom,
        CreatedTo = r.CreatedTo,
        FromSequence = r.FromSequence,
        ToSequence = r.ToSequence,
        AccountLevel = r.AccountLevel,
        AccountCategory = r.AccountCategory is { } category ? (App.ProfessionalAccountCategory)category : null,
        IncludeParents = r.IncludeParents,
        IncludePostingAccounts = r.IncludePostingAccounts,
        IncludeEmptyAccounts = r.IncludeEmptyAccounts,
        OnlyNonZero = r.OnlyNonZero,
        IncludeInvoiceDetails = r.IncludeInvoiceDetails,
        IncludeAging = r.IncludeAging,
        PatternRevisionIds = r.PatternRevisionIds,
        AdvancedFilter = r.AdvancedFilter is null ? null : ToApplication(r.AdvancedFilter),
    };

    internal static Wire.ProfessionalReportRequest Map(App.ProfessionalReportRequest r) => new()
    {
        Kind = (Wire.ProfessionalReportKind)r.Kind,
        FiscalYearId = r.FiscalYearId,
        From = r.From,
        To = r.To,
        AccountId = r.AccountId,
        PartyId = r.PartyId,
        OppositeAccountId = r.OppositeAccountId,
        CostCenterId = r.CostCenterId,
        CurrencyCode = r.CurrencyCode,
        CurrencyBasis = (Wire.ProfessionalCurrencyBasis)r.CurrencyBasis,
        DisplayCurrencyCode = r.DisplayCurrencyCode,
        RateDate = r.RateDate,
        Sources = r.Sources.Select(v => (Wire.ProfessionalReportSource)v).ToArray(),
        Fields = r.Fields.Select(v => (Wire.ProfessionalReportField)v).ToArray(),
        Grouping = (Wire.ProfessionalReportGrouping)r.Grouping,
        Order = (Wire.ProfessionalReportOrder)r.Order,
        BalancePresentation = (Wire.ProfessionalBalancePresentation)r.BalancePresentation,
        DescriptionContains = r.DescriptionContains,
        DescriptionNotContains = r.DescriptionNotContains,
        PostingFrom = r.PostingFrom,
        PostingTo = r.PostingTo,
        CreatedFrom = r.CreatedFrom,
        CreatedTo = r.CreatedTo,
        FromSequence = r.FromSequence,
        ToSequence = r.ToSequence,
        AccountLevel = r.AccountLevel,
        AccountCategory = r.AccountCategory is { } category ? (Wire.ProfessionalAccountCategory)category : null,
        IncludeParents = r.IncludeParents,
        IncludePostingAccounts = r.IncludePostingAccounts,
        IncludeEmptyAccounts = r.IncludeEmptyAccounts,
        OnlyNonZero = r.OnlyNonZero,
        IncludeInvoiceDetails = r.IncludeInvoiceDetails,
        IncludeAging = r.IncludeAging,
        PatternRevisionIds = r.PatternRevisionIds,
        AdvancedFilter = r.AdvancedFilter is null ? null : Map(r.AdvancedFilter),
    };

    private static App.ProfessionalReportFilterGroup ToApplication(Wire.ProfessionalReportFilterGroup group) => new()
    {
        Logic = (App.ProfessionalFilterLogic)group.Logic,
        Conditions = group.Conditions.Select(condition => new App.ProfessionalReportFilterCondition
        {
            Field = (App.ProfessionalReportField)condition.Field,
            Operator = (App.ProfessionalFilterOperator)condition.Operator,
            Value = condition.Value,
            SecondValue = condition.SecondValue,
            Values = condition.Values,
        }).ToArray(),
        Groups = group.Groups.Select(ToApplication).ToArray(),
    };

    private static Wire.ProfessionalReportFilterGroup Map(App.ProfessionalReportFilterGroup group) => new()
    {
        Logic = (Wire.ProfessionalFilterLogic)group.Logic,
        Conditions = group.Conditions.Select(condition => new Wire.ProfessionalReportFilterCondition
        {
            Field = (Wire.ProfessionalReportField)condition.Field,
            Operator = (Wire.ProfessionalFilterOperator)condition.Operator,
            Value = condition.Value,
            SecondValue = condition.SecondValue,
            Values = condition.Values,
        }).ToArray(),
        Groups = group.Groups.Select(Map).ToArray(),
    };

    internal static Wire.ProfessionalReportCatalogResponse Map(App.ProfessionalReportCatalogResponse r) => new()
    {
        MaximumRows = r.MaximumRows, DescriptionSearchScope = r.DescriptionSearchScope,
        AccountCategories = Enum.GetValues<Wire.ProfessionalAccountCategory>(),
        Patterns = r.Patterns.Select(p => new Wire.ProfessionalPatternDto(p.RevisionId, p.Code, p.NameArabic, (Wire.ProfessionalReportSource)p.Source)).ToArray(),
        FiscalYears = r.FiscalYears.Select(y => new Wire.ProfessionalFiscalYearDto(y.Id, y.Code, y.From, y.To)).ToArray(),
        Reports = r.Reports.Select(d => new Wire.ProfessionalReportDefinitionDto
        {
            Kind = (Wire.ProfessionalReportKind)d.Kind, NameArabic = d.NameArabic, NameEnglish = d.NameEnglish,
            Sources = d.Sources.Select(s => new Wire.ProfessionalSourceDefinitionDto((Wire.ProfessionalReportSource)s.Source, s.LabelArabic, s.Supported, s.Reason)).ToArray(),
            Fields = d.Fields.Select(f => new Wire.ProfessionalFieldDefinitionDto((Wire.ProfessionalReportField)f.Field, f.LabelArabic, f.Supported, f.DefaultSelected, f.Reason)).ToArray(),
            Options = d.Options.Select(o => new Wire.ProfessionalOptionDefinitionDto((Wire.ProfessionalReportOption)o.Option, o.LabelArabic, o.Supported, o.Reason)).ToArray(),
        }).ToArray(),
    };

    internal static Wire.ProfessionalReportExecutionResponse Map(App.ProfessionalReportExecutionResponse r) => new()
    {
        ExecutionId = r.ExecutionId,
        GeneratedAtUtc = r.GeneratedAtUtc,
        CompanyId = r.CompanyId,
        BranchId = r.BranchId,
        FiscalYearId = r.FiscalYearId,
        CompanyName = r.CompanyName,
        BranchName = r.BranchName,
        FiscalYearCode = r.FiscalYearCode,
        CurrencyCode = r.CurrencyCode,
        SubjectCode = r.SubjectCode,
        SubjectName = r.SubjectName,
        BalanceConvention = r.BalanceConvention,
        Parameters = Map(r.Parameters),
        SelectedFields = r.SelectedFields.Select(v => (Wire.ProfessionalReportField)v).ToArray(),
        Rows = r.Rows.Select(Map).ToArray(),
        Summary = Map(r.Summary),
        Notes = r.Notes,
        InvoiceDetails = r.InvoiceDetails.Select(Map).ToArray(),
        Aging = r.Aging is { } aging ? Map(aging) : null,
    };

    internal static Wire.ProfessionalReportRowDto Map(App.ProfessionalReportRowDto r) => new()
    {
        RowId = r.RowId,
        JournalId = r.JournalId,
        DocumentId = r.DocumentId,
        Source = r.Source is { } valueSource ? (Wire.ProfessionalReportSource)valueSource : null,
        DocumentNumber = r.DocumentNumber,
        Date = r.Date,
        PostingSequence = r.PostingSequence,
        AccountId = r.AccountId,
        AccountCode = r.AccountCode,
        AccountName = r.AccountName,
        PartyCode = r.PartyCode,
        PartyName = r.PartyName,
        CostCenterCode = r.CostCenterCode,
        OppositeAccountCode = r.OppositeAccountCode,
        Description = r.Description,
        CurrencyCode = r.CurrencyCode,
        Debit = Money(r.Debit),
        Credit = Money(r.Credit),
        ActualBalance = Money(r.ActualBalance),
        DisplayBalance = Money(r.DisplayBalance),
        Opening = Money(r.Opening),
        OpeningDebit = Money(r.OpeningDebit),
        OpeningCredit = Money(r.OpeningCredit),
        ClosingDebit = Money(r.ClosingDebit),
        ClosingCredit = Money(r.ClosingCredit),
        Closing = Money(r.Closing),
        TransactionDebit = Money(r.TransactionDebit),
        TransactionCredit = Money(r.TransactionCredit),
        TransactionCurrencyCode = r.TransactionCurrencyCode,
        GroupKey = r.GroupKey,
        IsAggregate = r.IsAggregate,
        CanDrillThrough = r.CanDrillThrough,
    };

    internal static Wire.ProfessionalReportSummaryDto Map(App.ProfessionalReportSummaryDto r) => new()
    {
        ActualOpening = Money(r.ActualOpening),
        ActualPeriodDebit = Money(r.ActualPeriodDebit),
        ActualPeriodCredit = Money(r.ActualPeriodCredit),
        ActualClosing = Money(r.ActualClosing),
        FilteredDebit = Money(r.FilteredDebit),
        FilteredCredit = Money(r.FilteredCredit),
        DisplayClosing = Money(r.DisplayClosing),
        RowCount = r.RowCount,
        OpeningDebit = Money(r.OpeningDebit),
        OpeningCredit = Money(r.OpeningCredit),
        ClosingDebit = Money(r.ClosingDebit),
        ClosingCredit = Money(r.ClosingCredit),
    };

    private static Wire.ProfessionalInvoiceDetailDto Map(App.ProfessionalInvoiceDetailDto d) => new(d.InvoiceId, d.LineId, (Wire.ProfessionalReportSource)d.Source,
        d.InvoiceNumber, d.ItemCode, d.ItemName, d.UnitCode, Money(d.Quantity), Money(d.UnitPrice), Money(d.Discount), Money(d.Tax), Money(d.LineTotal), d.CurrencyCode);
    private static Wire.ProfessionalAgingDto Map(App.ProfessionalAgingDto d) => new(d.AsOf, d.CurrencyCode, Money(d.NotDue), Money(d.Days1To30),
        Money(d.Days31To60), Money(d.Days61To90), Money(d.Over90), Money(d.Advance), Money(d.Net));
    private static string Money(decimal value) => FinancialDecimalText.Format(value, FinancialDecimalText.MoneyScale);
}
