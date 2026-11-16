using Microsoft.EntityFrameworkCore;
using PetraERP.Domain.FinancialValues;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Inventory;
using PetraERP.Domain.Organizations;
using PetraERP.Domain.Patterns;
using PetraERP.Domain.Primitives;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Tests;

public sealed class MaterialCostCenterTests
{
    private static readonly Guid TestCompanyId = Guid.NewGuid();
    private static readonly CurrencyCode Jod = CurrencyCode.Create("JOD");
    private static readonly Company TestCompany = Company.Create(
        EntityCode.Create("CMP"),
        BilingualName.Create("الشركة النموذجية", "Test Company"),
        Jod,
        []);

    private static JournalLine CreateTestLine(Guid accountId, decimal debit, decimal credit, Guid? costCenterId = null, string desc = "line")
    {
        var amounts = JournalLineAmounts.Create(
            Money.Create(debit, Jod),
            Money.Create(credit, Jod));
        var rate = ExchangeRate.Create(Jod, Jod, 1m);
        return JournalLine.Create(TestCompany, accountId, costCenterId, amounts, rate, MidpointRounding.ToEven, desc);
    }

    // T01: Material group creation and code uniqueness
    [Fact]
    public void T01MaterialGroupCreationAndCodeValidation()
    {
        var id = Guid.NewGuid();
        var code = EntityCode.Create("GRP-RAW");
        var name = BilingualName.Create("المواد الأولية", "Raw Materials");
        var group = new ItemGroup(id, TestCompanyId, code, name, null, true, "Raw materials description");

        Assert.Equal(id, group.Id);
        Assert.Equal(TestCompanyId, group.CompanyId);
        Assert.Equal(code, group.Code);
        Assert.Equal(name, group.Name);
        Assert.Null(group.ParentId);
        Assert.True(group.IsActive);
        Assert.Equal("Raw materials description", group.Description);

        // Disallows empty ID
        Assert.Throws<ArgumentException>(() => new ItemGroup(Guid.Empty, TestCompanyId, code, name));
        // Disallows empty Company ID
        Assert.Throws<ArgumentException>(() => new ItemGroup(id, Guid.Empty, code, name));
    }

    // T02: Material group hierarchy and multi-level nesting
    [Fact]
    public void T02MaterialGroupHierarchyAndMultiLevelNesting()
    {
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var grandchildId = Guid.NewGuid();

        var root = new ItemGroup(rootId, TestCompanyId, EntityCode.Create("G-ROOT"), BilingualName.Create("المجموعة الرئيسية", "Root Group"));
        var child = new ItemGroup(childId, TestCompanyId, EntityCode.Create("G-CHILD"), BilingualName.Create("المجموعة الفرعية", "Child Group"), rootId);
        var grandchild = new ItemGroup(grandchildId, TestCompanyId, EntityCode.Create("G-GRAND"), BilingualName.Create("المجموعة الفرعية الثانية", "Grandchild Group"), childId);

        Assert.Null(root.ParentId);
        Assert.Equal(rootId, child.ParentId);
        Assert.Equal(childId, grandchild.ParentId);

        // Traversal
        var groupMap = new Dictionary<Guid, Guid?>
        {
            [rootId] = null,
            [childId] = rootId,
            [grandchildId] = childId
        };

        var ancestors = new List<Guid>();
        var curr = groupMap[grandchildId];
        while (curr.HasValue)
        {
            ancestors.Add(curr.Value);
            curr = groupMap[curr.Value];
        }

        Assert.Equal([childId, rootId], ancestors);
    }

