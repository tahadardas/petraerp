namespace PetraERP.Application.Security;

/// <summary>A granular permission code and the scope against which it is evaluated.</summary>
public sealed record PermissionDefinition(string Code, PermissionScope Scope);
