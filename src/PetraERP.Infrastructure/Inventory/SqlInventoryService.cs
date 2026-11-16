using Microsoft.EntityFrameworkCore;

using PetraERP.Application.Common;

using PetraERP.Application.Inventory;

using PetraERP.Domain.Inventory;

using PetraERP.Domain.Parties;

using PetraERP.Domain.Primitives;

using PetraERP.Infrastructure.Persistence;



namespace PetraERP.Infrastructure.Inventory;



public sealed partial class SqlInventoryService(FinancialDbContext context, TimeProvider? clock = null) : IInventoryService

{

    private TimeProvider Clock => clock ?? TimeProvider.System;

    // ==========================================

    // Items

    // ==========================================



    public async Task<ItemPageResult> GetItemsAsync(

        Guid companyId,

        ItemSearchQuery query,

        CancellationToken cancellationToken = default)

    {

        ArgumentNullException.ThrowIfNull(query);

        await ValidateCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);



        var baseQuery = context.Items

            .AsNoTracking()

            .Include(i => i.Group)

            .Include(i => i.BaseUnit)

            .Include(i => i.Barcodes)

            .Where(i => i.CompanyId == companyId);



        if (query.GroupId.HasValue)

        {

            baseQuery = baseQuery.Where(i => i.GroupId == query.GroupId.Value);

        }



        if (query.IsActive.HasValue)

        {

            baseQuery = baseQuery.Where(i => i.IsActive == query.IsActive.Value);

        }



        List<ItemRow> candidates;

        if (!string.IsNullOrWhiteSpace(query.SearchText))

        {

            var rawList = await baseQuery

                .OrderBy(i => i.Code)

                .ToListAsync(cancellationToken)

                .ConfigureAwait(false);



            candidates = rawList

                .Where(i => MatchesItemSearch(i, query.SearchText))

                .ToList();

        }

        else

        {

            candidates = await baseQuery

                .OrderBy(i => i.Code)

                .ToListAsync(cancellationToken)

                .ConfigureAwait(false);

        }



        var totalCount = candidates.Count;

        var page = Math.Max(1, query.Page);

        var pageSize = Math.Clamp(query.PageSize, 1, 200);



        var pagedRows = candidates

            .Skip((page - 1) * pageSize)

            .Take(pageSize)

            .ToList();



        var pagedItemIds = pagedRows.Select(i => i.Id).ToList();



        var stockMap = await GetStockBalancesForItemsAsync(companyId, pagedItemIds, query.WarehouseId, query.BranchId, cancellationToken).ConfigureAwait(false);



        var items = pagedRows.Select(i =>

        {

            var currentStock = stockMap.GetValueOrDefault(i.Id, 0m);

            return new ItemSummaryResult(

                Id: i.Id,

                CompanyId: i.CompanyId,

                GroupId: i.GroupId,

                GroupName: i.Group?.NameArabic,

                Code: i.Code,

                NameArabic: i.NameArabic,

                NameEnglish: i.NameEnglish,

                BaseUnitId: i.BaseUnitId,

                BaseUnitCode: i.BaseUnit?.Code ?? string.Empty,

                BaseUnitSymbol: i.BaseUnit?.Symbol ?? string.Empty,

                CostMethod: (CostMethod)i.CostMethod,

                MinStock: i.MinStock,

                MaxStock: i.MaxStock,

                ReorderPoint: i.ReorderPoint,

                IsActive: i.IsActive,

                CurrentStock: currentStock);

        }).ToList();



