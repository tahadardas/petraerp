using PetraERP.Domain.Patterns;
using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.Tests;

public sealed class PatternDomainTests
{
    [Fact]
    public void RunnableOperationsCatalogRules()
    {
        Assert.True(BusinessOperationCatalog.IsRunnable(BusinessOperationKind.ManualJournal));
        Assert.True(BusinessOperationCatalog.IsRunnable(BusinessOperationKind.SalesInvoice));
        Assert.False(BusinessOperationCatalog.IsRunnable(BusinessOperationKind.SalesReturn));
        Assert.True(BusinessOperationCatalog.IsRunnable(BusinessOperationKind.PurchaseInvoice));
        Assert.False(BusinessOperationCatalog.IsRunnable(BusinessOperationKind.PurchaseReturn));
        Assert.False(BusinessOperationCatalog.IsRunnable(BusinessOperationKind.InventoryIssue));
        Assert.False(BusinessOperationCatalog.IsRunnable(BusinessOperationKind.InventoryReceipt));
        Assert.True(BusinessOperationCatalog.IsRunnable(BusinessOperationKind.CashReceipt));
        Assert.True(BusinessOperationCatalog.IsRunnable(BusinessOperationKind.CashPayment));
        Assert.False(BusinessOperationCatalog.IsRunnable(BusinessOperationKind.CashJournal));

        Assert.Equal(BusinessOperationCategory.Invoice, BusinessOperationCatalog.CategoryOf(BusinessOperationKind.SalesInvoice));
        Assert.Equal(BusinessOperationCategory.Voucher, BusinessOperationCatalog.CategoryOf(BusinessOperationKind.CashReceipt));
        Assert.Equal(BusinessOperationCategory.Journal, BusinessOperationCatalog.CategoryOf(BusinessOperationKind.ManualJournal));
        Assert.Equal(BusinessOperationCategory.Inventory, BusinessOperationCatalog.CategoryOf(BusinessOperationKind.InventoryIssue));
    }

    [Fact]
    public void DocumentPatternRejectsPublishingNonRunnableOperation()
    {
        var pattern = new DocumentPattern(
            Guid.NewGuid(),
            Guid.NewGuid(),
            BusinessOperationKind.SalesReturn, // Not runnable yet
            EntityCode.Create("SR-CONCRETE"),
            BilingualName.Create("مرتجع بيتون", "Sales Return Concrete"),
            "SRC");

        var rev = pattern.AddDraftRevision(Guid.NewGuid(), "{}", Guid.NewGuid(), AuditReason.Create("Initial"), DateTime.UtcNow);
        rev.Validate();

        var ex = Assert.Throws<InvalidOperationException>(() => pattern.PublishRevision(rev.Id, DateTime.UtcNow));
        Assert.Equal(RuleViolation.PatternOperationNotRunnable, RuleViolation.CodeOf(ex));
    }

    [Fact]
    public void DocumentPatternPublishesRunnableOperationAndRetiresPreviousRevision()
    {
        var pattern = new DocumentPattern(
            Guid.NewGuid(),
            Guid.NewGuid(),
            BusinessOperationKind.ManualJournal,
            EntityCode.Create("MAN-GL"),
            BilingualName.Create("قيد عام", "General Journal"),
            "GL");

        var t1 = DateTime.UtcNow;
        var rev1 = pattern.AddDraftRevision(Guid.NewGuid(), "{\"v\":1}", Guid.NewGuid(), AuditReason.Create("Draft 1"), t1);
        pattern.PublishRevision(rev1.Id, t1);

        Assert.Equal(PatternRevisionState.Published, rev1.State);
        Assert.Equal(t1, rev1.EffectiveFromUtc);
        Assert.Null(rev1.EffectiveToUtc);
        Assert.Equal(rev1.Id, pattern.ActiveRevision?.Id);

        // Cannot mutate published revision
        var ex = Assert.Throws<InvalidOperationException>(() => rev1.UpdateDraft("{\"v\":1.1}", Guid.NewGuid(), AuditReason.Create("Edit")));
        Assert.Equal(RuleViolation.PatternRevisionImmutable, RuleViolation.CodeOf(ex));

        // Create second draft revision
        var t2 = t1.AddHours(2);
        var rev2 = pattern.AddDraftRevision(Guid.NewGuid(), "{\"v\":2}", Guid.NewGuid(), AuditReason.Create("Draft 2"), t2);
        Assert.Equal(2, rev2.RevisionNumber);
        Assert.Equal(PatternRevisionState.Draft, rev2.State);

        pattern.PublishRevision(rev2.Id, t2);

        Assert.Equal(PatternRevisionState.Retired, rev1.State);
        Assert.Equal(t2, rev1.EffectiveToUtc);
        Assert.Equal(PatternRevisionState.Published, rev2.State);
        Assert.Equal(t2, rev2.EffectiveFromUtc);
        Assert.Equal(rev2.Id, pattern.ActiveRevision?.Id);
    }

