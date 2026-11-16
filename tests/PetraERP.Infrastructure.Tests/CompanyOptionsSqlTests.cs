using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.CompanyOptions;
using PetraERP.Application.Security;
using PetraERP.Domain.Inventory;
using PetraERP.Infrastructure.CompanyOptions;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Persistence.Actors;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.Tests;

public sealed class CompanyOptionsSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task CompanyPoliciesPersistAcrossDisposedContexts()
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var result = await fixture.ProvisionAsync("OPTPERSIST").ConfigureAwait(true);
            Assert.True(result.Succeeded, result.Error);

            CompanyPolicySnapshot savedSnapshot;

            // 1. Save company options in first context
            using (var annual = await fixture.Factory.CreateDbContextAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                var service = new SqlCompanyOptionsService(annual);
                var initial = await service.GetAsync(result.CompanyId, Cancellation).ConfigureAwait(true);

                var policyToSave = new CompanyFinancialPolicy
                {
                    DocumentPostingMode = DocumentPostingMode.ApproveAndPost,
                    AllowSaveAsDraft = false,
                    NegativeIssuePolicy = NegativeIssuePolicy.RequestApproval,
                    RequireDocumentApproval = true,
                    RequireProfitLossCostCenter = true,
                    AllowCreditSales = true,
                    AllowCreditPurchases = false,
                    AllowManualNumbering = true,
                    AllowPatternNumberingOverrides = false,
                    DefaultNumberingScope = 2,
                    InitialNumberSequence = 500,
                };

                savedSnapshot = await service.SaveAsync(
                    result.CompanyId,
                    result.FiscalYearId,
                    fixture.Actor,
                    policyToSave,
                    initial.Version,
                    "Configuring operational policies",
                    Cancellation).ConfigureAwait(true);

                Assert.False(string.IsNullOrWhiteSpace(savedSnapshot.Version));
            }

            // 2. Open fresh context and verify full persistence across disposed instances
            using (var freshAnnual = await fixture.Factory.CreateDbContextAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                var freshService = new SqlCompanyOptionsService(freshAnnual);
                var loadedSnapshot = await freshService.GetAsync(result.CompanyId, Cancellation).ConfigureAwait(true);

                Assert.Equal(savedSnapshot.Version, loadedSnapshot.Version);
                Assert.Equal(DocumentPostingMode.ApproveAndPost, loadedSnapshot.Policy.DocumentPostingMode);
                Assert.False(loadedSnapshot.Policy.AllowSaveAsDraft);
                Assert.Equal(NegativeIssuePolicy.RequestApproval, loadedSnapshot.Policy.NegativeIssuePolicy);
                Assert.True(loadedSnapshot.Policy.RequireDocumentApproval);
                Assert.True(loadedSnapshot.Policy.RequireProfitLossCostCenter);
                Assert.True(loadedSnapshot.Policy.AllowCreditSales);
                Assert.False(loadedSnapshot.Policy.AllowCreditPurchases);
                Assert.True(loadedSnapshot.Policy.AllowManualNumbering);
                Assert.False(loadedSnapshot.Policy.AllowPatternNumberingOverrides);
                Assert.Equal(2, loadedSnapshot.Policy.DefaultNumberingScope);
                Assert.Equal(500, loadedSnapshot.Policy.InitialNumberSequence);
                Assert.False(loadedSnapshot.NegativePostingAvailable);
            }
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task NewCompanyDefaultsToManualPostingWithDraftAllowed()
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var result = await fixture.ProvisionAsync("OPTDEFAULT").ConfigureAwait(true);
            Assert.True(result.Succeeded, result.Error);

            using var annual = await fixture.Factory.CreateDbContextAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true);
            var snapshot = await new SqlCompanyOptionsService(annual).GetAsync(result.CompanyId, Cancellation).ConfigureAwait(true);

            Assert.Equal(DocumentPostingMode.ApproveOnly, snapshot.Policy.DocumentPostingMode);
            Assert.True(snapshot.Policy.AllowSaveAsDraft);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task CompanyPolicyEnforcesRowVersionOptimisticConcurrency()
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var result = await fixture.ProvisionAsync("OPTCONCUR").ConfigureAwait(true);
            Assert.True(result.Succeeded, result.Error);

            using var annual = await fixture.Factory.CreateDbContextAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true);
            var service = new SqlCompanyOptionsService(annual);

            // Read initial state
            var snapshot1 = await service.GetAsync(result.CompanyId, Cancellation).ConfigureAwait(true);

            // First update succeeds and produces a new version
            var snapshot2 = await service.SaveAsync(
                result.CompanyId,
                result.FiscalYearId,
                fixture.Actor,
                snapshot1.Policy with { AllowManualNumbering = true },
                snapshot1.Version,
                "First update",
                Cancellation).ConfigureAwait(true);

            Assert.NotEqual(snapshot1.Version, snapshot2.Version);

            // Concurrent caller attempts to save using stale initial version -> throws ConcurrencyConflictException
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
                service.SaveAsync(
                    result.CompanyId,
                    result.FiscalYearId,
                    fixture.Actor,
                    snapshot1.Policy with { AllowManualNumbering = false },
                    snapshot1.Version, // Stale!
                    "Conflicting update",
                    Cancellation)).ConfigureAwait(true);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task CompanyPolicyChangesProduceImmutableAuditRecords()
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var result = await fixture.ProvisionAsync("OPTAUDIT").ConfigureAwait(true);
            Assert.True(result.Succeeded, result.Error);

            using var annual = await fixture.Factory.CreateDbContextAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true);
            var service = new SqlCompanyOptionsService(annual);

            var initial = await service.GetAsync(result.CompanyId, Cancellation).ConfigureAwait(true);
            await service.SaveAsync(
                result.CompanyId,
                result.FiscalYearId,
                fixture.Actor,
                initial.Policy with { RequireDocumentApproval = true },
                initial.Version,
                "Audit policy changes",
                Cancellation).ConfigureAwait(true);

            // Verify audit record exists
            var auditRow = await annual.CompanyPolicyAudit
                .OrderByDescending(a => a.OccurredAtUtc)
                .FirstAsync(a => a.CompanyId == result.CompanyId, Cancellation).ConfigureAwait(true);

            Assert.Equal("CompanyPolicyChanged", auditRow.EventType);
            Assert.Equal("Audit policy changes", auditRow.Reason);
            Assert.Equal(fixture.Actor, auditRow.ActorUserId);
            Assert.Contains("\"RequireDocumentApproval\":true", auditRow.AfterJson);

            // Direct UPDATE on audit table is strictly prohibited by TR_CompanyPolicyAudit_Immutable
            var updateEx = await Assert.ThrowsAnyAsync<SqlException>(() =>
                annual.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE [audit].[CompanyPolicyAudit] SET [Reason] = N'Tampered' WHERE [Id] = {auditRow.Id}",
                    Cancellation)).ConfigureAwait(true);
            Assert.Contains("immutable", updateEx.Message, StringComparison.OrdinalIgnoreCase);

            // Direct DELETE on audit table is also prohibited
            var deleteEx = await Assert.ThrowsAnyAsync<SqlException>(() =>
                annual.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM [audit].[CompanyPolicyAudit] WHERE [Id] = {auditRow.Id}",
                    Cancellation)).ConfigureAwait(true);
            Assert.Contains("immutable", deleteEx.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task NegativeInventoryPoliciesEnforceStrictGuardsAndDisallowNegativePosting()
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var result = await fixture.ProvisionAsync("OPTINV").ConfigureAwait(true);
            Assert.True(result.Succeeded, result.Error);

            Guid branchId;
            Guid warehouseId;
            var unit = new UnitOfMeasureRow { Id = Guid.NewGuid(), CompanyId = result.CompanyId, Code = "EA", NameArabic = "حبة", Symbol = "ea" };
            var item = new ItemRow { Id = Guid.NewGuid(), CompanyId = result.CompanyId, Code = "NEGITEM", NameArabic = "مادة سالبة", NormalizedSearchText = "مادة سالبة", BaseUnitId = unit.Id };

            using (var setupContext = await fixture.Factory.CreateDbContextAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                branchId = await setupContext.Branches.Select(b => b.Id).SingleAsync(Cancellation).ConfigureAwait(true);
                warehouseId = await setupContext.Warehouses.Select(w => w.Id).SingleAsync(Cancellation).ConfigureAwait(true);
                item.DefaultWarehouseId = warehouseId;

                setupContext.BindProvisioningScope(new AccountingWriteScope(result.CompanyId, result.FiscalYearId, fixture.Actor, PermissionCatalog.DatabaseManage));
                var tx = await setupContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, Cancellation).ConfigureAwait(true);
                await using (tx.ConfigureAwait(true))
                {
                    setupContext.UnitsOfMeasure.Add(unit);
                    setupContext.Items.Add(item);
                    await setupContext.SaveChangesAsync(Cancellation).ConfigureAwait(true);
                    await tx.CommitAsync(Cancellation).ConfigureAwait(true);
                }
            }

            // 1. Under NegativeIssuePolicy.Block, negative issue request is rejected
            using (var blockContext = await fixture.Factory.CreateDbContextAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                var service = new SqlCompanyOptionsService(blockContext);
                var blockSnapshot = await service.GetAsync(result.CompanyId, Cancellation).ConfigureAwait(true);
                await service.SaveAsync(result.CompanyId, result.FiscalYearId, fixture.Actor,
                    blockSnapshot.Policy with { NegativeIssuePolicy = NegativeIssuePolicy.Block },
                    blockSnapshot.Version, "Block negative issue", Cancellation).ConfigureAwait(true);
            }

            var reqId = Guid.NewGuid();
            using (var blockReqContext = await fixture.Factory.CreateDbContextAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                var service = new SqlCompanyOptionsService(blockReqContext);
                var blockedEx = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                    service.RequestAsync(result.CompanyId, result.FiscalYearId, branchId, fixture.Actor,
                        item.Id, warehouseId, 5m, "Urgent shipment", reqId, Cancellation)).ConfigureAwait(true);
                Assert.Equal("inventory.negative_request_disabled", blockedEx.Code);
            }

            // 2. Change policy to NegativeIssuePolicy.RequestApproval
            using (var allowContext = await fixture.Factory.CreateDbContextAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                var service = new SqlCompanyOptionsService(allowContext);
                var allowSnapshot = await service.GetAsync(result.CompanyId, Cancellation).ConfigureAwait(true);
                await service.SaveAsync(result.CompanyId, result.FiscalYearId, fixture.Actor,
                    allowSnapshot.Policy with { NegativeIssuePolicy = NegativeIssuePolicy.RequestApproval },
                    allowSnapshot.Version, "Allow negative approval requests", Cancellation).ConfigureAwait(true);
            }

            // Request can now be submitted
            NegativeIssueRequest approvalRequest;
            using (var reqContext = await fixture.Factory.CreateDbContextAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                var service = new SqlCompanyOptionsService(reqContext);
                approvalRequest = await service.RequestAsync(result.CompanyId, result.FiscalYearId, branchId, fixture.Actor,
                    item.Id, warehouseId, 5m, "Urgent shipment", reqId, Cancellation).ConfigureAwait(true);
                Assert.Equal("Pending", approvalRequest.Status);
                Assert.False(approvalRequest.NegativePostingAvailable);
            }

            // Self-approval is rejected
            using (var selfReviewContext = await fixture.Factory.CreateDbContextAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                var service = new SqlCompanyOptionsService(selfReviewContext);
                var selfReviewEx = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                    service.ReviewAsync(result.CompanyId, result.FiscalYearId, branchId, fixture.Actor,
                        approvalRequest.Id, true, approvalRequest.Version, "Self approval", Cancellation)).ConfigureAwait(true);
                Assert.Equal("inventory.approval_review_denied", selfReviewEx.Code);
            }

            // Register a second authorized actor for reviewing
            var reviewerId = Guid.NewGuid();
            fixture.System.Users.Add(new UserRow
            {
                Id = reviewerId,
                UserName = "reviewer",
                NormalizedUserName = "REVIEWER",
                DisplayNameArabic = "مدير المراجعة",
                IsActive = true,
                PasswordAlgorithm = 1,
                PasswordIterations = 100_000,
                PasswordSalt = [1, 2, 3],
                PasswordHash = [4, 5, 6],
                PasswordChangedAtUtc = DateTimeOffset.UtcNow,
                MustChangePassword = false,
                SecurityStamp = Guid.NewGuid(),
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            });
            var adminRole = await fixture.System.Roles.SingleAsync(r => r.Code == SqlSecurityBootstrapper.AdministratorRoleCode, Cancellation).ConfigureAwait(true);
            fixture.System.UserRoles.Add(new UserRoleRow { UserId = reviewerId, RoleId = adminRole.Id });
            fixture.System.UserCompanyScopes.Add(new UserCompanyScopeRow { UserId = reviewerId, CompanyId = result.CompanyId });
            fixture.System.UserBranchScopes.Add(new UserBranchScopeRow { UserId = reviewerId, CompanyId = result.CompanyId, BranchId = branchId });
            await fixture.System.SaveChangesAsync(Cancellation).ConfigureAwait(true);

            // Review by the second user succeeds
            using (var reviewerContext = await fixture.Factory.CreateDbContextAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                var service = new SqlCompanyOptionsService(reviewerContext);
                var reviewed = await service.ReviewAsync(result.CompanyId, result.FiscalYearId, branchId, reviewerId,
                    approvalRequest.Id, true, approvalRequest.Version, "Approved for workflow", Cancellation).ConfigureAwait(true);
                Assert.Equal("ApprovedPolicyOnly", reviewed.Status);
                Assert.False(reviewed.NegativePostingAvailable);
            }

            // 3. Mandate verification: Even with ApprovedPolicyOnly, actual negative stock posting remains blocked
            using var freshContext = await fixture.Factory.CreateDbContextAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true);
            freshContext.BindWriteScope(new AccountingWriteScope(result.CompanyId, result.FiscalYearId, fixture.Actor, PermissionCatalog.InventoryAdjustmentsManage, branchId));

            freshContext.StockMovements.Add(new StockMovementRow
            {
                Id = Guid.NewGuid(),
                CompanyId = result.CompanyId,
                BranchId = branchId,
                WarehouseId = warehouseId,
                ItemId = item.Id,
                UnitId = unit.Id,
                MovementKind = (int)StockMovementKind.AdjustmentDecrease,
                Quantity = 5m,
                BaseQuantity = -5m,
                UnitCost = 10m,
                TotalCost = 50m,
                MovementDateUtc = DateTimeOffset.UtcNow,
                DocumentType = "NegativeTest",
                DocumentId = Guid.NewGuid(),
                CreatedAtUtc = DateTimeOffset.UtcNow,
                CreatedByUserId = fixture.Actor,
            });

            var stockEx = await Assert.ThrowsAsync<BusinessRuleException>(() => freshContext.SaveChangesAsync(Cancellation)).ConfigureAwait(true);
            Assert.Equal("inventory.negative_stock_blocked", stockEx.Code);
        }
    }
}
