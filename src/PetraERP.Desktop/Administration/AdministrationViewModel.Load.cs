using System.Globalization;
using PetraERP.ApiClient;

namespace PetraERP.Desktop.Administration;

public sealed partial class AdministrationViewModel
{
    public async Task LoadPageAsync(string page, CancellationToken cancellationToken)
    {
        var stamp = epoch;
        if (page == "users" && permissions.Contains("System.Users.View"))
        {
            var org = await client.OrganizationAsync(cancellationToken).ConfigureAwait(true);
            var roles = CanViewRoles ? (await client.RolesAsync(cancellationToken).ConfigureAwait(true)).Items : [];
            if (stamp != epoch) return;
            organization = org.Items; roleCatalog = roles;
            await LoadUsersAsync(1, cancellationToken).ConfigureAwait(true);
        }
        else if (page == "roles" && permissions.Contains("System.Roles.View"))
        {
            var definitions = await client.PermissionsAsync(cancellationToken).ConfigureAwait(true);
            var roles = await client.RolesAsync(cancellationToken).ConfigureAwait(true);
            if (stamp != epoch) return;
            permissionCatalog = definitions.Items; Roles.Clear(); foreach (var row in roles.Items) Roles.Add(row); Notify();
        }
        else if (page == "audit" && permissions.Contains("Audit.Security.View")) await LoadAuditAsync(1, cancellationToken).ConfigureAwait(true);
    }
    private async Task LoadUsersAsync(int page, CancellationToken cancellationToken)
    {
        if (!permissions.Contains("System.Users.View")) return;
        var stamp = epoch;
        var result = await client.UsersAsync(UserSearch, Math.Max(1, page), cancellationToken).ConfigureAwait(true);
        if (stamp != epoch) return;
        Users.Clear(); foreach (var row in result.Items) Users.Add(row);
        UserPage = result.Page; UserTotal = result.TotalCount; SelectedUser = null;
        user = null; IsUserEditorOpen = false; TemporaryPassword = string.Empty; Notify();
    }
    private async Task OpenUserAsync()
    {
        if (!permissions.Contains("System.Users.View") || SelectedUser is null) return;
        var stamp = epoch;
        var result = await client.UserAsync(SelectedUser.Id, context.Token).ConfigureAwait(true);
        if (stamp == epoch) ApplyUser(result);
    }
    private void ApplyUser(UserDetailsResponse value)
    {
        user = value; LoginName = value.UserName; DisplayArabic = value.DisplayNameArabic; DisplayEnglish = value.DisplayNameEnglish;
        IsUserEditorOpen = true; TemporaryPassword = Reason = string.Empty;
        UserRoles.Clear(); foreach (var row in roleCatalog) UserRoles.Add(new(row.Id.ToString("D"), row.Code + " · " + row.NameArabic, value.RoleIds.Contains(row.Id)));
        Scopes.Clear(); foreach (var row in organization)
        {
            var grant = value.Scopes.FirstOrDefault(item => item.CompanyId == row.Id);
            Scopes.Add(new(row.Id, row.Code + " · " + row.NameArabic + (row.IsActive ? string.Empty : " · " + localization["D_Disabled"]), grant is not null,
                row.Branches.Select(branch => new GrantOption(branch.Id.ToString("D"), branch.Code + " · " + branch.NameArabic + (branch.IsActive ? string.Empty : " · " + localization["D_Disabled"]), grant?.BranchIds.Contains(branch.Id) == true, branch.IsActive)), row.IsActive));
        }
        Localize();
    }
    private void NewUser()
    {
        if (!CanManageUsers) return;
        user = null; IsUserEditorOpen = true; LoginName = DisplayArabic = TemporaryPassword = Reason = string.Empty; DisplayEnglish = null;
        UserRoles.Clear(); Scopes.Clear(); Notify();
    }
    private async Task OpenRoleAsync()
    {
        if (!permissions.Contains("System.Roles.View") || SelectedRole is null) return;
        var stamp = epoch;
        var result = await client.RoleAsync(SelectedRole.Id, context.Token).ConfigureAwait(true);
        if (stamp == epoch) ApplyRole(result);
    }
    private void ApplyRole(RoleDetailsResponse value)
    {
        role = value; IsRoleEditorOpen = true; RoleCode = value.Code; RoleArabic = value.NameArabic; RoleEnglish = value.NameEnglish;
        RolePermissions.Clear(); foreach (var row in permissionCatalog) RolePermissions.Add(new(row.Code, localization["Permission_" + row.Code.Replace('.', '_')] + " · " + localization["D_Scope_" + row.Scope], value.PermissionCodes.Contains(row.Code)));
        OnPropertyChanged(nameof(PermissionGroups)); Notify();
    }
    private void NewRole()
    {
        if (!CanManageRoles) return;
        role = null; IsRoleEditorOpen = true; RoleCode = RoleArabic = string.Empty; RoleEnglish = null;
        RolePermissions.Clear(); foreach (var row in permissionCatalog) RolePermissions.Add(new(row.Code, localization["Permission_" + row.Code.Replace('.', '_')] + " · " + localization["D_Scope_" + row.Scope], false)); OnPropertyChanged(nameof(PermissionGroups)); Notify();
    }
    private async Task LoadAuditAsync(int page, CancellationToken cancellationToken)
    {
        if (!permissions.Contains("Audit.Security.View")) return;
        Guid? userId = null; DateTimeOffset? from = null, to = null;
        if (AuditUser.Length > 0) { if (!Guid.TryParse(AuditUser, out var id)) { operation.SetError("Validation"); return; } userId = id; }
        if (!ParseUtc(AuditFrom, out from) || !ParseUtc(AuditTo, out to) || from > to) { operation.SetError("Validation"); return; }
        var stamp = epoch;
        var result = await client.AuditAsync(userId, AuditType, from, to, Math.Max(1, page), cancellationToken).ConfigureAwait(true);
        if (stamp != epoch) return;
        Audit.Clear(); foreach (var row in result.Items) Audit.Add(row); AuditPage = result.Page; AuditTotal = result.TotalCount; Notify();
    }
    public void Localize()
    {
        foreach (var option in RolePermissions)
        {
            var definition = permissionCatalog.FirstOrDefault(item => item.Code == option.Key);
            if (definition is not null) option.Label = localization["Permission_" + definition.Code.Replace('.', '_')] + " · " + localization["D_Scope_" + definition.Scope];
        }
        foreach (var option in UserRoles)
        {
            var row = roleCatalog.FirstOrDefault(item => item.Id.ToString("D") == option.Key);
            if (row is not null) option.Label = row.Code + " · " + LocalName(row.NameArabic, row.NameEnglish);
        }
        foreach (var option in Scopes)
        {
            var row = organization.FirstOrDefault(item => item.Id == option.Id);
            if (row is null) continue;
            option.Label = row.Code + " · " + LocalName(row.NameArabic, row.NameEnglish) + (row.IsActive ? string.Empty : " · " + localization["D_Disabled"]);
            foreach (var branch in option.Branches)
            {
                var entry = row.Branches.FirstOrDefault(item => item.Id.ToString("D") == branch.Key);
                if (entry is not null) branch.Label = entry.Code + " · " + LocalName(entry.NameArabic, entry.NameEnglish) + (entry.IsActive ? string.Empty : " · " + localization["D_Disabled"]);
            }
        }
        OnPropertyChanged(nameof(UserState)); OnPropertyChanged(nameof(PermissionGroups));
    }
    public IEnumerable<PermissionGroup> PermissionGroups => RolePermissions.GroupBy(option => option.Key.StartsWith("System.", StringComparison.Ordinal) || option.Key.StartsWith("Audit.", StringComparison.Ordinal) ? "administration" : "accounting")
        .Select(group => new PermissionGroup(localization["D_NavGroup_" + group.Key], group.ToArray()));
    public sealed record PermissionGroup(string Label, IReadOnlyList<GrantOption> Items);
    private string LocalName(string arabic, string? english) => localization.IsRightToLeft ? arabic : english ?? arabic;
    private static bool ParseUtc(string text, out DateTimeOffset? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!DateTimeOffset.TryParseExact(text, "yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)) return false;
        value = parsed.ToUniversalTime(); return true;
    }
}