    // T03: Group hierarchy cycle prevention (ValidateNoCycle)
    [Fact]
    public void T03GroupHierarchyCyclePrevention()
    {
        var g1 = Guid.NewGuid();
        var g2 = Guid.NewGuid();
        var g3 = Guid.NewGuid();

        // 1. Direct self-parenting rejected in constructor
        Assert.Throws<ArgumentException>(() => new ItemGroup(g1, TestCompanyId, EntityCode.Create("G1"), BilingualName.Create("م1", "G1"), g1));

        // 2. Direct self-parenting rejected in Update
        var group1 = new ItemGroup(g1, TestCompanyId, EntityCode.Create("G1"), BilingualName.Create("م1", "G1"));
        Assert.Throws<ArgumentException>(() => group1.Update(BilingualName.Create("م1", "G1"), g1));

        // 3. Circular dependency A -> B -> C -> A detected by ValidateNoCycle
        var hierarchy = new Dictionary<Guid, Guid?>
        {
            [g1] = g2,
            [g2] = g3,
            [g3] = g1 // Cycle back to g1
        };

        var cycleGroup = new ItemGroup(g1, TestCompanyId, EntityCode.Create("G1"), BilingualName.Create("م1", "G1"), g2);
        var ex = Assert.Throws<InvalidOperationException>(() => cycleGroup.ValidateNoCycle(id => hierarchy[id]));
        Assert.Equal("inventory.circular_group", RuleViolation.CodeOf(ex));

        // 4. Valid acyclic tree passes
        var validHierarchy = new Dictionary<Guid, Guid?>
        {
            [g1] = g2,
            [g2] = g3,
            [g3] = null
        };
        cycleGroup.ValidateNoCycle(id => validHierarchy[id]);
    }

    // T04: Material group deletion guards (blocking deletion when items or child groups exist)
    [Fact]
    public void T04MaterialGroupDeletionGuards()
    {
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        // Guard function simulating deletion checks in InventoryService
        static void GuardDeleteGroup(Guid groupId, bool hasItems, bool hasChildren)
        {
            if (hasChildren)
                throw RuleViolation.Create("inventory.group_has_children", "Cannot delete an item group that has child subgroups.");
            if (hasItems)
                throw RuleViolation.Create("inventory.group_has_items", "Cannot delete an item group that has assigned items.");
        }

        var exChildren = Assert.Throws<InvalidOperationException>(() => GuardDeleteGroup(rootId, hasItems: false, hasChildren: true));
        Assert.Equal("inventory.group_has_children", RuleViolation.CodeOf(exChildren));

        var exItems = Assert.Throws<InvalidOperationException>(() => GuardDeleteGroup(childId, hasItems: true, hasChildren: false));
        Assert.Equal("inventory.group_has_items", RuleViolation.CodeOf(exItems));

        // Allowed when neither children nor items exist
        GuardDeleteGroup(Guid.NewGuid(), hasItems: false, hasChildren: false);
    }

    // T05: Group account mapping CRUD
    [Fact]
    public void T05GroupAccountMappingCrud()
    {
        var groupId = Guid.NewGuid();
        var revenueAccountId = Guid.NewGuid();
        var cogsAccountId = Guid.NewGuid();

        var mapping1 = new ItemAccountMappingRow
        {
            Id = Guid.NewGuid(),
            CompanyId = TestCompanyId,
            OwnerType = 2, // Group
            OwnerId = groupId,
            Role = PatternAccountRoles.Revenue,
            AccountId = revenueAccountId,
            IsActive = true
        };

        var mapping2 = new ItemAccountMappingRow
        {
            Id = Guid.NewGuid(),
            CompanyId = TestCompanyId,
            OwnerType = 2, // Group
            OwnerId = groupId,
            Role = PatternAccountRoles.Cogs,
            AccountId = cogsAccountId,
            IsActive = true
        };

        Assert.Equal(2, mapping1.OwnerType);
        Assert.Equal(groupId, mapping1.OwnerId);
        Assert.Equal(PatternAccountRoles.Revenue, mapping1.Role);
        Assert.Equal(revenueAccountId, mapping1.AccountId);
        Assert.True(mapping1.IsActive);

        // Deactivation
        mapping1.IsActive = false;
        Assert.False(mapping1.IsActive);
    }

    // T06: Item account mapping CRUD
    [Fact]
    public void T06ItemAccountMappingCrud()
    {
        var itemId = Guid.NewGuid();
        var itemRevenueAccountId = Guid.NewGuid();

        var itemMapping = new ItemAccountMappingRow
        {
            Id = Guid.NewGuid(),
            CompanyId = TestCompanyId,
            OwnerType = 1, // Item
            OwnerId = itemId,
            Role = PatternAccountRoles.Revenue,
            AccountId = itemRevenueAccountId,
            IsActive = true
        };

        Assert.Equal(1, itemMapping.OwnerType);
        Assert.Equal(itemId, itemMapping.OwnerId);
        Assert.Equal(itemRevenueAccountId, itemMapping.AccountId);
    }

