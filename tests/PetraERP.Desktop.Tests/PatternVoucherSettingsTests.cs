using PetraERP.ApiClient;
using PetraERP.Desktop.Patterns;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

/// <summary>AMN-VPAT-GATE-005: the existing pattern card edits the real voucher block and previews what the server runs.</summary>
public sealed class PatternVoucherSettingsTests
{
    private static ShellFixture CreateFixture()
    {
        var fixture = new ShellFixture();
        fixture.User.Permissions = ["Patterns.Documents.View", "Patterns.Documents.Manage", "Patterns.Documents.Publish", "Ledger.Accounts.View"];
        return fixture;
    }

    private static (DocumentPatternResponse Pattern, PatternRevisionResponse Revision) Sample(Guid company, BusinessOperationKind kind, PatternVoucherPolicyDto? voucher)
    {
        var patternId = Guid.NewGuid();
        var revision = new PatternRevisionResponse
        {
            Id = Guid.NewGuid(), PatternId = patternId, RevisionNumber = 1, State = PatternRevisionState.Published, EffectiveFromUtc = DateTimeOffset.UtcNow,
            Configuration = new PatternConfigurationDto
            {
                SchemaVersion = 2, Voucher = voucher,
                NumberingPolicy = new PatternNumberingPolicyDto { Scope = PatternNumberingScope.Branch, Prefix = "PV-", MinimumDigits = 5 },
                ApprovalPolicy = new PatternApprovalPolicyDto { RequiresApproval = true, ThresholdAmount = "50.0000" },
                LineFields = [new PatternFieldRuleDto { FieldName = "CostCenter", IsVisible = true, IsRequired = true, DisplayOrder = 1 }],
            },
        };
        var pattern = new DocumentPatternResponse
        {
            Id = patternId, CompanyId = company, Code = "PV-1", ShortCode = "PV", NameArabic = "سند دفع", NameEnglish = "Payment",
            OperationKind = kind, Group = "Vouchers", IsActive = true, IsDefault = false, IsRunnable = true, Version = "AAAAAAAAB9A=",
            BranchIds = [], ActiveRevision = revision,
        };
        return (pattern, revision);
    }

    private static async Task<(PatternCardViewModel Card, AccountingFixture Accounting, ShellViewModelHandle Shell)> OpenAsync(BusinessOperationKind kind, PatternVoucherPolicyDto? voucher)
    {
        var fixture = CreateFixture();
        var accounting = new AccountingFixture();
        var (pattern, revision) = Sample(fixture.Company.Id, kind, voucher);
        accounting.Patterns.Add(pattern);
        accounting.Revisions.Add(revision);
        var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        await shell.NavigateToAsync("patterns", new OpenPatternCardRequest(pattern.Id!.Value)).ConfigureAwait(true);
        return (Assert.IsType<PatternCardViewModel>(shell.CurrentView), accounting, new ShellViewModelHandle(shell));
    }

    private sealed class ShellViewModelHandle(IDisposable shell) : IDisposable
    {
        public void Dispose() => shell.Dispose();
    }

    [Fact]
    public async Task AVoucherPatternLoadsItsRealVoucherSettingsIntoTheDraft()
    {
        var root = Guid.NewGuid();
        var (card, _, handle) = await OpenAsync(BusinessOperationKind.CashPayment, new PatternVoucherPolicyDto
        {
            CaptionMode = VoucherCaptionMode.FromTo, PostingMode = VoucherPostingMode.ManualOnly, DenyMutationsOnOrBefore = new DateOnly(2026, 10, 31),
            Lookup = new PatternLookupPolicyDto { EligibleAccountRootIds = [root], EligibleCostCenterIds = [] },
        });
        using (handle)
        {
            Assert.True(card.IsVoucherOperation);
            Assert.Equal(VoucherCaptionMode.FromTo, card.DraftCaptionMode);
            Assert.Equal(VoucherPostingMode.ManualOnly, card.DraftPostingMode);
            Assert.Equal(new DateTime(2026, 10, 31), card.DraftCutoffDate);
            Assert.Equal([root], card.DraftEligibleAccountIds);
        }
    }

