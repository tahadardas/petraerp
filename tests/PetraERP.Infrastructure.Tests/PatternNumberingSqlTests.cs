using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.CompanyOptions;
using PetraERP.Application.Patterns;
using PetraERP.Application.Security;
using PetraERP.Domain.Patterns;
using PetraERP.Domain.Primitives;
using PetraERP.Infrastructure.CompanyOptions;
using PetraERP.Infrastructure.Patterns;
using PetraERP.Infrastructure.Persistence.Registry;

namespace PetraERP.Infrastructure.Tests;

public sealed class PatternNumberingSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task FiftyConcurrentAllocationsProduceUniqueMonotonicNumbersWithoutGaps()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            Guid patternId;
            Guid fiscalYearId;

            using (var initContext = database.Context())
            {
                var service = new SqlPatternService(initContext);
                var scope = PatternScope.Create(scenario.CompanyA, scenario.BranchA1, scenario.Actor);

                fiscalYearId = await initContext.FiscalYears
                    .Where(y => y.CompanyId == scenario.CompanyA)
                    .Select(y => y.Id)
                    .FirstAsync(Cancellation).ConfigureAwait(true);

                var cmd = new CreateDocumentPatternCommand
                {
                    OperationKind = BusinessOperationKind.ManualJournal,
                    Code = "MJ-CONCUR",
                    NameArabic = "سند قيد متزامن",
                    ShortCode = "MJC",
                    BranchIds = [scenario.BranchA1],
                    InitialConfiguration = new PatternConfiguration
                    {
                        NumberingPolicy = new PatternNumberingPolicy
                        {
                            Prefix = "MJ-",
                            Suffix = "-2026",
                            MinimumDigits = 5,
                            InitialSequence = 1,
                            Scope = PatternNumberingScope.Company,
                        }
                    }
                };

                var created = await service.CreatePatternAsync(scope, cmd, Cancellation).ConfigureAwait(true);
                patternId = created.Id;

                await service.PublishRevisionAsync(scope, patternId, new PublishPatternRevisionCommand
                {
                    ExpectedVersion = created.Version,
                    RevisionId = created.ActiveRevision!.Id,
                    Reason = "Publish for concurrency test",
                }, Cancellation).ConfigureAwait(true);
            }

            // Execute 50 concurrent allocations across separate DbContext instances
            var tasks = Enumerable.Range(1, 50).Select(async i =>
            {
                using var threadContext = database.Context();
                var threadService = new SqlPatternService(threadContext);
                var threadScope = PatternScope.Create(scenario.CompanyA, scenario.BranchA1, scenario.Actor);

                return await threadService.GenerateNextNumberAsync(threadScope, new GetNextPatternNumberCommand
                {
                    PatternId = patternId,
                    FiscalYearId = fiscalYearId,
                    AccountingDate = new DateOnly(2026, 4, 1),
                    AllocationKey = Guid.NewGuid(),
                }, cancellationToken: Cancellation).ConfigureAwait(false);
            });

            var results = await Task.WhenAll(tasks).ConfigureAwait(true);

            // Assertions
            Assert.Equal(50, results.Length);

            var sequenceNumbers = results.Select(r => r.SequenceNumber).ToList();
            var formattedNumbers = results.Select(r => r.FormattedNumber).ToList();

            Assert.Equal(50, sequenceNumbers.Distinct().Count());
            Assert.Equal(50, formattedNumbers.Distinct().Count());

            // Monotonic progression without duplicates: sequences 1 to 50
            var sortedSequences = sequenceNumbers.OrderBy(s => s).ToList();
            for (var i = 0; i < 50; i++)
            {
                Assert.Equal(i + 1, sortedSequences[i]);
                Assert.Equal($"MJ-{(i + 1):D5}-2026", results.First(r => r.SequenceNumber == i + 1).FormattedNumber);
            }

            // Verify central reservations table has exactly 50 rows
            using var verifyContext = database.Context();
            var reservationCount = await verifyContext.Identity.Set<DocumentNumberReservationRow>()
                .CountAsync(r => r.CompanyId == scenario.CompanyA && r.PatternId == patternId, Cancellation).ConfigureAwait(true);
            Assert.Equal(50, reservationCount);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task NumberAllocationIsIdempotentAndDetectsConflictingKeys()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var service = new SqlPatternService(context);
            var scope = PatternScope.Create(scenario.CompanyA, scenario.BranchA1, scenario.Actor);

            var fiscalYearId = await context.FiscalYears
                .Where(y => y.CompanyId == scenario.CompanyA)
                .Select(y => y.Id)
                .FirstAsync(Cancellation).ConfigureAwait(true);

            var created = await service.CreatePatternAsync(scope, new CreateDocumentPatternCommand
            {
                OperationKind = BusinessOperationKind.ManualJournal,
                Code = "MJ-IDEMP",
                NameArabic = "سند قيد مطابقة",
                ShortCode = "MJID",
                BranchIds = [scenario.BranchA1],
                InitialConfiguration = new PatternConfiguration
                {
                    NumberingPolicy = new PatternNumberingPolicy
                    {
                        Prefix = "MJ-",
                        MinimumDigits = 4,
                        InitialSequence = 1,
                        Scope = PatternNumberingScope.FiscalYear,
                        AllowManualNumbering = true,
                    }
                }
            }, Cancellation).ConfigureAwait(true);

            // Allow manual numbering on company policy as well
            var policyService = new SqlCompanyOptionsService(context);
            await policyService.SaveAsync(scenario.CompanyA, fiscalYearId, scenario.Actor,
                new CompanyFinancialPolicy { AllowManualNumbering = true }, "", "Enable manual numbering", Cancellation).ConfigureAwait(true);

            // Grant actor the Patterns.ManualNumbering permission
            var roleId = await context.UserRoles.Where(ur => ur.UserId == scenario.Actor).Select(ur => ur.RoleId).FirstAsync(Cancellation).ConfigureAwait(true);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"IF NOT EXISTS (SELECT 1 FROM [security].[RolePermissions] WHERE [RoleId]={roleId} AND [PermissionCode]={PermissionCatalog.PatternsManualNumbering}) INSERT INTO [security].[RolePermissions]([RoleId],[PermissionCode]) VALUES({roleId},{PermissionCatalog.PatternsManualNumbering})",
                Cancellation).ConfigureAwait(true);

            await service.PublishRevisionAsync(scope, created.Id, new PublishPatternRevisionCommand
            {
                ExpectedVersion = created.Version,
                RevisionId = created.ActiveRevision!.Id,
                Reason = "Publish for idempotency test",
            }, Cancellation).ConfigureAwait(true);

            var allocationKey = Guid.NewGuid();

            // 1. Initial allocation
            var first = await service.GenerateNextNumberAsync(scope, new GetNextPatternNumberCommand
            {
                PatternId = created.Id,
                FiscalYearId = fiscalYearId,
                AccountingDate = new DateOnly(2026, 5, 1),
                AllocationKey = allocationKey,
            }, cancellationToken: Cancellation).ConfigureAwait(true);

            Assert.Equal(1, first.SequenceNumber);
            Assert.Equal("MJ-0001", first.FormattedNumber);

            // 2. Replay with identical key and identical content -> idempotent response without counter increment
            var second = await service.GenerateNextNumberAsync(scope, new GetNextPatternNumberCommand
            {
                PatternId = created.Id,
                FiscalYearId = fiscalYearId,
                AccountingDate = new DateOnly(2026, 5, 1),
                AllocationKey = allocationKey,
            }, cancellationToken: Cancellation).ConfigureAwait(true);

            Assert.Equal(first.SequenceNumber, second.SequenceNumber);
            Assert.Equal(first.FormattedNumber, second.FormattedNumber);

            // 3. Replay with identical key but conflicting content (different manual number) -> throws conflict
            var conflictEx = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                service.GenerateNextNumberAsync(scope, new GetNextPatternNumberCommand
                {
                    PatternId = created.Id,
                    FiscalYearId = fiscalYearId,
                    AccountingDate = new DateOnly(2026, 5, 1),
                    AllocationKey = allocationKey,
                    ManualNumber = "MJ-MANUAL-99",
                }, cancellationToken: Cancellation)).ConfigureAwait(true);

            Assert.Equal("patterns.numbering_key_conflict", conflictEx.Code);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task MonthlyResetPolicyResetsSequenceOnMonthBoundary()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var service = new SqlPatternService(context);
            var scope = PatternScope.Create(scenario.CompanyA, scenario.BranchA1, scenario.Actor);

            var fiscalYearId = await context.FiscalYears
                .Where(y => y.CompanyId == scenario.CompanyA)
                .Select(y => y.Id)
                .FirstAsync(Cancellation).ConfigureAwait(true);

            var created = await service.CreatePatternAsync(scope, new CreateDocumentPatternCommand
            {
                OperationKind = BusinessOperationKind.ManualJournal,
                Code = "MJ-MONTHLY",
                NameArabic = "سند قيد شهري",
                ShortCode = "MJM",
                BranchIds = [scenario.BranchA1],
                InitialConfiguration = new PatternConfiguration
                {
                    NumberingPolicy = new PatternNumberingPolicy
                    {
                        Prefix = "MJ-{yyyyMM}-",
                        MinimumDigits = 3,
                        InitialSequence = 1,
                        Scope = PatternNumberingScope.Company,
                        ResetPolicy = PatternNumberResetPolicy.Monthly,
                    }
                }
            }, Cancellation).ConfigureAwait(true);

            await service.PublishRevisionAsync(scope, created.Id, new PublishPatternRevisionCommand
            {
                ExpectedVersion = created.Version,
                RevisionId = created.ActiveRevision!.Id,
                Reason = "Publish for monthly reset test",
            }, Cancellation).ConfigureAwait(true);

            // Allocation in Month 1 (January)
            var jan1 = await service.GenerateNextNumberAsync(scope, new GetNextPatternNumberCommand
            {
                PatternId = created.Id,
                FiscalYearId = fiscalYearId,
                AccountingDate = new DateOnly(2026, 1, 10),
            }, cancellationToken: Cancellation).ConfigureAwait(true);

            var jan2 = await service.GenerateNextNumberAsync(scope, new GetNextPatternNumberCommand
            {
                PatternId = created.Id,
                FiscalYearId = fiscalYearId,
                AccountingDate = new DateOnly(2026, 1, 20),
            }, cancellationToken: Cancellation).ConfigureAwait(true);

            Assert.Equal("MJ-202601-001", jan1.FormattedNumber);
            Assert.Equal(1, jan1.SequenceNumber);
            Assert.Equal("MJ-202601-002", jan2.FormattedNumber);
            Assert.Equal(2, jan2.SequenceNumber);

            // Allocation in Month 2 (February) -> Sequence must reset to 1
            var feb1 = await service.GenerateNextNumberAsync(scope, new GetNextPatternNumberCommand
            {
                PatternId = created.Id,
                FiscalYearId = fiscalYearId,
                AccountingDate = new DateOnly(2026, 2, 5),
            }, cancellationToken: Cancellation).ConfigureAwait(true);

            Assert.Equal("MJ-202602-001", feb1.FormattedNumber);
            Assert.Equal(1, feb1.SequenceNumber);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ManualNumberingEnforcesPermissionsAndDetectsDuplicates()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var service = new SqlPatternService(context);
            var scope = PatternScope.Create(scenario.CompanyA, scenario.BranchA1, scenario.Actor);

            var fiscalYearId = await context.FiscalYears
                .Where(y => y.CompanyId == scenario.CompanyA)
                .Select(y => y.Id)
                .FirstAsync(Cancellation).ConfigureAwait(true);

            var created = await service.CreatePatternAsync(scope, new CreateDocumentPatternCommand
            {
                OperationKind = BusinessOperationKind.ManualJournal,
                Code = "MJ-MANUAL",
                NameArabic = "سند قيد ترقيم يدوي",
                ShortCode = "MJMN",
                BranchIds = [scenario.BranchA1],
                InitialConfiguration = new PatternConfiguration
                {
                    NumberingPolicy = new PatternNumberingPolicy
                    {
                        Prefix = "MJ-",
                        MinimumDigits = 4,
                        InitialSequence = 1,
                        Scope = PatternNumberingScope.FiscalYear,
                        AllowManualNumbering = true,
                    }
                }
            }, Cancellation).ConfigureAwait(true);

            await service.PublishRevisionAsync(scope, created.Id, new PublishPatternRevisionCommand
            {
                ExpectedVersion = created.Version,
                RevisionId = created.ActiveRevision!.Id,
                Reason = "Publish for manual numbering test",
            }, Cancellation).ConfigureAwait(true);

            // When company policy disallows manual numbering, request is denied
            var policyService = new SqlCompanyOptionsService(context);
            await policyService.SaveAsync(scenario.CompanyA, fiscalYearId, scenario.Actor,
                new CompanyFinancialPolicy { AllowManualNumbering = false }, "", "Disallow manual numbering", Cancellation).ConfigureAwait(true);

            var deniedEx = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                service.GenerateNextNumberAsync(scope, new GetNextPatternNumberCommand
                {
                    PatternId = created.Id,
                    FiscalYearId = fiscalYearId,
                    AccountingDate = new DateOnly(2026, 6, 1),
                    ManualNumber = "MJ-CUSTOM-01",
                }, cancellationToken: Cancellation)).ConfigureAwait(true);

            Assert.Equal("patterns.manual_number_denied", deniedEx.Code);

            // Now allow manual numbering on company policy and grant the permission
            var snapshot = await policyService.GetAsync(scenario.CompanyA, Cancellation).ConfigureAwait(true);
            await policyService.SaveAsync(scenario.CompanyA, fiscalYearId, scenario.Actor,
                new CompanyFinancialPolicy { AllowManualNumbering = true }, snapshot.Version, "Allow manual numbering", Cancellation).ConfigureAwait(true);

            var roleId = await context.UserRoles.Where(ur => ur.UserId == scenario.Actor).Select(ur => ur.RoleId).FirstAsync(Cancellation).ConfigureAwait(true);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"IF NOT EXISTS (SELECT 1 FROM [security].[RolePermissions] WHERE [RoleId]={roleId} AND [PermissionCode]={PermissionCatalog.PatternsManualNumbering}) INSERT INTO [security].[RolePermissions]([RoleId],[PermissionCode]) VALUES({roleId},{PermissionCatalog.PatternsManualNumbering})",
                Cancellation).ConfigureAwait(true);

            // Successful manual number reservation
            var manualResult = await service.GenerateNextNumberAsync(scope, new GetNextPatternNumberCommand
            {
                PatternId = created.Id,
                FiscalYearId = fiscalYearId,
                AccountingDate = new DateOnly(2026, 6, 1),
                ManualNumber = "MJ-CUSTOM-01",
            }, cancellationToken: Cancellation).ConfigureAwait(true);

            Assert.Equal("MJ-CUSTOM-01", manualResult.FormattedNumber);

            // Duplicate manual number is rejected
            var dupEx = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                service.GenerateNextNumberAsync(scope, new GetNextPatternNumberCommand
                {
                    PatternId = created.Id,
                    FiscalYearId = fiscalYearId,
                    AccountingDate = new DateOnly(2026, 6, 2),
                    ManualNumber = "MJ-CUSTOM-01",
                }, cancellationToken: Cancellation)).ConfigureAwait(true);

            Assert.Equal("patterns.number_conflict", dupEx.Code);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ReservationFollowedByFailedPostingMaintainsTraceableReservationAndPermitsGap()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var service = new SqlPatternService(context);
            var scope = PatternScope.Create(scenario.CompanyA, scenario.BranchA1, scenario.Actor);

            var fiscalYearId = await context.FiscalYears
                .Where(y => y.CompanyId == scenario.CompanyA)
                .Select(y => y.Id)
                .FirstAsync(Cancellation).ConfigureAwait(true);

            var created = await service.CreatePatternAsync(scope, new CreateDocumentPatternCommand
            {
                OperationKind = BusinessOperationKind.ManualJournal,
                Code = "MJ-GAP",
                NameArabic = "سند قيد فجوات الترقيم",
                ShortCode = "MJGP",
                BranchIds = [scenario.BranchA1],
                InitialConfiguration = new PatternConfiguration
                {
                    NumberingPolicy = new PatternNumberingPolicy
                    {
                        Prefix = "MJ-",
                        MinimumDigits = 5,
                        InitialSequence = 1,
                        Scope = PatternNumberingScope.FiscalYear,
                    }
                }
            }, Cancellation).ConfigureAwait(true);

            await service.PublishRevisionAsync(scope, created.Id, new PublishPatternRevisionCommand
            {
                ExpectedVersion = created.Version,
                RevisionId = created.ActiveRevision!.Id,
                Reason = "Publish for traceable gaps test",
            }, Cancellation).ConfigureAwait(true);

            var abortKey = Guid.NewGuid();

            // 1. Reserve number 1 for a transaction that will be aborted
            var reserved = await service.GenerateNextNumberAsync(scope, new GetNextPatternNumberCommand
            {
                PatternId = created.Id,
                FiscalYearId = fiscalYearId,
                AccountingDate = new DateOnly(2026, 7, 1),
                AllocationKey = abortKey,
            }, cancellationToken: Cancellation).ConfigureAwait(true);

            Assert.Equal(1, reserved.SequenceNumber);
            Assert.Equal("MJ-00001", reserved.FormattedNumber);

            // Simulate aborted annual posting: reservation remains centrally traceable
            var reservationRow = await context.Identity.Set<DocumentNumberReservationRow>()
                .SingleAsync(r => r.CompanyId == scenario.CompanyA && r.AllocationKey == abortKey, Cancellation).ConfigureAwait(true);
            Assert.Equal("MJ-00001", reservationRow.Number);
            Assert.Equal(1, reservationRow.Sequence);

            // 2. A subsequent allocation with a new key gets the NEXT sequence (demonstrating traceable gap if #1 is never posted)
            var nextKey = Guid.NewGuid();
            var nextAlloc = await service.GenerateNextNumberAsync(scope, new GetNextPatternNumberCommand
            {
                PatternId = created.Id,
                FiscalYearId = fiscalYearId,
                AccountingDate = new DateOnly(2026, 7, 1),
                AllocationKey = nextKey,
            }, cancellationToken: Cancellation).ConfigureAwait(true);

            Assert.Equal(2, nextAlloc.SequenceNumber);
            Assert.Equal("MJ-00002", nextAlloc.FormattedNumber);

            // 3. Retrying the aborted request with the same allocation key safely reuses the original reservation
            var retryAlloc = await service.GenerateNextNumberAsync(scope, new GetNextPatternNumberCommand
            {
                PatternId = created.Id,
                FiscalYearId = fiscalYearId,
                AccountingDate = new DateOnly(2026, 7, 1),
                AllocationKey = abortKey,
            }, cancellationToken: Cancellation).ConfigureAwait(true);

            Assert.Equal(1, retryAlloc.SequenceNumber);
            Assert.Equal("MJ-00001", retryAlloc.FormattedNumber);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task NumberPreviewReadsTheNextSequenceWithoutConsumingIt()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var service = new SqlPatternService(context);
            var scope = PatternScope.Create(scenario.CompanyA, scenario.BranchA1, scenario.Actor);

            var fiscalYearId = await context.FiscalYears
                .Where(y => y.CompanyId == scenario.CompanyA)
                .Select(y => y.Id)
                .FirstAsync(Cancellation).ConfigureAwait(true);

            var created = await service.CreatePatternAsync(scope, new CreateDocumentPatternCommand
            {
                OperationKind = BusinessOperationKind.ManualJournal,
                Code = "MJ-PREVIEW",
                NameArabic = "سند قيد المعاينة",
                ShortCode = "MJPV",
                BranchIds = [scenario.BranchA1],
                InitialConfiguration = new PatternConfiguration
                {
                    NumberingPolicy = new PatternNumberingPolicy
                    {
                        Prefix = "PV-",
                        MinimumDigits = 4,
                        InitialSequence = 1,
                        Scope = PatternNumberingScope.FiscalYear,
                    }
                }
            }, Cancellation).ConfigureAwait(true);

            await service.PublishRevisionAsync(scope, created.Id, new PublishPatternRevisionCommand
            {
                ExpectedVersion = created.Version,
                RevisionId = created.ActiveRevision!.Id,
                Reason = "Publish for preview test",
            }, Cancellation).ConfigureAwait(true);

            // The card sends its pattern, date and revision only — no allocation key, because it reserves nothing.
            var previewCommand = new GetNextPatternNumberCommand
            {
                PatternId = created.Id,
                FiscalYearId = fiscalYearId,
                AccountingDate = new DateOnly(2026, 5, 1),
                RevisionId = created.ActiveRevision!.Id,
            };

            for (var read = 0; read < 5; read++)
            {
                var preview = await service.PreviewNextNumberAsync(scope, previewCommand, Cancellation).ConfigureAwait(true);
                Assert.True(preview.IsAvailable, preview.UnavailableReason);
                Assert.Equal(1, preview.SequenceNumber);
                Assert.Equal("PV-0001", preview.FormattedNumber);
            }

            Assert.False(await context.Set<DocumentNumberCounterRow>().AnyAsync(r => r.PatternId == created.Id, Cancellation).ConfigureAwait(true));
            Assert.Equal(0, await context.Set<DocumentNumberReservationRow>().CountAsync(r => r.CompanyId == scenario.CompanyA, Cancellation).ConfigureAwait(true));

            // Saving the document allocates the number that was previewed — the estimate consumed nothing.
            var allocated = await service.GenerateNextNumberAsync(scope, new GetNextPatternNumberCommand
            {
                PatternId = created.Id,
                FiscalYearId = fiscalYearId,
                AccountingDate = new DateOnly(2026, 5, 1),
                AllocationKey = Guid.NewGuid(),
            }, cancellationToken: Cancellation).ConfigureAwait(true);

            Assert.Equal("PV-0001", allocated.FormattedNumber);

            var afterSave = await service.PreviewNextNumberAsync(scope, previewCommand, Cancellation).ConfigureAwait(true);
            Assert.Equal(2, afterSave.SequenceNumber);
            Assert.Equal("PV-0002", afterSave.FormattedNumber);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task NumberPreviewIsUnavailableInsteadOfThrowingWhenThePatternCannotNumber()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var service = new SqlPatternService(context);
            var scope = PatternScope.Create(scenario.CompanyA, scenario.BranchA1, scenario.Actor);

            var preview = await service.PreviewNextNumberAsync(scope, new GetNextPatternNumberCommand
            {
                PatternId = Guid.NewGuid(),
            }, Cancellation).ConfigureAwait(true);

            Assert.False(preview.IsAvailable);
            Assert.Equal("patterns.numbering_unavailable", preview.UnavailableReason);
        }
    }
}
