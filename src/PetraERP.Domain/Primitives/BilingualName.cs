namespace PetraERP.Domain.Primitives;

public sealed record BilingualName
{
    public const int MaximumLength = 200;

    private BilingualName(string arabic, string? english)
    {
        Arabic = arabic;
        English = english;
    }

    public string Arabic { get; }
    public string? English { get; }

    public static BilingualName Create(string arabic, string? english = null) => new(
        DomainText.Required(arabic, MaximumLength, nameof(arabic)),
        string.IsNullOrWhiteSpace(english) ? null : DomainText.Required(english, MaximumLength, nameof(english)));
}
