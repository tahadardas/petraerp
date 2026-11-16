using PetraERP.Api.Endpoints;
using PetraERP.Application.Sales;
using Xunit;

namespace PetraERP.Api.Tests;

public sealed class SalesInvoiceSortParsingTests
{
    [Fact]
    public void AbsentSortKeepsTheHistoricalOrder()
    {
        Assert.True(SalesInvoiceEndpoints.TryParseSort(null, null, out var sort));
        Assert.Null(sort);
        Assert.True(SalesInvoiceEndpoints.TryParseSort("  ", null, out sort));
        Assert.Null(sort);
    }

    [Theory]
    [InlineData("date", null, SalesInvoiceSortField.Date, false)]
    [InlineData("Customer", true, SalesInvoiceSortField.Customer, true)]
    [InlineData(" GRANDTOTAL ", false, SalesInvoiceSortField.GrandTotal, false)]
    [InlineData("Status", true, SalesInvoiceSortField.Status, true)]
    public void KnownFieldsAreAcceptedCaseInsensitively(string by, bool? descending, SalesInvoiceSortField field, bool expectedDescending)
    {
        Assert.True(SalesInvoiceEndpoints.TryParseSort(by, descending, out var sort));
        Assert.Equal(new SalesInvoiceSort(field, expectedDescending), sort);
    }

    [Theory]
    [InlineData("nonsense", null)]
    [InlineData("1", null)]
    [InlineData("99", true)]
    [InlineData("Date;DROP", false)]
    [InlineData(null, true)]   // direction without a field is a malformed request, not a silent default
    public void UnknownNumericOrIncompleteSortIsRejected(string? by, bool? descending) =>
        Assert.False(SalesInvoiceEndpoints.TryParseSort(by, descending, out _));
}
