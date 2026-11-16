using PetraERP.Api.Errors;
using PetraERP.Api.Security;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.Reports;
using PetraERP.Application.Reports;
using PetraERP.Application.Security;
using PetraERP.Contracts.Financial;
using PetraERP.Contracts.Reports;
using AppAging = PetraERP.Application.Reports.AgingRow;
using AppKind = PetraERP.Application.Reports.PartyStatementKind;
using AppPurchaseGrouping = PetraERP.Application.Reports.PurchaseAnalysisGrouping;
using AppSalesGrouping = PetraERP.Application.Reports.SalesAnalysisGrouping;
using ContractAccountType = PetraERP.Contracts.Ledger.AccountType;
using ContractKind = PetraERP.Contracts.Reports.PartyStatementKind;
using ContractMovementKind = PetraERP.Contracts.Inventory.StockMovementKind;
using ContractPurchaseGrouping = PetraERP.Contracts.Reports.PurchaseAnalysisGrouping;
using ContractSalesGrouping = PetraERP.Contracts.Reports.SalesAnalysisGrouping;

namespace PetraERP.Api.Endpoints;

/// <summary>
/// Read-only financial and commercial reports (PT-118..PT-124). Every route needs Reports.View plus the view permission of the
/// module whose data it exposes; GL statements accept <c>consolidated=true</c> only with a scope grant for every branch.
/// </summary>
internal static class CommercialReportEndpoints
{
    internal static void MapCommercialReportEndpoints(this RouteGroupBuilder api)
    {
        var reports = api.MapGroup("/companies/{companyId:guid}/branches/{branchId:guid}/reports").WithTags("Financial and commercial reports")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.ReportsView));
        reports.MapGet("/financial/income-statement", IncomeStatementAsync).WithName("GetIncomeStatement")
            .WithSummary("Revenue and expense accounts for a period from posted journal lines, with net income.")
            .Produces<IncomeStatementResponse>().ProducesStandardProblems();
        reports.MapGet("/financial/balance-sheet", BalanceSheetAsync).WithName("GetBalanceSheet")
            .WithSummary("Asset, liability and equity balances at a date plus unclosed current earnings, with a balance check.")
            .Produces<BalanceSheetResponse>().ProducesStandardProblems();
        reports.MapGet("/parties/{partyId:guid}/statement", PartyStatementAsync).WithName("GetPartyStatement")
            .WithSummary("Customer or supplier statement in one currency: opening, documents with running balance, closing.")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.PartiesView))
            .Produces<PartyStatementResponse>().ProducesStandardProblems();
        reports.MapGet("/aging", AgingAsync).WithName("GetAging")
            .WithSummary("Receivable or payable aging by due date (invoice date + payment terms) with advances.")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.PartiesView))
            .Produces<AgingResponse>().ProducesStandardProblems();
        reports.MapGet("/sales-analysis", SalesAnalysisAsync).WithName("GetSalesAnalysis")
            .WithSummary("Posted sales less returns by pattern, customer or item in base currency, reconciled with GL revenue.")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.SalesInvoicesView))
            .Produces<SalesAnalysisResponse>().ProducesStandardProblems();
        reports.MapGet("/purchase-analysis", PurchaseAnalysisAsync).WithName("GetPurchaseAnalysis")
            .WithSummary("Posted purchases less returns by pattern, supplier or item with base quantities and stock cost.")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.PurchaseInvoicesView))
            .Produces<PurchaseAnalysisResponse>().ProducesStandardProblems();
        reports.MapGet("/inventory/valuation", InventoryValuationAsync).WithName("GetInventoryValuation")
            .WithSummary("Quantity and value per warehouse and item at a date, reconciled with the GL inventory accounts.")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.InventoryItemsView))
            .Produces<InventoryValuationResponse>().ProducesStandardProblems();
        reports.MapGet("/inventory/movements/{itemId:guid}", InventoryMovementsAsync).WithName("GetInventoryMovementReport")
            .WithSummary("Opening, movements with origin documents and running quantity/value, closing for one item.")
            .RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.InventoryMovementsView))
            .Produces<InventoryMovementReportResponse>().ProducesStandardProblems();
        reports.MapGet("/cash/accounts", CashAccountsAsync).WithName("ListCashAccounts")
            .WithSummary("Accounts mapped as Cash or Bank by any document pattern.")
            .Produces<IReadOnlyList<CashAccountDto>>().ProducesStandardProblems();
        reports.MapGet("/cash/{accountId:guid}/statement", CashStatementAsync).WithName("GetCashStatement")
            .WithSummary("Cash or bank statement: opening, posted lines with source documents and running balance, closing.")
            .Produces<CashStatementResponse>().ProducesStandardProblems();
    }

    private static LedgerReportScope Scope(Guid companyId, Guid branchId, bool consolidated, HttpContext http) =>
        LedgerReportScope.Create(companyId, branchId, http.RequireSession().UserId, consolidated);

    private static DateOnly Required(DateOnly? value, string name) =>
        value ?? throw new RequestValidationException("reports.date_required", $"'{name}' is required.", name);

    private static async Task<IResult> IncomeStatementAsync(Guid companyId, Guid branchId, DateOnly? from, DateOnly? to, bool? consolidated,
        ICommercialReportService service, HttpContext http, CancellationToken cancellationToken)
    {
        var r = await service.GetIncomeStatementAsync(Scope(companyId, branchId, consolidated ?? false, http), Required(from, "from"), Required(to, "to"),
            cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new IncomeStatementResponse
        {
            From = r.From, To = r.To, BaseCurrency = r.BaseCurrency, Lines = r.Lines.Select(Map).ToList(),
            TotalRevenue = Money(r.TotalRevenue), TotalExpenses = Money(r.TotalExpenses), NetIncome = Money(r.NetIncome),
        });
    }

    private static async Task<IResult> BalanceSheetAsync(Guid companyId, Guid branchId, DateOnly? asOf, bool? consolidated,
        ICommercialReportService service, HttpContext http, CancellationToken cancellationToken)
    {
        var r = await service.GetBalanceSheetAsync(Scope(companyId, branchId, consolidated ?? false, http), Required(asOf, "asOf"), cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(new BalanceSheetResponse
        {
            AsOf = r.AsOf, BaseCurrency = r.BaseCurrency, Lines = r.Lines.Select(Map).ToList(), TotalAssets = Money(r.TotalAssets),
            TotalLiabilities = Money(r.TotalLiabilities), TotalEquity = Money(r.TotalEquity), CurrentEarnings = Money(r.CurrentEarnings), Balanced = r.Balanced,
        });
    }

    private static async Task<IResult> PartyStatementAsync(Guid companyId, Guid branchId, Guid partyId, ContractKind? kind, string? currencyCode,
        DateOnly? from, DateOnly? to, ICommercialReportService service, HttpContext http, CancellationToken cancellationToken)
    {
        var r = await service.GetPartyStatementAsync(Scope(companyId, branchId, false, http), partyId, Kind(kind), currencyCode ?? string.Empty,
            Required(from, "from"), Required(to, "to"), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new PartyStatementResponse
        {
            PartyId = r.PartyId, PartyCode = r.PartyCode, PartyName = r.PartyName, Kind = (ContractKind)r.Kind, CurrencyCode = r.CurrencyCode, From = r.From,
            To = r.To, Opening = Money(r.Opening), TotalDebit = Money(r.TotalDebit), TotalCredit = Money(r.TotalCredit), Closing = Money(r.Closing),
            Truncated = r.Truncated,
            Lines = r.Lines.Select(l => new PartyStatementLineDto
            {
                DateUtc = l.DateUtc, DocumentType = l.DocumentType, DocumentId = l.DocumentId, DocumentNumber = l.DocumentNumber, Description = l.Description,
                Debit = Money(l.Debit), Credit = Money(l.Credit), Balance = Money(l.Balance),
            }).ToList(),
        });
    }

    private static async Task<IResult> AgingAsync(Guid companyId, Guid branchId, ContractKind? kind, string? currencyCode, DateOnly? asOf,
        ICommercialReportService service, HttpContext http, CancellationToken cancellationToken)
    {
        var r = await service.GetAgingAsync(Scope(companyId, branchId, false, http), Kind(kind), currencyCode ?? string.Empty, Required(asOf, "asOf"),
            cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new AgingResponse
        {
            Kind = (ContractKind)r.Kind, AsOf = r.AsOf, CurrencyCode = r.CurrencyCode, Rows = r.Rows.Select(Map).ToList(), Totals = Map(r.Totals),
            Documents = r.Documents.Select(d => new AgingDocumentDto
            {
                PartyId = d.PartyId, InvoiceId = d.InvoiceId, InvoiceNumber = d.InvoiceNumber, InvoiceDate = d.InvoiceDate, DueDate = d.DueDate,
                DaysOverdue = d.DaysOverdue, Outstanding = Money(d.Outstanding),
            }).ToList(),
        });
    }

    private static async Task<IResult> SalesAnalysisAsync(Guid companyId, Guid branchId, DateOnly? from, DateOnly? to, ContractSalesGrouping? groupBy,
        Guid? patternRevisionId, Guid? customerId, Guid? itemId, ICommercialReportService service, HttpContext http, CancellationToken cancellationToken)
    {
        var r = await service.GetSalesAnalysisAsync(Scope(companyId, branchId, false, http), Required(from, "from"), Required(to, "to"),
            (AppSalesGrouping)(groupBy ?? ContractSalesGrouping.Customer), patternRevisionId, customerId, itemId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new SalesAnalysisResponse
        {
            From = r.From, To = r.To, Grouping = (ContractSalesGrouping)r.Grouping, BaseCurrency = r.BaseCurrency, NetBase = Money(r.NetBase),
            AdditionsBase = Money(r.AdditionsBase), TaxBase = Money(r.TaxBase), TotalBase = Money(r.TotalBase),
            GlRevenueBase = r.GlRevenueBase is { } gl ? Money(gl) : null, RevenueDifference = r.RevenueDifference is { } diff ? Money(diff) : null,
            Rows = r.Rows.Select(x => new SalesAnalysisRowDto
            {
                KeyId = x.KeyId, KeyCode = x.KeyCode, KeyName = x.KeyName, UnitCode = x.UnitCode, Quantity = Money(x.Quantity), DocumentCount = x.DocumentCount,
                NetBase = Money(x.NetBase), AdditionsBase = Money(x.AdditionsBase), TaxBase = Money(x.TaxBase), TotalBase = Money(x.TotalBase),
            }).ToList(),
        });
    }

    private static async Task<IResult> PurchaseAnalysisAsync(Guid companyId, Guid branchId, DateOnly? from, DateOnly? to, ContractPurchaseGrouping? groupBy,
        Guid? patternRevisionId, Guid? supplierId, Guid? itemId, ICommercialReportService service, HttpContext http, CancellationToken cancellationToken)
    {
        var r = await service.GetPurchaseAnalysisAsync(Scope(companyId, branchId, false, http), Required(from, "from"), Required(to, "to"),
            (AppPurchaseGrouping)(groupBy ?? ContractPurchaseGrouping.Supplier), patternRevisionId, supplierId, itemId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new PurchaseAnalysisResponse
        {
            From = r.From, To = r.To, Grouping = (ContractPurchaseGrouping)r.Grouping, BaseCurrency = r.BaseCurrency, NetBase = Money(r.NetBase),
            TaxBase = Money(r.TaxBase), TotalBase = Money(r.TotalBase), StockCostBase = Money(r.StockCostBase),
            Rows = r.Rows.Select(x => new PurchaseAnalysisRowDto
            {
                KeyId = x.KeyId, KeyCode = x.KeyCode, KeyName = x.KeyName, BaseUnitCode = x.BaseUnitCode, BaseQuantity = Money(x.BaseQuantity),
                DocumentCount = x.DocumentCount, NetBase = Money(x.NetBase), TaxBase = Money(x.TaxBase), TotalBase = Money(x.TotalBase),
                StockCostBase = Money(x.StockCostBase),
            }).ToList(),
        });
    }

    private static async Task<IResult> InventoryValuationAsync(Guid companyId, Guid branchId, DateOnly? asOf, Guid? warehouseId,
        ICommercialReportService service, HttpContext http, CancellationToken cancellationToken)
    {
        var r = await service.GetInventoryValuationAsync(Scope(companyId, branchId, false, http), Required(asOf, "asOf"), warehouseId, cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(new InventoryValuationResponse
        {
            AsOf = r.AsOf, BaseCurrency = r.BaseCurrency, TotalValue = Money(r.TotalValue), InventoryAccountIds = r.InventoryAccountIds,
            GlInventoryBalance = Money(r.GlInventoryBalance), Difference = Money(r.Difference),
            Rows = r.Rows.Select(x => new InventoryValuationRowDto
            {
                WarehouseId = x.WarehouseId, WarehouseCode = x.WarehouseCode, ItemId = x.ItemId, ItemCode = x.ItemCode, ItemName = x.ItemName,
                BaseUnitCode = x.BaseUnitCode, Quantity = Money(x.Quantity), Value = Money(x.Value), AverageCost = x.AverageCost is { } c ? Money(c) : null,
            }).ToList(),
        });
    }

    private static async Task<IResult> InventoryMovementsAsync(Guid companyId, Guid branchId, Guid itemId, Guid? warehouseId, DateOnly? from, DateOnly? to,
        ICommercialReportService service, HttpContext http, CancellationToken cancellationToken)
    {
        var r = await service.GetInventoryMovementsAsync(Scope(companyId, branchId, false, http), itemId, warehouseId, Required(from, "from"), Required(to, "to"),
            cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new InventoryMovementReportResponse
        {
            ItemId = r.ItemId, ItemCode = r.ItemCode, ItemName = r.ItemName, BaseUnitCode = r.BaseUnitCode, WarehouseId = r.WarehouseId, From = r.From, To = r.To,
            OpeningQuantity = Money(r.OpeningQuantity), OpeningValue = Money(r.OpeningValue), InQuantity = Money(r.InQuantity), OutQuantity = Money(r.OutQuantity),
            ClosingQuantity = Money(r.ClosingQuantity), ClosingValue = Money(r.ClosingValue), Truncated = r.Truncated,
            Lines = r.Lines.Select(l => new InventoryMovementLineDto
            {
                DateUtc = l.DateUtc, Kind = (ContractMovementKind)(int)l.Kind, DocumentType = l.DocumentType, DocumentId = l.DocumentId,
                DocumentNumber = l.DocumentNumber, WarehouseId = l.WarehouseId, WarehouseCode = l.WarehouseCode, Quantity = Money(l.Quantity),
                Value = Money(l.Value), BalanceQuantity = Money(l.BalanceQuantity), BalanceValue = Money(l.BalanceValue),
            }).ToList(),
        });
    }

    private static async Task<IResult> CashAccountsAsync(Guid companyId, Guid branchId, ICommercialReportService service, HttpContext http,
        CancellationToken cancellationToken) =>
        TypedResults.Ok((await service.GetCashAccountsAsync(Scope(companyId, branchId, false, http), cancellationToken).ConfigureAwait(false))
            .Select(a => new CashAccountDto { AccountId = a.AccountId, Code = a.Code, NameArabic = a.NameArabic, Role = a.Role }).ToList());

    private static async Task<IResult> CashStatementAsync(Guid companyId, Guid branchId, Guid accountId, DateOnly? from, DateOnly? to, bool? consolidated,
        ICommercialReportService service, HttpContext http, CancellationToken cancellationToken)
    {
        var r = await service.GetCashStatementAsync(Scope(companyId, branchId, consolidated ?? false, http), accountId, Required(from, "from"), Required(to, "to"),
            cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new CashStatementResponse
        {
            AccountId = r.AccountId, AccountCode = r.AccountCode, AccountName = r.AccountName, From = r.From, To = r.To, BaseCurrency = r.BaseCurrency,
            OpeningBase = Money(r.OpeningBase), TotalDebit = Money(r.TotalDebit), TotalCredit = Money(r.TotalCredit), ClosingBase = Money(r.ClosingBase),
            Truncated = r.Truncated,
            Lines = r.Lines.Select(l => new CashStatementLineDto
            {
                Date = l.Date, JournalId = l.JournalId, PostingSequence = l.PostingSequence, Description = l.Description, SourceType = l.SourceType,
                SourceId = l.SourceId, SourceNumber = l.SourceNumber, TransactionCurrency = l.TransactionCurrency, TransactionDebit = Money(l.TransactionDebit),
                TransactionCredit = Money(l.TransactionCredit), BaseDebit = Money(l.BaseDebit), BaseCredit = Money(l.BaseCredit), BaseBalance = Money(l.BaseBalance),
            }).ToList(),
        });
    }

    private static AppKind Kind(ContractKind? kind) =>
        kind is { } value && Enum.IsDefined(value) ? (AppKind)value
            : throw new RequestValidationException("reports.kind_required", "'kind' must be Customer or Supplier.", "kind");

    private static StatementAccountLineDto Map(StatementAccountLine l) => new()
    {
        AccountId = l.AccountId, Code = l.Code, NameArabic = l.NameArabic, NameEnglish = l.NameEnglish, Type = (ContractAccountType)l.Type, Amount = Money(l.Amount),
    };

    private static AgingRowDto Map(AppAging r) => new()
    {
        PartyId = r.PartyId, PartyCode = r.PartyCode, PartyName = r.PartyName, NotDue = Money(r.NotDue), Days1To30 = Money(r.Days1To30),
        Days31To60 = Money(r.Days31To60), Days61To90 = Money(r.Days61To90), Over90 = Money(r.Over90), Advance = Money(r.Advance), Net = Money(r.Net),
    };

    private static string Money(decimal value) => FinancialDecimalText.Format(value, FinancialDecimalText.MoneyScale);
}
