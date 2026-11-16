namespace PetraERP.Application.Security;

/// <summary>The kind of client that opened a session. Stored for session review and audit.</summary>
public enum ClientKind
{
    Desktop = 1,
    Web = 2,
    Mobile = 3,
    Integration = 4,
}
