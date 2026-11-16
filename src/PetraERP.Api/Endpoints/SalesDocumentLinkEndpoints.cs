using PetraERP.Api.Errors;
using PetraERP.Api.Security;
using PetraERP.Application.Sales;
using PetraERP.Application.Security;
using PetraERP.Contracts.Financial;
using PetraERP.Contracts.Sales;

namespace PetraERP.Api.Endpoints;

internal static class SalesDocumentLinkEndpoints
{
    internal static void MapSalesDocumentLinkEndpoints(this RouteGroupBuilder api)
    {
        var links = api.MapGroup("/companies/{companyId:guid}/branches/{branchId:guid}/sales/links")
            .WithTags("SalesDocumentLinks").RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.SalesInvoicesView));
        Map(links, "customers", "ListCustomerSalesLinks", SalesLinkSource.Customer, PermissionCatalog.PartiesView);
        Map(links, "items", "ListItemSalesLinks", SalesLinkSource.Item, PermissionCatalog.InventoryItemsView);
        Map(links, "journals", "ListJournalSalesLinks", SalesLinkSource.Journal, PermissionCatalog.JournalsView);
        Map(links, "movements", "ListMovementSalesLinks", SalesLinkSource.Movement, PermissionCatalog.InventoryMovementsView);
        Map(links, "invoices", "ListInvoiceMovementLinks", SalesLinkSource.Invoice, PermissionCatalog.InventoryMovementsView);
    }

    private static void Map(RouteGroupBuilder group, string path, string operation, SalesLinkSource source, string permission)
    {
        group.MapGet($"/{path}/{{sourceId:guid}}", async (Guid companyId, Guid branchId, Guid sourceId,
            ISalesDocumentLinkService service, CancellationToken cancellationToken, int page = 1, int pageSize = 50) =>
        {
            var result = await service.GetAsync(companyId, branchId, source, sourceId, page, pageSize, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(new SalesDocumentLinkPageResponse
            {
                Page = result.Page, PageSize = result.PageSize, TotalCount = result.TotalCount,
                Items = result.Items.Select(r => new SalesDocumentLinkDto
                {
                    Id = r.Id, ItemId = r.ItemId, Number = r.Number, DateUtc = r.DateUtc, Description = r.Description,
                    Amount = FinancialDecimalText.Format(r.Amount, FinancialDecimalText.MoneyScale), CurrencyCode = r.CurrencyCode,
                    Quantity = r.Quantity is { } quantity ? FinancialDecimalText.Format(quantity, FinancialDecimalText.MoneyScale) : null,
                }).ToArray(),
            });
        }).WithName(operation).WithSummary("Resolves paged authoritative Sales links in the authorized company and branch.")
            .RequireAuthorization(PetraPolicies.Permission(permission)).Produces<SalesDocumentLinkPageResponse>().ProducesStandardProblems();
    }
}
