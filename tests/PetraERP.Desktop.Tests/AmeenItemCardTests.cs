using PetraERP.ApiClient;
using PetraERP.Desktop.Inventory;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

/// <summary>The Al-Ameen item card: three units, price matrix, barcodes per unit, record navigation and the conditional delete.</summary>
public sealed class AmeenItemCardTests
{
    private static ShellFixture CreateFixture()
    {
        var fixture = new ShellFixture();
        fixture.User.Permissions = ["Inventory.Items.View", "Inventory.Items.Manage", "Inventory.Movements.View", "Inventory.Warehouses.View"];
        return fixture;
    }

    private static async Task<(ShellFixture Fixture, AccountingFixture Accounting, DesktopViewModel Shell, UnitOfMeasureDto Piece, UnitOfMeasureDto Carton)> StartAsync()
    {
        var fixture = CreateFixture();
        var accounting = new AccountingFixture();
        var piece = new UnitOfMeasureDto { Id = Guid.NewGuid(), Code = "PCS", NameArabic = "قطعة", Symbol = "pc" };
        var carton = new UnitOfMeasureDto { Id = Guid.NewGuid(), Code = "CRT", NameArabic = "كرتونة", Symbol = "ct" };
        accounting.Units.Add(piece);
        accounting.Units.Add(carton);
        var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin";
        shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        return (fixture, accounting, shell, piece, carton);
    }

    private static ItemDetailDto Sample(string code, Guid? unit, Guid company) => new()
    {
        Id = Guid.NewGuid(), CompanyId = company, Code = code, NameArabic = "مادة " + code, BaseUnitId = unit, CostMethod = CostMethod.WeightedAverage,
        IsActive = true, CurrentStock = "0.0000", Barcodes = [], Prices = [], Conversions = [],
    };

    [Fact]
    public async Task NewItemSendsProfileUnitsBarcodesAndPriceMatrixInOneCreateRequest()
    {
        var (fixture, accounting, shell, piece, carton) = await StartAsync().ConfigureAwait(true);
        using var _ = shell;
        await shell.NavigateToAsync("items", new NewItemCardRequest()).ConfigureAwait(true);
        var card = (ItemCardViewModel)shell.Documents.Active.Content!;

        card.DraftCode = "AM-1";
        card.DraftNameArabic = "مادة الأمين";
        card.DraftBaseUnitId = piece.Id;
        card.DraftUnit2Id = carton.Id;
        card.DraftUnit2Factor = "12";
        card.DraftUnit2Variable = true;
        card.DraftDefaultUnitIndex = 2;
        card.DraftGiftQuantity = "1";
        card.DraftGiftPerQuantity = "10";
        card.DraftMeasure = "كبير";
        card.DraftTracksExpiryDate = true;
        card.DraftEnforceDatesOnReceipt = true;
        card.DraftHideInSearch = true;
        card.DraftUnit1Barcode = "111";
        card.DraftUnit2Barcode = "222";
        card.DraftPriceCurrency = "JOD";
        var wholesale = card.PriceMatrix.Single(r => r.Code == "WHOLESALE");
        wholesale.Unit1 = "5.5";
        wholesale.Unit2 = "60";
        card.PriceMatrix.Single(r => r.Code == "RETAIL").Unit1 = "7";

        Assert.True(card.IsUnit2Default);
        Assert.True(card.HasUnit2);
        Assert.False(card.HasUnit3);
        Assert.True(card.CanEditSerialEnforcement == false);
        Assert.Empty(card.GetValidationErrors());
        await card.SaveCardCommand.ExecuteAsync(null).ConfigureAwait(true);

        var sent = accounting.LastCreateItem!;
        Assert.Equal(carton.Id, sent.Profile!.Unit2Id);
        Assert.Equal("12", sent.Profile.Unit2Factor);
        Assert.True(sent.Profile.Unit2FactorVariable);
        Assert.Equal(2, sent.Profile.DefaultUnitIndex);
        Assert.Equal("كبير", sent.Profile.Measure);
        Assert.True(sent.Profile.TracksExpiryDate);
        Assert.True(sent.Profile.EnforceDatesOnReceipt);
        Assert.True(sent.Profile.HideInSearch);
        Assert.Null(sent.Profile.Unit3Id);
        Assert.Equal(["111", "222"], sent.Barcodes!.Select(b => b.Barcode).Order().ToArray());
        Assert.Equal(carton.Id, sent.Barcodes!.Single(b => b.IsDefault == true).UnitId);
        Assert.Equal(3, sent.Prices!.Count);
        Assert.Contains(sent.Prices, p => p.PriceListCode == "WHOLESALE" && p.UnitId == carton.Id && p.Price == "60" && p.CurrencyCode == "JOD");
        Assert.Contains(sent.Prices, p => p.PriceListCode == "RETAIL" && p.UnitId == piece.Id && p.Price == "7");
        Assert.True(card.IsViewMode);
        Assert.Equal("5.5", card.PriceMatrix.Single(r => r.Code == "WHOLESALE").Unit1);
        Assert.Equal("60", card.PriceMatrix.Single(r => r.Code == "WHOLESALE").Unit2);
        Assert.Equal(carton.Id, card.DraftUnit2Id);
    }

