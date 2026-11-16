using PetraERP.Domain.FinancialValues;
using PetraERP.Domain.Organizations;
using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.GeneralLedger;

public sealed class Account
{
    private Account(Guid id, Guid companyId, EntityCode code, BilingualName name, AccountType type,
        bool postingAllowed, CostCenterPolicy costPolicy, AccountCurrencyPolicy currencyPolicy, Guid? parentId, bool isActive)
    {
        Id = id;
        CompanyId = companyId;
        Code = code;
        Name = name;
        Type = type;
        PostingAllowed = postingAllowed;
        CostCenterPolicy = costPolicy;
        CurrencyPolicy = currencyPolicy;
        ParentId = parentId;
        IsActive = isActive;
    }

    public Guid Id { get; }
    public Guid CompanyId { get; }
    public EntityCode Code { get; }
    public BilingualName Name { get; }
    public AccountType Type { get; }
    public bool PostingAllowed { get; }
    public CostCenterPolicy CostCenterPolicy { get; }
    public AccountCurrencyPolicy CurrencyPolicy { get; }
    public Guid? ParentId { get; }
    public bool IsActive { get; }

    public static Account Create(Company company, EntityCode code, BilingualName name, AccountType type,
        bool postingAllowed, CostCenterPolicy costPolicy, AccountCurrencyPolicy currencyPolicy,
        Account? parent = null, bool isActive = true)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(currencyPolicy);
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        if (!Enum.IsDefined(costPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(costPolicy));
        }

        if (parent is not null && (parent.CompanyId != company.Id || parent.Type != type || parent.PostingAllowed || !parent.IsActive))
        {
            throw RuleViolation.Tag(new ArgumentException("Parent must be an active control account in the same company and account type.", nameof(parent)),
                RuleViolation.AccountParentInvalid);
        }

        currencyPolicy.EnsureConfiguredFor(company);
        return new Account(Guid.CreateVersion7(), company.Id, code, name, type, postingAllowed,
            costPolicy, currencyPolicy, parent?.Id, isActive);
    }

    public static Account Reconstitute(Guid id, Guid companyId, EntityCode code, BilingualName name, AccountType type,
        bool postingAllowed, CostCenterPolicy costPolicy, AccountCurrencyPolicy currencyPolicy, Guid? parentId, bool isActive)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(currencyPolicy);
        return new Account(id, companyId, code, name, type, postingAllowed, costPolicy, currencyPolicy, parentId, isActive);
    }

    /// <summary>
    /// Returns the revised account. History-bearing accounts keep code, type, currency policy and posting capability,
    /// so posted lines never change meaning. Accounts with children stay control accounts. The parent must be an
    /// active control account of the same company and type and must not be this account or one of its descendants
    /// (<paramref name="parentAncestorIds"/> lists the proposed parent's ancestors, nearest first).
    /// </summary>
    public Account Revise(Company company, EntityCode code, BilingualName name, AccountType type, bool postingAllowed,
        CostCenterPolicy costPolicy, AccountCurrencyPolicy currencyPolicy, Account? parent,
        IReadOnlyCollection<Guid> parentAncestorIds, AccountUsage usage)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(currencyPolicy);
        ArgumentNullException.ThrowIfNull(parentAncestorIds);
        ArgumentNullException.ThrowIfNull(usage);
        if (company.Id != CompanyId)
        {
            throw new ArgumentException("Account belongs to another company.", nameof(company));
        }

        if (!Enum.IsDefined(type) || !Enum.IsDefined(costPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        if (parent is not null)
        {
            if (parent.Id == Id || parentAncestorIds.Contains(Id))
            {
                throw RuleViolation.Create(RuleViolation.AccountHierarchyCycle, "An account cannot be placed under itself or its descendant.");
            }

            if (parent.CompanyId != CompanyId || parent.Type != type || parent.PostingAllowed || !parent.IsActive)
            {
                throw RuleViolation.Create(RuleViolation.AccountParentInvalid,
                    "Parent must be an active control account in the same company and account type.");
            }
        }

        if (usage.HasJournalLines && (code != Code || type != Type || currencyPolicy != CurrencyPolicy || postingAllowed != PostingAllowed))
        {
            throw RuleViolation.Create(RuleViolation.AccountInUse,
                "Code, type, currency policy and posting capability are fixed once journal lines reference the account.");
        }

        if (usage.HasChildren && (postingAllowed || type != Type))
        {
            throw RuleViolation.Create(RuleViolation.AccountHasChildren, "An account with child accounts must remain a control account of the same type.");
        }

        currencyPolicy.EnsureConfiguredFor(company);
        return new Account(Id, CompanyId, code, name, type, postingAllowed, costPolicy, currencyPolicy, parent?.Id, IsActive);
    }

    /// <summary>Deactivation keeps history; it is refused while active children exist. Reactivation needs an active parent.</summary>
    public Account WithActivation(bool isActive, Account? parent, AccountUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        if (!isActive && usage.HasActiveChildren)
        {
            throw RuleViolation.Create(RuleViolation.AccountHasChildren, "Deactivate child accounts first.");
        }

        if (isActive && ParentId is not null && (parent is null || parent.Id != ParentId || !parent.IsActive))
        {
            throw RuleViolation.Create(RuleViolation.AccountParentInvalid, "Activate the parent account first.");
        }

        return new Account(Id, CompanyId, Code, Name, Type, PostingAllowed, CostCenterPolicy, CurrencyPolicy, ParentId, isActive);
    }

    /// <summary>
    /// Only an unused leaf may be deleted: no child account and no journal line in any state (draft lines are references too).
    /// Anything else keeps its history and can only be deactivated.
    /// </summary>
    public static void EnsureDeletable(AccountUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        if (usage.HasChildren)
        {
            throw RuleViolation.Create(RuleViolation.AccountHasChildren, "An account with child accounts cannot be deleted; deactivate it instead.");
        }

        if (usage.HasJournalLines)
        {
            throw RuleViolation.Create(RuleViolation.AccountInUse, "An account referenced by journal lines cannot be deleted; deactivate it instead.");
        }
    }

    /// <summary>Checks account policy only; authorization, period, balance and commit are separate requirements.</summary>
    public void ValidatePostingRules(Company company, CurrencyCode transactionCurrency, CostCenter? costCenter)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(transactionCurrency);
        if (company.Id != CompanyId || (costCenter is not null && costCenter.CompanyId != CompanyId))
        {
            throw new ArgumentException("Account and cost center must belong to the supplied company.", nameof(company));
        }

        if (!company.IsActive || !IsActive || !PostingAllowed || costCenter is { IsActive: false })
        {
            var code = !company.IsActive ? RuleViolation.ScopeInactive
                : costCenter is { IsActive: false } && IsActive && PostingAllowed ? RuleViolation.CostCenterInvalid
                : RuleViolation.AccountInvalid;
            throw RuleViolation.Create(code, "Inactive entities or control accounts cannot accept postings.");
        }

        if ((CostCenterPolicy == CostCenterPolicy.Required && costCenter is null)
            || (CostCenterPolicy == CostCenterPolicy.NotAllowed && costCenter is not null))
        {
            throw RuleViolation.Create(costCenter is null ? RuleViolation.CostCenterRequired : RuleViolation.CostCenterNotAllowed,
                "Cost center does not satisfy the account policy.");
        }

        CurrencyPolicy.Validate(company, transactionCurrency);
    }
}
