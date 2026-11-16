using PetraERP.Domain.Patterns;

namespace PetraERP.Domain.Tests;

public sealed class BranchLinkPrecedenceTests
{
    private static PatternConfiguration Config(Guid pattern) => new()
    {
        AccountMappings = [new PatternAccountMapping { Role = "Revenue", AccountId = pattern, IsRequired = true }],
    };

    [Fact]
    public void BranchLinkBeatsPatternAndCompanyDefaults()
    {
        Guid pattern = Guid.NewGuid(), company = Guid.NewGuid(), branch = Guid.NewGuid();
        var r = PostingRuleResolver.Resolve("Revenue", Config(pattern), companyDefaultAccountId: company, branchLinkAccountId: branch);
        Assert.Equal(branch, r.AccountId);
        Assert.Equal(AccountResolutionSource.BranchLink, r.Source);
    }

    [Fact]
    public void ItemGroupAndPartyContractsBeatBranchLink()
    {
        Guid pattern = Guid.NewGuid(), branch = Guid.NewGuid(), item = Guid.NewGuid(), group = Guid.NewGuid(), party = Guid.NewGuid();
        Assert.Equal(AccountResolutionSource.ItemOverride, PostingRuleResolver.Resolve("Revenue", Config(pattern), itemContractAccountId: item, branchLinkAccountId: branch).Source);
        Assert.Equal(AccountResolutionSource.GroupContract, PostingRuleResolver.Resolve("Revenue", Config(pattern), groupContractAccountId: group, branchLinkAccountId: branch).Source);
        Assert.Equal(AccountResolutionSource.PartyContract, PostingRuleResolver.Resolve("Revenue", Config(pattern), partyContractAccountId: party, branchLinkAccountId: branch).Source);
    }

    [Fact]
    public void WithoutBranchLinkThePatternDefaultStillApplies()
    {
        var pattern = Guid.NewGuid();
        var r = PostingRuleResolver.Resolve("Revenue", Config(pattern));
        Assert.Equal(AccountResolutionSource.PatternDefault, r.Source);
    }
}
