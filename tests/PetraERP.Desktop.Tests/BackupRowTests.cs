using PetraERP.ApiClient;
using PetraERP.Desktop.Accounting;

namespace PetraERP.Desktop.Tests;

public sealed class BackupRowTests
{
    [Fact]
    public void ABackupRowShowsAShortChecksumAndWhetherTheBackupWasVerified()
    {
        var created = new DateTimeOffset(2026, 10, 31, 8, 0, 0, TimeSpan.Zero);
        var unverified = BackupRow.From(new BackupCatalogItemResponse
        {
            Id = Guid.NewGuid(), DatabaseName = "Petra", BackupType = "Full", BackupFilePath = @"D:\Backups\Petra.bak",
            ChecksumSha256 = new string('A', 64), CreatedAtUtc = created,
            IsCompressed = true,
        }, "غير مفحوصة", "مضغوطة", "محمية");
        Assert.Equal("مضغوطة", unverified.Protection);
        Assert.False(unverified.IsEncrypted);
        Assert.False(unverified.IsVerified);
        Assert.Equal("غير مفحوصة", unverified.Verified);
        Assert.Equal(17, unverified.Checksum.Length);

        var verified = BackupRow.From(new BackupCatalogItemResponse
        {
            Id = Guid.NewGuid(), DatabaseName = "Petra", BackupType = "Full", BackupFilePath = "x", ChecksumSha256 = "abc",
            CreatedAtUtc = created, VerifiedAtUtc = created.AddMinutes(5), IsCompressed = true, IsEncrypted = true,
        }, "—", "مضغوطة", "محمية");
        Assert.Equal("محمية", verified.Protection);
        Assert.True(verified.IsEncrypted);
        Assert.True(verified.IsVerified);
        Assert.Equal("abc", verified.Checksum);
    }
}
