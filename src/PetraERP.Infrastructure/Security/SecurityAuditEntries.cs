using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using PetraERP.Application.Security;

namespace PetraERP.Infrastructure.Security;

/// <summary>Builds append-only audit rows. Details are compact JSON that never contains credentials.</summary>
internal static class SecurityAuditEntries
{
    private const int MaximumDetailsLength = 4000;
    private const int MaximumReasonLength = 500;

    private static readonly JsonSerializerOptions DetailsJson = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        WriteIndented = false,
    };

    internal static SecurityAuditEventRow Create(
        DateTimeOffset occurredAtUtc,
        SecurityAuditEventType eventType,
        ClientContext client,
        Guid? userId = null,
        string? userName = null,
        Guid? actorUserId = null,
        Guid? sessionId = null,
        Guid? companyId = null,
        Guid? branchId = null,
        string? reason = null,
        object? details = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        return new SecurityAuditEventRow
        {
            Id = Guid.CreateVersion7(occurredAtUtc),
            OccurredAtUtc = occurredAtUtc,
            EventType = (byte)eventType,
            UserId = userId,
            UserNameSnapshot = Bound(userName, 64),
            ActorUserId = actorUserId,
            SessionId = sessionId,
            CompanyId = companyId,
            BranchId = branchId,
            IpAddress = client.IpAddress,
            UserAgent = client.UserAgent,
            CorrelationId = client.CorrelationId,
            Reason = Bound(reason, MaximumReasonLength),
            Details = SerializeDetails(details),
        };
    }

    private static string? SerializeDetails(object? details)
    {
        if (details is null)
        {
            return null;
        }

        var json = JsonSerializer.Serialize(details, DetailsJson);
        return json.Length <= MaximumDetailsLength
            ? json
            : JsonSerializer.Serialize(new { truncated = true, originalLength = json.Length }, DetailsJson);
    }

    private static string? Bound(string? value, int length)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= length ? trimmed : trimmed[..length];
    }
}
