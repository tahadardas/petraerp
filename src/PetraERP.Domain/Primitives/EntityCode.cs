namespace PetraERP.Domain.Primitives;

public sealed record EntityCode
{
    public const int MaximumLength = 64;

    private EntityCode(string value) => Value = value;

    public string Value { get; }

    public static EntityCode Create(string value) =>
        new(DomainText.Required(value, MaximumLength, nameof(value)));

    public override string ToString() => Value;
}
