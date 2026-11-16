using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.Tests;

public sealed class JournalContextBoundaryTests
{
    [Fact]
    public void ReusingUnownedLineInputCreatesIndependentJournalLineIdentities()
    {
        var fixture = new JournalFixture();
        var input = fixture.Line(fixture.DebitAccount, 100m, 0m);
        var first = fixture.Draft([input, fixture.Line(fixture.CreditAccount, 0m, 100m)]);
        var second = fixture.Draft([input, fixture.Line(fixture.CreditAccount, 0m, 100m)]);
        Assert.NotEqual(first.Lines[0].Id, second.Lines[0].Id);
        Assert.Null(input.JournalId);
        Assert.Equal(first.Id, first.Lines[0].JournalId);
        Assert.Equal(second.Id, second.Lines[0].JournalId);
        Assert.Throws<ArgumentException>(() => fixture.Draft(first.Lines));
        Assert.Throws<ArgumentException>(() => second.Revise(fixture.Date, "نقل سطر", first.Lines, fixture.Action()));
        var revised = first.Revise(fixture.Date, "وصف جديد", first.Lines, fixture.Action());
        Assert.Equal(first.Lines[0].Id, revised.Lines[0].Id);
    }

    [Fact]
    public void DraftsRejectDuplicateNullAndForeignCompanyLines()
    {
        var fixture = new JournalFixture();
        var line = fixture.Line(fixture.DebitAccount, 100m, 0m);
        Assert.Throws<ArgumentException>(() => fixture.Draft([line, line]));
        Assert.Throws<ArgumentException>(() => fixture.Draft([null!]));
        var foreign = new JournalFixture();
        Assert.Throws<ArgumentException>(() => fixture.Draft([foreign.Line(foreign.DebitAccount, 100m, 0m)]));
        Assert.Throws<ArgumentException>(() => fixture.Draft().Revise(fixture.Date, "مسودة", [line, line], fixture.Action()));
    }

    [Fact]
    public void CompanyBranchAndFiscalDateAreAuthoritativeValidationBoundaries()
    {
        var fixture = new JournalFixture();
        var foreign = new JournalFixture();
        Assert.Throws<ArgumentException>(() => fixture.Draft().Validate(foreign.Validation(), fixture.Action()));
        var otherBranch = JournalValidationContext.Create(fixture.Company, FiscalCalendarTests.Scope(fixture.Company),
            fixture.Year, [fixture.DebitAccount, fixture.CreditAccount], []);
        Assert.Throws<ArgumentException>(() => fixture.Draft().Validate(otherBranch, fixture.Action()));
        var initial = fixture.Draft();
        var outside = initial.Revise(fixture.Year.Range.End.AddDays(1), "تاريخ خارج السنة", initial.Lines, fixture.Action());
        Assert.Throws<ArgumentOutOfRangeException>(() => outside.Validate(fixture.Validation(), fixture.Action()));
        Assert.Throws<ArgumentException>(() => JournalValidationContext.Create(fixture.Company, foreign.Scope,
            fixture.Year, [], []));
        Assert.Throws<ArgumentException>(() => JournalValidationContext.Create(fixture.Company, fixture.Scope,
            foreign.Year, [], []));
    }

