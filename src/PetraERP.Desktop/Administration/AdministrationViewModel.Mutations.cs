using PetraERP.ApiClient;

namespace PetraERP.Desktop.Administration;

public sealed partial class AdministrationViewModel
{
    private enum UserMutation { Profile, Roles, Scopes, Status, Unlock, Password, Revoke }
    private async Task MutateUserAsync(UserMutation action)
    {
        if (!CanManageUsers || !IsUserEditorOpen) return;
        if (DisplayArabic.Trim().Length is < 1 or > 200 || DisplayEnglish?.Length > 200) { operation.SetError("Validation"); return; }
        var target = user; var stamp = epoch; var secret = TemporaryPassword; TemporaryPassword = string.Empty;
        if (target is null && (LoginName.Length is < 3 or > 64 || secret.Length is < 10 or > 128)) { operation.SetError("PasswordRule"); return; }
        if (action is UserMutation.Status or UserMutation.Password or UserMutation.Revoke && string.IsNullOrWhiteSpace(Reason)) { operation.SetError("ReasonRequired"); return; }
        if (action == UserMutation.Password && secret.Length is < 10 or > 128) { operation.SetError("PasswordRule"); return; }
        if (!await confirmation.ConfirmAsync(HasOtherUserEdits(action) ? "D_ConfirmDiscardOtherEdits" : "D_ConfirmSecurityChange").ConfigureAwait(true)) return;
        if (stamp != epoch || !CanManageUsers || target?.Id != user?.Id || target?.Version != user?.Version) return;
        await RunAsync(async () =>
        {
            UserDetailsResponse? updated;
            if (target is null)
                updated = await client.CreateUserAsync(new CreateUserRequest { UserName = LoginName, DisplayNameArabic = DisplayArabic, DisplayNameEnglish = OptionalName(DisplayEnglish), InitialPassword = secret }, context.Token).ConfigureAwait(true);
            else
                updated = action switch
                {
                    UserMutation.Profile => await client.ProfileAsync(target.Id, new UpdateUserProfileRequest { DisplayNameArabic = DisplayArabic, DisplayNameEnglish = OptionalName(DisplayEnglish), Version = target.Version }, context.Token).ConfigureAwait(true),
                    UserMutation.Roles when CanViewRoles => await client.RolesAsync(target.Id, new SetUserRolesRequest { Version = target.Version, RoleIds = UserRoles.Where(item => item.IsSelected).Select(item => Guid.Parse(item.Key)).ToArray() }, context.Token).ConfigureAwait(true),
                    UserMutation.Scopes => await client.ScopesAsync(target.Id, new SetUserScopesRequest { Version = target.Version, Scopes = Scopes.Where(item => item.IsSelected).Select(item => new CompanyScopeEntry { CompanyId = item.Id, BranchIds = item.Branches.Where(branch => branch.IsSelected).Select(branch => Guid.Parse(branch.Key)).ToArray() }).ToArray() }, context.Token).ConfigureAwait(true),
                    UserMutation.Status => await client.StatusAsync(target.Id, new SetUserStatusRequest { IsActive = !target.IsActive, Reason = Reason, Version = target.Version }, context.Token).ConfigureAwait(true),
                    UserMutation.Unlock => await client.UnlockAsync(target.Id, new UnlockUserRequest { Version = target.Version }, context.Token).ConfigureAwait(true),
                    UserMutation.Password => await client.PasswordAsync(target.Id, new ResetUserPasswordRequest { TemporaryPassword = secret, Reason = Reason, Version = target.Version }, context.Token).ConfigureAwait(true),
                    UserMutation.Revoke => null,
                    _ => throw new InvalidOperationException("Unsupported security operation."),
                };
            if (action == UserMutation.Revoke && target is not null)
            {
                await client.RevokeAsync(target.Id, new RevokeUserSessionsRequest { Reason = Reason }, context.Token).ConfigureAwait(true);
                updated = await client.UserAsync(target.Id, context.Token).ConfigureAwait(true);
            }
            if (stamp == epoch && updated is not null)
            {
                ApplyUser(updated);
                var page = await client.UsersAsync(UserSearch, UserPage, context.Token).ConfigureAwait(true);
                if (stamp == epoch) { Users.Clear(); foreach (var row in page.Items) Users.Add(row); UserTotal = page.TotalCount; Notify(); }
            }
        }).ConfigureAwait(true);
    }
    private static string? OptionalName(string? text) => text?.Length == 0 ? null : text;
    private bool HasOtherUserEdits(UserMutation action)
    {
        if (user is null) return false;
        if (action != UserMutation.Profile && (DisplayArabic != user.DisplayNameArabic || OptionalName(DisplayEnglish) != user.DisplayNameEnglish)) return true;
        if (action != UserMutation.Roles && CanViewRoles && !UserRoles.Where(item => item.IsSelected).Select(item => Guid.Parse(item.Key)).Order().SequenceEqual(user.RoleIds.Order())) return true;
        if (action == UserMutation.Scopes) return false;
        var draft = Scopes.Where(item => item.IsSelected).Select(item => item.Id.ToString("D") + ":" + string.Join(",", item.Branches.Where(branch => branch.IsSelected).Select(branch => branch.Key).Order(StringComparer.Ordinal))).Order(StringComparer.Ordinal);
        var saved = user.Scopes.Select(item => item.CompanyId.ToString("D") + ":" + string.Join(",", item.BranchIds.Select(id => id.ToString("D")).Order(StringComparer.Ordinal))).Order(StringComparer.Ordinal);
        return !draft.SequenceEqual(saved, StringComparer.Ordinal);
    }
    private async Task SaveRoleAsync()
    {
        if (!CanManageRoles || !IsRoleEditorOpen) return;
        if (RoleArabic.Trim().Length is < 1 or > 200 || RoleEnglish?.Length > 200 || role is null && RoleCode.Length is < 2 or > 64) { operation.SetError("Validation"); return; }
        var target = role; var stamp = epoch;
        if (!await confirmation.ConfirmAsync("D_ConfirmSecurityChange").ConfigureAwait(true)) return;
        if (stamp != epoch || !CanManageRoles || target?.Id != role?.Id || target?.Version != role?.Version) return;
        await RunAsync(async () =>
        {
            var codes = RolePermissions.Where(item => item.IsSelected).Select(item => item.Key).ToArray();
            var result = target is null ?
                await client.CreateRoleAsync(new CreateRoleRequest { Code = RoleCode, NameArabic = RoleArabic, NameEnglish = OptionalName(RoleEnglish), PermissionCodes = codes }, context.Token).ConfigureAwait(true) :
                await client.UpdateRoleAsync(target.Id, new UpdateRoleRequest { NameArabic = RoleArabic, NameEnglish = OptionalName(RoleEnglish), PermissionCodes = codes, Version = target.Version }, context.Token).ConfigureAwait(true);
            if (stamp != epoch) return;
            ApplyRole(result);
            var rows = await client.RolesAsync(context.Token).ConfigureAwait(true);
            if (stamp == epoch) { roleCatalog = rows.Items; Roles.Clear(); foreach (var row in rows.Items) Roles.Add(row); Notify(); }
        }).ConfigureAwait(true);
    }
}
