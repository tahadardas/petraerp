using PetraERP.Api.Security;
using PetraERP.Application.Common;
using PetraERP.Application.Inventory;
using PetraERP.Application.Security;
using PetraERP.Contracts.Financial;
using PetraERP.Contracts.Inventory;

namespace PetraERP.Api.Endpoints;

internal static class InventoryDocumentEndpoints
{
    internal static void MapInventoryDocumentEndpoints(this RouteGroupBuilder api)
    {
        var inventory = api.MapGroup("/companies/{companyId:guid}/branches/{branchId:guid}/inventory")
            .WithTags("Inventory documents").RequireAuthorization(PetraPolicies.Permission(PermissionCatalog.InventoryMovementsView));
        inventory.MapGet("/documents", ListAsync).WithName("ListInventoryDocuments").Produces<InventoryDocumentPageResponse>().ProducesStandardProblems();
        inventory.MapGet("/documents/{id:guid}", GetAsync).WithName("GetInventoryDocument").Produces<InventoryDocumentDto>().ProducesStandardProblems();
        inventory.MapPost("/documents", CreateAsync).WithName("CreateInventoryDocument").Validate<SaveInventoryDocumentRequest>().Produces<InventoryDocumentDto>().ProducesStandardProblems();
        inventory.MapPut("/documents/{id:guid}", UpdateAsync).WithName("UpdateInventoryDocument").Validate<SaveInventoryDocumentRequest>().Produces<InventoryDocumentDto>().ProducesStandardProblems();
        inventory.MapPost("/documents/{id:guid}/post", PostAsync).WithName("PostInventoryDocument").Validate<InventoryDocumentActionRequest>().Produces<InventoryDocumentDto>().ProducesStandardProblems();
        inventory.MapPost("/documents/{id:guid}/reverse", ReverseAsync).WithName("ReverseInventoryDocument").Validate<InventoryDocumentActionRequest>().Produces<InventoryDocumentDto>().ProducesStandardProblems();
        inventory.MapGet("/balances", BalancesAsync).WithName("ListInventoryBalances").Produces<IReadOnlyList<InventoryBalanceDto>>().ProducesStandardProblems();
        inventory.MapGet("/movement-audit", MovementsAsync).WithName("ListInventoryMovementAudit").Produces<InventoryMovementAuditPageResponse>().ProducesStandardProblems();
    }

