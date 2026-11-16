using System.ComponentModel.DataAnnotations;

namespace PetraERP.Contracts.Authentication;

public sealed record LoginRequest
{
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public required string UserName { get; init; }

    [Required]
    [StringLength(128, MinimumLength = 1)]
    public required string Password { get; init; }

    public ClientKind ClientKind { get; init; } = ClientKind.Desktop;

    /// <summary>Optional user-facing label of the device, for example the Windows machine name.</summary>
    [StringLength(100)]
    public string? DeviceName { get; init; }

    public override string ToString() => $"LoginRequest({ClientKind})";
}
