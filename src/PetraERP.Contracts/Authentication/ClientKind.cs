namespace PetraERP.Contracts.Authentication;

/// <summary>The kind of client opening a session.</summary>
public enum ClientKind
{
    Desktop = 1,
    Web = 2,
    Mobile = 3,
    Integration = 4,
}