    private static InventoryDocumentScope Scope(Guid companyId, Guid branchId, HttpContext http) => new(companyId, branchId, http.RequireSession().UserId);
    private static async Task<IResult> ListAsync(Guid companyId, Guid branchId, string? search, IInventoryDocumentService service, HttpContext http,
        CancellationToken token, int page = 1, int pageSize = 50)
    {
        var result = await service.ListAsync(Scope(companyId, branchId, http), search, page, pageSize, token).ConfigureAwait(false);
        return TypedResults.Ok(new InventoryDocumentPageResponse { Page = result.Page, PageSize = result.PageSize, TotalCount = result.TotalCount, Items = result.Items.Select(Map).ToList() });
    }
    private static async Task<IResult> GetAsync(Guid companyId, Guid branchId, Guid id, IInventoryDocumentService service, HttpContext http, CancellationToken token) =>
        TypedResults.Ok(Map(await service.GetAsync(Scope(companyId, branchId, http), id, token).ConfigureAwait(false)));
    private static async Task<IResult> CreateAsync(Guid companyId, Guid branchId, SaveInventoryDocumentRequest request, IInventoryDocumentService service, HttpContext http, CancellationToken token) =>
        TypedResults.Ok(Map(await service.SaveAsync(Scope(companyId, branchId, http), null, Command(request), token).ConfigureAwait(false)));
    private static async Task<IResult> UpdateAsync(Guid companyId, Guid branchId, Guid id, SaveInventoryDocumentRequest request, IInventoryDocumentService service, HttpContext http, CancellationToken token) =>
        TypedResults.Ok(Map(await service.SaveAsync(Scope(companyId, branchId, http), id, Command(request), token).ConfigureAwait(false)));
    private static async Task<IResult> PostAsync(Guid companyId, Guid branchId, Guid id, InventoryDocumentActionRequest request, IInventoryDocumentService service, HttpContext http, CancellationToken token) =>
        TypedResults.Ok(Map(await service.PostAsync(Scope(companyId, branchId, http), id, Action(request), token).ConfigureAwait(false)));
    private static async Task<IResult> ReverseAsync(Guid companyId, Guid branchId, Guid id, InventoryDocumentActionRequest request, IInventoryDocumentService service, HttpContext http, CancellationToken token) =>
        TypedResults.Ok(Map(await service.ReverseAsync(Scope(companyId, branchId, http), id, Action(request), token).ConfigureAwait(false)));
    private static async Task<IResult> BalancesAsync(Guid companyId, Guid branchId, Guid? warehouseId, Guid? itemId, IInventoryDocumentService service, HttpContext http, CancellationToken token) =>
        TypedResults.Ok((await service.BalancesAsync(Scope(companyId, branchId, http), warehouseId, itemId, token).ConfigureAwait(false))
            .Select(r => new InventoryBalanceDto { WarehouseId = r.WarehouseId, ItemId = r.ItemId, WarehouseCode = r.WarehouseCode, ItemCode = r.ItemCode,
                Quantity = Money(r.Quantity), Value = Money(r.Value), LastSequence = r.LastSequence, AuditQuantity = Optional(r.AuditQuantity), AuditValue = Optional(r.AuditValue), MatchesAudit = r.MatchesAudit }).ToList());
    private static async Task<IResult> MovementsAsync(Guid companyId, Guid branchId, Guid? warehouseId, Guid? itemId, DateTimeOffset? from, DateTimeOffset? to,
        IInventoryDocumentService service, HttpContext http, CancellationToken token, int page = 1, int pageSize = 100)
    {
        var r = await service.MovementsAsync(Scope(companyId, branchId, http), warehouseId, itemId, from, to, page, pageSize, token).ConfigureAwait(false);
        return TypedResults.Ok(new InventoryMovementAuditPageResponse { Page = r.Page, PageSize = r.PageSize, TotalCount = r.TotalCount, Items = r.Items.Select(m => new InventoryMovementAuditDto
        {
            Id = m.Id, Sequence = m.Sequence, WarehouseId = m.WarehouseId, ItemId = m.ItemId, Kind = (StockMovementKind)m.Kind, Quantity = Money(m.Quantity), Value = Money(m.Value),
            WarehouseCode = m.WarehouseCode, ItemCode = m.ItemCode,
            QuantityBefore = Optional(m.QuantityBefore), QuantityAfter = Optional(m.QuantityAfter), ValueBefore = Optional(m.ValueBefore), ValueAfter = Optional(m.ValueAfter),
            DocumentType = m.DocumentType, DocumentId = m.DocumentId, Number = m.Number, ActorId = m.ActorId, DateUtc = m.DateUtc, RecordedAtUtc = m.RecordedAtUtc, Reason = m.Reason, ReversesMovementId = m.ReversesMovementId
        }).ToList() });
    }
    private static SaveInventoryDocumentCommand Command(SaveInventoryDocumentRequest r) => new((Domain.Inventory.InventoryDocumentKind)r.Kind,
        r.WarehouseId, r.TargetWarehouseId, r.DateUtc, r.Reason, r.CostCenterId, r.InventoryAccountId, r.OffsetAccountId,
        (r.Lines is null || r.Lines.Any(l => l is null) ? throw new RequestValidationException("inventory.lines", "الأسطر مطلوبة ولا تقبل عناصر فارغة / Lines are required and cannot contain null entries.", "lines") : r.Lines)
            .Select(l => new InventoryDocumentLineInput(l.ItemId, l.UnitId, Parse(l.Quantity)) { OpeningUnitCost = string.IsNullOrWhiteSpace(l.OpeningUnitCost) ? null : Parse(l.OpeningUnitCost) }).ToList(), r.ExpectedVersion) { CreationKey = r.CreationKey };
    private static InventoryDocumentAction Action(InventoryDocumentActionRequest r) => new(r.ExpectedVersion, r.IdempotencyKey, r.Reason);
    private static decimal Parse(string text) => FinancialDecimalText.TryParse(text, 4, 15, out var number) && number.HasValue ? number.Value
        : throw new RequestValidationException("inventory.quantity", "كمية غير صالحة / Invalid quantity.", "quantity");
    private static string Money(decimal value) => FinancialDecimalText.Format(value, 4);
    private static string? Optional(decimal? value) => value.HasValue ? Money(value.Value) : null;
    private static InventoryDocumentDto Map(InventoryDocumentResult r) => new()
    {
        Id = r.Id, Number = r.Number, Kind = (InventoryDocumentKind)r.Kind, State = (InventoryDocumentState)r.State,
        IsLegacy = r.IsLegacy, PostReason = r.PostReason, ReverseReason = r.ReverseReason, ReversedBy = r.ReversedBy, ReversedAtUtc = r.ReversedAtUtc,
        WarehouseId = r.WarehouseId, TargetWarehouseId = r.TargetWarehouseId, DateUtc = r.DateUtc, Reason = r.Reason, CostCenterId = r.CostCenterId,
        InventoryAccountId = r.InventoryAccountId, OffsetAccountId = r.OffsetAccountId, JournalId = r.JournalId, ReversalJournalId = r.ReversalJournalId,
        CreatedBy = r.CreatedBy, CreatedAtUtc = r.CreatedAtUtc, PostedBy = r.PostedBy, PostedAtUtc = r.PostedAtUtc, Version = r.Version,
        Lines = r.Lines.Select(l => new InventoryDocumentLineDto { Id = l.Id, ItemId = l.ItemId, ItemCode = l.ItemCode, UnitId = l.UnitId,
            Quantity = Money(l.Quantity), BaseQuantity = Money(l.BaseQuantity), SnapshotQuantity = Optional(l.SnapshotQuantity), SnapshotSequence = l.SnapshotSequence, Cost = Optional(l.Cost), OpeningUnitCost = Optional(l.OpeningUnitCost) }).ToList()
    };
}
