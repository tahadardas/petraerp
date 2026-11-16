namespace PetraERP.Application.Common;

/// <summary>The request conflicts with an existing resource (for example a duplicate unique code). HTTP 409.</summary>
public sealed class ResourceConflictException : Exception
{
    public ResourceConflictException()
        : this("resource.conflict", "The request conflicts with an existing resource.")
    {
    }

    public ResourceConflictException(string message)
        : this("resource.conflict", message)
    {
    }

    public ResourceConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = "resource.conflict";
    }

    public ResourceConflictException(string code, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
    }

    public string Code { get; }
}
