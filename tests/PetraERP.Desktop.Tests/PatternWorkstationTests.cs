using System.Collections.ObjectModel;
using PetraERP.ApiClient;
using PetraERP.Desktop.Accounting;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Patterns;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

public sealed class PatternWorkstationTests
{
    private static ShellFixture CreateFixture(params string[] permissions)
    {
        var fixture = new ShellFixture();
        fixture.User.Permissions = permissions.Length > 0
            ? permissions
            : ["Patterns.Documents.View", "Patterns.Documents.Manage", "Patterns.Documents.Publish", "Ledger.Accounts.View"];
        return fixture;
    }

    private static (DocumentPatternResponse Pattern, PatternRevisionResponse Revision) CreateSamplePattern(
        Guid companyId,
        string code = "PAT-01",
        string nameAr = "نمط فواتير نقدية",
        BusinessOperationKind kind = BusinessOperationKind.CashReceipt,
        string? shortCode = null,
        string? nameEn = null)
    {
        shortCode ??= code.Contains('-') ? code.Split('-')[0] : code;
        nameEn ??= $"Pattern {code}";
        var patternId = Guid.NewGuid();
        var revId = Guid.NewGuid();
        var revision = new PatternRevisionResponse
        {
            Id = revId,
            PatternId = patternId,
            RevisionNumber = 1,
            State = PatternRevisionState.Published,
            EffectiveFromUtc = DateTimeOffset.UtcNow,
            Configuration = new PatternConfigurationDto
            {
                AllowedPaymentMethods = ["Cash", "Card"],
                DefaultCurrencyCode = "JOD",
                NumberingPolicy = new PatternNumberingPolicyDto
                {
                    Scope = PatternNumberingScope.Branch,
                    Prefix = $"{shortCode}-",
                    MinimumDigits = 5,
                },
                LineFields =
                [
                    new PatternFieldRuleDto { FieldName = "CostCenter", IsVisible = true, IsRequired = false, DisplayOrder = 1 },
                ],
            },
        };

        var pattern = new DocumentPatternResponse
        {
            Id = patternId,
            CompanyId = companyId,
            Code = code,
            ShortCode = shortCode,
            NameArabic = nameAr,
            NameEnglish = nameEn,
            OperationKind = kind,
            Group = "Vouchers",
            Icon = "Receipt",
            IsActive = true,
            IsDefault = true,
            IsRunnable = true,
            BranchIds = [],
            Version = "AAAAAAAAB9E=",
            ActiveRevision = revision,
        };

        return (pattern, revision);
    }

    [Fact]
    public async Task DocumentPatternsViewModelLoadsAndFiltersPatterns()
    {
        var fixture = CreateFixture();
        var accounting = new AccountingFixture();
        var (p1, r1) = CreateSamplePattern(fixture.Company.Id, "INV-01", "فاتورة مبيعات نقدية", BusinessOperationKind.SalesInvoice);
        var (p2, r2) = CreateSamplePattern(fixture.Company.Id, "REC-01", "سند قبض نقدي", BusinessOperationKind.CashReceipt);
        var (p3, r3) = CreateSamplePattern(fixture.Company.Id, "JRN-01", "قيد يومية تسوية", BusinessOperationKind.ManualJournal);
        p3.IsActive = false; // inactive

        accounting.Patterns.AddRange([p1, p2, p3]);
        accounting.Revisions.AddRange([r1, r2, r3]);

        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin";
        shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        var vm = shell.Pages.Patterns;
        await shell.NavigateToAsync("patterns", null).ConfigureAwait(true);

        Assert.Equal(3, vm.Patterns.Count);
        Assert.Equal(3, vm.VisiblePatterns.Count);

        // Tab filter: vouchers only
        vm.FilterTab = "vouchers";
        var vouchers = vm.VisiblePatterns;
        Assert.Equal(2, vouchers.Count);
        Assert.All(vouchers, v => Assert.True(v.OperationKind is BusinessOperationKind.CashReceipt or BusinessOperationKind.ManualJournal));

        // Tab filter: invoices only
        vm.FilterTab = "invoices";
        var invoices = vm.VisiblePatterns;
        Assert.Single(invoices);
        Assert.Equal("INV-01", invoices[0].Code);

        // Reset tab to all and search
        vm.FilterTab = "all";
        vm.Search = "قبض";
        var searched = vm.VisiblePatterns;
        Assert.Single(searched);
        Assert.Equal("REC-01", searched[0].Code);

        // Search by shortcode
        vm.Search = "REC";
        Assert.Single(vm.VisiblePatterns);

        // Active only filter
        vm.Search = string.Empty;
        vm.ActiveOnly = true;
        Assert.Equal(2, vm.VisiblePatterns.Count);
        Assert.DoesNotContain(vm.VisiblePatterns, p => p.Code == "JRN-01");
    }