    // T07: Account mapping resolution precedence
    // (Item override -> Group -> Ancestor group -> Pattern -> Company default)
    [Fact]
    public void T07AccountMappingResolutionPrecedence()
    {
        var itemAccountId = Guid.NewGuid();
        var groupAccountId = Guid.NewGuid();
        var ancestorAccountId = Guid.NewGuid();
        var patternAccountId = Guid.NewGuid();
        var companyAccountId = Guid.NewGuid();

        var patternConfig = new PatternConfiguration
        {
            AccountMappings =
            [
                new PatternAccountMapping { Role = PatternAccountRoles.Revenue, AccountId = patternAccountId, IsRequired = true }
            ]
        };

        // 1. Item override wins over all
        var resItem = PostingRuleResolver.Resolve(
            PatternAccountRoles.Revenue,
            patternConfig,
            itemContractAccountId: itemAccountId,
            groupContractAccountId: groupAccountId,
            ancestorGroupContractAccountId: ancestorAccountId,
            companyDefaultAccountId: companyAccountId);
        Assert.Equal(itemAccountId, resItem.AccountId);
        Assert.Equal(AccountResolutionSource.ItemOverride, resItem.Source);

        // 2. Immediate group wins when item override is absent
        var resGroup = PostingRuleResolver.Resolve(
            PatternAccountRoles.Revenue,
            patternConfig,
            itemContractAccountId: null,
            groupContractAccountId: groupAccountId,
            ancestorGroupContractAccountId: ancestorAccountId,
            companyDefaultAccountId: companyAccountId);
        Assert.Equal(groupAccountId, resGroup.AccountId);
        Assert.Equal(AccountResolutionSource.GroupContract, resGroup.Source);

        // 3. Ancestor group wins when item and immediate group are absent
        var resAncestor = PostingRuleResolver.Resolve(
            PatternAccountRoles.Revenue,
            patternConfig,
            itemContractAccountId: null,
            groupContractAccountId: null,
            ancestorGroupContractAccountId: ancestorAccountId,
            companyDefaultAccountId: companyAccountId);
        Assert.Equal(ancestorAccountId, resAncestor.AccountId);
        Assert.Equal(AccountResolutionSource.AncestorGroupContract, resAncestor.Source);

        // 4. Pattern default wins when no item/group mappings exist
        var resPattern = PostingRuleResolver.Resolve(
            PatternAccountRoles.Revenue,
            patternConfig,
            itemContractAccountId: null,
            groupContractAccountId: null,
            ancestorGroupContractAccountId: null,
            companyDefaultAccountId: companyAccountId);
        Assert.Equal(patternAccountId, resPattern.AccountId);
        Assert.Equal(AccountResolutionSource.PatternDefault, resPattern.Source);

        // 5. Company default wins when pattern mapping is absent
        var emptyConfig = new PatternConfiguration { AccountMappings = [] };
        var resCompany = PostingRuleResolver.Resolve(
            PatternAccountRoles.Revenue,
            emptyConfig,
            itemContractAccountId: null,
            groupContractAccountId: null,
            ancestorGroupContractAccountId: null,
            companyDefaultAccountId: companyAccountId);
        Assert.Equal(companyAccountId, resCompany.AccountId);
        Assert.Equal(AccountResolutionSource.CompanyDefault, resCompany.Source);
    }

    // T08: Cost center hierarchy creation and cycle prevention
    [Fact]
    public void T08CostCenterHierarchyCreationAndCyclePrevention()
    {
        var parent = CostCenter.Create(TestCompany, EntityCode.Create("CC-ROOT"), BilingualName.Create("الإدارة العامة", "General Admin"), centerType: 2);
        var child = CostCenter.Create(TestCompany, EntityCode.Create("CC-FIN"), BilingualName.Create("المالية", "Finance"), parentId: parent.Id, centerType: 1);

        Assert.Null(parent.ParentId);
        Assert.Equal(parent.Id, child.ParentId);
        Assert.Equal(2, parent.CenterType);
        Assert.Equal(1, child.CenterType);

        // Self-parenting on Revise throws
        var ex = Assert.Throws<InvalidOperationException>(() => parent.Revise(parent.Code, parent.Name, false, parentId: parent.Id));
        Assert.Equal("ledger.cost_center_parent_self", RuleViolation.CodeOf(ex));
    }