    [Fact]
    public async Task InvalidUnitsAndPricesAreReportedBeforeSaving()
    {
        var (_, _, shell, piece, carton) = await StartAsync().ConfigureAwait(true);
        using var _ = shell;
        await shell.NavigateToAsync("items", new NewItemCardRequest()).ConfigureAwait(true);
        var card = (ItemCardViewModel)shell.Documents.Active.Content!;
        card.DraftCode = "AM-2";
        card.DraftNameArabic = "مادة";
        card.DraftBaseUnitId = piece.Id;

        card.DraftUnit2Id = carton.Id;
        Assert.Contains(card.GetValidationErrors(), e => e.Contains("عامل التحويل", StringComparison.Ordinal));
        card.DraftUnit2Factor = "0";
        Assert.Contains(card.GetValidationErrors(), e => e.Contains("عامل التحويل", StringComparison.Ordinal));
        card.DraftUnit2Factor = "12";
        Assert.Empty(card.GetValidationErrors());

        card.DraftUnit2Id = piece.Id;
        Assert.Contains(card.GetValidationErrors(), e => e.Contains("تختلف", StringComparison.Ordinal));
        card.DraftUnit2Id = null;
        Assert.Null(card.DraftUnit2Factor);

        card.PriceMatrix.Single(r => r.Code == "EXPORT").Unit1 = "abc";
        Assert.Contains(card.GetValidationErrors(), e => e.Contains("التصدير", StringComparison.Ordinal));
        card.PriceMatrix.Single(r => r.Code == "EXPORT").Unit1 = "9";
        card.DraftPriceCurrency = "";
        Assert.Contains(card.GetValidationErrors(), e => e.Contains("العملة", StringComparison.Ordinal));
        card.PriceMatrix.Single(r => r.Code == "EXPORT").Unit3 = "9";
        Assert.Contains(card.GetValidationErrors(), e => e.Contains("وحدة 3", StringComparison.Ordinal));
        Assert.False(card.CanSaveDraft && card.GetValidationErrors().Count == 0);
    }

    [Fact]
    public async Task NavigationMovesBetweenSavedItemsAndStaysOffWhileEditing()
    {
        var (fixture, accounting, shell, piece, _) = await StartAsync().ConfigureAwait(true);
        using var _ = shell;
        var company = fixture.Company.Id;
        var first = Sample("A-1", piece.Id, company);
        var second = Sample("A-2", piece.Id, company);
        var third = Sample("A-3", piece.Id, company);
        accounting.Items.AddRange([first, second, third]);

        await shell.NavigateToAsync("items", new OpenItemCardRequest(second.Id!.Value)).ConfigureAwait(true);
        var card = (ItemCardViewModel)shell.Documents.Active.Content!;
        Assert.Equal("A-2", card.RecordCode);
        Assert.True(card.NextItemCommand.CanExecute(null));

        await card.NextItemCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal(third.Id!.Value, card.ItemId!.Value);
        Assert.Equal("A-3", card.RecordCode);
        await card.NextItemCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal(third.Id!.Value, card.ItemId!.Value);
        await card.FirstItemCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal(first.Id!.Value, card.ItemId!.Value);
        await card.LastItemCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal(third.Id!.Value, card.ItemId!.Value);
        await card.PreviousItemCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal(second.Id!.Value, card.ItemId!.Value);

        card.BeginEditCommand.Execute(null);
        Assert.False(card.NextItemCommand.CanExecute(null));
        Assert.False(card.DeleteItemCommand.CanExecute(null));
    }

    [Fact]
    public async Task DeleteIsOfferedOnlyWithoutMovementsAndRemovesTheItem()
    {
        var (fixture, accounting, shell, piece, _) = await StartAsync().ConfigureAwait(true);
        using var _ = shell;
        var company = fixture.Company.Id;
        var keep = Sample("K-1", piece.Id, company);
        var doomed = Sample("K-2", piece.Id, company);
        accounting.Items.AddRange([keep, doomed]);

        await shell.NavigateToAsync("items", new OpenItemCardRequest(doomed.Id!.Value)).ConfigureAwait(true);
        var card = (ItemCardViewModel)shell.Documents.Active.Content!;
        Assert.Empty(card.Movements);
        Assert.True(card.CanDeleteItem);

        card.Movements.Add(new StockMovementDto { Id = Guid.NewGuid(), MovementKind = StockMovementKind.OpeningBalance });
        Assert.False(card.CanDeleteItem);
        Assert.False(card.DeleteItemCommand.CanExecute(null));
        card.Movements.Clear();

        fixture.Confirm = false;
        await card.DeleteItemCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Empty(accounting.DeletedItems);

        fixture.Confirm = true;
        await card.DeleteItemCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal([doomed.Id!.Value], accounting.DeletedItems);
        Assert.Equal("D_DeleteItemPrompt", fixture.LastConfirmation);
        Assert.Equal(keep.Id!.Value, card.ItemId!.Value);
        Assert.DoesNotContain(accounting.Items, i => i.Id == doomed.Id);
    }
}
