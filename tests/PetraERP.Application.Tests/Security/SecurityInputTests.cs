using PetraERP.Application.Common;
using PetraERP.Application.Security;

namespace PetraERP.Application.Tests.Security;

public sealed class SecurityInputTests
{
    private static readonly string[] DuplicatedPermission = [PermissionCatalog.UsersView, PermissionCatalog.UsersView];
    private static readonly string[] UnknownPermission = ["System.Users.Destroy"];
    private static readonly string[] ValidPermissions = [PermissionCatalog.UsersView, PermissionCatalog.JournalsPost];

    [Fact]
    public void ReasonsPreserveArabicAndRejectEmptyOrOverlongText()
    {
        Assert.Equal("تعطيل بسبب انتهاء العقد", SecurityInput.RequireReason("  تعطيل بسبب انتهاء العقد  ", "reason"));
        var empty = Assert.Throws<RequestValidationException>(static () => SecurityInput.RequireReason("   ", "reason"));
        Assert.Equal("request.reason_invalid", empty.Code);
        Assert.Equal("reason", empty.Field);
        Assert.Throws<RequestValidationException>(static () => SecurityInput.RequireReason(new string('س', 501), "reason"));
    }

    [Fact]
    public void RoleCodesAreUpperCasedAndPermissionListsAreValidated()
    {
        Assert.Equal("CHIEF.ACCOUNTANT", SecurityInput.RequireRoleCode(" chief.accountant ", "code"));
        Assert.Throws<RequestValidationException>(static () => SecurityInput.RequireRoleCode("مدير", "code"));
        Assert.Equal(ValidPermissions.Order(StringComparer.Ordinal), SecurityInput.RequirePermissionCodes(ValidPermissions, "p"));
        Assert.Equal("security.duplicate_permission",
            Assert.Throws<RequestValidationException>(static () => SecurityInput.RequirePermissionCodes(DuplicatedPermission, "p")).Code);
        Assert.Equal("security.unknown_permission",
            Assert.Throws<RequestValidationException>(static () => SecurityInput.RequirePermissionCodes(UnknownPermission, "p")).Code);
    }

    [Fact]
    public void PagingIsBoundedOnTheServer()
    {
        var page = PageRequest.Create(null, null);
        Assert.Equal(1, page.Page);
        Assert.Equal(PageRequest.DefaultPageSize, page.PageSize);
        Assert.Equal(100, PageRequest.Create(3, 50).Skip);
        Assert.Throws<RequestValidationException>(static () => PageRequest.Create(0, 10));
        Assert.Throws<RequestValidationException>(static () => PageRequest.Create(1, PageRequest.MaximumPageSize + 1));
    }

    [Fact]
    public void ClientContextBoundsAndSanitizesClientSuppliedText()
    {
        var context = ClientContext.Create(" 10.0.0.5 ", new string('u', 400) + "\n", "  ");
        Assert.Equal("10.0.0.5", context.IpAddress);
        Assert.Equal(ClientContext.MaximumUserAgentLength, context.UserAgent!.Length);
        Assert.Null(context.CorrelationId);
    }

    [Fact]
    public void AuthenticationDefaultsAreValidAndUnsafeValuesAreRejected()
    {
        AuthenticationSettings.Default.Validate();
        Assert.Throws<InvalidOperationException>(static () =>
            (AuthenticationSettings.Default with { PasswordHashIterations = 1000 }).Validate());
        Assert.Throws<InvalidOperationException>(static () =>
            (AuthenticationSettings.Default with { AccessTokenLifetime = TimeSpan.FromHours(2) }).Validate());
        Assert.Throws<InvalidOperationException>(static () =>
            (AuthenticationSettings.Default with { MaxFailedAccessAttempts = 0 }).Validate());
    }
}
