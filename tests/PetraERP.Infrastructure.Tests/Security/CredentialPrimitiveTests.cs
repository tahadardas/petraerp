using PetraERP.Application.Security;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.Tests.Security;

public sealed class CredentialPrimitiveTests
{
    private static readonly Pbkdf2PasswordHasher Hasher = new(AuthenticationSettings.Default);

    [Fact]
    public void Pbkdf2VerifiesOnlyTheOriginalPasswordAndUsesUniqueSalts()
    {
        var first = Hasher.Hash("كلمة مرور طويلة وآمنة");
        var second = Hasher.Hash("كلمة مرور طويلة وآمنة");
        Assert.Equal(Pbkdf2PasswordHasher.AlgorithmPbkdf2Sha512, first.Algorithm);
        Assert.Equal(AuthenticationSettings.MinimumPasswordIterations, first.Iterations);
        Assert.Equal(Pbkdf2PasswordHasher.SaltSize, first.Salt.Length);
        Assert.Equal(Pbkdf2PasswordHasher.HashSize, first.Hash.Length);
        Assert.False(first.Salt.Span.SequenceEqual(second.Salt.Span));
        Assert.False(first.Hash.Span.SequenceEqual(second.Hash.Span));
        Assert.True(Hasher.Verify("كلمة مرور طويلة وآمنة", first));
        Assert.False(Hasher.Verify("كلمة مرور طويلة وآمنه", first));
        Assert.False(Hasher.Verify(string.Empty, first));
    }

    [Fact]
    public void TamperedOrUnknownVerifiersNeverValidate()
    {
        var stored = Hasher.Hash("correct horse battery staple");
        var tampered = stored.Hash.ToArray();
        tampered[0] ^= 0x01;
        Assert.False(Hasher.Verify("correct horse battery staple",
            new PasswordHash(stored.Algorithm, stored.Iterations, stored.Salt, tampered)));
        Assert.False(Hasher.Verify("correct horse battery staple",
            new PasswordHash(99, stored.Iterations, stored.Salt, stored.Hash)));
        Assert.True(Hasher.NeedsRehash(new PasswordHash(stored.Algorithm, 100_000, stored.Salt, stored.Hash)));
        Assert.False(Hasher.NeedsRehash(stored));
        Assert.Throws<ArgumentException>(static () => new Pbkdf2PasswordHasher(
            AuthenticationSettings.Default with { PasswordHashIterations = 1_000 }));
    }

    [Fact]
    public void RefreshTokensAreHighEntropyAndOnlyTheirHashIsComparable()
    {
        var (token, hash) = RefreshTokenCodec.Generate();
        var (other, otherHash) = RefreshTokenCodec.Generate();
        Assert.Equal(RefreshTokenCodec.EncodedLength, token.Length);
        Assert.NotEqual(token, other);
        Assert.Equal(32, hash.Length);
        Assert.False(hash.AsSpan().SequenceEqual(otherHash));
        Assert.True(RefreshTokenCodec.TryHash(token)!.AsSpan().SequenceEqual(hash));
        Assert.Null(RefreshTokenCodec.TryHash(null));
        Assert.Null(RefreshTokenCodec.TryHash(token[..42]));
        Assert.Null(RefreshTokenCodec.TryHash(token[..42] + "*"));
        Assert.Null(RefreshTokenCodec.TryHash(new string('=', 43)));
    }
}
