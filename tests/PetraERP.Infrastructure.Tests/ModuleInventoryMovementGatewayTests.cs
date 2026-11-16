using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.Posting;
using PetraERP.Application.Security;
using PetraERP.Infrastructure.Modules;
using PetraERP.Modules.Abstractions;

namespace PetraERP.Infrastructure.Tests;

/// <summary>
/// The argument and authority checks of the module stock gateway run before any service is touched, so they are
/// verifiable without a database. The stock movement itself is the built-in warehouse transfer document.
/// </summary>
public sealed class ModuleInventoryMovementGatewayTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static SqlInventoryMovementGateway Gateway() => new(null!, null!);

    private static ModuleRequestContext Context(params string[] granted) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "ar", new HashSet<string>(granted));

    private static InventoryMovementRequest Movement(decimal quantity = 5m, Guid? from = null, Guid? to = null, string reason = "restock") =>
        new(Guid.NewGuid(), from ?? Guid.NewGuid(), to ?? Guid.NewGuid(), Guid.NewGuid(), quantity, new DateOnly(2026, 3, 10), reason, Guid.NewGuid());

    [Fact]
    public async Task MoveRequiresTheTransferPermissionGrantedToTheModule()
    {
        await Assert.ThrowsAsync<PostingAuthorizationException>(() => Gateway().MoveAsync(Context(), Movement(), Cancellation));
        await Assert.ThrowsAsync<PostingAuthorizationException>(() => Gateway().MoveAsync(Context(PermissionCatalog.JournalsPost), Movement(), Cancellation));
    }

    [Fact]
    public async Task MoveRejectsAnInvalidRequestBeforeTouchingStock()
    {
        var granted = Context(PermissionCatalog.InventoryTransfersManage);
        var same = Guid.NewGuid();
        await Assert.ThrowsAsync<BusinessRuleException>(() => Gateway().MoveAsync(granted, Movement(from: same, to: same), Cancellation));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Gateway().MoveAsync(granted, Movement(quantity: 0m), Cancellation));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Gateway().MoveAsync(granted, Movement(quantity: -3m), Cancellation));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Gateway().MoveAsync(granted, Movement(reason: "  "), Cancellation));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Gateway().MoveAsync(granted, Movement() with { IdempotencyKey = Guid.Empty }, Cancellation));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Gateway().MoveAsync(granted, Movement() with { ItemId = Guid.Empty }, Cancellation));
    }

    [Fact]
    public async Task QueryRequiresACompleteScope()
    {
        var incomplete = Context() with { BranchId = Guid.Empty };
        await Assert.ThrowsAsync<BusinessRuleException>(() => Gateway().QueryBalanceAsync(incomplete, Guid.NewGuid(), Guid.NewGuid(), Cancellation));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Gateway().QueryBalanceAsync(Context(), Guid.Empty, Guid.NewGuid(), Cancellation));
    }
}
