using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using PetraERP.Application.Common;

namespace PetraERP.Infrastructure.Administration;

/// <summary>
/// Compression and optional password protection of a database backup file.
/// <list type="bullet">
/// <item>Plain: the .bak is gzip-compressed (<c>.bak.gz</c>).</item>
/// <item>Protected: the gzip stream is encrypted (<c>.pbk</c>) with AES-256-GCM in 1 MiB chunks. The key comes from the password with
/// PBKDF2-SHA256 (310 000 iterations, random salt). Every chunk is authenticated together with the header and a final-chunk flag, so a wrong
/// password, a changed byte, a cut-off file or appended data are all refused before any database restore begins.</item>
/// </list>
/// The password is never stored or logged; without it a protected archive cannot be turned back into a backup.
/// </summary>
internal static class BackupArchive
{
    public const string CompressedExtension = ".bak.gz";
    public const string ProtectedExtension = ".pbk";
    public const int MinPasswordLength = 8;

    private static readonly byte[] Magic = "PBK1"u8.ToArray();
    private const byte Version = 1;
    private const int ChunkSize = 1 << 20;
    private const int SaltSize = 16, PrefixSize = 4, TagSize = 16, KeySize = 32;
    private const int Iterations = 310_000, MinIterations = 100_000, MaxIterations = 2_000_000;
    private const int HeaderSize = 4 + 1 + 4 + SaltSize + PrefixSize;

