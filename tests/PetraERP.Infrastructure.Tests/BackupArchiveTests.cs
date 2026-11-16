using System.IO;
using System.Security.Cryptography;
using PetraERP.Application.Common;
using PetraERP.Infrastructure.Administration;

namespace PetraERP.Infrastructure.Tests;

public sealed class BackupArchiveTests : IDisposable
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;
    private readonly string directory = Path.Combine(Path.GetTempPath(), "petra-archive-" + Guid.NewGuid().ToString("N"));

    public BackupArchiveTests() => Directory.CreateDirectory(directory);
    public void Dispose() { try { Directory.Delete(directory, true); } catch (IOException) { } }

    private string Source(int size)
    {
        var path = Path.Combine(directory, "source.bak");
        var data = new byte[size];
        // Half random, half repetitive: spans several 1 MiB encryption chunks and still compresses.
        RandomNumberGenerator.Fill(data.AsSpan(0, size / 2));
        File.WriteAllBytes(path, data);
        return path;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1 << 20)]
    [InlineData((1 << 20) * 5 / 2)]
    public async Task APlainArchiveIsCompressedAndRestoresIdentically(int size)
    {
        var source = Source(size);
        var archive = Path.Combine(directory, "plain.bak.gz");
        await BackupArchive.PackAsync(source, archive, null, Cancellation);
        Assert.True(BackupArchive.IsCompressedPath(archive));
        Assert.False(BackupArchive.IsProtectedPath(archive));
        using var unpacked = await BackupArchive.UnpackAsync(archive, null, Cancellation);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(unpacked.Path));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(1 << 20)]
    [InlineData((1 << 20) * 5 / 2)]
    public async Task AProtectedArchiveNeedsItsPasswordAndRestoresIdentically(int size)
    {
        var source = Source(size);
        var archive = Path.Combine(directory, "secret.pbk");
        await BackupArchive.PackAsync(source, archive, "pa55-كلمة-سر", Cancellation);
        Assert.True(BackupArchive.IsProtectedPath(archive));

        var missing = await Assert.ThrowsAsync<BusinessRuleException>(() => BackupArchive.UnpackAsync(archive, null, Cancellation));
        Assert.Equal("backup.password_required", missing.Code);
        var wrong = await Assert.ThrowsAsync<BusinessRuleException>(() => BackupArchive.UnpackAsync(archive, "another-password", Cancellation));
        Assert.Equal("backup.password_invalid", wrong.Code);
        Assert.Empty(Directory.GetFiles(directory, "*.restore-*"));

        using var unpacked = await BackupArchive.UnpackAsync(archive, "pa55-كلمة-سر", Cancellation);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(unpacked.Path));
    }

    [Fact]
    public async Task ThePasswordProtectedFileDoesNotContainTheBackupInTheClear()
    {
        var source = Path.Combine(directory, "marker.bak");
        File.WriteAllBytes(source, System.Text.Encoding.ASCII.GetBytes(new string('A', 4096) + "VERY-SECRET-LEDGER-ROW"));
        var archive = Path.Combine(directory, "marker.pbk");
        await BackupArchive.PackAsync(source, archive, "password-123", Cancellation);
        Assert.DoesNotContain("VERY-SECRET", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(archive)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AChangedCutOrExtendedProtectedArchiveIsRefused()
    {
        var source = Source((1 << 20) * 5 / 2);
        var archive = Path.Combine(directory, "tamper.pbk");
        await BackupArchive.PackAsync(source, archive, "password-123", Cancellation);
        var original = File.ReadAllBytes(archive);

        async Task AssertRefusedAsync(byte[] bytes)
        {
            var file = Path.Combine(directory, "variant.pbk");
            File.WriteAllBytes(file, bytes);
            var failure = await Assert.ThrowsAsync<BusinessRuleException>(() => BackupArchive.UnpackAsync(file, "password-123", Cancellation)).ConfigureAwait(true);
            Assert.StartsWith("backup.", failure.Code, StringComparison.Ordinal);
        }

        var flipped = (byte[])original.Clone();
        flipped[flipped.Length / 2] ^= 0xFF;
        await AssertRefusedAsync(flipped);
        await AssertRefusedAsync(original[..^1]);
        await AssertRefusedAsync(original[..(original.Length - (1 << 20))]);
        await AssertRefusedAsync([.. original, 0]);
    }
}
