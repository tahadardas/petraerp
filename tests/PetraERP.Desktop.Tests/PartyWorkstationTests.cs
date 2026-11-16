using PetraERP.ApiClient;
using PetraERP.Desktop.Accounting;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Parties;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

public sealed class PartyWorkstationTests
{
    private static ShellFixture CreateFixture(params string[] permissions)
    {
        var fixture = new ShellFixture();
        fixture.User.Permissions = permissions.Length > 0
            ? permissions
            : ["Parties.Directory.View", "Parties.Directory.Manage", "Ledger.Accounts.View"];
        return fixture;
    }

    private static PartyDetailDto CreateSampleParty(
        Guid companyId,
        string code = "CUST-001",
        string nameAr = "شركة الأمل للمقاولات",
        PartyRole role = PartyRole.Customer,
        string? nameEn = "Al-Amal Contracting Co",
        bool isActive = true,
        string creditLimit = "50000.0000",
        CreditPolicyKind policyKind = CreditPolicyKind.BlockOnExceed)
    {
        return new PartyDetailDto
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            Code = code,
            NameArabic = nameAr,
            NameEnglish = nameEn,
            Role = role,
            IsActive = isActive,
            DefaultCurrencyCode = "JOD",
            TaxNumber = "TAX-998877",
            CommercialRegistration = "CR-12345",
            CreditLimit = creditLimit,
            PaymentTermDays = 45,
            PolicyKind = policyKind,
            CurrentBalance = "12500.0000",
            AvailableCredit = "37500.0000",
            ReceivableAccountId = Guid.NewGuid(),
            PayableAccountId = role == PartyRole.Supplier || role == PartyRole.Both ? Guid.NewGuid() : null,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            VersionToken = "v1",
        };
    }

    [Fact]
    public async Task PartiesViewModelLoadsAndFiltersParties()
    {
        var fixture = CreateFixture();
        var accounting = new AccountingFixture();
        var p1 = CreateSampleParty(fixture.Company.Id, "CUST-01", "شركة القدس التجارية", PartyRole.Customer);
        var p2 = CreateSampleParty(fixture.Company.Id, "SUPP-01", "مؤسسة الرافدين للتوريدات", PartyRole.Supplier);
        var p3 = CreateSampleParty(fixture.Company.Id, "BOTH-01", "شركة الشرق للمقاولات والخدمات", PartyRole.Both);
        p3.IsActive = false;

        accounting.Parties.AddRange([p1, p2, p3]);

        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin";
        shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        var vm = shell.Pages.Parties;
        await shell.NavigateToAsync("parties", null).ConfigureAwait(true);

        Assert.Equal(3, vm.Parties.Count);
        Assert.Equal(3, vm.VisibleParties.Count);

        // Filter by role: Customer
        vm.FilterRole = "customer";
        Assert.Single(vm.VisibleParties);
        Assert.Equal("CUST-01", vm.VisibleParties[0].Code);

        // Filter by role: Supplier
        vm.FilterRole = "supplier";
        Assert.Single(vm.VisibleParties);
        Assert.Equal("SUPP-01", vm.VisibleParties[0].Code);

        // Filter by role: Both
        vm.FilterRole = "both";
        Assert.Single(vm.VisibleParties);
        Assert.Equal("BOTH-01", vm.VisibleParties[0].Code);

        // Reset role filter and search by name
        vm.FilterRole = "all";
        vm.Search = "القدس";
        Assert.Single(vm.VisibleParties);
        Assert.Equal("CUST-01", vm.VisibleParties[0].Code);

        // Search by code
        vm.Search = "SUPP";
        Assert.Single(vm.VisibleParties);
        Assert.Equal("SUPP-01", vm.VisibleParties[0].Code);

        // Active only filter
        vm.Search = string.Empty;
        vm.ActiveOnly = true;
        Assert.Equal(2, vm.VisibleParties.Count);
        Assert.DoesNotContain(vm.VisibleParties, p => p.Code == "BOTH-01");
    }

    [Fact]
    public async Task PartiesViewModelCanToggleActiveState()
    {
        var fixture = CreateFixture();
        var accounting = new AccountingFixture();
        var p1 = CreateSampleParty(fixture.Company.Id, "CUST-01", "شركة القدس التجارية", PartyRole.Customer, isActive: true);
        accounting.Parties.Add(p1);

        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin";
        shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        var vm = shell.Pages.Parties;
        await shell.NavigateToAsync("parties", null).ConfigureAwait(true);

        vm.SelectedParty = vm.VisibleParties[0];
        Assert.True(vm.SelectedParty.IsActive);

        // Deactivate
        await vm.ToggleActiveCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.False(vm.SelectedParty.IsActive);
        Assert.False(accounting.Parties[0].IsActive);

        // Reactivate
        await vm.ToggleActiveCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.True(vm.SelectedParty.IsActive);
        Assert.True(accounting.Parties[0].IsActive);
    }

    [Fact]
    public async Task PartyCardViewModelLoadsPartyDetailsAndDerivedExposure()
    {
        var fixture = CreateFixture();
        var accounting = new AccountingFixture();
        var p1 = CreateSampleParty(fixture.Company.Id, "CUST-01", "شركة النور للمقاولات", PartyRole.Customer, creditLimit: "75000.0000");
        accounting.Parties.Add(p1);

        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin";
        shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        var card = shell.Pages.PartyCards.Create();
        var context = new ShellContext(fixture.Company, fixture.Branch, new HashSet<string>(fixture.User.Permissions));
        await card.ActivateAsync(context, new OpenPartyCardRequest(p1.Id!.Value), CancellationToken.None).ConfigureAwait(true);

        Assert.True(card.HasParty);
        Assert.Equal("CUST-01", card.Party!.Code);
        Assert.Equal("شركة النور للمقاولات", card.Party.NameArabic);
        Assert.Equal("CUST-01 - شركة النور للمقاولات", card.Title);
        Assert.Equal(EntityCardMode.View, card.Mode);

        // Verify credit policy and exposure
        Assert.NotNull(card.Exposure);
        Assert.Equal("75000.0000", card.Exposure!.CreditLimit);
        Assert.Equal(CreditPolicyKind.BlockOnExceed, card.Exposure.PolicyKind);
    }

    [Fact]
    public async Task PartyCardViewModelEnforcesRoleAccountInvariants()
    {
        var fixture = CreateFixture();
        var accounting = new AccountingFixture();

        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin";
        shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        var card = shell.Pages.PartyCards.Create();
        var context = new ShellContext(fixture.Company, fixture.Branch, new HashSet<string>(fixture.User.Permissions));
        await card.ActivateAsync(context, new NewPartyCardRequest(), CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(EntityCardMode.New, card.Mode);
        card.DraftCode = "NEW-01";
        card.DraftNameArabic = "عميل تجريبي";

        // An empty account is a valid choice: the server creates the party's own account, and the card says which.
        card.DraftRole = PartyRole.Customer;
        card.DraftReceivableAccountId = null;
        Assert.True(card.CanSaveDraft);
        Assert.Contains("121001", card.ReceivableAccountPlanText, StringComparison.Ordinal);
        Assert.Equal(string.Empty, card.PayableAccountPlanText); // a customer needs no payable account

        card.DraftReceivableAccountId = Guid.NewGuid(); // an existing account chosen instead: nothing will be created
        Assert.True(card.CanSaveDraft);
        Assert.Equal(string.Empty, card.ReceivableAccountPlanText);

        card.DraftRole = PartyRole.Supplier;
        card.DraftPayableAccountId = null;
        Assert.True(card.CanSaveDraft);
        Assert.Contains("221001", card.PayableAccountPlanText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PartyCardViewModelCreatesNewPartyAndSavesDraft()
    {
        var fixture = CreateFixture();
        var accounting = new AccountingFixture();

        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin";
        shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        var card = shell.Pages.PartyCards.Create();
        var context = new ShellContext(fixture.Company, fixture.Branch, new HashSet<string>(fixture.User.Permissions));
        await card.ActivateAsync(context, new NewPartyCardRequest(), CancellationToken.None).ConfigureAwait(true);

        card.DraftCode = "CUST-999";
        card.DraftNameArabic = "مجموعة السلام للمقاولات";
        card.DraftRole = PartyRole.Customer;
        card.DraftReceivableAccountId = Guid.NewGuid();
        card.DraftCreditLimit = "100000.0000";
        card.DraftPolicyKind = CreditPolicyKind.BlockOnExceed;

        Assert.True(card.SaveCardCommand.CanExecute(null));
        await card.SaveCardCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal(EntityCardMode.View, card.Mode);
        Assert.True(card.HasParty);
        Assert.Equal("CUST-999", card.Party!.Code);
        Assert.Equal("مجموعة السلام للمقاولات", card.Party.NameArabic);
        Assert.Single(accounting.Parties);
        Assert.Equal("CUST-999", accounting.Parties[0].Code);
    }

    [Fact]
    public async Task PartyCardViewModelTracksUnsavedChanges()
    {
        var fixture = CreateFixture();
        var accounting = new AccountingFixture();
        var p1 = CreateSampleParty(fixture.Company.Id, "CUST-01", "شركة القدس التجارية", PartyRole.Customer);
        accounting.Parties.Add(p1);

        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin";
        shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        var card = shell.Pages.PartyCards.Create();
        var context = new ShellContext(fixture.Company, fixture.Branch, new HashSet<string>(fixture.User.Permissions));
        await card.ActivateAsync(context, new OpenPartyCardRequest(p1.Id!.Value), CancellationToken.None).ConfigureAwait(true);

        Assert.False(card.HasUnsavedChanges);

        card.BeginEditCommand.Execute(null);
        Assert.Equal(EntityCardMode.Edit, card.Mode);
        Assert.False(card.HasUnsavedChanges);

        // Edit a field
        card.DraftNameArabic = "شركة القدس للتجارة العامة (محدثة)";
        Assert.True(card.HasUnsavedChanges);

        // Cancel reverts
        await card.CancelCardCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal(EntityCardMode.View, card.Mode);
        Assert.False(card.HasUnsavedChanges);
    }
}