    public static bool IsProtectedPath(string path) => path.EndsWith(ProtectedExtension, StringComparison.OrdinalIgnoreCase);
    public static bool IsCompressedPath(string path) => IsProtectedPath(path) || path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);

    /// <summary>Compresses <paramref name="sourcePath"/> into <paramref name="destinationPath"/>; protected with the password when one is given.</summary>
    public static async Task PackAsync(string sourcePath, string destinationPath, string? password, CancellationToken cancellationToken)
    {
        var protectedArchive = !string.IsNullOrEmpty(password);
        var gzipPath = protectedArchive ? destinationPath + ".gz.tmp" : destinationPath;
        try
        {
            using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
            using (var target = new FileStream(gzipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            using (var gzip = new GZipStream(target, CompressionLevel.Optimal))
                await source.CopyToAsync(gzip, cancellationToken).ConfigureAwait(false);
            if (protectedArchive) await EncryptAsync(gzipPath, destinationPath, password!, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            TryDelete(destinationPath);
            throw;
        }
        finally
        {
            if (protectedArchive) TryDelete(gzipPath);
        }
    }

    /// <summary>
    /// Turns an archive back into a plain .bak next to it (SQL Server must be able to read it). A legacy uncompressed .bak is returned as is.
    /// Dispose the result to delete the temporary file.
    /// </summary>
    public static async Task<UnpackedBackup> UnpackAsync(string archivePath, string? password, CancellationToken cancellationToken)
    {
        if (!IsCompressedPath(archivePath) && !LooksLikeArchive(archivePath)) return new UnpackedBackup(archivePath, false);
        var bakPath = archivePath + $".restore-{Guid.NewGuid():N}.bak";
        var gzipPath = bakPath + ".gz";
        try
        {
            var protectedArchive = HasMagic(archivePath);
            var gzipSource = archivePath;
            if (protectedArchive)
            {
                if (string.IsNullOrEmpty(password))
                    throw new BusinessRuleException("backup.password_required", "This backup is password protected; enter its password.");
                await DecryptAsync(archivePath, gzipPath, password, cancellationToken).ConfigureAwait(false);
                gzipSource = gzipPath;
            }

            using (var source = new FileStream(gzipSource, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
            using (var gzip = new GZipStream(source, CompressionMode.Decompress))
            using (var target = new FileStream(bakPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                try { await gzip.CopyToAsync(target, cancellationToken).ConfigureAwait(false); }
                catch (InvalidDataException)
                {
                    throw new BusinessRuleException("backup.archive_corrupt", "The backup archive is damaged.");
                }
            }

            return new UnpackedBackup(bakPath, true);
        }
        catch
        {
            TryDelete(bakPath);
            throw;
        }
        finally
        {
            TryDelete(gzipPath);
        }
    }

    private static bool HasMagic(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> head = stackalloc byte[4];
            return stream.ReadAtLeast(head, 4, false) == 4 && head.SequenceEqual(Magic);
        }
        catch (IOException) { return false; }
    }

    private static bool LooksLikeArchive(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> head = stackalloc byte[4];
            var read = stream.ReadAtLeast(head, 4, false);
            return read >= 2 && head[0] == 0x1F && head[1] == 0x8B || read == 4 && head.SequenceEqual(Magic);
        }
        catch (IOException) { return false; }
    }

    private static byte[] DeriveKey(string password, ReadOnlySpan<byte> salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, KeySize);

    private static async Task EncryptAsync(string gzipPath, string destinationPath, string password, CancellationToken cancellationToken)
    {
        var header = new byte[HeaderSize];
        Magic.CopyTo(header, 0);
        header[4] = Version;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(5, 4), Iterations);
        RandomNumberGenerator.Fill(header.AsSpan(9, SaltSize + PrefixSize));
        var key = DeriveKey(password, header.AsSpan(9, SaltSize), Iterations);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            using var source = new FileStream(gzipPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            using var target = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            await target.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            var plain = new byte[ChunkSize];
            var cipher = new byte[ChunkSize];
            var tag = new byte[TagSize];
            var lengthBytes = new byte[4];
            var nonce = new byte[12];
            header.AsSpan(9 + SaltSize, PrefixSize).CopyTo(nonce);
            for (ulong counter = 0; ; counter++)
            {
                var read = await source.ReadAtLeastAsync(plain.AsMemory(0, ChunkSize), ChunkSize, false, cancellationToken).ConfigureAwait(false);
                var final = read < ChunkSize;
                BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(PrefixSize), counter);
                BinaryPrimitives.WriteInt32BigEndian(lengthBytes, read);
                aes.Encrypt(nonce, plain.AsSpan(0, read), cipher.AsSpan(0, read), tag, AssociatedData(header, final, lengthBytes));
                await target.WriteAsync(lengthBytes, cancellationToken).ConfigureAwait(false);
                await target.WriteAsync(cipher.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                await target.WriteAsync(tag, cancellationToken).ConfigureAwait(false);
                if (final) break;
            }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static async Task DecryptAsync(string archivePath, string gzipPath, string password, CancellationToken cancellationToken)
    {
        using var source = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        var header = new byte[HeaderSize];
        if (await source.ReadAtLeastAsync(header, HeaderSize, false, cancellationToken).ConfigureAwait(false) < HeaderSize
            || !header.AsSpan(0, 4).SequenceEqual(Magic) || header[4] != Version)
            throw new BusinessRuleException("backup.archive_corrupt", "The backup archive is damaged or not supported.");
        var iterations = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(5, 4));
        if (iterations is < MinIterations or > MaxIterations)
            throw new BusinessRuleException("backup.archive_corrupt", "The backup archive is damaged or not supported.");
        var key = DeriveKey(password, header.AsSpan(9, SaltSize), iterations);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            using var target = new FileStream(gzipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            var cipher = new byte[ChunkSize];
            var plain = new byte[ChunkSize];
            var tag = new byte[TagSize];
            var lengthBytes = new byte[4];
            var nonce = new byte[12];
            header.AsSpan(9 + SaltSize, PrefixSize).CopyTo(nonce);
            for (ulong counter = 0; ; counter++)
            {
                if (await source.ReadAtLeastAsync(lengthBytes, 4, false, cancellationToken).ConfigureAwait(false) < 4)
                    throw new BusinessRuleException("backup.archive_corrupt", "The backup archive is incomplete.");
                var length = BinaryPrimitives.ReadInt32BigEndian(lengthBytes);
                if (length is < 0 or > ChunkSize) throw new BusinessRuleException("backup.archive_corrupt", "The backup archive is damaged.");
                if (await source.ReadAtLeastAsync(cipher.AsMemory(0, length), length, false, cancellationToken).ConfigureAwait(false) < length
                    || await source.ReadAtLeastAsync(tag, TagSize, false, cancellationToken).ConfigureAwait(false) < TagSize)
                    throw new BusinessRuleException("backup.archive_corrupt", "The backup archive is incomplete.");
                var final = source.Position == source.Length;
                BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(PrefixSize), counter);
                try { aes.Decrypt(nonce, cipher.AsSpan(0, length), tag, plain.AsSpan(0, length), AssociatedData(header, final, lengthBytes)); }
                catch (CryptographicException)
                {
                    // The first chunk fails for a wrong password; a later failure means the file was changed or cut.
                    throw counter == 0
                        ? new BusinessRuleException("backup.password_invalid", "The password is wrong or the backup archive is damaged.")
                        : new BusinessRuleException("backup.archive_corrupt", "The backup archive was changed or is incomplete.");
                }

                await target.WriteAsync(plain.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
                if (final) break;
            }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static byte[] AssociatedData(byte[] header, bool final, byte[] lengthBytes)
    {
        var data = new byte[header.Length + 1 + lengthBytes.Length];
        header.CopyTo(data, 0);
        data[header.Length] = final ? (byte)1 : (byte)0;
        lengthBytes.CopyTo(data, header.Length + 1);
        return data;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

/// <summary>A backup ready for SQL Server to read; <see cref="Dispose"/> removes it when it is a temporary extraction.</summary>
internal sealed class UnpackedBackup(string path, bool isTemporary) : IDisposable
{
    public string Path { get; } = path;
    public void Dispose()
    {
        if (!isTemporary) return;
        try { if (File.Exists(Path)) File.Delete(Path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
