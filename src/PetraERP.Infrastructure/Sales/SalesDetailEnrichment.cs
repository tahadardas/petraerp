namespace PetraERP.Infrastructure.Sales;

/// <summary>
/// The invoice detail carries read-only enrichment (what is settled, returned and outstanding, and the credit exposure snapshot) that costs
/// about eight queries each time a detail is built. A save-and-process request builds a detail after create and after approve only to
/// pass the document to the next step, which reads its status and version; the client sees the final one. Inside this scope those
/// intermediate details skip the enrichment. Anything that would be returned to a caller must re-read the document outside the scope.
/// </summary>
internal static class SalesDetailEnrichment
{
    private static readonly AsyncLocal<int> Depth = new();

    internal static bool Suppressed => Depth.Value > 0;

    internal static IDisposable Suppress()
    {
        Depth.Value++;
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Depth.Value--;
        }
    }
}
