using PetraERP.Application.GeneralLedger.Posting;
using PetraERP.Domain.Primitives;

namespace PetraERP.Application.Tests;

public sealed class PostingRequestTests
{
    [Fact]
    public void RetryTransportIdentityDoesNotChangeTheBusinessFingerprint()
    {
        var scope = PostingRequestScope.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var journalId = Guid.NewGuid();
        var version = ExpectedJournalVersion.Create([1, 2, 3, 4, 5, 6, 7, 8]);
        var first = PostingRequest.Post(scope, Guid.NewGuid(), journalId, version, AuditReason.Create("ترحيل قيد"));
        var second = PostingRequest.Post(scope, Guid.NewGuid(), journalId, version, AuditReason.Create("ترحيل قيد"));
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.NotEqual(first.IdempotencyKey, second.IdempotencyKey);
    }

    [Fact]
    public void CanonicalArabicFingerprintMatchesAnIndependentPythonSha256Vector()
    {
        var scope = PostingRequestScope.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"), Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var request = PostingRequest.Post(scope, Guid.NewGuid(), Guid.Parse("44444444-4444-4444-4444-444444444444"),
            ExpectedJournalVersion.Create([1, 2, 3, 4, 5, 6, 7, 8]), AuditReason.Create("ترحيل قيد"));
        Assert.Equal("84AA5712BB04ACA59540712977A2B14F4B5176F3D9ECDCEEC10F2F80752B30C2", request.Fingerprint.Hex);
        Assert.Equal(request.Fingerprint, PostingFingerprint.FromHex(request.Fingerprint.Hex.ToLowerInvariant()));
    }

    [Fact]
    public void AllBusinessIdentityAndContentChangesChangeTheFingerprint()
    {
        var company = Guid.NewGuid();
        var branch = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var scope = PostingRequestScope.Create(company, branch, actor);
        var key = Guid.NewGuid();
        var journal = Guid.NewGuid();
        var version = ExpectedJournalVersion.Create([1, 2, 3, 4, 5, 6, 7, 8]);
        var next = ExpectedJournalVersion.Create([1, 2, 3, 4, 5, 6, 7, 9]);
        var reason = AuditReason.Create("ترحيل قيد");
        var baseline = PostingRequest.Post(scope, key, journal, version, reason);
        var variants = new[]
        {
            PostingRequest.Post(PostingRequestScope.Create(Guid.NewGuid(), branch, actor), key, journal, version, reason),
            PostingRequest.Post(PostingRequestScope.Create(company, Guid.NewGuid(), actor), key, journal, version, reason),
            PostingRequest.Post(PostingRequestScope.Create(company, branch, Guid.NewGuid()), key, journal, version, reason),
            PostingRequest.Post(scope, key, Guid.NewGuid(), version, reason),
            PostingRequest.Post(scope, key, journal, next, reason),
            PostingRequest.Post(scope, key, journal, version, AuditReason.Create("ترحيل قيد آخر")),
            PostingRequest.Reverse(scope, key, journal, version, Guid.NewGuid(), version, reason),
        };
        Assert.All(variants, variant => Assert.NotEqual(baseline.Fingerprint, variant.Fingerprint));
        Assert.Equal(variants.Length, variants.Select(static request => request.Fingerprint).Distinct().Count());
        Assert.Equal(baseline.Fingerprint, PostingRequest.Post(scope, key, journal, version, AuditReason.Create("  ترحيل قيد  ")).Fingerprint);
    }

    [Fact]
    public void ReversalFingerprintBindsBothOriginalIdentityAndOriginalVersion()
    {
        var scope = PostingRequestScope.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var key = Guid.NewGuid();
        var journal = Guid.NewGuid();
        var original = Guid.NewGuid();
        var version = ExpectedJournalVersion.Create([1, 2, 3, 4, 5, 6, 7, 8]);
        var next = ExpectedJournalVersion.Create([1, 2, 3, 4, 5, 6, 7, 9]);
        var reason = AuditReason.Create("عكس للتصحيح");
        var baseline = PostingRequest.Reverse(scope, key, journal, version, original, version, reason);
        Assert.NotEqual(baseline.Fingerprint, PostingRequest.Reverse(scope, key, journal, version, Guid.NewGuid(), version, reason).Fingerprint);
        Assert.NotEqual(baseline.Fingerprint, PostingRequest.Reverse(scope, key, journal, version, original, next, reason).Fingerprint);
        Assert.Throws<ArgumentException>(() => PostingRequest.Reverse(scope, key, journal, version, journal, version, reason));
        Assert.Throws<ArgumentException>(() => PostingRequest.Reverse(scope, key, journal, version, Guid.Empty, version, reason));
        Assert.Throws<ArgumentNullException>(() => PostingRequest.Reverse(scope, key, journal, version, original, null!, reason));
    }

    [Fact]
    public void MissingScopeIdentityKeysAndVersionsFailBeforeFingerprinting()
    {
        var id = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => PostingRequestScope.Create(Guid.Empty, id, id));
        Assert.Throws<ArgumentException>(() => PostingRequestScope.Create(id, Guid.Empty, id));
        Assert.Throws<ArgumentException>(() => PostingRequestScope.Create(id, id, Guid.Empty));
        var scope = PostingRequestScope.Create(id, id, id);
        var version = ExpectedJournalVersion.Create(new byte[8]);
        var reason = AuditReason.Create("سبب");
        Assert.Throws<ArgumentException>(() => PostingRequest.Post(scope, Guid.Empty, id, version, reason));
        Assert.Throws<ArgumentException>(() => PostingRequest.Post(scope, id, Guid.Empty, version, reason));
        Assert.Throws<ArgumentNullException>(() => PostingRequest.Post(scope, id, id, null!, reason));
        Assert.Throws<ArgumentNullException>(() => PostingRequest.Post(scope, id, id, version, null!));
        Assert.Throws<ArgumentNullException>(() => PostingRequest.Post(null!, id, id, version, reason));
    }

    [Fact]
    public void ReasonPreservesArabicAndRejectsMalformedUnicodeAndExcessLength()
    {
        Assert.Equal("المَزّة\nمراجعة", AuditReason.Create("  المَزّة\nمراجعة  ").Value);
        Assert.Throws<ArgumentException>(() => AuditReason.Create(new string((char)0xD800, 1)));
        Assert.Throws<ArgumentException>(() => AuditReason.Create(new string('س', 1001)));
        Assert.Throws<ArgumentException>(() => AuditReason.Create(" "));
        Assert.Throws<ArgumentException>(() => AuditReason.Create("سبب\0تالف"));
    }
}