    [Fact]
    public async Task DocumentPatternsViewModelNewAndDuplicateCommandsNavigateToCard()
    {
        var fixture = CreateFixture();
        var accounting = new AccountingFixture();
        var (p1, r1) = CreateSamplePattern(fixture.Company.Id, "INV-01", "فاتورة مبيعات", BusinessOperationKind.SalesInvoice);
        accounting.Patterns.Add(p1);
        accounting.Revisions.Add(r1);

        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin";
        shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        await shell.NavigateToAsync("patterns", null).ConfigureAwait(true);

        var vm = shell.Pages.Patterns;

        // New pattern command navigates
        Assert.True(vm.NewPatternCommand.CanExecute(null));
        await vm.NewPatternCommand.ExecuteAsync(null).ConfigureAwait(true);
        var card = Assert.IsType<PatternCardViewModel>(shell.CurrentView);
        Assert.True(card.IsNewMode);
        Assert.False(card.HasPattern);

        // Back to list, select pattern and duplicate
        await shell.NavigateToAsync("patterns", null).ConfigureAwait(true);
        vm.SelectedPattern = vm.Patterns.First(p => p.Id == p1.Id);
        Assert.True(vm.DuplicatePatternCommand.CanExecute(null));
        await vm.DuplicatePatternCommand.ExecuteAsync(null).ConfigureAwait(true);

        var dupCard = Assert.IsType<PatternCardViewModel>(shell.CurrentView);
        Assert.True(dupCard.IsNewMode);
        Assert.Equal("INV-01-COPY", dupCard.DraftCode);
        Assert.Contains("(نسخة)", dupCard.DraftNameArabic);
    }

    [Fact]
    public async Task PatternCardViewModelSaveNewPatternCreatesPatternAndSwitchesToViewMode()
    {
        var fixture = CreateFixture();
        var accounting = new AccountingFixture();

        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin";
        shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync("patterns", new NewPatternCardRequest()).ConfigureAwait(true);
        var card = Assert.IsType<PatternCardViewModel>(shell.CurrentView);

        Assert.True(card.IsNewMode);
        card.DraftCode = "NEW-PAT";
        card.DraftShortCode = "NP";
        card.DraftNameArabic = "نمط تجريبي جديد";
        card.DraftOperationKind = BusinessOperationKind.ManualJournal;
        card.DraftGroup = "General";
        card.DraftPrefix = "NP-";
        card.DraftMinimumDigits = 4;

        Assert.True(card.SaveCardCommand.CanExecute(null));
        await card.SaveCardCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Empty(shell.Operation.Error);
        Assert.True(card.IsViewMode);
        Assert.True(card.HasPattern);
        Assert.Equal("NEW-PAT", card.Pattern!.Code);
        Assert.Equal("نمط تجريبي جديد", card.Pattern.NameArabic);
        Assert.Single(accounting.Patterns);
    }

    [Fact]
    public async Task PatternCardViewModelEditExistingPatternUpdatesDraftAndCreatesRevision()
    {
        var fixture = CreateFixture();
        var accounting = new AccountingFixture();
        var (p1, r1) = CreateSamplePattern(fixture.Company.Id, "EDIT-01", "نمط قابل للتعديل", BusinessOperationKind.ManualJournal);
        accounting.Patterns.Add(p1);
        accounting.Revisions.Add(r1);

        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin";
        shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync("patterns", new OpenPatternCardRequest(p1.Id!.Value)).ConfigureAwait(true);
        var card = Assert.IsType<PatternCardViewModel>(shell.CurrentView);

        Assert.True(card.IsViewMode);
        Assert.Equal("EDIT-01", card.Pattern!.Code);

        // Enter edit mode
        Assert.True(card.BeginEditCommand.CanExecute(null));
        card.BeginEditCommand.Execute(null);
        Assert.True(card.IsEditMode);

        card.DraftNameArabic = "نمط معدل بعد التحرير";
        card.DraftShortCode = "EDT";
        Assert.True(card.HasUnsavedChanges);

        // Save edit
        Assert.True(card.SaveCardCommand.CanExecute(null));
        await card.SaveCardCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Empty(shell.Operation.Error);
        Assert.True(card.IsViewMode);
        Assert.Equal("نمط معدل بعد التحرير", card.Pattern.NameArabic);
        Assert.Equal("EDT", card.Pattern.ShortCode);
    }

