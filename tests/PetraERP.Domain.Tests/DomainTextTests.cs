using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.Tests;

public sealed class DomainTextTests
{
    [Fact]
    public void TextLimitsAreExplicitAndArabicDiacriticsRemainUnchanged()
    {
        Assert.Equal(64, EntityCode.Create(new string('س', 64)).Value.Length);
        Assert.Throws<ArgumentException>(() => EntityCode.Create(new string('س', 65)));
        Assert.Equal(200, BilingualName.Create(new string('ع', 200)).Arabic.Length);
        Assert.Throws<ArgumentException>(() => BilingualName.Create(new string('ع', 201)));
        Assert.Equal("المَزّة", BilingualName.Create("  المَزّة  ").Arabic);
        Assert.Null(BilingualName.Create("اسم", " ").English);
    }

    [Fact]
    public void CodesAreOrdinalAndControlCharactersAreRejected()
    {
        Assert.NotEqual(EntityCode.Create("a"), EntityCode.Create("A"));
        Assert.Equal(EntityCode.Create(" A "), EntityCode.Create("A"));
        Assert.Throws<ArgumentException>(() => EntityCode.Create("A\0B"));
        Assert.Throws<ArgumentException>(() => BilingualName.Create("اسم", "A\tB"));
        Assert.Throws<ArgumentNullException>(() => EntityCode.Create(null!));
    }
}
