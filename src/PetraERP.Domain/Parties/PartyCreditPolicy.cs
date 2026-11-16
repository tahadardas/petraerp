namespace PetraERP.Domain.Parties;

public sealed record PartyCreditPolicy
{
    public decimal CreditLimit { get; }
    public int PaymentTermDays { get; }
    public CreditPolicyKind PolicyKind { get; }

    private PartyCreditPolicy(decimal creditLimit, int paymentTermDays, CreditPolicyKind policyKind)
    {
        if (creditLimit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(creditLimit), "Credit limit cannot be negative.");
        }

        if (paymentTermDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(paymentTermDays), "Payment term days cannot be negative.");
        }

        if (!Enum.IsDefined(policyKind))
        {
            throw new ArgumentOutOfRangeException(nameof(policyKind), "Unknown credit policy kind.");
        }

        CreditLimit = creditLimit;
        PaymentTermDays = paymentTermDays;
        PolicyKind = policyKind;
    }

    public static PartyCreditPolicy Default => new(0m, 0, CreditPolicyKind.NoLimit);

    public static PartyCreditPolicy Create(decimal creditLimit, int paymentTermDays, CreditPolicyKind policyKind) =>
        new(creditLimit, paymentTermDays, policyKind);
}
