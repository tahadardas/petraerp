using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Security;

/// <summary>An installation user. Login names are ASCII; display names are Unicode (Arabic first).</summary>
internal sealed class UserRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public required string UserName { get; set; }
    public required string NormalizedUserName { get; set; }
    public required string DisplayNameArabic { get; set; }
    public string? DisplayNameEnglish { get; set; }

    // Password verifier: PBKDF2 algorithm id, iteration count, salt and derived key. Never the password.
    public byte PasswordAlgorithm { get; set; }
    public int PasswordIterations { get; set; }
    public byte[] PasswordSalt { get; set; } = [];
    public byte[] PasswordHash { get; set; } = [];
    public DateTimeOffset PasswordChangedAtUtc { get; set; }

    /// <summary>Set for administrator-assigned passwords; the user must choose a new one before other access.</summary>
    public bool MustChangePassword { get; set; }

    public bool IsActive { get; set; }
    public int AccessFailedCount { get; set; }
    public DateTimeOffset? LockoutEndUtc { get; set; }
    public DateTimeOffset? LastLoginAtUtc { get; set; }

    /// <summary>Rotated on password change, reset or disable; sessions issued under an older stamp become invalid.</summary>
    public Guid SecurityStamp { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