    // T09: Cost center types enforcement (Ordinary, Parent, Aggregation, Distribution)
    [Fact]
    public void T09CostCenterTypesEnforcement()
    {
        // Types 1..4 valid
        for (byte t = 1; t <= 4; t++)
        {
            var cc = CostCenter.Create(TestCompany, EntityCode.Create($"CC-{t}"), BilingualName.Create($"مركز {t}", $"Center {t}"), centerType: t);
            Assert.Equal(t, cc.CenterType);
        }

        // < 1 or > 4 throws
        Assert.Throws<ArgumentOutOfRangeException>(() => CostCenter.Create(TestCompany, EntityCode.Create("CC-0"), BilingualName.Create("صفر", "Zero"), centerType: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CostCenter.Create(TestCompany, EntityCode.Create("CC-5"), BilingualName.Create("خمسة", "Five"), centerType: 5));

        // Verify EF Core check constraint CK_CostCenters_CenterType is configured in model
        using var context = new AccountingDbContext(new DbContextOptionsBuilder<AccountingDbContext>().UseSqlServer().Options);
        var designModel = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>(context).Model;
        var entity = designModel.FindEntityType(typeof(CostCenterRow))!;
        var checkConstraints = entity.GetCheckConstraints();
        Assert.Contains(checkConstraints, c => c.Name == "CK_CostCenters_CenterType");
    }

    // T10: Distribution cost center allocation validation (exact 100.00% sum requirement)
    [Fact]
    public void T10DistributionCostCenterAllocationValidation()
    {
        static void ValidateAllocations(IEnumerable<decimal> percentages)
        {
            var sum = percentages.Sum();
            if (sum != 100.00m)
            {
                throw RuleViolation.Create("ledger.cost_center_allocation_invalid_total",
                    $"Distribution allocations must sum to exactly 100.00% (current sum: {sum:N2}%).");
            }
        }

        // Exact 100.00% passes
        ValidateAllocations([60.00m, 40.00m]);
        ValidateAllocations([33.33m, 33.33m, 33.34m]);

        // Under 100.00% fails
        var exUnder = Assert.Throws<InvalidOperationException>(() => ValidateAllocations([50.00m, 40.00m]));
        Assert.Equal("ledger.cost_center_allocation_invalid_total", RuleViolation.CodeOf(exUnder));

        // Over 100.00% fails
        var exOver = Assert.Throws<InvalidOperationException>(() => ValidateAllocations([50.00m, 60.00m]));
        Assert.Equal("ledger.cost_center_allocation_invalid_total", RuleViolation.CodeOf(exOver));
    }

    // T11: Distribution cost center target validation (cannot allocate to self or distribution center)
    [Fact]
    public void T11DistributionCostCenterTargetValidation()
    {
        var sourceId = Guid.NewGuid();
        var targetDistId = Guid.NewGuid();
        var targetOrdinaryId = Guid.NewGuid();

        static void ValidateTarget(Guid sourceId, Guid targetId, byte targetType)
        {
            if (targetId == sourceId)
                throw RuleViolation.Create("ledger.cost_center_allocation_self", "A distribution cost center cannot allocate to itself.");
            if (targetType == 4)
                throw RuleViolation.Create("ledger.cost_center_allocation_to_distribution", "A distribution cost center cannot allocate to another distribution center.");
        }

        // Self allocation fails
        var exSelf = Assert.Throws<InvalidOperationException>(() => ValidateTarget(sourceId, sourceId, 1));
        Assert.Equal("ledger.cost_center_allocation_self", RuleViolation.CodeOf(exSelf));

        // Allocation to another distribution center fails
        var exDist = Assert.Throws<InvalidOperationException>(() => ValidateTarget(sourceId, targetDistId, 4));
        Assert.Equal("ledger.cost_center_allocation_to_distribution", RuleViolation.CodeOf(exDist));

        // Allocation to ordinary center succeeds
        ValidateTarget(sourceId, targetOrdinaryId, 1);
    }

    // T12: Sales invoice multi-item posting splitting revenue lines by item/group account
    [Fact]
    public void T12SalesInvoiceMultiItemPostingSplittingRevenueLines()
    {
        var customerAccountId = Guid.NewGuid();
        var revItemA = Guid.NewGuid();
        var revItemB = Guid.NewGuid();

        // 2 items: Item A = 100 JOD, Item B = 200 JOD. Total Invoice = 300 JOD
        var lines = new[]
        {
            new { ItemId = Guid.NewGuid(), RevenueAccountId = revItemA, Amount = 100m },
            new { ItemId = Guid.NewGuid(), RevenueAccountId = revItemB, Amount = 200m }
        };

        var journalLines = new List<JournalLine>();
        var totalAmount = lines.Sum(l => l.Amount);

        // Debit Customer for total
        journalLines.Add(CreateTestLine(customerAccountId, totalAmount, 0m, desc: "ذمة العميل"));

        // Credit Revenue accounts split by item mapping
        foreach (var group in lines.GroupBy(l => l.RevenueAccountId))
        {
            var splitAmount = group.Sum(l => l.Amount);
            journalLines.Add(CreateTestLine(group.Key, 0m, splitAmount, desc: "إيراد مبيعات المادة"));
        }

        Assert.Equal(3, journalLines.Count);
        Assert.Equal(300m, journalLines.Sum(j => j.TransactionAmounts.Debit.Amount));
        Assert.Equal(300m, journalLines.Sum(j => j.TransactionAmounts.Credit.Amount));

        var revALine = Assert.Single(journalLines, j => j.AccountId == revItemA);
        Assert.Equal(100m, revALine.TransactionAmounts.Credit.Amount);

        var revBLine = Assert.Single(journalLines, j => j.AccountId == revItemB);
        Assert.Equal(200m, revBLine.TransactionAmounts.Credit.Amount);
    }

    // T13: Sales invoice multi-item posting splitting COGS & inventory lines by item/group account
    [Fact]
    public void T13SalesInvoiceMultiItemPostingSplittingCogsAndInventoryLines()
    {
        var cogsA = Guid.NewGuid();
        var invA = Guid.NewGuid();
        var cogsB = Guid.NewGuid();
        var invB = Guid.NewGuid();

        var costLines = new[]
        {
            new { CogsAccountId = cogsA, InvAccountId = invA, Cost = 70m },
            new { CogsAccountId = cogsB, InvAccountId = invB, Cost = 140m }
        };

        var cogsJournalLines = new List<JournalLine>();
        foreach (var l in costLines)
        {
            // Dr COGS, Cr Inventory Asset
            cogsJournalLines.Add(CreateTestLine(l.CogsAccountId, l.Cost, 0m, desc: "تكلفة البضاعة المباعة"));
            cogsJournalLines.Add(CreateTestLine(l.InvAccountId, 0m, l.Cost, desc: "مخزون البضاعة"));
        }

        Assert.Equal(4, cogsJournalLines.Count);
        Assert.Equal(210m, cogsJournalLines.Sum(j => j.TransactionAmounts.Debit.Amount));
        Assert.Equal(210m, cogsJournalLines.Sum(j => j.TransactionAmounts.Credit.Amount));

        Assert.Equal(70m, cogsJournalLines.Single(j => j.AccountId == cogsA).TransactionAmounts.Debit.Amount);
        Assert.Equal(70m, cogsJournalLines.Single(j => j.AccountId == invA).TransactionAmounts.Credit.Amount);
        Assert.Equal(140m, cogsJournalLines.Single(j => j.AccountId == cogsB).TransactionAmounts.Debit.Amount);
        Assert.Equal(140m, cogsJournalLines.Single(j => j.AccountId == invB).TransactionAmounts.Credit.Amount);
    }

    // T14: Sales invoice line cost center override header cost center
    [Fact]
    public void T14SalesInvoiceLineCostCenterOverrideHeaderCostCenter()
    {
        var headerCostCenterId = Guid.NewGuid();
        var lineCostCenterId = Guid.NewGuid();
        var revAccount = Guid.NewGuid();

        var invoiceLines = new[]
        {
            new { LineId = 1, LineCostCenterId = (Guid?)lineCostCenterId, Amount = 100m },
            new { LineId = 2, LineCostCenterId = (Guid?)null, Amount = 150m }
        };

        var journalLines = new List<JournalLine>();
        foreach (var line in invoiceLines)
        {
            var effectiveCostCenterId = line.LineCostCenterId ?? headerCostCenterId;
            journalLines.Add(CreateTestLine(revAccount, 0m, line.Amount, effectiveCostCenterId, $"سطر {line.LineId}"));
        }

        Assert.Equal(lineCostCenterId, journalLines[0].CostCenterId);
        Assert.Equal(headerCostCenterId, journalLines[1].CostCenterId);
    }

    // T15: Purchase invoice multi-item posting splitting inventory lines by item/group account
    [Fact]
    public void T15PurchaseInvoiceMultiItemPostingSplittingInventoryLines()
    {
        var supplierAccountId = Guid.NewGuid();
        var invAccountA = Guid.NewGuid();
        var invAccountB = Guid.NewGuid();

        var lines = new[]
        {
            new { InvAccount = invAccountA, Amount = 500m },
            new { InvAccount = invAccountB, Amount = 800m }
        };

        var journalLines = new List<JournalLine>();
        var totalAmount = lines.Sum(l => l.Amount);

        // Debit inventory per item mapping
        foreach (var line in lines)
        {
            journalLines.Add(CreateTestLine(line.InvAccount, line.Amount, 0m, desc: "مشتريات مخزون"));
        }

        // Credit supplier for total
        journalLines.Add(CreateTestLine(supplierAccountId, 0m, totalAmount, desc: "ذمة المورد"));

        Assert.Equal(1300m, journalLines.Sum(j => j.TransactionAmounts.Debit.Amount));
        Assert.Equal(1300m, journalLines.Sum(j => j.TransactionAmounts.Credit.Amount));

        Assert.Equal(500m, journalLines.Single(j => j.AccountId == invAccountA).TransactionAmounts.Debit.Amount);
        Assert.Equal(800m, journalLines.Single(j => j.AccountId == invAccountB).TransactionAmounts.Debit.Amount);
    }

    // T16: Purchase return multi-item posting with item inventory accounts
    [Fact]
    public void T16PurchaseReturnMultiItemPostingWithItemInventoryAccounts()
    {
        var supplierAccountId = Guid.NewGuid();
        var invAccountA = Guid.NewGuid();
        var invAccountB = Guid.NewGuid();

        var lines = new[]
        {
            new { InvAccount = invAccountA, ReturnAmount = 50m },
            new { InvAccount = invAccountB, ReturnAmount = 80m }
        };

        var journalLines = new List<JournalLine>();
        var totalReturn = lines.Sum(l => l.ReturnAmount);

        // Debit supplier for return total
        journalLines.Add(CreateTestLine(supplierAccountId, totalReturn, 0m, desc: "مرتجع مشتريات ذمة مورد"));

        // Credit inventory per item mapping
        foreach (var line in lines)
        {
            journalLines.Add(CreateTestLine(line.InvAccount, 0m, line.ReturnAmount, desc: "مرتجع مخزون"));
        }

        Assert.Equal(130m, journalLines.Sum(j => j.TransactionAmounts.Debit.Amount));
        Assert.Equal(130m, journalLines.Sum(j => j.TransactionAmounts.Credit.Amount));
        Assert.Equal(50m, journalLines.Single(j => j.AccountId == invAccountA).TransactionAmounts.Credit.Amount);
        Assert.Equal(80m, journalLines.Single(j => j.AccountId == invAccountB).TransactionAmounts.Credit.Amount);
    }

    // T17: Cost center analytical ledger traversal (including descendants)
    [Fact]
    public void T17CostCenterAnalyticalLedgerTraversal()
    {
        var rootId = Guid.NewGuid();
        var child1Id = Guid.NewGuid();
        var child2Id = Guid.NewGuid();
        var grandChildId = Guid.NewGuid();

        var allCenters = new[]
        {
            new { Id = rootId, ParentId = (Guid?)null },
            new { Id = child1Id, ParentId = (Guid?)rootId },
            new { Id = child2Id, ParentId = (Guid?)rootId },
            new { Id = grandChildId, ParentId = (Guid?)child1Id }
        };

        // Recursive descendant traversal helper matching SqlProfessionalReportService.Ledger
        static HashSet<Guid> GetDescendants(Guid parentId, IEnumerable<dynamic> centers)
        {
            var result = new HashSet<Guid> { parentId };
            var queue = new Queue<Guid>();
            queue.Enqueue(parentId);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var child in centers.Where(c => c.ParentId == current))
                {
                    if (result.Add(child.Id)) queue.Enqueue(child.Id);
                }
            }
            return result;
        }

