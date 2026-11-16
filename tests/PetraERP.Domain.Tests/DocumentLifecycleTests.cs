using PetraERP.Domain.Documents;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Inventory;
using PetraERP.Domain.Purchases;
using PetraERP.Domain.Sales;
using PetraERP.Domain.Treasury;

namespace PetraERP.Domain.Tests;

public sealed class DocumentLifecycleTests
{
    private static readonly AccountingDocumentKind[] Kinds = Enum.GetValues<AccountingDocumentKind>();
    private static readonly DocumentLifecycleState[] States = Enum.GetValues<DocumentLifecycleState>();

    [Fact]
    public void ImmutableStatesAcceptOnlyPostedToReversed()
    {
        foreach (var kind in Kinds)
            foreach (var from in States.Where(DocumentLifecycle.IsImmutable))
                foreach (var to in States)
                {
                    var expected = from == DocumentLifecycleState.Posted && to == DocumentLifecycleState.Reversed;
                    Assert.Equal(expected, DocumentLifecycle.CanTransition(kind, from, to));
                }
    }

    [Fact]
    public void NoDocumentCanMoveBackwardsOrSkipToReversedFromDraft()
    {
        foreach (var kind in Kinds)
        {
            Assert.False(DocumentLifecycle.CanTransition(kind, DocumentLifecycleState.Approved, DocumentLifecycleState.Draft));
            Assert.False(DocumentLifecycle.CanTransition(kind, DocumentLifecycleState.Draft, DocumentLifecycleState.Reversed));
            Assert.False(DocumentLifecycle.CanTransition(kind, DocumentLifecycleState.Posted, DocumentLifecycleState.Draft));
            Assert.False(DocumentLifecycle.CanTransition(kind, DocumentLifecycleState.Reversed, DocumentLifecycleState.Posted));
        }
    }

    [Fact]
    public void ApprovalAndCancellationAreFamilySpecific()
    {
        Assert.False(DocumentLifecycle.CanTransition(AccountingDocumentKind.InventoryDocument, DocumentLifecycleState.Draft, DocumentLifecycleState.Approved));
        Assert.True(DocumentLifecycle.CanTransition(AccountingDocumentKind.SalesInvoice, DocumentLifecycleState.Approved, DocumentLifecycleState.Cancelled));
        foreach (var kind in Kinds.Where(k => k != AccountingDocumentKind.SalesInvoice))
            Assert.False(DocumentLifecycle.CanTransition(kind, DocumentLifecycleState.Draft, DocumentLifecycleState.Cancelled));
        foreach (var kind in Kinds.Where(k => k != AccountingDocumentKind.InventoryDocument))
            Assert.True(DocumentLifecycle.CanTransition(kind, DocumentLifecycleState.Approved, DocumentLifecycleState.Posted));
    }

    [Fact]
    public void EditingAnImmutableDocumentIsRejected()
    {
        foreach (var kind in Kinds)
        {
            DocumentLifecycle.EnsureEditable(kind, DocumentLifecycleState.Draft);
            DocumentLifecycle.EnsureEditable(kind, DocumentLifecycleState.Approved);
            Assert.Throws<InvalidOperationException>(() => DocumentLifecycle.EnsureEditable(kind, DocumentLifecycleState.Posted));
            Assert.Throws<InvalidOperationException>(() => DocumentLifecycle.EnsureTransition(kind, DocumentLifecycleState.Posted, DocumentLifecycleState.Approved));
        }
    }

    [Fact]
    public void EveryDomainStatusMapsToACanonicalState()
    {
        foreach (var s in Enum.GetValues<SalesInvoiceStatus>()) _ = DocumentLifecycle.From(s);
        foreach (var s in Enum.GetValues<PurchaseInvoiceStatus>()) _ = DocumentLifecycle.From(s);
        foreach (var s in Enum.GetValues<VoucherStatus>()) _ = DocumentLifecycle.From(s);
        foreach (var s in Enum.GetValues<InventoryDocumentState>()) _ = DocumentLifecycle.From(s);
        foreach (var s in Enum.GetValues<JournalEntryState>()) _ = DocumentLifecycle.From(s);
        Assert.Equal(DocumentLifecycleState.Draft, DocumentLifecycle.From(JournalEntryState.Validated));
        Assert.Equal(DocumentLifecycleState.Cancelled, DocumentLifecycle.From(SalesInvoiceStatus.Cancelled));
    }
}