    [Fact]
    public async Task PatternCardViewModelPublishAndRetireExecutesLifecycleTransitions()
    {
        var fixture = CreateFixture();
        var accounting = new AccountingFixture();
        var (p1, r1) = CreateSamplePattern(fixture.Company.Id, "LIFE-01", "نمط دورة الحياة", BusinessOperationKind.ManualJournal);
        r1.State = PatternRevisionState.Draft;
        p1.ActiveRevision = r1;
        accounting.Patterns.Add(p1);
        accounting.Revisions.Add(r1);

        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin";
        shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync("patterns", new OpenPatternCardRequest(p1.Id!.Value)).ConfigureAwait(true);
        var card = Assert.IsType<PatternCardViewModel>(shell.CurrentView);

        // Publish active revision
        Assert.True(card.PublishRevisionCommand.CanExecute(null));
        fixture.Confirm = true;
        await card.PublishRevisionCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal("D_ConfirmPublishPattern", fixture.LastConfirmation);
        Assert.Equal(PatternRevisionState.Published, card.Pattern!.ActiveRevision!.State);

        // Retire pattern
        Assert.True(card.RetireCommand.CanExecute(null));
        await card.RetireCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal("D_ConfirmRetirePattern", fixture.LastConfirmation);
        Assert.False(card.Pattern.IsActive);
        Assert.Equal(BadgeKind.Neutral, card.StatusKind);
    }

    [Fact]
    public async Task PatternCardViewModelDiscardChangesRestoresOriginalDraftValues()
    {
        var fixture = CreateFixture();
        var accounting = new AccountingFixture();
        var (p1, r1) = CreateSamplePattern(fixture.Company.Id, "DISC-01", "نمط أصلي", BusinessOperationKind.ManualJournal);
        accounting.Patterns.Add(p1);
        accounting.Revisions.Add(r1);

        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin";
        shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync("patterns", new OpenPatternCardRequest(p1.Id!.Value)).ConfigureAwait(true);
        var card = Assert.IsType<PatternCardViewModel>(shell.CurrentView);

        card.BeginEditCommand.Execute(null);
        card.DraftNameArabic = "تغيير لم يُحفظ";
        Assert.True(card.HasUnsavedChanges);

        // Discard
        Assert.True(card.CancelCardCommand.CanExecute(null));
        await card.CancelCardCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.True(card.IsViewMode);
        Assert.False(card.HasUnsavedChanges);
        Assert.Equal("نمط أصلي", card.DraftNameArabic);
    }

    [Fact]
    public async Task PatternPermissionsViewOnlyUserCannotManageOrPublish()
    {
        // User with View only permission
        var fixture = CreateFixture("Patterns.Documents.View");
        var accounting = new AccountingFixture();
        var (p1, r1) = CreateSamplePattern(fixture.Company.Id, "PERM-01", "نمط الفحص", BusinessOperationKind.ManualJournal);
        accounting.Patterns.Add(p1);
        accounting.Revisions.Add(r1);

        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin";
        shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync("patterns", null).ConfigureAwait(true);
        var listVm = shell.Pages.Patterns;
        Assert.True(listVm.HasContext);
        Assert.False(listVm.CanManage);
        Assert.False(listVm.NewPatternCommand.CanExecute(null));

        await shell.NavigateToAsync("patterns", new OpenPatternCardRequest(p1.Id!.Value)).ConfigureAwait(true);
        var card = Assert.IsType<PatternCardViewModel>(shell.CurrentView);
        Assert.False(card.CanManage);
        Assert.False(card.CanPublish);
        Assert.False(card.BeginEditCommand.CanExecute(null));
        Assert.False(card.PublishRevisionCommand.CanExecute(null));
        Assert.False(card.RetireCommand.CanExecute(null));
    }
}
