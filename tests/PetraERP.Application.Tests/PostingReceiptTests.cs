using PetraERP.Application.GeneralLedger.Posting;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Primitives;

namespace PetraERP.Application.Tests;

public sealed class PostingReceiptTests
{
    [Fact]
    public void KeyBindingRejectsChangedRequestsEvenBeforeAnyFinancialCompletionExists()
    {
        var fixture = new PostingFixture();
        var request = fixture.Request(fixture.Draft());
        var binding = PostingRequestBinding.Bind(request);
        Assert.True(binding.Matches(request));
        var changed = PostingRequest.Post(request.Scope, request.IdempotencyKey, request.JournalId,
            ExpectedJournalVersion.Create([0, 0, 0, 0, 0, 0, 0, 2]), request.Reason);
        Assert.False(binding.Matches(changed));
        Assert.Equal(request.Fingerprint, binding.Fingerprint);
        Assert.Equal(request.IdempotencyKey, binding.IdempotencyKey);
    }

    [Fact]
    public void SameRequestReturnsTheRecordedIdentityAndNumber()
    {
        var fixture = new PostingFixture();
        var draft = fixture.Draft();
        var request = fixture.Request(draft);
        var posted = fixture.Post(draft);
        var receipt = PostingReceipt.ForPost(request, posted);
        var retry = PostingRequest.Post(request.Scope, request.IdempotencyKey, request.JournalId,
            request.ExpectedVersion, request.Reason);
        Assert.Equal(PostingReplayDecision.ReturnRecordedResult, receipt.EvaluateReplay(retry));
        Assert.Equal(posted.Id, receipt.JournalId);
        Assert.Equal(posted.PostingNumber, receipt.PostingNumber);
        Assert.Null(receipt.OriginalJournalId);
    }

    [Fact]
    public void ReusingAKeyWithDifferentIdentityVersionOrReasonIsAConflict()
    {
        var fixture = new PostingFixture();
        var draft = fixture.Draft();
        var request = fixture.Request(draft);
        var receipt = PostingReceipt.ForPost(request, fixture.Post(draft));
        var altered = new[]
        {
            PostingRequest.Post(request.Scope, request.IdempotencyKey, Guid.NewGuid(), request.ExpectedVersion, request.Reason),
            PostingRequest.Post(request.Scope, request.IdempotencyKey, request.JournalId,
                ExpectedJournalVersion.Create([0, 0, 0, 0, 0, 0, 0, 2]), request.Reason),
            PostingRequest.Post(request.Scope, request.IdempotencyKey, request.JournalId, request.ExpectedVersion, AuditReason.Create("سبب مختلف")),
            PostingRequest.Post(PostingRequestScope.Create(request.Scope.CompanyId, request.Scope.BranchId, Guid.NewGuid()),
                request.IdempotencyKey, request.JournalId, request.ExpectedVersion, request.Reason),
        };
        Assert.All(altered, incoming => Assert.Equal(PostingReplayDecision.RejectDifferentRequest, receipt.EvaluateReplay(incoming)));
    }

    [Fact]
    public void ReceiptCannotBeUsedOutsideItsLookupNamespace()
    {
        var fixture = new PostingFixture();
        var draft = fixture.Draft();
        var request = fixture.Request(draft);
        var receipt = PostingReceipt.ForPost(request, fixture.Post(draft));
        var unrelated = new[]
        {
            PostingRequest.Post(request.Scope, Guid.NewGuid(), request.JournalId, request.ExpectedVersion, request.Reason),
            PostingRequest.Post(PostingRequestScope.Create(Guid.NewGuid(), request.Scope.BranchId, request.Scope.ActorUserId),
                request.IdempotencyKey, request.JournalId, request.ExpectedVersion, request.Reason),
            PostingRequest.Post(PostingRequestScope.Create(request.Scope.CompanyId, Guid.NewGuid(), request.Scope.ActorUserId),
                request.IdempotencyKey, request.JournalId, request.ExpectedVersion, request.Reason),
            PostingRequest.Reverse(request.Scope, request.IdempotencyKey, request.JournalId, request.ExpectedVersion,
                Guid.NewGuid(), request.ExpectedVersion, request.Reason),
        };
        Assert.All(unrelated, incoming => Assert.Throws<ArgumentException>(() => receipt.EvaluateReplay(incoming)));
    }

