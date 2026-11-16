namespace PetraERP.Application.Security;

/// <summary>Slow salted password hashing with constant-time verification.</summary>
public interface IPasswordHasher
{
    PasswordHash Hash(string password);

    bool Verify(string password, PasswordHash stored);

    /// <summary>Spends comparable work for an unknown login name to reduce timing-based enumeration.</summary>
    void SimulateVerification(string password);

    /// <summary>True when the stored verifier is weaker than the current policy and should be re-derived.</summary>
    bool NeedsRehash(PasswordHash stored);
}

/// <summary>Stored password verifier material. Contains no recoverable password.</summary>
public sealed class PasswordHash
{
    public PasswordHash(byte algorithm, int iterations, ReadOnlyMemory<byte> salt, ReadOnlyMemory<byte> hash)
    {
        if (salt.IsEmpty || hash.IsEmpty)
        {
            throw new ArgumentException("Password verifier material is required.", nameof(salt));
        }

        Algorithm = algorithm;
        Iterations = iterations;
        Salt = salt;
        Hash = hash;
    }

    public byte Algorithm { get; }
    public int Iterations { get; }

    public ReadOnlyMemory<byte> Salt { get; }
    public ReadOnlyMemory<byte> Hash { get; }

    public override string ToString() => "PasswordHash";
}
