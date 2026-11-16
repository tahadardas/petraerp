namespace PetraERP.Application.Common;

/// <summary>A request field is malformed or out of its documented range (HTTP 400).</summary>
public sealed class RequestValidationException : Exception
{
    public RequestValidationException()
        : this("request.invalid", "The request is invalid.", null)
    {
    }

    public RequestValidationException(string message)
        : this("request.invalid", message, null)
    {
    }

    public RequestValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = "request.invalid";
    }

    public RequestValidationException(string code, string message, string? field)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
        Field = field;
    }

    public string Code { get; }

    public string? Field { get; }
}
