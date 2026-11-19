using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Patterns;
using PetraERP.Domain.Patterns;
using PetraERP.Domain.Primitives;
using PetraERP.Infrastructure.Patterns;

namespace PetraERP.Infrastructure.Tests;

public sealed class PatternEngineSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task PatternEngineCreatesDraftRevisionAndEnforcesCodeUniqueness()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var service = new SqlPatternService(context);
            var scope = PatternScope.Create(scenario.CompanyA, scenario.BranchA1, scenario.Actor);

            var cmd = new CreateDocumentPatternCommand
            {
                OperationKind = BusinessOperationKind.ManualJournal,
                Code = "MJ-STD",
                NameArabic = "سند قيد قياسي",
                NameEnglish = "Standard Manual Journal",
                ShortCode = "MJ",
                Icon = "journal",
                Group = "General",
                IsDefault = true,
                BranchIds = [scenario.BranchA1],
                InitialConfiguration = new PatternConfiguration
                {
                    NumberingPolicy = new PatternNumberingPolicy
                    {
                        Prefix = "MJ-",
                        MinimumDigits = 5,
                        Scope = PatternNumberingScope.BranchAndFiscalYear,
                    }
                }
            };

            var created = await service.CreatePatternAsync(scope, cmd, Cancellation).ConfigureAwait(true);
            Assert.Equal("MJ-STD", created.Code);
            Assert.Equal("سند قيد قياسي", created.NameArabic);
            Assert.True(created.IsActive);
            Assert.True(created.IsDefault);
            Assert.True(created.IsRunnable);
            Assert.Single(created.BranchIds);
            Assert.Equal(scenario.BranchA1, created.BranchIds[0]);

            // Attempting to create duplicate pattern with same company, operation kind, and code throws ResourceConflictException
            await Assert.ThrowsAsync<ResourceConflictException>(() =>
                service.CreatePatternAsync(scope, cmd, Cancellation)).ConfigureAwait(true);

            // Same code under Company B is allowed (company isolation)
            var scopeB = PatternScope.Create(scenario.CompanyB, scenario.BranchB1, scenario.Actor);
            var createdB = await service.CreatePatternAsync(scopeB, cmd with { BranchIds = [scenario.BranchB1] }, Cancellation).ConfigureAwait(true);
            Assert.Equal("MJ-STD", createdB.Code);
            Assert.Equal(scenario.CompanyB, createdB.CompanyId);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task NonRunnableOperationRejectsPublication()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var service = new SqlPatternService(context);
            var scope = PatternScope.Create(scenario.CompanyA, scenario.BranchA1, scenario.Actor);

            var cmd = new CreateDocumentPatternCommand
            {
                OperationKind = BusinessOperationKind.InventoryIssue,
                Code = "II-DISABLED",
                NameArabic = "صرف داخلي غير متاح",
                ShortCode = "II",
            };

            var created = await service.CreatePatternAsync(scope, cmd, Cancellation).ConfigureAwait(true);
            var patternId = created.Id;
            var revId = created.ActiveRevision!.Id;

            var publishCmd = new PublishPatternRevisionCommand
            {
                ExpectedVersion = created.Version,
                RevisionId = revId,
                Reason = "Attempt to publish non-runnable",
            };

            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                service.PublishRevisionAsync(scope, patternId, publishCmd, Cancellation)).ConfigureAwait(true);
            Assert.Equal(RuleViolation.PatternOperationNotRunnable, ex.Code);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task RunnableOperationPublishesAndRetiresRevisionsWithImmutability()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var service = new SqlPatternService(context);
            var scope = PatternScope.Create(scenario.CompanyA, scenario.BranchA1, scenario.Actor);

            var cmd = new CreateDocumentPatternCommand
            {
                OperationKind = BusinessOperationKind.ManualJournal,
                Code = "MJ-ADJ",
                NameArabic = "قيد تسوية",
                ShortCode = "ADJ",
            };

            var created = await service.CreatePatternAsync(scope, cmd, Cancellation).ConfigureAwait(true);
            var rev1Id = created.ActiveRevision!.Id;

            // Publish revision 1
            var pub1 = await service.PublishRevisionAsync(scope, created.Id, new PublishPatternRevisionCommand
            {
                ExpectedVersion = created.Version,
                RevisionId = rev1Id,
                Reason = "Publish initial revision",
            }, Cancellation).ConfigureAwait(true);

            Assert.NotNull(pub1.ActiveRevision);
            Assert.Equal(PatternRevisionState.Published, pub1.ActiveRevision.State);
            Assert.NotNull(pub1.ActiveRevision.EffectiveFromUtc);
            Assert.Null(pub1.ActiveRevision.EffectiveToUtc);

            // Create revision 2 (draft)
            var rev2 = await service.CreateRevisionAsync(scope, created.Id, new CreatePatternRevisionCommand
            {
                ExpectedVersion = pub1.Version,
                Configuration = new PatternConfiguration
                {
                    NumberingPolicy = new PatternNumberingPolicy { Prefix = "ADJ-2-", MinimumDigits = 6 }
                },
                Reason = "Revision 2 draft",
            }, Cancellation).ConfigureAwait(true);

            Assert.Equal(2, rev2.RevisionNumber);
            Assert.Equal(PatternRevisionState.Draft, rev2.State);

            // Re-fetch pattern to get latest concurrency token
            var fresh = await service.GetPatternAsync(scope, created.Id, Cancellation).ConfigureAwait(true);

            // Publish revision 2
            var pub2 = await service.PublishRevisionAsync(scope, created.Id, new PublishPatternRevisionCommand
            {
                ExpectedVersion = fresh.Version,
                RevisionId = rev2.Id,
                Reason = "Publish revision 2",
            }, Cancellation).ConfigureAwait(true);

            Assert.NotNull(pub2.ActiveRevision);
            Assert.Equal(2, pub2.ActiveRevision.RevisionNumber);
            Assert.Equal(PatternRevisionState.Published, pub2.ActiveRevision.State);

            // Verify rev 1 was retired in database
            var rev1Row = await context.PatternRevisions.FirstAsync(r => r.Id == rev1Id, Cancellation).ConfigureAwait(true);
            Assert.Equal((int)PatternRevisionState.Retired, rev1Row.State);
            Assert.NotNull(rev1Row.EffectiveToUtc);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task AtomicSequenceAllocationIsThreadSafeAndRespectsPolicy()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var service = new SqlPatternService(context);
            var scope = PatternScope.Create(scenario.CompanyA, scenario.BranchA1, scenario.Actor);

            var cmd = new CreateDocumentPatternCommand
            {
                OperationKind = BusinessOperationKind.ManualJournal,
                Code = "MJ-SEQ",
                NameArabic = "قيد ترقيم ذري",
                ShortCode = "SQ",
                InitialConfiguration = new PatternConfiguration
                {
                    NumberingPolicy = new PatternNumberingPolicy
                    {
                        Prefix = "JRN/",
                        Suffix = "/26",
                        MinimumDigits = 5,
                        Scope = PatternNumberingScope.BranchAndFiscalYear,
                    }
                }
            };

            var created = await service.CreatePatternAsync(scope, cmd, Cancellation).ConfigureAwait(true);

            // Publish revision 1
            await service.PublishRevisionAsync(scope, created.Id, new PublishPatternRevisionCommand
            {
                ExpectedVersion = created.Version,
                RevisionId = created.ActiveRevision!.Id,
                Reason = "Ready for numbering",
            }, Cancellation).ConfigureAwait(true);

            var yearRow = await context.FiscalYears.FirstAsync(y => y.CompanyId == scenario.CompanyA, Cancellation).ConfigureAwait(true);

            var num1 = await service.GenerateNextNumberAsync(scope, new GetNextPatternNumberCommand
            {
                PatternId = created.Id,
                FiscalYearId = yearRow.Id,
            }, Cancellation).ConfigureAwait(true);

            var num2 = await service.GenerateNextNumberAsync(scope, new GetNextPatternNumberCommand
            {
                PatternId = created.Id,
                FiscalYearId = yearRow.Id,
            }, Cancellation).ConfigureAwait(true);

            Assert.Equal(1, num1.SequenceNumber);
            Assert.Equal("JRN/00001/26", num1.FormattedNumber);
            Assert.Equal(2, num2.SequenceNumber);
            Assert.Equal("JRN/00002/26", num2.FormattedNumber);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task VoucherBlockIsStoredAtSchemaTwoAndInvalidBlocksAreRefusedWithStableCodes()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var service = new SqlPatternService(context);
            var scope = PatternScope.Create(scenario.CompanyA, scenario.BranchA1, scenario.Actor);

            CreateDocumentPatternCommand Command(string code, PatternConfiguration configuration, BusinessOperationKind kind = BusinessOperationKind.ManualJournal) => new()
            {
                OperationKind = kind, Code = code, NameArabic = "نمط " + code, ShortCode = code[..2], BranchIds = [scenario.BranchA1],
                InitialConfiguration = configuration,
            };

            // A schema-1 configuration without the block is stored upgraded, keeps its values and runs with the defaults.
            var legacy = await service.CreatePatternAsync(scope, Command("VL-1", new PatternConfiguration
            {
                SchemaVersion = 1, NumberingPolicy = new PatternNumberingPolicy { Prefix = "VL-", MinimumDigits = 5 },
            }), Cancellation).ConfigureAwait(true);
            var legacyConfig = legacy.ActiveRevision!.Configuration;
            Assert.Equal(PatternConfiguration.CurrentSchemaVersion, legacyConfig.SchemaVersion);
            Assert.Equal("VL-", legacyConfig.NumberingPolicy.Prefix);
            Assert.Null(legacyConfig.Voucher);
            Assert.Equal(VoucherFormKind.GenericVoucher, VoucherPatternRules.Effective(legacyConfig).FormKind);

            // The block round-trips through the database.
            var withBlock = await service.CreatePatternAsync(scope, Command("VB-1", new PatternConfiguration
            {
                Voucher = new PatternVoucherPolicy { FormKind = VoucherFormKind.GenericVoucher, CaptionMode = VoucherCaptionMode.FromTo },
            }), Cancellation).ConfigureAwait(true);
            var read = await service.GetPatternAsync(scope, withBlock.Id, Cancellation).ConfigureAwait(true);
            var stored = Assert.Single(read.Revisions).Configuration; // a new pattern holds one draft revision
            Assert.Equal(VoucherCaptionMode.FromTo, stored.Voucher!.CaptionMode);
            Assert.Equal(PatternConfiguration.CurrentSchemaVersion, stored.SchemaVersion);

            async Task AssertRefusedAsync(string code, string expected, PatternConfiguration configuration, BusinessOperationKind kind = BusinessOperationKind.ManualJournal)
            {
                var exception = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreatePatternAsync(scope, Command(code, configuration, kind), Cancellation)).ConfigureAwait(true);
                Assert.Equal(expected, exception.Code);
            }

            await AssertRefusedAsync("VM-1", VoucherPatternRules.FormUnsupported,
                new() { Voucher = new PatternVoucherPolicy { FormKind = VoucherFormKind.MultiMethodReceipt } }).ConfigureAwait(true);
            await AssertRefusedAsync("VM-2", VoucherPatternRules.OptionNotEnforced,
                new() { Voucher = new PatternVoucherPolicy { AutoPost = true } }).ConfigureAwait(true);
            await AssertRefusedAsync("VM-3", VoucherPatternRules.BlockNotAllowed,
                new() { Voucher = new PatternVoucherPolicy() }, BusinessOperationKind.SalesInvoice).ConfigureAwait(true);
            await AssertRefusedAsync("VM-4", VoucherPatternRules.SchemaUnsupported, new() { SchemaVersion = 3 }).ConfigureAwait(true);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task BranchGrantFiltersRunnablePatterns()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var service = new SqlPatternService(context);
            var scopeA1 = PatternScope.Create(scenario.CompanyA, scenario.BranchA1, scenario.Actor);
            var scopeA2 = PatternScope.Create(scenario.CompanyA, scenario.BranchA2, scenario.Actor);

            // Pattern 1: Granted only to Branch A1
            var p1 = await service.CreatePatternAsync(scopeA1, new CreateDocumentPatternCommand
            {
                OperationKind = BusinessOperationKind.ManualJournal,
                Code = "MJ-A1-ONLY",
                NameArabic = "خاص بفرع دمشق",
                ShortCode = "D1",
                BranchIds = [scenario.BranchA1],
            }, Cancellation).ConfigureAwait(true);

            await service.PublishRevisionAsync(scopeA1, p1.Id, new PublishPatternRevisionCommand
            {
                ExpectedVersion = p1.Version,
                RevisionId = p1.ActiveRevision!.Id,
                Reason = "Publish A1 only",
            }, Cancellation).ConfigureAwait(true);

            // Pattern 2: Global (no branch grants => all branches)
            var p2 = await service.CreatePatternAsync(scopeA1, new CreateDocumentPatternCommand
            {
                OperationKind = BusinessOperationKind.ManualJournal,
                Code = "MJ-GLOBAL",
                NameArabic = "عام لكافة الفروع",
                ShortCode = "GL",
                BranchIds = [],
            }, Cancellation).ConfigureAwait(true);

            await service.PublishRevisionAsync(scopeA1, p2.Id, new PublishPatternRevisionCommand
            {
                ExpectedVersion = p2.Version,
                RevisionId = p2.ActiveRevision!.Id,
                Reason = "Publish global",
            }, Cancellation).ConfigureAwait(true);

            var a1List = await service.GetRunnablePatternsForBranchAsync(scopeA1, Cancellation).ConfigureAwait(true);
            var a2List = await service.GetRunnablePatternsForBranchAsync(scopeA2, Cancellation).ConfigureAwait(true);

            Assert.Contains(a1List, p => p.Code == "MJ-A1-ONLY");
            Assert.Contains(a1List, p => p.Code == "MJ-GLOBAL");

            Assert.DoesNotContain(a2List, p => p.Code == "MJ-A1-ONLY");
            Assert.Contains(a2List, p => p.Code == "MJ-GLOBAL");
        }
    }
}
