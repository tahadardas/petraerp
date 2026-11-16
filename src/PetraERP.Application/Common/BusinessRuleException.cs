namespace PetraERP.Application.Common;

/// <summary>
/// A request was well formed but violates a business rule. <see cref="Code"/> is a stable,
/// documented error code that clients map to localized text; the message is diagnostic only.
/// </summary>
public sealed class BusinessRuleException : Exception
{
    public BusinessRuleException()
        : this("rule.violated", "A business rule was violated.")
    {
    }

    public BusinessRuleException(string message)
        : this("rule.violated", message)
    {
    }

    public BusinessRuleException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = "rule.violated";
    }

    public BusinessRuleException(string code, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
    }

    public BusinessRuleException(string code, string message, string? field)
        : this(code, message)
    {
        Field = field;
    }

    public string Code { get; }

    public string? Field { get; }
}
