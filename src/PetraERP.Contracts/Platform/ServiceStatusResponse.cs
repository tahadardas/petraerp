namespace PetraERP.Contracts.Platform;

public sealed record ServiceStatusResponse
{
    public required string Status { get; init; }
    public required string ApiVersion { get; init; }
    public required string ProductVersion { get; init; }
    public required DateTimeOffset ServerTimeUtc { get; init; }
}
