using System.ComponentModel.DataAnnotations;

namespace PetraERP.Contracts.Authentication;

public sealed record ChangePasswordRequest
{
    [Required]
    [StringLength(128, MinimumLength = 1)]
    public required string CurrentPassword { get; init; }

    [Required]
    [StringLength(128, MinimumLength = 1)]
    public required string NewPassword { get; init; }

    public ClientKind ClientKind { get; init; } = ClientKind.Desktop;

    public override string ToString() => "ChangePasswordRequest";
}
