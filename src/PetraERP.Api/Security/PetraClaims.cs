namespace PetraERP.Api.Security;

/// <summary>JWT claim names. Inbound claim mapping is disabled, so these arrive exactly as issued.</summary>
internal static class PetraClaims
{
    internal const string Subject = "sub";
    internal const string Session = "sid";
    internal const string TokenId = "jti";
    internal const string PasswordChangeRequired = "petra_pcr";
}
