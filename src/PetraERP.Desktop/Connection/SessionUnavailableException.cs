namespace PetraERP.Desktop.Connection;

public sealed class SessionUnavailableException : InvalidOperationException
{
    public SessionUnavailableException() : base("Sign in again.") { }
    public SessionUnavailableException(string message) : base(message) { }
    public SessionUnavailableException(string message, Exception innerException) : base(message, innerException) { }
}
