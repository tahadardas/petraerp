namespace PetraERP.Application.Security;

/// <summary>Where a permission applies. The server resolves the scope; clients never assert it.</summary>
public enum PermissionScope
{
    /// <summary>Installation-wide administration; no company or branch is involved.</summary>
    Installation = 1,

    /// <summary>Requires an explicit company scope grant for the requested company.</summary>
    Company = 2,

    /// <summary>Requires explicit company and branch scope grants for the requested branch.</summary>
    Branch = 3,
}
