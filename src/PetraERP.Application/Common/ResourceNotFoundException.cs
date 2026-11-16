namespace PetraERP.Application.Common;

/// <summary>The requested resource does not exist or is outside the caller's resolved scope.</summary>
public sealed class ResourceNotFoundException : Exception
{
    public ResourceNotFoundException()
        : this("resource")
    {
    }

    public ResourceNotFoundException(string resource)
        : base($"The requested {resource} was not found.")
    {
        Resource = resource;
    }

    public ResourceNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
        Resource = "resource";
    }

    public string Resource { get; }
}