    [Fact]
    public void ReceiptRejectsUnpostedWrongJournalWrongScopeAndWrongAuditProvenance()
    {
        var fixture = new PostingFixture();
        var draft = fixture.Draft();
        var request = fixture.Request(draft);
        Assert.Throws<ArgumentException>(() => PostingReceipt.ForPost(request, draft));
        Assert.Throws<ArgumentException>(() => PostingReceipt.ForPost(request, fixture.Post(fixture.Draft())));
        var posted = fixture.Post(draft);
        var foreignScope = PostingRequestScope.Create(Guid.NewGuid(), fixture.Scope.BranchId, fixture.Scope.ActorUserId);
        Assert.Throws<ArgumentException>(() => PostingReceipt.ForPost(PostingRequest.Post(foreignScope, request.IdempotencyKey,
            request.JournalId, request.ExpectedVersion, request.Reason), posted));
        var otherActor = PostingRequestScope.Create(fixture.Scope.CompanyId, fixture.Scope.BranchId, Guid.NewGuid());
        Assert.Throws<ArgumentException>(() => PostingReceipt.ForPost(PostingRequest.Post(otherActor, request.IdempotencyKey,
            request.JournalId, request.ExpectedVersion, request.Reason), posted));
        Assert.Throws<ArgumentException>(() => PostingReceipt.ForPost(PostingRequest.Post(fixture.Scope, request.IdempotencyKey,
            request.JournalId, request.ExpectedVersion, AuditReason.Create("سبب آخر")), posted));
    }

    [Fact]
    public void ReversalReceiptRequiresTheExactOriginalAndReversalPair()
    {
        var fixture = new PostingFixture();
        var original = fixture.Post(fixture.Draft());
        var reversal = original.CreateReversalDraft(fixture.BranchScope, fixture.Date, "عكس معتمد", fixture.Action());
        var request = PostingRequest.Reverse(fixture.Scope, Guid.NewGuid(), reversal.Id, PostingFixture.Version,
            original.Id, PostingFixture.Version, fixture.Reason);
        var result = reversal.Validate(fixture.Validation, fixture.Action()).Approve(fixture.Action())
            .PostReversal(original, fixture.Validation, fixture.Number(2), fixture.Action());
        var receipt = PostingReceipt.ForReversal(request, result);
        Assert.Equal(PostingReplayDecision.ReturnRecordedResult, receipt.EvaluateReplay(request));
        Assert.Equal(original.Id, receipt.OriginalJournalId);
        Assert.Equal(reversal.Id, receipt.JournalId);
        Assert.Equal(fixture.Number(2), receipt.PostingNumber);
        var wrongOriginal = PostingRequest.Reverse(fixture.Scope, request.IdempotencyKey, reversal.Id, PostingFixture.Version,
            Guid.NewGuid(), PostingFixture.Version, fixture.Reason);
        Assert.Throws<ArgumentException>(() => PostingReceipt.ForReversal(wrongOriginal, result));
        Assert.Throws<ArgumentException>(() => PostingReceipt.ForPost(fixture.Request(result.ReversalEntry), result.ReversalEntry));
        Assert.Throws<ArgumentException>(() => PostingReceipt.ForReversal(fixture.Request(result.ReversalEntry), result));
        var changedVersion = PostingRequest.Reverse(fixture.Scope, request.IdempotencyKey, reversal.Id, PostingFixture.Version,
            original.Id, ExpectedJournalVersion.Create([0, 0, 0, 0, 0, 0, 0, 3]), fixture.Reason);
        Assert.Equal(PostingReplayDecision.RejectDifferentRequest, receipt.EvaluateReplay(changedVersion));
    }
}