    [Fact]
    public void ResolvedContextIsCopiedAndRejectsAmbiguousOrForeignReferences()
    {
        var fixture = new JournalFixture();
        var foreign = new JournalFixture();
        var accounts = new List<Account> { fixture.DebitAccount, fixture.CreditAccount };
        var context = fixture.Validation(accounts: accounts);
        accounts.Clear();
        Assert.Equal(JournalEntryState.Validated, fixture.Draft().Validate(context, fixture.Action()).State);
        Assert.Throws<ArgumentException>(() => fixture.Validation(accounts: [fixture.DebitAccount, fixture.DebitAccount]));
        Assert.Throws<ArgumentException>(() => fixture.Validation(accounts: [null!]));
        Assert.Throws<ArgumentException>(() => fixture.Validation(accounts: [foreign.DebitAccount]));
        var center = CostCenter.Create(fixture.Company, EntityCode.Create("CC"), BilingualName.Create("مركز"));
        var foreignCenter = CostCenter.Create(foreign.Company, EntityCode.Create("CC"), BilingualName.Create("مركز آخر"));
        Assert.Throws<ArgumentException>(() => fixture.Validation(centers: [center, center]));
        Assert.Throws<ArgumentException>(() => fixture.Validation(centers: [null!]));
        Assert.Throws<ArgumentException>(() => fixture.Validation(centers: [foreignCenter]));
        var centers = new List<CostCenter> { center };
        var copied = fixture.Validation(centers: centers);
        centers.Clear();
        var draft = fixture.Draft([fixture.Line(fixture.DebitAccount, 100m, 0m, centerId: center.Id), fixture.Line(fixture.CreditAccount, 0m, 100m)]);
        Assert.Equal(JournalEntryState.Validated, draft.Validate(copied, fixture.Action()).State);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void PostingSequenceMustBePositive(long sequence)
    {
        var fixture = new JournalFixture();
        Assert.Throws<ArgumentOutOfRangeException>(() => JournalPostingNumber.Create(fixture.Scope, fixture.Year, sequence));
    }

    [Fact]
    public void PostingNumberCannotBeBorrowedFromAnotherCompanyBranchOrYear()
    {
        var fixture = new JournalFixture();
        var foreign = new JournalFixture();
        var approved = fixture.AtState(JournalEntryState.Approved);
        var numbers = new[]
        {
            JournalPostingNumber.Create(foreign.Scope, foreign.Year, 1),
            JournalPostingNumber.Create(FiscalCalendarTests.Scope(fixture.Company), fixture.Year, 1),
            JournalPostingNumber.Create(fixture.Scope, FiscalCalendarTests.Year(fixture.Company), 1),
        };
        foreach (var number in numbers)
        {
            Assert.Throws<ArgumentException>(() => approved.Post(fixture.Validation(), number, fixture.Action()));
        }

        Assert.Throws<ArgumentException>(() => JournalPostingNumber.Create(fixture.Scope, foreign.Year, 1));
        Assert.Equal(long.MaxValue, JournalPostingNumber.Create(fixture.Scope, fixture.Year, long.MaxValue).Sequence);
    }

    [Fact]
    public void AuditAndDescriptionTextHaveExplicitLimitsAndValidUtcIdentity()
    {
        var fixture = new JournalFixture();
        var id = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => JournalActionContext.Create(Guid.Empty, id, "سبب", JournalFixture.Time));
        Assert.Throws<ArgumentException>(() => JournalActionContext.Create(id, Guid.Empty, "سبب", JournalFixture.Time));
        Assert.Throws<ArgumentException>(() => JournalActionContext.Create(id, id, "سبب", default));
        Assert.Throws<ArgumentException>(() => JournalActionContext.Create(id, id, "سبب", JournalFixture.Time.ToOffset(TimeSpan.FromHours(3))));
        Assert.Throws<ArgumentException>(() => fixture.Action(" "));
        Assert.Throws<ArgumentException>(() => fixture.Action(new string('س', 1001)));
        Assert.Equal("سبب\nتفصيلي", fixture.Action("سبب\nتفصيلي").Reason);
        Assert.Throws<ArgumentException>(() => fixture.Draft().Revise(fixture.Date, new string('ع', 1001), [], fixture.Action()));
        Assert.Throws<ArgumentException>(() => fixture.Draft().Revise(fixture.Date, new string((char)0xD800, 1), [], fixture.Action()));
        var revised = fixture.Draft().Revise(fixture.Date, new string('ع', 1000), [], fixture.Action());
        Assert.Equal(1000, revised.Description.Length);
    }
}
