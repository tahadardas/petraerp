using System.ComponentModel.DataAnnotations;

namespace PetraERP.Contracts.Authentication;

public sealed record RefreshSessionRequest
{
    [Required]
    [StringLength(43, MinimumLength = 43)]
    public required string RefreshToken { get; init; }

    public override string ToString() => "RefreshSessionRequest";
}