    [Fact]
    public async Task EditingTheVoucherSettingsCreatesARevisionCarryingThemAndKeepsTheOtherSettings()
    {
        var (card, accounting, handle) = await OpenAsync(BusinessOperationKind.CashPayment, null); // a schema-1 style revision: no block yet
        using (handle)
        {
            Assert.Equal(VoucherPostingMode.InheritFromCompany, card.DraftPostingMode); // defaults shown for a revision without the block
            card.BeginEditCommand.Execute(null);
            var account = Guid.NewGuid();
            card.DraftPostingMode = VoucherPostingMode.ManualOnly;
            card.DraftCaptionMode = VoucherCaptionMode.FromTo;
            card.DraftCutoffDate = new DateTime(2026, 10, 18);
            card.AccountToAdd = account;
            card.AddEligibleAccountCommand.Execute(null);

            // Configuration changes are saved as a new draft revision through the card's revision command.
            Assert.True(card.CreateDraftRevisionCommand.CanExecute(null));
            await card.CreateDraftRevisionCommand.ExecuteAsync(null).ConfigureAwait(true);

            var saved = accounting.Revisions.Last().Configuration!;
            Assert.Equal(2, saved.SchemaVersion);
            Assert.Equal(VoucherPostingMode.ManualOnly, saved.Voucher!.PostingMode);
            Assert.Equal(VoucherCaptionMode.FromTo, saved.Voucher.CaptionMode);
            Assert.Equal(new DateOnly(2026, 10, 18), saved.Voucher.DenyMutationsOnOrBefore);
            Assert.Equal([account], saved.Voucher.Lookup!.EligibleAccountRootIds);            Assert.Equal(VoucherFormKind.GenericVoucher, saved.Voucher.FormKind);
            Assert.False(saved.Voucher.PerLineJournal); Assert.False(saved.Voucher.AutoPost); // reserved options are never sent on
            // Nothing else was lost.
            Assert.Equal("PV-", saved.NumberingPolicy!.Prefix);
            Assert.True(saved.ApprovalPolicy!.RequiresApproval);
            Assert.Equal("50.0000", saved.ApprovalPolicy.ThresholdAmount);
            Assert.True(Assert.Single(saved.LineFields!).IsRequired);
        }
    }

    [Fact]
    public async Task ANonVoucherPatternHasNoVoucherSettingsAndSendsNoBlock()
    {
        var (card, accounting, handle) = await OpenAsync(BusinessOperationKind.SalesInvoice, null);
        using (handle)
        {
            Assert.False(card.IsVoucherOperation);
            card.BeginEditCommand.Execute(null);
            card.DraftNameArabic = "فاتورة معدلة";
            await card.SaveCardCommand.ExecuteAsync(null).ConfigureAwait(true);
            Assert.Null(accounting.Revisions.Last().Configuration!.Voucher); // the server refuses a voucher block on an invoice
        }
    }

    [Fact]
    public async Task ThePreviewIsTheServersEffectiveAnswerNotALocalGuess()
    {
        var (card, _, handle) = await OpenAsync(BusinessOperationKind.CashPayment, new PatternVoucherPolicyDto
        {
            PostingMode = VoucherPostingMode.ManualOnly, DenyMutationsOnOrBefore = new DateOnly(2026, 10, 31),
            Lookup = new PatternLookupPolicyDto { EligibleAccountRootIds = [Guid.NewGuid(), Guid.NewGuid()], EligibleCostCenterIds = [] },
        });
        using (handle)
        {
            Assert.True(card.LoadEffectivePreviewCommand.CanExecute(null));
            await card.LoadEffectivePreviewCommand.ExecuteAsync(null).ConfigureAwait(true);
            Assert.Contains("يدوي فقط", card.EffectivePreviewText);
            Assert.Contains("2026-10-31", card.EffectivePreviewText);
            Assert.Contains("مطلوب من مبلغ 50.0000", card.EffectivePreviewText);
            Assert.Contains("2 جذر حساب", card.EffectivePreviewText);
            Assert.Contains("CostCenter (إلزامي)", card.EffectivePreviewText);
        }
    }

    [Fact]
    public void ARevisionWithoutTheBlockIsDescribedWithTheDefaults()
    {
        var text = PatternCardViewModel.DescribeEffective(new EffectivePatternResponse
        {
            Code = "X-1", RevisionNumber = 3, Configuration = new PatternConfigurationDto { SchemaVersion = 1 },
        });
        Assert.Contains("سند عادي", text);
        Assert.Contains("مدين / دائن", text);
        Assert.Contains("حسب سياسة الشركة", text);
        Assert.Contains("بدون", text);
        Assert.Contains("غير مطلوب", text);
    }
}
