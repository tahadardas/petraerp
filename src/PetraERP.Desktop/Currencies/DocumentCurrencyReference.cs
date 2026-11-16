using PetraERP.ApiClient;

namespace PetraERP.Desktop.Currencies;

/// <summary>
/// The rate the company register holds for one currency on one date. <see cref="Found"/> false is an answer, not a
/// failure: no rate exists, so the document must carry one the operator supplies.
/// </summary>
public sealed record CurrencyRateReading(bool Found, string BaseCurrencyCode, string? Rate, DateOnly? EffectiveDate, string? Source);

/// <summary>
/// Reads what the server already knows about a company's currencies so a document offers the enabled codes and fills the
/// rate from the register instead of inventing either. An unreadable answer is null, which the caller reports rather than
/// papering over with a guessed currency or a default rate of one.
/// </summary>
public sealed class DocumentCurrencyReference(ICurrenciesClient? client)
{
    public bool IsAvailable => client is not null;

    /// <summary>The company currencies a document may use, base currency first, or null when the settings could not be read.</summary>
    public async Task<CompanyCurrencySettingsResponse?> ReadSettingsAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken)
    {
        if (client is null) return null;
        try
        {
            return await client.GetSettingsAsync(companyId, branchId, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { return null; }
        catch (PetraApiException) { return null; }
    }

    public async Task<CurrencyRateReading?> ReadRateAsync(Guid companyId, Guid branchId, string currencyCode, DateOnly date, CancellationToken cancellationToken)
    {
        if (client is null) return null;
        try
        {
            var effective = await client.GetEffectiveRateAsync(companyId, branchId, currencyCode, date, cancellationToken).ConfigureAwait(true);
            return effective.Found != true
                ? new CurrencyRateReading(false, effective.BaseCurrencyCode ?? string.Empty, null, null, null)
                : new CurrencyRateReading(true, effective.BaseCurrencyCode ?? string.Empty, effective.Rate, effective.EffectiveDate, effective.Source);
        }
        catch (OperationCanceledException) { return null; }
        catch (PetraApiException) { return null; }
    }
}
