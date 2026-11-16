using PetraERP.Application.Common;
using PetraERP.Application.Reports.Professional;

namespace PetraERP.Application.Tests;

public sealed class ProfessionalReportCatalogTests
{
    private static ProfessionalReportRequest Ledger() => new()
    {
        Kind = ProfessionalReportKind.AccountLedger, FiscalYearId = Guid.NewGuid(), AccountId = Guid.NewGuid(),
        From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 12, 31), Sources = [ProfessionalReportSource.UnclassifiedJournal],
    };

    [Fact]
    public void CatalogNeverClaimsManualOriginFromMissingBusinessLink()
    {
        var catalog = ProfessionalReportCatalog.Define(ProfessionalReportKind.AccountLedger);
        Assert.False(catalog.Sources.Single(s => s.Source == ProfessionalReportSource.ManualJournal).Supported);
        Assert.True(catalog.Sources.Single(s => s.Source == ProfessionalReportSource.UnclassifiedJournal).Supported);
        Assert.Throws<RequestValidationException>(() => ProfessionalReportCatalog.Validate(Ledger() with { Sources = [ProfessionalReportSource.ManualJournal] }));
    }

    [Fact]
    public void TypedAllowlistRejectsUnknownColumnsAndCrossCurrencyTotals()
    {
        Assert.Throws<RequestValidationException>(() => ProfessionalReportCatalog.Validate(Ledger() with { Fields = [(ProfessionalReportField)999] }));
        Assert.Throws<RequestValidationException>(() => ProfessionalReportCatalog.Validate(Ledger() with { CurrencyBasis = ProfessionalCurrencyBasis.Transaction }));
        Assert.Throws<RequestValidationException>(() => ProfessionalReportCatalog.Validate(Ledger() with { IncludeAging = true }));
        Assert.Throws<RequestValidationException>(() => ProfessionalReportCatalog.Validate(Ledger() with { Grouping = (ProfessionalReportGrouping)99 }));
    }

    [Fact]
    public void ArabicFiltersRemainLiteralAndSelectedColumnsAreIndependentOfBalances()
    {
        const string text = "أحمد [%]_ مُحَمَّد";
        var request = ProfessionalReportCatalog.Validate(Ledger() with { DescriptionContains = text, DescriptionNotContains = "غير مسدد", Fields = [ProfessionalReportField.Date] });
        Assert.Equal(text, request.DescriptionContains);
        Assert.Single(request.Fields);
        Assert.Equal(ProfessionalBalancePresentation.Actual, request.BalancePresentation);
        var trial = ProfessionalReportCatalog.Define(ProfessionalReportKind.TrialBalance);
        Assert.All(new[] { ProfessionalReportField.OpeningDebit, ProfessionalReportField.OpeningCredit, ProfessionalReportField.ClosingDebit, ProfessionalReportField.ClosingCredit },
            field => Assert.True(trial.Fields.Single(f => f.Field == field).DefaultSelected));
    }

    [Fact]
    public void PartyDetailsAndAgingRequireSingleCurrencyAndCorrectPartyRoleSource()
    {
        var request = Ledger() with
        {
            Kind = ProfessionalReportKind.SupplierStatement, AccountId = null, PartyId = Guid.NewGuid(), CurrencyBasis = ProfessionalCurrencyBasis.Transaction,
            CurrencyCode = " syp ", Sources = [ProfessionalReportSource.PurchaseInvoice, ProfessionalReportSource.PaymentVoucher, ProfessionalReportSource.Settlement],
            IncludeInvoiceDetails = true, IncludeAging = true,
        };
        Assert.Equal("SYP", ProfessionalReportCatalog.Validate(request).CurrencyCode);
        Assert.Throws<RequestValidationException>(() => ProfessionalReportCatalog.Validate(request with { Sources = [ProfessionalReportSource.SalesInvoice] }));
        Assert.Throws<RequestValidationException>(() => ProfessionalReportCatalog.Validate(request with { CostCenterId = Guid.NewGuid() }));
    }
}