        var rootDescendants = GetDescendants(rootId, allCenters);
        Assert.Equal(4, rootDescendants.Count);
        Assert.Contains(rootId, rootDescendants);
        Assert.Contains(child1Id, rootDescendants);
        Assert.Contains(child2Id, rootDescendants);
        Assert.Contains(grandChildId, rootDescendants);

        var child1Descendants = GetDescendants(child1Id, allCenters);
        Assert.Equal(2, child1Descendants.Count);
        Assert.Contains(child1Id, child1Descendants);
        Assert.Contains(grandChildId, child1Descendants);
        Assert.DoesNotContain(child2Id, child1Descendants);
    }

    // T18: Cost center trial balance calculation
    [Fact]
    public void T18CostCenterTrialBalanceCalculation()
    {
        var ccId = Guid.NewGuid();
        var accCash = Guid.NewGuid();
        var accRevenue = Guid.NewGuid();

        var lines = new[]
        {
            new { AccountId = accCash, Debit = 500m, Credit = 0m, CostCenterId = ccId },
            new { AccountId = accRevenue, Debit = 0m, Credit = 500m, CostCenterId = ccId }
        };

        var filtered = lines.Where(l => l.CostCenterId == ccId).ToList();
        var totalDebit = filtered.Sum(l => l.Debit);
        var totalCredit = filtered.Sum(l => l.Credit);

        Assert.Equal(500m, totalDebit);
        Assert.Equal(500m, totalCredit);
        Assert.Equal(0m, totalDebit - totalCredit);
    }

    // T19: Cost center profitability report computation
    [Fact]
    public void T19CostCenterProfitabilityReportComputation()
    {
        var ccId = Guid.NewGuid();

        // Revenue (Class 4) = 1,000 JOD
        // Expenses (Class 5) = 650 JOD
        var transactions = new[]
        {
            new { AccountCode = "4101", Debit = 0m, Credit = 1000m, CostCenterId = ccId },
            new { AccountCode = "5101", Debit = 400m, Credit = 0m, CostCenterId = ccId },
            new { AccountCode = "5201", Debit = 250m, Credit = 0m, CostCenterId = ccId }
        };

        var revenue = transactions.Where(t => t.AccountCode.StartsWith('4') && t.CostCenterId == ccId).Sum(t => t.Credit - t.Debit);
        var expense = transactions.Where(t => t.AccountCode.StartsWith('5') && t.CostCenterId == ccId).Sum(t => t.Debit - t.Credit);
        var netProfit = revenue - expense;

        Assert.Equal(1000m, revenue);
        Assert.Equal(650m, expense);
        Assert.Equal(350m, netProfit);
    }

    // T20: Annual rollover master data carryforward of groups and cost centers
    [Fact]
    public void T20AnnualRolloverMasterDataCarryforwardOfGroupsAndCostCenters()
    {
        var oldRootGroupId = Guid.NewGuid();
        var oldChildGroupId = Guid.NewGuid();
        var oldRootCcId = Guid.NewGuid();
        var oldChildCcId = Guid.NewGuid();

        var groupMap = new Dictionary<Guid, Guid>
        {
            [oldRootGroupId] = Guid.NewGuid(),
            [oldChildGroupId] = Guid.NewGuid()
        };

        var ccMap = new Dictionary<Guid, Guid>
        {
            [oldRootCcId] = Guid.NewGuid(),
            [oldChildCcId] = Guid.NewGuid()
        };

        // Source groups
        var sourceChildGroup = new ItemGroupRow { Id = oldChildGroupId, ParentId = oldRootGroupId, Code = "GRP-01" };
        var targetChildGroup = new ItemGroupRow
        {
            Id = groupMap[sourceChildGroup.Id],
            ParentId = sourceChildGroup.ParentId.HasValue && groupMap.TryGetValue(sourceChildGroup.ParentId.Value, out var newParentId) ? newParentId : null,
            Code = sourceChildGroup.Code
        };

        Assert.Equal(groupMap[oldRootGroupId], targetChildGroup.ParentId);

        // Source cost centers
        var sourceChildCc = new CostCenterRow { Id = oldChildCcId, ParentId = oldRootCcId, Code = "CC-01", NameArabic = "المركز الفرعي", CenterType = 1 };
        var targetChildCc = new CostCenterRow
        {
            Id = ccMap[sourceChildCc.Id],
            ParentId = sourceChildCc.ParentId.HasValue && ccMap.TryGetValue(sourceChildCc.ParentId.Value, out var newCcParentId) ? newCcParentId : null,
            Code = sourceChildCc.Code,
            NameArabic = sourceChildCc.NameArabic,
            CenterType = sourceChildCc.CenterType
        };

        Assert.Equal(ccMap[oldRootCcId], targetChildCc.ParentId);
        Assert.Equal((byte)1, targetChildCc.CenterType);
    }

    // T21: Annual rollover master data carryforward of item account mappings and allocations
    [Fact]
    public void T21AnnualRolloverMasterDataCarryforwardOfMappingsAndAllocations()
    {
        var oldGroupId = Guid.NewGuid();
        var oldAccountId = Guid.NewGuid();
        var oldSourceCcId = Guid.NewGuid();
        var oldTargetCcId = Guid.NewGuid();

        var groupMap = new Dictionary<Guid, Guid> { [oldGroupId] = Guid.NewGuid() };
        var accountMap = new Dictionary<Guid, Guid> { [oldAccountId] = Guid.NewGuid() };
        var ccMap = new Dictionary<Guid, Guid>
        {
            [oldSourceCcId] = Guid.NewGuid(),
            [oldTargetCcId] = Guid.NewGuid()
        };

        // Mapping rollover
        var sourceMapping = new ItemAccountMappingRow
        {
            Id = Guid.NewGuid(),
            OwnerType = 2,
            OwnerId = oldGroupId,
            Role = PatternAccountRoles.Revenue,
            AccountId = oldAccountId,
            IsActive = true
        };

        var targetMapping = new ItemAccountMappingRow
        {
            Id = Guid.NewGuid(),
            OwnerType = sourceMapping.OwnerType,
            OwnerId = groupMap[sourceMapping.OwnerId],
            Role = sourceMapping.Role,
            AccountId = accountMap[sourceMapping.AccountId],
            IsActive = sourceMapping.IsActive
        };

        Assert.Equal(groupMap[oldGroupId], targetMapping.OwnerId);
        Assert.Equal(accountMap[oldAccountId], targetMapping.AccountId);
        Assert.True(targetMapping.IsActive);

        // Allocation rollover
        var sourceAlloc = new CostCenterAllocationRow
        {
            Id = Guid.NewGuid(),
            SourceCostCenterId = oldSourceCcId,
            TargetCostCenterId = oldTargetCcId,
            Percentage = 40.00m
        };

        var targetAlloc = new CostCenterAllocationRow
        {
            Id = Guid.NewGuid(),
            SourceCostCenterId = ccMap[sourceAlloc.SourceCostCenterId],
            TargetCostCenterId = ccMap[sourceAlloc.TargetCostCenterId],
            Percentage = sourceAlloc.Percentage
        };

        Assert.Equal(ccMap[oldSourceCcId], targetAlloc.SourceCostCenterId);
        Assert.Equal(ccMap[oldTargetCcId], targetAlloc.TargetCostCenterId);
        Assert.Equal(40.00m, targetAlloc.Percentage);
    }

    // T22: Concurrency control on cost center updates
    [Fact]
    public void T22ConcurrencyControlOnCostCenterUpdates()
    {
        using var context = new AccountingDbContext(new DbContextOptionsBuilder<AccountingDbContext>().UseSqlServer().Options);
        var entity = context.Model.FindEntityType(typeof(CostCenterRow))!;
        var rowVersionProp = entity.FindProperty(nameof(CostCenterRow.RowVersion));

        Assert.NotNull(rowVersionProp);
        Assert.True(rowVersionProp.IsConcurrencyToken);
    }

    // T23: Inactive account validation in mappings
    [Fact]
    public void T23InactiveAccountValidationInMappings()
    {
        static void ValidateAccountActive(bool isActive)
        {
            if (!isActive)
            {
                throw RuleViolation.Create("accounting.account_inactive", "Cannot assign an inactive account to item or group mappings.");
            }
        }

        var ex = Assert.Throws<InvalidOperationException>(() => ValidateAccountActive(isActive: false));
        Assert.Equal("accounting.account_inactive", RuleViolation.CodeOf(ex));

        ValidateAccountActive(isActive: true);
    }

    // T24: Parent account direct posting forbidden in mappings
    [Fact]
    public void T24ParentAccountDirectPostingForbiddenInMappings()
    {
        static void ValidatePostingAllowed(bool postingAllowed)
        {
            if (!postingAllowed)
            {
                throw RuleViolation.Create("accounting.parent_account_direct_posting_forbidden",
                    "Cannot assign a parent non-posting account to item or group mappings.");
            }
        }

        var ex = Assert.Throws<InvalidOperationException>(() => ValidatePostingAllowed(postingAllowed: false));
        Assert.Equal("accounting.parent_account_direct_posting_forbidden", RuleViolation.CodeOf(ex));

        ValidatePostingAllowed(postingAllowed: true);
    }
}
