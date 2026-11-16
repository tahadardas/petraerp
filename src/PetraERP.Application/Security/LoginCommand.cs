namespace PetraERP.Application.Security;

/// <summary>Login input. A class (not a record) so the password can never appear in generated ToString output.</summary>
public sealed class LoginCommand
{
    public LoginCommand(string userName, string password, ClientKind clientKind, string? deviceName)
    {
        ArgumentNullException.ThrowIfNull(userName);
        ArgumentNullException.ThrowIfNull(password);
        UserName = userName;
        Password = password;
        ClientKind = clientKind;
        DeviceName = deviceName;
    }

    public string UserName { get; }
    public string Password { get; }
    public ClientKind ClientKind { get; }
    public string? DeviceName { get; }

    public override string ToString() => $"LoginCommand({ClientKind})";
}