    [Fact]
    public void PostingRuleResolverEnforcesStrictPrecedenceOrder()
    {
        var enforcedAccount = Guid.NewGuid();
        var overrideAccount = Guid.NewGuid();
        var partyAccount = Guid.NewGuid();
        var patternAccount = Guid.NewGuid();
        var companyAccount = Guid.NewGuid();

        var config = new PatternConfiguration
        {
            AccountMappings =
            [
                new PatternAccountMapping { Role = "Revenue", AccountId = patternAccount, IsRequired = true, AllowOverride = true }
            ],
            OptionOverridesWhitelist = ["Revenue"],
        };

        // 1. Enforced beats everything
        var r1 = PostingRuleResolver.Resolve("Revenue", config, enforcedAccountId: enforcedAccount, documentOverrideAccountId: overrideAccount,
            partyContractAccountId: partyAccount, companyDefaultAccountId: companyAccount);
        Assert.Equal(enforcedAccount, r1.AccountId);
        Assert.Equal(AccountResolutionSource.EnforcedRule, r1.Source);

        // 2. Override beats party, pattern, company (when whitelisted)
        var r2 = PostingRuleResolver.Resolve("Revenue", config, documentOverrideAccountId: overrideAccount, partyContractAccountId: partyAccount,
            companyDefaultAccountId: companyAccount);
        Assert.Equal(overrideAccount, r2.AccountId);
        Assert.Equal(AccountResolutionSource.DocumentOverride, r2.Source);

        // 3. Party beats pattern, company
        var r3 = PostingRuleResolver.Resolve("Revenue", config, partyContractAccountId: partyAccount, companyDefaultAccountId: companyAccount);
        Assert.Equal(partyAccount, r3.AccountId);
        Assert.Equal(AccountResolutionSource.PartyContract, r3.Source);

        // 4. Pattern beats company
        var r4 = PostingRuleResolver.Resolve("Revenue", config, companyDefaultAccountId: companyAccount);
        Assert.Equal(patternAccount, r4.AccountId);
        Assert.Equal(AccountResolutionSource.PatternDefault, r4.Source);

        // 5. Company fallback when pattern mapping is empty
        var emptyConfig = new PatternConfiguration
        {
            AccountMappings = [new PatternAccountMapping { Role = "Tax", AccountId = Guid.Empty, IsRequired = false }]
        };
        var r5 = PostingRuleResolver.Resolve("Tax", emptyConfig, companyDefaultAccountId: companyAccount);
        Assert.Equal(companyAccount, r5.AccountId);
        Assert.Equal(AccountResolutionSource.CompanyDefault, r5.Source);

        // 6. Non-whitelisted override is rejected
        var lockedConfig = new PatternConfiguration
        {
            AccountMappings = [new PatternAccountMapping { Role = "Inventory", AccountId = patternAccount, IsRequired = true, AllowOverride = false }],
            OptionOverridesWhitelist = [],
        };
        var ex = Assert.Throws<InvalidOperationException>(() =>
            PostingRuleResolver.Resolve("Inventory", lockedConfig, documentOverrideAccountId: overrideAccount));
        Assert.Equal(RuleViolation.PatternOverrideNotAllowed, RuleViolation.CodeOf(ex));

        // 7. Unresolved required account throws
        var unmappedConfig = new PatternConfiguration { AccountMappings = [] };
        var ex2 = Assert.Throws<InvalidOperationException>(() =>
            PostingRuleResolver.Resolve("Receivable", unmappedConfig));
        Assert.Equal(RuleViolation.PatternAccountUnresolved, RuleViolation.CodeOf(ex2));
    }
}
