using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace PetraERP.ApiClient;

public sealed record RegisteredTemplateAccount(string AccountCode, string AccountNameArabic, string? ParentCode, bool PostingAllowed);
public sealed record RegisteredChartTemplate(Guid TemplateId, string TemplateCode, string TemplateNameArabic,
    string TemplateNameEnglish, string CountryCode, int TemplateVersion, string TemplateSource, IReadOnlyList<RegisteredTemplateAccount> Accounts);

public partial interface IPetraApiClient
{
    Task<IReadOnlyList<RegisteredChartTemplate>> GetRegisteredAccountTemplatesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

public partial class PetraApiClient
{
    public async Task<IReadOnlyList<RegisteredChartTemplate>> GetRegisteredAccountTemplatesAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("api/v1/workspaces/account-templates", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RegisteredChartTemplate[]>(new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web), cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The server returned no account template catalog.");
    }
}
