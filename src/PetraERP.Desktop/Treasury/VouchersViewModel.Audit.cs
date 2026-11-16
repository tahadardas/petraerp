using System.Globalization;

namespace PetraERP.Desktop.Treasury;

/// <summary>
/// Al-Ameen keeps who added, last changed, posted and reversed every voucher. The server returns that trail with the voucher;
/// this only formats it for the voucher window (read-only, local time).
/// </summary>
public sealed partial class VouchersViewModel
{
    public bool HasAudit => Current?.Audit is not null;

    public string AuditText
    {
        get
        {
            if (Current?.Audit is not { } audit) return string.Empty;
            var lines = new List<string>();
            if (audit.CreatedAtUtc is { } created) lines.Add(Format("D_AuditCreated", audit.CreatedBy, created));
            if (audit.UpdatedAtUtc is { } updated && audit.CreatedAtUtc is { } first && updated - first > TimeSpan.FromSeconds(5))
                lines.Add(string.Format(CultureInfo.CurrentCulture, Localization["D_AuditUpdated"], Stamp(updated)));
            if (audit.PostedAtUtc is { } posted) lines.Add(Format("D_AuditPosted", audit.PostedBy, posted));
            if (audit.ReversedAtUtc is { } reversed) lines.Add(Format("D_AuditReversed", audit.ReversedBy, reversed));
            return string.Join(Environment.NewLine, lines);
        }
    }

    private string Format(string key, string? user, DateTimeOffset at) =>
        string.Format(CultureInfo.CurrentCulture, Localization[key], string.IsNullOrWhiteSpace(user) ? "—" : user, Stamp(at));

    private static string Stamp(DateTimeOffset at) => at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
