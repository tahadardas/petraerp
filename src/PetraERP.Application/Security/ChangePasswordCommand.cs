namespace PetraERP.Application.Security;

public sealed class ChangePasswordCommand
{
    public ChangePasswordCommand(Guid userId, Guid sessionId, string currentPassword, string newPassword)
    {
        ArgumentNullException.ThrowIfNull(currentPassword);
        ArgumentNullException.ThrowIfNull(newPassword);
        UserId = userId;
        SessionId = sessionId;
        CurrentPassword = currentPassword;
        NewPassword = newPassword;
    }

    public Guid UserId { get; }
    public Guid SessionId { get; }
    public string CurrentPassword { get; }
    public string NewPassword { get; }

    public override string ToString() => $"ChangePasswordCommand({UserId})";
}
