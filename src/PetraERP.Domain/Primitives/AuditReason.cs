namespace PetraERP.Domain.Primitives;

public sealed record AuditReason
{
    public const int MaximumLength = 1000;

    private AuditReason(string value) => Value = value;

    public string Value { get; }

    public static AuditReason Create(string value) =>
        Create(value, nameof(value));

    internal static AuditReason Create(string value, string parameterName) =>
        new(DomainText.Required(value, MaximumLength, parameterName, multiline: true));
}