        return new ItemPageResult(page, pageSize, totalCount, items);

    }



    public Task<ItemDetailResult> GetItemAsync(Guid companyId, Guid itemId, CancellationToken cancellationToken = default)

        => GetItemInBranchAsync(companyId, itemId, null, cancellationToken);



    public async Task<ItemDetailResult> GetItemInBranchAsync(

        Guid companyId,

        Guid itemId,

        Guid? branchId, CancellationToken cancellationToken = default)

    {

        var row = await context.Items

            .AsNoTracking()

            .Include(i => i.Barcodes)

            .Include(i => i.Prices)

            .Include(i => i.Conversions)

            .FirstOrDefaultAsync(i => i.CompanyId == companyId && i.Id == itemId, cancellationToken)

            .ConfigureAwait(false);



        if (row is null)

        {

            throw new ResourceNotFoundException("Item");

        }



        var stock = await GetItemTotalStockAsync(companyId, itemId, branchId, cancellationToken).ConfigureAwait(false);



        var units = await context.UnitsOfMeasure

            .AsNoTracking()

            .Where(u => u.CompanyId == companyId)

            .ToDictionaryAsync(u => u.Id, cancellationToken)

            .ConfigureAwait(false);



        return new ItemDetailResult(

            Id: row.Id,

            CompanyId: row.CompanyId,

            GroupId: row.GroupId,

            Code: row.Code,

            NameArabic: row.NameArabic,

            NameEnglish: row.NameEnglish,

            BaseUnitId: row.BaseUnitId,

            DefaultWarehouseId: row.DefaultWarehouseId,

            CostMethod: (CostMethod)row.CostMethod,

            MinStock: row.MinStock,

            MaxStock: row.MaxStock,

            ReorderPoint: row.ReorderPoint,

            IsActive: row.IsActive,

            CurrentStock: stock,

            Barcodes: row.Barcodes.Select(b => new ItemBarcodeResult(

                Id: b.Id,

                ItemId: b.ItemId,

                UnitId: b.UnitId,

                UnitCode: units.TryGetValue(b.UnitId, out var u) ? u.Code : string.Empty,

                Barcode: b.Barcode,

                IsDefault: b.IsDefault)).ToList(),

            Prices: row.Prices.Select(p => new ItemPriceResult(

                Id: p.Id,

                ItemId: p.ItemId,

                UnitId: p.UnitId,

                UnitCode: units.TryGetValue(p.UnitId, out var u) ? u.Code : string.Empty,

                PriceListCode: p.PriceListCode,

                Price: p.Price,

                CurrencyCode: p.CurrencyCode,

                MinQuantity: p.MinQuantity,

                PartyId: p.PartyId,

                PatternId: p.PatternId,

                ValidFrom: p.ValidFrom,

                ValidTo: p.ValidTo)).ToList(),

            Conversions: row.Conversions.Select(c => new UnitConversionResult(

                Id: c.Id,

                CompanyId: c.CompanyId,

                ItemId: c.ItemId,

                FromUnitId: c.FromUnitId,

                FromUnitCode: units.TryGetValue(c.FromUnitId, out var fu) ? fu.Code : string.Empty,

                FromUnitSymbol: units.TryGetValue(c.FromUnitId, out var fu2) ? fu2.Symbol : string.Empty,

                ToUnitId: c.ToUnitId,

                ToUnitCode: units.TryGetValue(c.ToUnitId, out var tu) ? tu.Code : string.Empty,

                ToUnitSymbol: units.TryGetValue(c.ToUnitId, out var tu2) ? tu2.Symbol : string.Empty,

                Factor: c.Factor)).ToList(),

            Profile: ToProfile(row));

    }



    public Task<IReadOnlyList<ItemLookupResult>> LookupItemsAsync(Guid companyId, string? searchText, int maxResults = 20, CancellationToken cancellationToken = default)

        => LookupItemsInBranchAsync(companyId, searchText, maxResults, null, cancellationToken);



    public async Task<IReadOnlyList<ItemLookupResult>> LookupItemsInBranchAsync(

        Guid companyId,

        string? searchText,

        int maxResults,

        Guid? branchId, CancellationToken cancellationToken = default)

    {

        var query = context.Items

            .AsNoTracking()

            .Include(i => i.BaseUnit)

            .Include(i => i.Barcodes)

            .Where(i => i.CompanyId == companyId && i.IsActive && !i.HideInSearch);



        var clampedLimit = Math.Clamp(maxResults, 1, 100);

        List<ItemRow> results;



        if (string.IsNullOrWhiteSpace(searchText))

        {

            results = await query

                .OrderBy(i => i.Code)

                .Take(clampedLimit)

                .ToListAsync(cancellationToken)

                .ConfigureAwait(false);

        }

        else

        {

            var raw = searchText.Trim();

            var candidateQuery = query;

            foreach (var token in ArabicTextNormalizer.Tokenize(raw))

            {

                var searchToken = token;

                candidateQuery = candidateQuery.Where(i => i.Code.Contains(searchToken)

                    || i.NameArabic.Contains(searchToken)

                    || (i.NameEnglish != null && i.NameEnglish.Contains(searchToken))

                    || i.Barcodes.Any(b => b.Barcode.Contains(searchToken)));

            }



            var dbCandidates = await candidateQuery

                .OrderBy(i => i.Code)

                .Take(clampedLimit * 3)

                .ToListAsync(cancellationToken)

                .ConfigureAwait(false);



            results = dbCandidates

                .Where(i => MatchesItemSearch(i, searchText))

                .Take(clampedLimit)

                .ToList();



            if (results.Count == 0 && dbCandidates.Count > 0)

            {

                results = dbCandidates.Take(clampedLimit).ToList();

            }

        }

        var itemIds = results.Select(i => i.Id).ToList();

        var stockMap = await GetStockBalancesForItemsAsync(companyId, itemIds, null, branchId, cancellationToken).ConfigureAwait(false);



        return results.Select(i => new ItemLookupResult(

            Id: i.Id,

            Code: i.Code,

            NameArabic: i.NameArabic,

            NameEnglish: i.NameEnglish,

            BaseUnitId: i.BaseUnitId,

            BaseUnitCode: i.BaseUnit?.Code ?? string.Empty,

            BaseUnitSymbol: i.BaseUnit?.Symbol ?? string.Empty,

            CurrentStock: stockMap.GetValueOrDefault(i.Id, 0m))).ToArray();

    }



    public Task<ItemDetailResult> CreateItemAsync(Guid companyId, CreateItemCommand command, CancellationToken cancellationToken = default)

        => CreateItemInBranchAsync(companyId, command, null, cancellationToken);



    public async Task<ItemDetailResult> CreateItemInBranchAsync(

        Guid companyId,

        CreateItemCommand command,

        Guid? branchId, CancellationToken cancellationToken = default)

    {

        if (!Enum.IsDefined(command.CostMethod))
            throw new BusinessRuleException("inventory.cost_method_invalid", "طريقة التكلفة غير معروفة / Unknown cost method.");



        ArgumentNullException.ThrowIfNull(command);

        var currencyTransaction = await PetraERP.Infrastructure.Currencies.CurrencyWriteTransaction.BeginAsync(context, companyId, cancellationToken).ConfigureAwait(false);

        await using var currencyLifetime = currencyTransaction.ConfigureAwait(false);

        await ValidateCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);



        var codeNorm = command.Code.Trim();

        var codeExists = await context.Items

            .AnyAsync(i => i.CompanyId == companyId && i.Code == codeNorm, cancellationToken)

            .ConfigureAwait(false);



        if (codeExists)

        {

            throw new ResourceConflictException("Item", $"An item with code '{codeNorm}' already exists in this company.");

        }



        var unitExists = await context.UnitsOfMeasure

            .AnyAsync(u => u.CompanyId == companyId && u.Id == command.BaseUnitId, cancellationToken)

            .ConfigureAwait(false);



        if (!unitExists)

        {

            throw new BusinessRuleException("inventory.unit_not_found", $"Base unit '{command.BaseUnitId}' was not found.");

        }



        if (command.GroupId.HasValue)

        {

            var groupExists = await context.ItemGroups

                .AnyAsync(g => g.CompanyId == companyId && g.Id == command.GroupId.Value, cancellationToken)

                .ConfigureAwait(false);



            if (!groupExists)

            {

                throw new BusinessRuleException("inventory.group_not_found", $"Item group '{command.GroupId.Value}' was not found.");

            }

        }



        if (command.DefaultWarehouseId.HasValue)

        {

            var whExists = await context.Warehouses

                .AnyAsync(w => w.CompanyId == companyId && w.Id == command.DefaultWarehouseId.Value && (!branchId.HasValue || w.BranchId == branchId.Value), cancellationToken)

                .ConfigureAwait(false);



            if (!whExists)

            {

                throw new BusinessRuleException("inventory.warehouse_not_found", $"Warehouse '{command.DefaultWarehouseId.Value}' was not found.");

            }

        }



        var itemId = Guid.NewGuid();

        var now = DateTimeOffset.UtcNow;



        var row = new ItemRow

        {

            Id = itemId,

            CompanyId = companyId,

            GroupId = command.GroupId,

            Code = codeNorm,

            NameArabic = command.NameArabic.Trim(),

            NameEnglish = string.IsNullOrWhiteSpace(command.NameEnglish) ? null : command.NameEnglish.Trim(),

            BaseUnitId = command.BaseUnitId,

            DefaultWarehouseId = command.DefaultWarehouseId,

            CostMethod = (int)command.CostMethod,

            MinStock = command.MinStock,

            MaxStock = command.MaxStock,

            ReorderPoint = command.ReorderPoint,

            IsActive = true,

            CreatedAtUtc = now,

            UpdatedAtUtc = now

        };



        await ApplyProfileAsync(row, command.Profile, companyId, command.BaseUnitId, true, cancellationToken).ConfigureAwait(false);

        context.Items.Add(row);



        if (command.Barcodes != null)

        {

            foreach (var b in command.Barcodes)

            {

                context.ItemBarcodes.Add(new ItemBarcodeRow
                {
                    Id = Guid.NewGuid(),
                    CompanyId = companyId,
                    ItemId = itemId,
                    UnitId = b.UnitId,

                    Barcode = b.Barcode.Trim(),

                    IsDefault = b.IsDefault

                });

            }

        }



        if (command.Prices != null)

        {

            foreach (var p in command.Prices)

            {

                context.ItemPrices.Add(await NewPriceRowAsync(companyId, itemId, command.BaseUnitId, p, cancellationToken).ConfigureAwait(false));

            }

        }



        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await currencyTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return await GetItemInBranchAsync(companyId, itemId, branchId, cancellationToken).ConfigureAwait(false);

    }



    public Task<ItemDetailResult> UpdateItemAsync(Guid companyId, Guid itemId, UpdateItemCommand command, CancellationToken cancellationToken = default)

        => UpdateItemInBranchAsync(companyId, itemId, command, null, cancellationToken);



    public async Task<ItemDetailResult> UpdateItemInBranchAsync(

        Guid companyId,

        Guid itemId,

        UpdateItemCommand command,

        Guid? branchId, CancellationToken cancellationToken = default)

    {

        var transaction = await PetraERP.Infrastructure.Persistence.OwnedDatabaseTransaction.BeginAsync(context, System.Data.IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);

        await using var transactionLifetime = transaction.ConfigureAwait(false);

        await InventoryWriteScope.LockCompanyAsync(context, companyId, cancellationToken).ConfigureAwait(false);



        if (!Enum.IsDefined(command.CostMethod))
            throw new BusinessRuleException("inventory.cost_method_invalid", "طريقة التكلفة غير معروفة / Unknown cost method.");



        ArgumentNullException.ThrowIfNull(command);



        var row = await context.Items

            .FirstOrDefaultAsync(i => i.CompanyId == companyId && i.Id == itemId, cancellationToken)

            .ConfigureAwait(false);



        if (row is null)

        {

            throw new ResourceNotFoundException("Item");

        }



        if (command.GroupId.HasValue)

        {

            var groupExists = await context.ItemGroups

                .AnyAsync(g => g.CompanyId == companyId && g.Id == command.GroupId.Value, cancellationToken)

                .ConfigureAwait(false);



            if (!groupExists)

            {

                throw new BusinessRuleException("inventory.group_not_found", $"Item group '{command.GroupId.Value}' was not found.");

            }

        }



        var unitExists = await context.UnitsOfMeasure

            .AnyAsync(u => u.CompanyId == companyId && u.Id == command.BaseUnitId, cancellationToken)

            .ConfigureAwait(false);



        if (!unitExists)

        {

            throw new BusinessRuleException("inventory.unit_not_found", $"Base unit '{command.BaseUnitId}' was not found.");

        }



        if (command.DefaultWarehouseId.HasValue)

        {

            var whExists = await context.Warehouses

                .AnyAsync(w => w.CompanyId == companyId && w.Id == command.DefaultWarehouseId.Value && (!branchId.HasValue || w.BranchId == branchId.Value), cancellationToken)

                .ConfigureAwait(false);



            if (!whExists)

            {

                throw new BusinessRuleException("inventory.warehouse_not_found", $"Warehouse '{command.DefaultWarehouseId.Value}' was not found.");

            }

        }



        if ((row.BaseUnitId != command.BaseUnitId || row.CostMethod != (int)command.CostMethod)

            && await context.StockMovements.AnyAsync(m => m.CompanyId == companyId && m.ItemId == itemId, cancellationToken).ConfigureAwait(false))

            throw new BusinessRuleException("inventory.unit_immutable", "لا يمكن تغيير وحدة الأساس أو طريقة التكلفة بعد وجود حركات / Base unit and cost method cannot change after movements exist.");

        row.NameArabic = command.NameArabic.Trim();

        row.NameEnglish = string.IsNullOrWhiteSpace(command.NameEnglish) ? null : command.NameEnglish.Trim();

        row.GroupId = command.GroupId;

        row.BaseUnitId = command.BaseUnitId;

        row.DefaultWarehouseId = command.DefaultWarehouseId;

        row.CostMethod = (int)command.CostMethod;

        row.MinStock = command.MinStock;

        row.MaxStock = command.MaxStock;

        row.ReorderPoint = command.ReorderPoint;

        await ApplyProfileAsync(row, command.Profile, companyId, command.BaseUnitId, false, cancellationToken).ConfigureAwait(false);

        await SyncBarcodesAsync(companyId, itemId, command.Barcodes, cancellationToken).ConfigureAwait(false);

        row.UpdatedAtUtc = DateTimeOffset.UtcNow;



        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return await GetItemInBranchAsync(companyId, itemId, branchId, cancellationToken).ConfigureAwait(false);

    }



    public async Task<bool> DeactivateItemAsync(Guid companyId, Guid itemId, CancellationToken cancellationToken = default)

    {

        var row = await context.Items

            .FirstOrDefaultAsync(i => i.CompanyId == companyId && i.Id == itemId, cancellationToken)

            .ConfigureAwait(false);



        if (row is null)

        {

            throw new ResourceNotFoundException("Item");

        }



        row.IsActive = false;

        row.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;

    }



    public async Task<bool> ReactivateItemAsync(Guid companyId, Guid itemId, CancellationToken cancellationToken = default)

    {

        var row = await context.Items

            .FirstOrDefaultAsync(i => i.CompanyId == companyId && i.Id == itemId, cancellationToken)

            .ConfigureAwait(false);



        if (row is null)

        {

            throw new ResourceNotFoundException("Item");

        }



        row.IsActive = true;

        row.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;

    }



    // ==========================================

    // Groups

    // ==========================================



    public async Task<IReadOnlyList<ItemGroupResult>> GetItemGroupsAsync(Guid companyId, CancellationToken cancellationToken = default)

    {

        await ValidateCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);



        var rows = await context.ItemGroups

            .AsNoTracking()

            .Where(g => g.CompanyId == companyId)

            .OrderBy(g => g.Code)

            .ToListAsync(cancellationToken)

            .ConfigureAwait(false);



        return rows.Select(g => new ItemGroupResult(

            Id: g.Id,

            CompanyId: g.CompanyId,

            ParentId: g.ParentId,

            Code: g.Code,

            NameArabic: g.NameArabic,

            NameEnglish: g.NameEnglish,

            IsActive: g.IsActive)).ToList();

    }



    public async Task<ItemGroupResult> CreateItemGroupAsync(

        Guid companyId,

        CreateItemGroupCommand command,

        CancellationToken cancellationToken = default)

    {

        ArgumentNullException.ThrowIfNull(command);

        await ValidateCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);



        var codeNorm = command.Code.Trim();

        var exists = await context.ItemGroups

            .AnyAsync(g => g.CompanyId == companyId && g.Code == codeNorm, cancellationToken)

            .ConfigureAwait(false);



        if (exists)

        {

            throw new ResourceConflictException("ItemGroup", $"An item group with code '{codeNorm}' already exists.");

        }



        if (command.ParentId.HasValue)

        {

            var parentExists = await context.ItemGroups

                .AnyAsync(g => g.CompanyId == companyId && g.Id == command.ParentId.Value, cancellationToken)

                .ConfigureAwait(false);



            if (!parentExists)

            {

                throw new BusinessRuleException("inventory.parent_group_not_found", $"Parent item group '{command.ParentId.Value}' was not found.");

            }

        }



        var group = new ItemGroupRow

        {

            Id = Guid.NewGuid(),

            CompanyId = companyId,

            ParentId = command.ParentId,

            Code = codeNorm,

            NameArabic = command.NameArabic.Trim(),

            NameEnglish = string.IsNullOrWhiteSpace(command.NameEnglish) ? null : command.NameEnglish.Trim(),

            Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim(),

            IsActive = true

        };



        context.ItemGroups.Add(group);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);



        return new ItemGroupResult(

            Id: group.Id,

            CompanyId: group.CompanyId,

            ParentId: group.ParentId,

            Code: group.Code,

            NameArabic: group.NameArabic,

            NameEnglish: group.NameEnglish,

            IsActive: group.IsActive,

            Description: group.Description);

    }



    public async Task<ItemGroupResult> UpdateItemGroupAsync(

        Guid companyId,

        Guid groupId,

        UpdateItemGroupCommand command,

        CancellationToken cancellationToken = default)

    {

        ArgumentNullException.ThrowIfNull(command);



        var group = await context.ItemGroups

            .FirstOrDefaultAsync(g => g.CompanyId == companyId && g.Id == groupId, cancellationToken)

            .ConfigureAwait(false);



        if (group is null)

        {

            throw new ResourceNotFoundException("ItemGroup");

        }



        if (command.ParentId.HasValue)

        {

            if (command.ParentId.Value == groupId)

                throw new BusinessRuleException("inventory.circular_group", "An item group cannot be its own parent.");



            var current = command.ParentId.Value;

            var visited = new HashSet<Guid> { groupId };

            while (current != Guid.Empty)

            {

                if (!visited.Add(current))

                    throw new BusinessRuleException("inventory.circular_group", "Circular item group hierarchy detected.");

                var next = await context.ItemGroups.AsNoTracking()

                    .Where(g => g.CompanyId == companyId && g.Id == current)

                    .Select(g => g.ParentId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

                if (!next.HasValue) break;

                current = next.Value;

            }

        }



        group.NameArabic = command.NameArabic.Trim();

        group.NameEnglish = string.IsNullOrWhiteSpace(command.NameEnglish) ? null : command.NameEnglish.Trim();

        group.Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim();

        group.ParentId = command.ParentId;



        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);



        return new ItemGroupResult(

            Id: group.Id,

            CompanyId: group.CompanyId,

            ParentId: group.ParentId,

            Code: group.Code,

            NameArabic: group.NameArabic,

            NameEnglish: group.NameEnglish,

            IsActive: group.IsActive,

            Description: group.Description);

    }



    public async Task<ItemGroupDetailResult> GetItemGroupAsync(

        Guid companyId,

        Guid groupId,

        CancellationToken cancellationToken = default)

    {

        var group = await context.ItemGroups.AsNoTracking()

            .Include(g => g.Parent)

            .FirstOrDefaultAsync(g => g.CompanyId == companyId && g.Id == groupId, cancellationToken)

            .ConfigureAwait(false);



        if (group is null) throw new ResourceNotFoundException("ItemGroup");



        var subgroupsCount = await context.ItemGroups.AsNoTracking()

            .CountAsync(g => g.CompanyId == companyId && g.ParentId == groupId, cancellationToken)

            .ConfigureAwait(false);



        var itemsCount = await context.Items.AsNoTracking()

            .CountAsync(i => i.CompanyId == companyId && i.GroupId == groupId, cancellationToken)

            .ConfigureAwait(false);



        return new ItemGroupDetailResult(

            Id: group.Id,

            CompanyId: group.CompanyId,

            ParentId: group.ParentId,

            ParentNameArabic: group.Parent?.NameArabic,

            Code: group.Code,

            NameArabic: group.NameArabic,

            NameEnglish: group.NameEnglish,

            Description: group.Description,

            IsActive: group.IsActive,

            SubgroupsCount: subgroupsCount,

            ItemsCount: itemsCount,

            CreatedAtUtc: group.CreatedAtUtc,

            UpdatedAtUtc: group.UpdatedAtUtc,

            RowVersion: group.RowVersion);

    }



    public async Task<bool> DeactivateItemGroupAsync(Guid companyId, Guid groupId, CancellationToken cancellationToken = default)

    {

        var group = await context.ItemGroups

            .FirstOrDefaultAsync(g => g.CompanyId == companyId && g.Id == groupId, cancellationToken)

            .ConfigureAwait(false) ?? throw new ResourceNotFoundException("ItemGroup");



        group.IsActive = false;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;

    }



    public async Task<bool> ReactivateItemGroupAsync(Guid companyId, Guid groupId, CancellationToken cancellationToken = default)

    {

        var group = await context.ItemGroups

            .FirstOrDefaultAsync(g => g.CompanyId == companyId && g.Id == groupId, cancellationToken)

            .ConfigureAwait(false) ?? throw new ResourceNotFoundException("ItemGroup");



        group.IsActive = true;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;

    }



    public async Task<bool> DeleteItemGroupAsync(Guid companyId, Guid groupId, CancellationToken cancellationToken = default)

    {

        var group = await context.ItemGroups

            .FirstOrDefaultAsync(g => g.CompanyId == companyId && g.Id == groupId, cancellationToken)

            .ConfigureAwait(false) ?? throw new ResourceNotFoundException("ItemGroup");



        var hasChildren = await context.ItemGroups.AsNoTracking()

            .AnyAsync(g => g.CompanyId == companyId && g.ParentId == groupId, cancellationToken)

            .ConfigureAwait(false);



        if (hasChildren)

            throw new BusinessRuleException("inventory.group_has_children", "Cannot delete an item group that has child subgroups.");



        var hasItems = await context.Items.AsNoTracking()

            .AnyAsync(i => i.CompanyId == companyId && i.GroupId == groupId, cancellationToken)

            .ConfigureAwait(false);



        if (hasItems)

            throw new BusinessRuleException("inventory.group_has_items", "Cannot delete an item group that contains items. Reassign items first.");



        context.ItemGroups.Remove(group);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;

    }



    public async Task<IReadOnlyList<ItemAccountMappingResult>> GetAccountMappingsAsync(

        Guid companyId,

        int ownerType,

        Guid ownerId,

        CancellationToken cancellationToken = default)

    {

        var rows = await context.ItemAccountMappings.AsNoTracking()

            .Include(m => m.Account)

            .Where(m => m.CompanyId == companyId && m.OwnerType == (byte)ownerType && m.OwnerId == ownerId)

            .OrderBy(m => m.Role)

            .ToListAsync(cancellationToken).ConfigureAwait(false);



        return rows.Select(r => new ItemAccountMappingResult(

            Id: r.Id,

            CompanyId: r.CompanyId,

            OwnerType: r.OwnerType,

            OwnerId: r.OwnerId,

            Role: r.Role,

            AccountId: r.AccountId,

            AccountCode: r.Account?.Code ?? string.Empty,

            AccountNameArabic: r.Account?.NameArabic ?? string.Empty,

            IsActive: r.IsActive)).ToList().AsReadOnly();

    }



    public async Task SetAccountMappingsAsync(

        Guid companyId,

        int ownerType,

        Guid ownerId,

        IReadOnlyList<SetItemAccountMappingCommand> mappings,

        CancellationToken cancellationToken = default)

    {

        ArgumentNullException.ThrowIfNull(mappings);



        if (ownerType == 1)

        {

            var itemExists = await context.Items.AsNoTracking()

                .AnyAsync(i => i.CompanyId == companyId && i.Id == ownerId, cancellationToken).ConfigureAwait(false);

            if (!itemExists) throw new ResourceNotFoundException("Item");

        }

        else if (ownerType == 2)

        {

            var groupExists = await context.ItemGroups.AsNoTracking()

                .AnyAsync(g => g.CompanyId == companyId && g.Id == ownerId, cancellationToken).ConfigureAwait(false);

            if (!groupExists) throw new ResourceNotFoundException("ItemGroup");

        }

        else

        {

            throw new ArgumentOutOfRangeException(nameof(ownerType));

        }



        var accountIds = mappings.Select(m => m.AccountId).Distinct().ToList();

        if (accountIds.Count > 0)

        {

            var accounts = await context.Accounts.AsNoTracking()

                .Where(a => a.CompanyId == companyId && accountIds.Contains(a.Id))

                .ToListAsync(cancellationToken).ConfigureAwait(false);



            if (accounts.Count != accountIds.Count)

                throw new BusinessRuleException("inventory.account_not_found", "One or more mapped accounts were not found in this company.");



            if (accounts.Any(a => !a.IsActive))

                throw new BusinessRuleException("inventory.account_inactive", "Cannot map to an inactive accounting account.");



            if (accounts.Any(a => !a.PostingAllowed))

                throw new BusinessRuleException("inventory.account_parent_posting_forbidden", "Cannot map to an account that does not allow direct posting.");

        }



        var existing = await context.ItemAccountMappings

            .Where(m => m.CompanyId == companyId && m.OwnerType == (byte)ownerType && m.OwnerId == ownerId)

            .ToListAsync(cancellationToken).ConfigureAwait(false);



        context.ItemAccountMappings.RemoveRange(existing);



        foreach (var mapping in mappings)

        {

            context.ItemAccountMappings.Add(new ItemAccountMappingRow

            {

                Id = Guid.NewGuid(),

                CompanyId = companyId,

                OwnerType = (byte)ownerType,

                OwnerId = ownerId,

                Role = mapping.Role.Trim(),

                AccountId = mapping.AccountId,

                IsActive = true

            });

        }



        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

    }



    public async Task<EffectiveItemAccountMappingResult> GetEffectiveAccountMappingAsync(

        Guid companyId,

        Guid itemId,

        CancellationToken cancellationToken = default)

    {

        var item = await context.Items.AsNoTracking()

            .Include(i => i.Group)

            .FirstOrDefaultAsync(i => i.CompanyId == companyId && i.Id == itemId, cancellationToken)

            .ConfigureAwait(false) ?? throw new ResourceNotFoundException("Item");



        var allAccounts = await context.Accounts.AsNoTracking()

            .Where(a => a.CompanyId == companyId)

            .ToDictionaryAsync(a => a.Id, cancellationToken).ConfigureAwait(false);



        var itemMappings = await context.ItemAccountMappings.AsNoTracking()

            .Where(m => m.CompanyId == companyId && m.OwnerType == 1 && m.OwnerId == itemId && m.IsActive)

            .ToDictionaryAsync(m => m.Role, StringComparer.OrdinalIgnoreCase, cancellationToken).ConfigureAwait(false);



        var groupChain = new List<ItemGroupRow>();

        var currentGroupId = item.GroupId;

        while (currentGroupId.HasValue)

        {

            var grp = await context.ItemGroups.AsNoTracking()

                .FirstOrDefaultAsync(g => g.CompanyId == companyId && g.Id == currentGroupId.Value, cancellationToken).ConfigureAwait(false);

            if (grp is null) break;

            groupChain.Add(grp);

            currentGroupId = grp.ParentId;

        }



        var groupIds = groupChain.Select(g => g.Id).ToList();

        var allGroupMappings = await context.ItemAccountMappings.AsNoTracking()

            .Where(m => m.CompanyId == companyId && m.OwnerType == 2 && groupIds.Contains(m.OwnerId) && m.IsActive)

            .ToListAsync(cancellationToken).ConfigureAwait(false);



        var roles = new[] { "Revenue", "Inventory", "COGS", "Purchase", "SalesDiscount", "PurchaseDiscount", "SalesTax", "PurchaseTax" };

        var details = new List<EffectiveAccountDetailResult>();



        foreach (var role in roles)

        {

            if (itemMappings.TryGetValue(role, out var itemMap))

            {

                allAccounts.TryGetValue(itemMap.AccountId, out var acc);

                details.Add(new EffectiveAccountDetailResult(

                    Role: role,

                    AccountId: itemMap.AccountId,

                    AccountCode: acc?.Code,

                    AccountNameArabic: acc?.NameArabic,

                    Source: "ItemOverride",

                    SourceDescription: "مخصص على مستوى المادة"));

                continue;

            }



            var resolvedFromGroup = false;

            for (var i = 0; i < groupChain.Count; i++)

            {

                var grp = groupChain[i];

                var grpMap = allGroupMappings.FirstOrDefault(m => m.OwnerId == grp.Id && string.Equals(m.Role, role, StringComparison.OrdinalIgnoreCase));

                if (grpMap is not null)

                {

                    allAccounts.TryGetValue(grpMap.AccountId, out var acc);

                    var isImmediate = i == 0;

                    details.Add(new EffectiveAccountDetailResult(

                        Role: role,

                        AccountId: grpMap.AccountId,

                        AccountCode: acc?.Code,

                        AccountNameArabic: acc?.NameArabic,

                        Source: isImmediate ? "ImmediateGroup" : "AncestorGroup",

                        SourceDescription: isImmediate ? $"موروث من المجموعة: {grp.NameArabic}" : $"موروث من المجموعة الأصل: {grp.NameArabic}"));

                    resolvedFromGroup = true;

                    break;

                }

            }



            if (!resolvedFromGroup)

            {

                details.Add(new EffectiveAccountDetailResult(

                    Role: role,

                    AccountId: null,

                    AccountCode: null,

                    AccountNameArabic: null,

                    Source: "Unresolved",

                    SourceDescription: "غير معين (يتبع النمط أو الشركة)"));

            }

        }



        return new EffectiveItemAccountMappingResult(

            ItemId: item.Id,

            ItemCode: item.Code,

            ItemNameArabic: item.NameArabic,

            GroupId: item.GroupId,

            GroupNameArabic: item.Group?.NameArabic,

            Accounts: details);

    }



    // ==========================================

    // Units & Conversions

    // ==========================================



    public async Task<IReadOnlyList<UnitOfMeasureResult>> GetUnitsAsync(Guid companyId, CancellationToken cancellationToken = default)

    {

        await ValidateCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);



        var rows = await context.UnitsOfMeasure

            .AsNoTracking()

            .Where(u => u.CompanyId == companyId)

            .OrderBy(u => u.Code)

            .ToListAsync(cancellationToken)

            .ConfigureAwait(false);



        return rows.Select(u => new UnitOfMeasureResult(

            Id: u.Id,

            CompanyId: u.CompanyId,

            Code: u.Code,

            NameArabic: u.NameArabic,

            NameEnglish: u.NameEnglish,

            Symbol: u.Symbol,

            DecimalPlaces: u.DecimalPlaces,

            IsActive: u.IsActive)).ToList();

    }



    public async Task<UnitOfMeasureResult> CreateUnitAsync(

        Guid companyId,

        CreateUnitCommand command,

        CancellationToken cancellationToken = default)

    {

        ArgumentNullException.ThrowIfNull(command);

        var currencyTransaction = await PetraERP.Infrastructure.Currencies.CurrencyWriteTransaction.BeginAsync(context, companyId, cancellationToken).ConfigureAwait(false);

        await using var currencyLifetime = currencyTransaction.ConfigureAwait(false);

        await ValidateCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);



        var codeNorm = command.Code.Trim();

        var exists = await context.UnitsOfMeasure

            .AnyAsync(u => u.CompanyId == companyId && u.Code == codeNorm, cancellationToken)

            .ConfigureAwait(false);



        if (exists)

        {

            throw new ResourceConflictException("UnitOfMeasure", $"A unit with code '{codeNorm}' already exists.");

        }



        var unit = new UnitOfMeasureRow

        {

            Id = Guid.NewGuid(),

            CompanyId = companyId,

            Code = codeNorm,

            NameArabic = command.NameArabic.Trim(),

            NameEnglish = string.IsNullOrWhiteSpace(command.NameEnglish) ? null : command.NameEnglish.Trim(),

            Symbol = command.Symbol.Trim(),

            DecimalPlaces = command.DecimalPlaces,

            Dimension = (byte)RequireDimension(command.Dimension),

            IsActive = true

        };



        context.UnitsOfMeasure.Add(unit);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await currencyTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);



        return new UnitOfMeasureResult(

            Id: unit.Id,

            CompanyId: unit.CompanyId,

            Code: unit.Code,

            NameArabic: unit.NameArabic,

            NameEnglish: unit.NameEnglish,

            Symbol: unit.Symbol,

            DecimalPlaces: unit.DecimalPlaces,

            IsActive: unit.IsActive,

            Dimension: (UnitDimension)unit.Dimension);

    }



    public async Task<UnitOfMeasureResult> UpdateUnitAsync(

        Guid companyId,

        Guid unitId,

        UpdateUnitCommand command,

        CancellationToken cancellationToken = default)

    {

        ArgumentNullException.ThrowIfNull(command);

        var currencyTransaction = await PetraERP.Infrastructure.Currencies.CurrencyWriteTransaction.BeginAsync(context, companyId, cancellationToken).ConfigureAwait(false);

        await using var currencyLifetime = currencyTransaction.ConfigureAwait(false);



        var unit = await context.UnitsOfMeasure

            .FirstOrDefaultAsync(u => u.CompanyId == companyId && u.Id == unitId, cancellationToken)

            .ConfigureAwait(false);



        if (unit is null)

        {

            throw new ResourceNotFoundException("UnitOfMeasure");

        }



        unit.NameArabic = command.NameArabic.Trim();

        unit.NameEnglish = string.IsNullOrWhiteSpace(command.NameEnglish) ? null : command.NameEnglish.Trim();

        unit.Symbol = command.Symbol.Trim();

        unit.DecimalPlaces = command.DecimalPlaces;

        if (command.Dimension is { } dimension && (byte)RequireDimension(dimension) != unit.Dimension)

        {

            // Classifying a unit later is allowed; reclassifying one that conversions already rely on is not.

            if (unit.Dimension != 0 && await context.UnitConversions.AnyAsync(c => c.CompanyId == companyId && (c.FromUnitId == unitId || c.ToUnitId == unitId), cancellationToken).ConfigureAwait(false))

            {

                throw new BusinessRuleException("inventory.unit_dimension_locked", "The unit's dimension cannot change while conversions use it.");

            }

            unit.Dimension = (byte)dimension;

            var conversions = await context.UnitConversions.AsNoTracking()

                .Where(c => c.CompanyId == companyId && (c.FromUnitId == unitId || c.ToUnitId == unitId))

                .Select(c => new { c.ItemId, c.FromUnitId, c.ToUnitId }).ToListAsync(cancellationToken).ConfigureAwait(false);

            foreach (var conversion in conversions)

            {

                var otherId = conversion.FromUnitId == unitId ? conversion.ToUnitId : conversion.FromUnitId;

                var otherDimension = await context.UnitsOfMeasure.AsNoTracking().Where(u => u.Id == otherId && u.CompanyId == companyId)

                    .Select(u => u.Dimension).SingleAsync(cancellationToken).ConfigureAwait(false);

                try { UnitConversionPolicy.RequireAllowed(conversion.ItemId, unitId, dimension, otherId, (UnitDimension)otherDimension); }

                catch (InvalidOperationException exception) when (RuleViolation.CodeOf(exception) is { } code)

                { throw new BusinessRuleException(code, exception.Message, "dimension"); }

            }

        }



        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await currencyTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);



        return new UnitOfMeasureResult(

            Id: unit.Id,

            CompanyId: unit.CompanyId,

            Code: unit.Code,

            NameArabic: unit.NameArabic,

            NameEnglish: unit.NameEnglish,

            Symbol: unit.Symbol,

            DecimalPlaces: unit.DecimalPlaces,

            IsActive: unit.IsActive,

            Dimension: (UnitDimension)unit.Dimension);

    }



    public async Task<IReadOnlyList<UnitConversionResult>> GetUnitConversionsAsync(

        Guid companyId,

        Guid? itemId = null,

        CancellationToken cancellationToken = default)

    {

        await ValidateCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);



        var query = context.UnitConversions

            .AsNoTracking()

            .Include(c => c.FromUnit)

            .Include(c => c.ToUnit)

            .Where(c => c.CompanyId == companyId);



        if (itemId.HasValue)

        {

            query = query.Where(c => c.ItemId == itemId.Value || c.ItemId == null);

        }



        var rows = await query.ToListAsync(cancellationToken).ConfigureAwait(false);



        return rows.Select(c => new UnitConversionResult(

            Id: c.Id,

            CompanyId: c.CompanyId,

            ItemId: c.ItemId,

            FromUnitId: c.FromUnitId,

            FromUnitCode: c.FromUnit?.Code ?? string.Empty,

            FromUnitSymbol: c.FromUnit?.Symbol ?? string.Empty,

            ToUnitId: c.ToUnitId,

            ToUnitCode: c.ToUnit?.Code ?? string.Empty,

            ToUnitSymbol: c.ToUnit?.Symbol ?? string.Empty,

            Factor: c.Factor)).ToList();

    }



    public async Task<UnitConversionResult> CreateUnitConversionAsync(

        Guid companyId,

        CreateUnitConversionCommand command,

        CancellationToken cancellationToken = default)

    {

        ArgumentNullException.ThrowIfNull(command);

        var currencyTransaction = await PetraERP.Infrastructure.Currencies.CurrencyWriteTransaction.BeginAsync(context, companyId, cancellationToken).ConfigureAwait(false);

        await using var currencyLifetime = currencyTransaction.ConfigureAwait(false);

        await ValidateCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);



        if (command.Factor <= 0)

        {

            throw new BusinessRuleException("inventory.conversion_factor_invalid", "Conversion factor must be greater than zero.");

        }



        var fromUnit = await context.UnitsOfMeasure

            .FirstOrDefaultAsync(u => u.CompanyId == companyId && u.Id == command.FromUnitId, cancellationToken)

            .ConfigureAwait(false);



        if (fromUnit is null)

        {

            throw new BusinessRuleException("inventory.from_unit_not_found", $"Source unit '{command.FromUnitId}' was not found.");

        }



        var toUnit = await context.UnitsOfMeasure

            .FirstOrDefaultAsync(u => u.CompanyId == companyId && u.Id == command.ToUnitId, cancellationToken)

            .ConfigureAwait(false);



        if (toUnit is null)

        {

            throw new BusinessRuleException("inventory.to_unit_not_found", $"Target unit '{command.ToUnitId}' was not found.");

        }



        if (command.ItemId is { } conversionItemId

            && !await context.Items.AnyAsync(i => i.Id == conversionItemId && i.CompanyId == companyId, cancellationToken).ConfigureAwait(false))

        {

            throw new BusinessRuleException("inventory.item_not_found", "The item does not belong to this company.", "itemId");

        }



        try

        {

            UnitConversionPolicy.RequireAllowed(command.ItemId, command.FromUnitId, (UnitDimension)fromUnit.Dimension, command.ToUnitId, (UnitDimension)toUnit.Dimension);

        }

        catch (InvalidOperationException exception) when (RuleViolation.CodeOf(exception) is { } code)

        {

            throw new BusinessRuleException(code, exception.Message, "toUnitId");

        }



        if (decimal.Round(command.Factor, 6) != command.Factor)

        {

            throw new BusinessRuleException("inventory.conversion_factor_invalid", "The conversion factor allows at most six decimals.", "factor");

        }



        // One conversion per unit pair and scope in either direction, so the engine never has to choose between two factors.

        if (await context.UnitConversions.AnyAsync(c => c.CompanyId == companyId && c.ItemId == command.ItemId

                && ((c.FromUnitId == command.FromUnitId && c.ToUnitId == command.ToUnitId) || (c.FromUnitId == command.ToUnitId && c.ToUnitId == command.FromUnitId)),

                cancellationToken).ConfigureAwait(false))

        {

            throw new BusinessRuleException(UnitConversionPolicy.ConversionDuplicate, "A conversion between these units already exists for this scope.", "fromUnitId");

        }



        var row = new UnitConversionRow

        {

            Id = Guid.NewGuid(),

            CompanyId = companyId,

            ItemId = command.ItemId,

            FromUnitId = command.FromUnitId,

            ToUnitId = command.ToUnitId,

            Factor = command.Factor

        };



        context.UnitConversions.Add(row);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await currencyTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);



        return new UnitConversionResult(

            Id: row.Id,

            CompanyId: row.CompanyId,

            ItemId: row.ItemId,

            FromUnitId: row.FromUnitId,

            FromUnitCode: fromUnit.Code,

            FromUnitSymbol: fromUnit.Symbol,

            ToUnitId: row.ToUnitId,

            ToUnitCode: toUnit.Code,

            ToUnitSymbol: toUnit.Symbol,

            Factor: row.Factor);

    }



    // ==========================================

    // Warehouses

    // ==========================================



    public async Task<IReadOnlyList<WarehouseResult>> GetWarehousesAsync(

        Guid companyId,

        Guid? branchId = null,

        CancellationToken cancellationToken = default)

    {

        await ValidateCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);



        var query = context.Warehouses

            .AsNoTracking()

            .Where(w => w.CompanyId == companyId);



        if (branchId.HasValue)

        {

            query = query.Where(w => w.BranchId == branchId.Value);

        }



        var rows = await query

            .OrderBy(w => w.Code)

            .ToListAsync(cancellationToken)

            .ConfigureAwait(false);



        return rows.Select(w => new WarehouseResult(

            Id: w.Id,

            CompanyId: w.CompanyId,

            BranchId: w.BranchId,

            Code: w.Code,

            NameArabic: w.NameArabic,

            NameEnglish: w.NameEnglish,

            Location: w.Location,

            IsActive: w.IsActive)).ToList();

    }



    public async Task<WarehouseResult> CreateWarehouseAsync(

        Guid companyId,

        CreateWarehouseCommand command,

        CancellationToken cancellationToken = default)

    {

        ArgumentNullException.ThrowIfNull(command);

        await ValidateCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);



        var codeNorm = command.Code.Trim();

        var exists = await context.Warehouses

            .AnyAsync(w => w.CompanyId == companyId && w.Code == codeNorm, cancellationToken)

            .ConfigureAwait(false);



        if (exists)

        {

            throw new ResourceConflictException("Warehouse", $"A warehouse with code '{codeNorm}' already exists.");

        }



        var branchExists = await context.Branches

            .AnyAsync(b => b.CompanyId == companyId && b.Id == command.BranchId, cancellationToken)

            .ConfigureAwait(false);



        if (!branchExists)

        {

            throw new BusinessRuleException("inventory.branch_not_found", $"Branch '{command.BranchId}' was not found.");

        }



        var warehouse = new WarehouseRow

        {

            Id = Guid.NewGuid(),

            CompanyId = companyId,

            BranchId = command.BranchId,

            Code = codeNorm,

            NameArabic = command.NameArabic.Trim(),

            NameEnglish = string.IsNullOrWhiteSpace(command.NameEnglish) ? null : command.NameEnglish.Trim(),

            Location = string.IsNullOrWhiteSpace(command.Location) ? null : command.Location.Trim(),

            IsActive = true

        };



        context.Warehouses.Add(warehouse);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);



        return new WarehouseResult(

            Id: warehouse.Id,

            CompanyId: warehouse.CompanyId,

            BranchId: warehouse.BranchId,

            Code: warehouse.Code,

            NameArabic: warehouse.NameArabic,

            NameEnglish: warehouse.NameEnglish,

            Location: warehouse.Location,

            IsActive: warehouse.IsActive);

    }



    public Task<WarehouseResult> UpdateWarehouseAsync(Guid companyId, Guid warehouseId, UpdateWarehouseCommand command, CancellationToken cancellationToken = default)

        => UpdateWarehouseInBranchAsync(companyId, warehouseId, command, null, cancellationToken);



    public async Task<WarehouseResult> UpdateWarehouseInBranchAsync(

        Guid companyId,

        Guid warehouseId,

        UpdateWarehouseCommand command,

        Guid? branchId, CancellationToken cancellationToken = default)

    {

        ArgumentNullException.ThrowIfNull(command);



        var warehouse = await context.Warehouses

            .FirstOrDefaultAsync(w => w.CompanyId == companyId && w.Id == warehouseId && (!branchId.HasValue || w.BranchId == branchId.Value), cancellationToken)

            .ConfigureAwait(false);



        if (warehouse is null)

        {

            throw new ResourceNotFoundException("Warehouse");

        }



        warehouse.NameArabic = command.NameArabic.Trim();

        warehouse.NameEnglish = string.IsNullOrWhiteSpace(command.NameEnglish) ? null : command.NameEnglish.Trim();

        warehouse.Location = string.IsNullOrWhiteSpace(command.Location) ? null : command.Location.Trim();



        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);



        return new WarehouseResult(

            Id: warehouse.Id,

            CompanyId: warehouse.CompanyId,

            BranchId: warehouse.BranchId,

            Code: warehouse.Code,

            NameArabic: warehouse.NameArabic,

            NameEnglish: warehouse.NameEnglish,

            Location: warehouse.Location,

            IsActive: warehouse.IsActive);

    }



    // ==========================================

    // Stock & Movements

    // ==========================================



    public Task<ItemStockSummaryResult> GetItemStockAsync(Guid companyId, Guid itemId, Guid? warehouseId = null, CancellationToken cancellationToken = default)

        => GetItemStockInBranchAsync(companyId, itemId, warehouseId, null, cancellationToken);



    public async Task<ItemStockSummaryResult> GetItemStockInBranchAsync(

        Guid companyId,

        Guid itemId,

        Guid? warehouseId,

        Guid? branchId, CancellationToken cancellationToken = default)

    {

        var item = await context.Items

            .AsNoTracking()

            .FirstOrDefaultAsync(i => i.CompanyId == companyId && i.Id == itemId, cancellationToken)

            .ConfigureAwait(false);



        if (item is null)

        {

            throw new ResourceNotFoundException("Item");

        }



        var movementsQuery = context.StockMovements

            .AsNoTracking()

            .Where(m => m.CompanyId == companyId && m.ItemId == itemId);



        if (branchId.HasValue) movementsQuery = movementsQuery.Where(m => m.BranchId == branchId.Value);

        if (warehouseId.HasValue)

        {

            movementsQuery = movementsQuery.Where(m => m.WarehouseId == warehouseId.Value);

        }



        var warehouseStockData = await movementsQuery

            .GroupBy(m => m.WarehouseId)

            .Select(g => new

            {

                WarehouseId = g.Key,

                TotalStock = g.Sum(m => m.BaseQuantity),

                TotalValue = g.Sum(m => m.BaseQuantity < 0 ? -m.TotalCost : m.TotalCost)

            })

            .ToListAsync(cancellationToken)

            .ConfigureAwait(false);



        var warehouses = await context.Warehouses

            .AsNoTracking()

            .Where(w => w.CompanyId == companyId)

            .ToDictionaryAsync(w => w.Id, cancellationToken)

            .ConfigureAwait(false);



        var whResults = warehouseStockData.Select(ws => new WarehouseStockResult(

            WarehouseId: ws.WarehouseId,

            WarehouseCode: warehouses.TryGetValue(ws.WarehouseId, out var wh) ? wh.Code : string.Empty,

            WarehouseName: warehouses.TryGetValue(ws.WarehouseId, out var wh2) ? wh2.NameArabic : string.Empty,

            Stock: ws.TotalStock,

            Value: ws.TotalValue)).ToList();



        var totalStock = whResults.Sum(w => w.Stock);

        var totalVal = whResults.Sum(w => w.Value);

        var avgCost = totalStock != 0 ? totalVal / totalStock : 0m;



        var baseUnit = await context.UnitsOfMeasure

            .AsNoTracking()

            .FirstOrDefaultAsync(u => u.Id == item.BaseUnitId, cancellationToken)

            .ConfigureAwait(false);



        return new ItemStockSummaryResult(

            ItemId: item.Id,

            ItemCode: item.Code,

            ItemNameArabic: item.NameArabic,

            BaseUnitSymbol: baseUnit?.Symbol ?? string.Empty,

            TotalStock: totalStock,

            TotalValue: totalVal,

            AverageCost: avgCost,

            Warehouses: whResults);

    }



    public async Task<StockMovementPageResult> GetStockMovementsAsync(

        Guid companyId,

        StockMovementQuery query,

        CancellationToken cancellationToken = default)

    {

        ArgumentNullException.ThrowIfNull(query);

        await ValidateCompanyAsync(companyId, cancellationToken).ConfigureAwait(false);



        var baseQuery = context.StockMovements

            .AsNoTracking()

            .Where(m => m.CompanyId == companyId);



        if (query.BranchId.HasValue) baseQuery = baseQuery.Where(m => m.BranchId == query.BranchId.Value);

        if (query.FromDateUtc > query.ToDateUtc) throw new BusinessRuleException("inventory.range", "الفترة غير صالحة / Invalid date range.");

        if (query.WarehouseId.HasValue)

        {

            baseQuery = baseQuery.Where(m => m.WarehouseId == query.WarehouseId.Value);

        }



        if (query.ItemId.HasValue)

        {

            baseQuery = baseQuery.Where(m => m.ItemId == query.ItemId.Value);

        }



        if (query.FromDateUtc.HasValue)

        {

            baseQuery = baseQuery.Where(m => m.MovementDateUtc >= query.FromDateUtc.Value);

        }



        if (query.ToDateUtc.HasValue)

        {

            baseQuery = baseQuery.Where(m => m.MovementDateUtc <= query.ToDateUtc.Value);

        }



        var totalCount = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);



        var page = Math.Max(1, query.Page);

        var pageSize = Math.Clamp(query.PageSize, 1, 200);



        var rows = await baseQuery

            .OrderByDescending(m => m.MovementDateUtc)

            .ThenByDescending(m => m.CreatedAtUtc)

            .Skip((page - 1) * pageSize)

            .Take(pageSize)

            .ToListAsync(cancellationToken)

            .ConfigureAwait(false);



        var whIds = rows.Select(m => m.WarehouseId).Distinct().ToList();

        var itIds = rows.Select(m => m.ItemId).Distinct().ToList();

        var ccIds = rows.Where(m => m.CostCenterId.HasValue).Select(m => m.CostCenterId!.Value).Distinct().ToList();



        var warehouses = await context.Warehouses

            .AsNoTracking()

            .Where(w => w.CompanyId == companyId && whIds.Contains(w.Id))

            .ToDictionaryAsync(w => w.Id, cancellationToken)

            .ConfigureAwait(false);



        var itemsMap = await context.Items

            .AsNoTracking()

            .Where(i => i.CompanyId == companyId && itIds.Contains(i.Id))

            .ToDictionaryAsync(i => i.Id, cancellationToken)

            .ConfigureAwait(false);



        var costCenters = ccIds.Count > 0

            ? await context.CostCenters

                .AsNoTracking()

                .Where(c => c.CompanyId == companyId && ccIds.Contains(c.Id))

                .ToDictionaryAsync(c => c.Id, cancellationToken)

                .ConfigureAwait(false)

            : [];



        var uIds = rows.Select(m => m.UnitId).Distinct().ToList();

        var units = await context.UnitsOfMeasure

            .AsNoTracking()

            .Where(u => u.CompanyId == companyId && uIds.Contains(u.Id))

            .ToDictionaryAsync(u => u.Id, cancellationToken)

            .ConfigureAwait(false);



        var items = rows.Select(m => new StockMovementResult(

            Id: m.Id,

            CompanyId: m.CompanyId,

            BranchId: m.BranchId,

            WarehouseId: m.WarehouseId,

            WarehouseCode: warehouses.TryGetValue(m.WarehouseId, out var wh) ? wh.Code : string.Empty,

            ItemId: m.ItemId,

            ItemCode: itemsMap.TryGetValue(m.ItemId, out var itm) ? itm.Code : string.Empty,

            ItemName: itemsMap.TryGetValue(m.ItemId, out var itm2) ? itm2.NameArabic : string.Empty,

            UnitId: m.UnitId,

            UnitCode: units.TryGetValue(m.UnitId, out var un) ? un.Code : string.Empty,

            MovementKind: (StockMovementKind)m.MovementKind,

            Quantity: m.Quantity,

            BaseQuantity: m.BaseQuantity,

            UnitCost: m.UnitCost,

            TotalCost: m.TotalCost,

            MovementDateUtc: m.MovementDateUtc,

            DocumentType: m.DocumentType,

            DocumentNumber: m.DocumentNumber,

            CostCenterId: m.CostCenterId,

            CostCenterCode: m.CostCenterId.HasValue && costCenters.TryGetValue(m.CostCenterId.Value, out var cc) ? cc.Code : null,

            Notes: m.Notes)).ToList();



        return new StockMovementPageResult(page, pageSize, totalCount, items);

    }



    // ==========================================

    // Transfers (PT-089)

    // ==========================================







    // ==========================================

    // Adjustments (PT-090)

    // ==========================================











    // ==========================================

    // Internal Issues (PT-091)

    // ==========================================







    // ==========================================

    // Helpers

    // ==========================================



    private async Task ValidateCompanyAsync(Guid companyId, CancellationToken cancellationToken)

    {

        var exists = await context.Companies

            .AnyAsync(c => c.Id == companyId, cancellationToken)

            .ConfigureAwait(false);



        if (!exists)

        {

            throw new ResourceNotFoundException("Company");

        }

    }



    private static bool MatchesItemSearch(ItemRow item, string searchText)

    {

        if (string.IsNullOrWhiteSpace(searchText)) return true;



        var candidate = $"{item.Code} {item.NameArabic} {item.NameEnglish ?? string.Empty}";

        foreach (var b in item.Barcodes)

        {

            candidate += " " + b.Barcode;

        }



        return ArabicTextNormalizer.MatchesTokenPrefixes(searchText, candidate);

    }



    private async Task<Dictionary<Guid, decimal>> GetStockBalancesForItemsAsync(

        Guid companyId,

        List<Guid> itemIds,

        Guid? warehouseId,

        Guid? branchId, CancellationToken cancellationToken)

    {

        if (itemIds.Count == 0) return [];



        var query = context.StockMovements

            .AsNoTracking()

            .Where(m => m.CompanyId == companyId && itemIds.Contains(m.ItemId) && (!branchId.HasValue || m.BranchId == branchId.Value));



        if (warehouseId.HasValue)

        {

            query = query.Where(m => m.WarehouseId == warehouseId.Value);

        }



        var list = await query

            .GroupBy(m => m.ItemId)

            .Select(g => new { ItemId = g.Key, Balance = g.Sum(m => m.BaseQuantity) })

            .ToListAsync(cancellationToken)

            .ConfigureAwait(false);



        return list.ToDictionary(x => x.ItemId, x => x.Balance);

    }



    private async Task<decimal> GetItemTotalStockAsync(Guid companyId, Guid itemId, Guid? branchId, CancellationToken cancellationToken)

    {

        return await context.StockMovements

            .AsNoTracking()

            .Where(m => m.CompanyId == companyId && m.ItemId == itemId && (!branchId.HasValue || m.BranchId == branchId.Value))

            .SumAsync(m => (decimal?)m.BaseQuantity, cancellationToken)

            .ConfigureAwait(false) ?? 0m;

    }



    private async Task<decimal> GetWarehouseItemStockAsync(Guid companyId, Guid warehouseId, Guid itemId, CancellationToken cancellationToken)

    {

        return await context.StockMovements

            .AsNoTracking()

            .Where(m => m.CompanyId == companyId && m.WarehouseId == warehouseId && m.ItemId == itemId)

            .SumAsync(m => (decimal?)m.BaseQuantity, cancellationToken)

            .ConfigureAwait(false) ?? 0m;

    }



    private Task<decimal> ConvertToBaseUnitAsync(Guid companyId, Guid itemId, Guid fromUnitId, Guid baseUnitId, decimal quantity, CancellationToken cancellationToken)

        => InventoryUnits.BaseQuantityAsync(context, companyId, itemId, fromUnitId, baseUnitId, quantity, cancellationToken);

}

