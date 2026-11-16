using System.Security.Cryptography;
using PetraERP.Application.Security;

namespace PetraERP.Infrastructure.Security;

/// <summary>
/// PBKDF2-HMAC-SHA512 (OWASP 2023 minimum 210,000 iterations) with a 256-bit random salt and a
/// 512-bit derived key, verified in constant time. The iteration count is stored per user so it
/// can be raised later; weaker stored hashes are upgraded on the next successful login.
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    public const byte AlgorithmPbkdf2Sha512 = 1;
    public const int SaltSize = 32;
    public const int HashSize = 64;

    private readonly int iterations;
    private readonly byte[] simulationSalt = RandomNumberGenerator.GetBytes(SaltSize);

    public Pbkdf2PasswordHasher(AuthenticationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.PasswordHashIterations < AuthenticationSettings.MinimumPasswordIterations)
        {
            throw new ArgumentException("Password hashing iterations are below the supported minimum.", nameof(settings));
        }

        iterations = settings.PasswordHashIterations;
    }

    public int Iterations => iterations;

    public PasswordHash Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password.AsSpan(), salt, iterations, HashAlgorithmName.SHA512, HashSize);
        return new PasswordHash(AlgorithmPbkdf2Sha512, iterations, salt, hash);
    }

    public bool Verify(string password, PasswordHash stored)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(stored);
        if (stored.Algorithm != AlgorithmPbkdf2Sha512 || stored.Iterations < 1
            || stored.Salt.Length != SaltSize || stored.Hash.Length != HashSize)
        {
            return false;
        }

        var computed = Rfc2898DeriveBytes.Pbkdf2(password.AsSpan(), stored.Salt.Span, stored.Iterations,
            HashAlgorithmName.SHA512, HashSize);
        try
        {
            return CryptographicOperations.FixedTimeEquals(computed, stored.Hash.Span);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(computed);
        }
    }

    public void SimulateVerification(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var computed = Rfc2898DeriveBytes.Pbkdf2(password.AsSpan(), simulationSalt, iterations, HashAlgorithmName.SHA512, HashSize);
        CryptographicOperations.ZeroMemory(computed);
    }

    /// <summary>True when the stored verifier should be re-derived with the current policy.</summary>
    public bool NeedsRehash(PasswordHash stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        return stored.Algorithm != AlgorithmPbkdf2Sha512 || stored.Iterations < iterations;
    }
}
