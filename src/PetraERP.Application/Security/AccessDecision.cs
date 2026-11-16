namespace PetraERP.Application.Security;

public enum AccessDecision
{
    Granted = 1,
    PermissionDenied = 2,
    CompanyScopeDenied = 3,
    BranchScopeDenied = 4,
    UserInactive = 5,
}
