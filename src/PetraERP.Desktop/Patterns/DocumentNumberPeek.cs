using PetraERP.ApiClient;

namespace PetraERP.Desktop.Patterns;

/// <summary>
/// Reads the number a draft would most likely receive without reserving it, so closing a document without saving
/// leaves the sequence untouched. Answers superseded by a newer pattern or date, or by a cancelled context, are
/// dropped instead of reaching the screen; the authoritative number is always the one the server allocates at save.
/// </summary>
public sealed class DocumentNumberPeek(IPatternsClient client)
{
    private int generation;

    public async Task<string?> ReadAsync(Guid companyId, Guid branchId, Guid patternId, Guid? revisionId,
        DateOnly? accountingDate, CancellationToken cancellationToken)
    {
        var attempt = Interlocked.Increment(ref generation);
        try
        {
            var preview = await client.PreviewNextNumberAsync(companyId, branchId, patternId, null, accountingDate, revisionId,
                cancellationToken).ConfigureAwait(true);
            if (attempt != Volatile.Read(ref generation) || cancellationToken.IsCancellationRequested
                || preview.IsEstimate != true || string.IsNullOrWhiteSpace(preview.FormattedNumber))
            {
                return null;
            }

            return preview.FormattedNumber;
        }
        catch (OperationCanceledException) { return null; }
        catch (PetraApiException) { return null; }
    }
}
