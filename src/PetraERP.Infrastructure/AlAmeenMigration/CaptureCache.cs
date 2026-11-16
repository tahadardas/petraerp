using System.Collections.Concurrent;
using PetraERP.Application.AlAmeenMigration;
using PetraERP.Application.AlAmeenMigration.Mapping;
using PetraERP.Application.Common;

namespace PetraERP.Infrastructure.AlAmeenMigration;

/// <summary>
/// Holds the frozen capture between the steps of one migration run. The state machine, the evidence and the identity
/// bindings are durable in <c>system.MigrationRuns</c>; this cache only avoids re-reading the source between preview,
/// execute and reconcile. The source connection string is deliberately not kept: a restart makes the operator run a
/// fresh preview, which is the only safe way to re-establish the frozen capture.
/// </summary>
internal static class CaptureCache
{
    private const int MaximumCaptures = 8;
    private static readonly ConcurrentDictionary<Guid, Capture> Captures = new();

    internal sealed record Capture(MigrationPreviewRequest Request, SourceSnapshot Snapshot, IdentityResolution Identities, Guid OwnerUserId, DateTimeOffset CapturedAtUtc);

    public static void Remember(Guid runId, MigrationPreviewRequest request, SourceSnapshot snapshot, IdentityResolution identities, Guid ownerUserId)
    {
        Prune();
        if (Captures.Count >= MaximumCaptures)
            Captures.TryRemove(Captures.OrderBy(entry => entry.Value.CapturedAtUtc).First().Key, out _);

        // Only the non-secret part of the request survives; the connection string is used to capture and then dropped.
        Captures[runId] = new Capture(request with { SourceConnectionString = string.Empty }, snapshot, identities, ownerUserId, snapshot.CapturedAtUtc);
    }

    public static Capture Require(Guid runId, Guid ownerUserId)
    {
        Prune();
        if (!Captures.TryGetValue(runId, out var capture))
            throw new BusinessRuleException(
                "migration.capture_lost",
                "لم تعد اللقطة المجمدة متاحة في هذه العملية؛ أعد المعاينة ثم نفّذ الترحيل. الحالة والسجل المحفوظان لم يتغيرا.");
        if (capture.OwnerUserId != ownerUserId)
            throw new BusinessRuleException("migration.run_forbidden", "جلسة الترحيل تخص مستخدماً آخر.");
        return capture;
    }

    private static void Prune()
    {
        foreach (var entry in Captures.Where(e => (DateTimeOffset.UtcNow - e.Value.CapturedAtUtc) > TimeSpan.FromMinutes(30)).ToList())
            Captures.TryRemove(entry.Key, out _);
    }
}
