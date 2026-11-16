using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.GeneralLedger.Templates;

namespace PetraERP.Application.Tests;

public sealed class ChartOfAccountsTemplateTests
{
    [Fact]
    public void GenericPetraTemplateIsValidAndHasRequiredOperationalAccounts()
    {
        var template = ChartOfAccountsTemplates.Generic;
        Assert.NotNull(template);
        Assert.Equal("PETRA-GENERIC", template.TemplateCode);
        Assert.Equal("ZZ", template.CountryCode);

        // Validation produces zero errors
        var errors = template.Validate();
        Assert.Empty(errors);

        // Topological ordering succeeds and root accounts come first
        var ordered = template.OrderedAccounts();
        Assert.Equal(17, ordered.Count);
        Assert.Null(ordered[0].ParentCode); // Root account "1"

        // Default account roles map to valid posting leaf accounts
        var roleKeys = new[] { "Cash", "Bank", "Receivable", "Inventory", "Payable", "Revenue", "CostOfGoodsSold", "Expense", "FxGain", "FxLoss" };
        foreach (var key in roleKeys)
        {
            Assert.Contains(key, template.DefaultAccountRoles.Keys);
            var accountCode = template.DefaultAccountRoles[key];
            var account = template.Accounts.Single(a => a.AccountCode == accountCode);
            Assert.True(account.PostingAllowed, $"Account {accountCode} mapped to role {key} must allow posting.");
        }
    }

    [Fact]
    public void OfficialNationalTemplatesWithoutVerifiedSourceAreNotFabricated()
    {
        // Must NOT invent or fabricate official national charts
        Assert.Null(ChartOfAccountsTemplates.Find("SYRIA-OFFICIAL"));
        Assert.Null(ChartOfAccountsTemplates.Find("EGYPT-OFFICIAL"));
        Assert.Null(ChartOfAccountsTemplates.Find("SY-SIMPLIFIED"));

        // Only the explicitly declared generic starter exists in the catalog
        Assert.Single(ChartOfAccountsTemplates.All);
        Assert.Equal(ChartOfAccountsTemplates.GenericCode, ChartOfAccountsTemplates.All[0].TemplateCode);
    }

    [Fact]
    public void TemplateValidationRejectsDuplicateAccountCodes()
    {
        var invalid = new ChartOfAccountsTemplate(
            Guid.NewGuid(), "DUP-TEST", "دليل مكرر", "Duplicate Test", "ZZ", 1, "Test",
            [
                new("1", "الأصول", "Assets", null, AccountType.Assets, false),
                new("1", "الأصول مكررة", "Assets Duplicate", null, AccountType.Assets, false),
            ],
            new Dictionary<string, string>());

        var errors = invalid.Validate();
        Assert.Contains(errors, e => e.StartsWith("template.code_duplicate:1", StringComparison.Ordinal));
    }

    [Fact]
    public void TemplateValidationRejectsParentCycles()
    {
        var cyclic = new ChartOfAccountsTemplate(
            Guid.NewGuid(), "CYCLE-TEST", "دليل حلقي", "Cyclic Test", "ZZ", 1, "Test",
            [
                new("1", "حساب 1", "Account 1", "2", AccountType.Assets, false),
                new("2", "حساب 2", "Account 2", "1", AccountType.Assets, false),
            ],
            new Dictionary<string, string>());

        var errors = cyclic.Validate();
        Assert.Contains(errors, e => e.StartsWith("template.parent_cycle", StringComparison.Ordinal));
    }

    [Fact]
    public void TemplateValidationRejectsPostingAccountAsParent()
    {
        var invalidParent = new ChartOfAccountsTemplate(
            Guid.NewGuid(), "PARENT-TEST", "دليل أب غير صالح", "Parent Test", "ZZ", 1, "Test",
            [
                new("1", "حساب رئيسي يقبل الترحيل", "Posting Parent", null, AccountType.Assets, true), // PostingAllowed = true!
                new("11", "حساب فرعي", "Child", "1", AccountType.Assets, true),
            ],
            new Dictionary<string, string>());

        var errors = invalidParent.Validate();
        Assert.Contains(errors, e => e.StartsWith("template.parent_invalid:11", StringComparison.Ordinal));
    }

    [Fact]
    public void TemplateValidationRejectsMissingParentReferences()
    {
        var brokenRef = new ChartOfAccountsTemplate(
            Guid.NewGuid(), "BROKEN-TEST", "دليل مكسور", "Broken Test", "ZZ", 1, "Test",
            [
                new("1101", "حساب بدون أب", "Orphan Child", "9999", AccountType.Assets, true),
            ],
            new Dictionary<string, string>());

        var errors = brokenRef.Validate();
        Assert.Contains(errors, e => e.StartsWith("template.parent_missing:1101", StringComparison.Ordinal));
    }
}
