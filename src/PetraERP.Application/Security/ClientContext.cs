namespace PetraERP.Application.Security;

/// <summary>
/// Request-origin facts recorded with security events. Values are bounded and never contain
/// credentials. They are diagnostic evidence, not authorization input.
/// </summary>
public sealed record ClientContext
{
    public const int MaximumIpAddressLength = 45;
    public const int MaximumUserAgentLength = 256;
    public const int MaximumCorrelationIdLength = 64;

    private ClientContext(string? ipAddress, string? userAgent, string? correlationId)
    {
        IpAddress = ipAddress;
        UserAgent = userAgent;
        CorrelationId = correlationId;
    }

    public string? IpAddress { get; }
    public string? UserAgent { get; }
    public string? CorrelationId { get; }

    public static ClientContext Unknown { get; } = new(null, null, null);

    public static ClientContext Create(string? ipAddress, string? userAgent, string? correlationId) =>
        new(Bound(ipAddress, MaximumIpAddressLength), Bound(userAgent, MaximumUserAgentLength),
            Bound(correlationId, MaximumCorrelationIdLength));

    private static string? Bound(string? value, int length)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        var builder = new System.Text.StringBuilder(Math.Min(trimmed.Length, length));
        foreach (var character in trimmed)
        {
            if (builder.Length == length)
            {
                break;
            }

            builder.Append(char.IsControl(character) || char.IsSurrogate(character) ? '?' : character);
        }

        return builder.ToString();
    }
}
