using System.ComponentModel.DataAnnotations;
namespace PetraERP.Contracts.CompanyOptions;

public sealed record NegativeIssueApprovalRequest
{
    public Guid RequestId { get; init; }
    public Guid ItemId { get; init; }
    public Guid WarehouseId { get; init; }
    public string Quantity { get; init; }=string.Empty;
    [Required,StringLength(500,MinimumLength=1)] public string Reason { get; init; }=string.Empty;
}
public sealed record ReviewNegativeIssueRequest
{
    public bool Approve { get; init; }
    public string ExpectedVersion { get; init; }=string.Empty;
    [Required,StringLength(500,MinimumLength=1)] public string Reason { get; init; }=string.Empty;
}
