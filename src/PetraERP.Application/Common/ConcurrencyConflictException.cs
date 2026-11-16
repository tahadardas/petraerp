namespace PetraERP.Application.Common;

/// <summary>The caller's concurrency version is stale; newer changes were not overwritten.</summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException()
        : this("resource")
    {
    }

    public ConcurrencyConflictException(string resource)
        : base($"The {resource} was changed by another operation. Reload it and try again.")
    {
        Resource = resource;
    }

    public ConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
        Resource = "resource";
    }

    public string Resource { get; }
}
