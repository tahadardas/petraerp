using PetraERP.Application.CompanyOptions;

namespace PetraERP.Infrastructure.Tests;

public sealed class CompanyOptionsTests
{
    [Fact]
    public void CompanyPolicySnapshotDoesNotEnableNegativePostingOnItsOwn()
    {
        var policy = new CompanyFinancialPolicy
        {
            NegativeIssuePolicy = NegativeIssuePolicy.RequestApproval,
            AllowManualNumbering = true,
            DefaultNumberingScope = 4,
            InitialNumberSequence = 1,
        };

        var snapshot = new CompanyPolicySnapshot(policy, "v1");

        // The flag is derived by the service from an AuthorizedException policy; an approval request or a
        // hand-built snapshot never enables posting below zero.
        Assert.False(snapshot.NegativePostingAvailable);
        Assert.Equal("Installation security > company policy > permitted branch/pattern override > user display preference", snapshot.PolicyPrecedence);
    }

    [Fact]
    public void NegativeIssueRequestReportsNegativePostingUnavailable()
    {
        var request = new NegativeIssueRequest(
            Id: Guid.NewGuid(),
            BranchId: Guid.NewGuid(),
            ItemId: Guid.NewGuid(),
            WarehouseId: Guid.NewGuid(),
            Quantity: "10.0000",
            Reason: "Urgent shipment approval",
            Status: "ApprovedPolicyOnly",
            RequestedBy: Guid.NewGuid(),
            ReviewedBy: Guid.NewGuid(),
            Version: "v1");

        Assert.False(request.NegativePostingAvailable);
    }

    [Theory]
    [InlineData(NegativeIssuePolicy.Block)]
    [InlineData(NegativeIssuePolicy.RequestApproval)]
    [InlineData(NegativeIssuePolicy.AuthorizedException)]
    public void SupportedNegativeIssuePoliciesAreExplicitlyDefined(NegativeIssuePolicy policy)
    {
        Assert.True(Enum.IsDefined(policy));
    }
}
