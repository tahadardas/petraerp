using System.Net.Http.Json;
using System.Text.Json;

namespace PetraERP.ApiClient;

public sealed record ChartFilePreview(JsonElement Template, IReadOnlyList<string> Errors, bool CanImport);
public sealed record ChartFileImportResult(int AccountsImported, bool AlreadyImported, string ContentHash);

public partial interface IPetraApiClient
{
    Task<ChartFilePreview> PreviewChartFileAsync(Guid companyId,Guid branchId,string fileName,Stream content,string metadata,CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    Task<ChartFileImportResult> CommitChartFileAsync(Guid companyId,Guid branchId,JsonElement chartTemplate,CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}
public partial class PetraApiClient
{
    public async Task<ChartFilePreview> PreviewChartFileAsync(Guid companyId,Guid branchId,string fileName,Stream content,string metadata,CancellationToken cancellationToken = default)
    {
        using var form=new MultipartFormDataContent();
        form.Add(new StreamContent(content),"file",Path.GetFileName(fileName));
        form.Add(new StringContent(metadata,System.Text.Encoding.UTF8),"metadata");
        using var response=await _httpClient.PostAsync($"api/v1/companies/{companyId}/branches/{branchId}/ledger/chart/templates/file-preview",form,cancellationToken).ConfigureAwait(false);
        return await ReadPhaseTwoResponseAsync<ChartFilePreview>(response,cancellationToken).ConfigureAwait(false);
    }
    public async Task<ChartFileImportResult> CommitChartFileAsync(Guid companyId,Guid branchId,JsonElement chartTemplate,CancellationToken cancellationToken = default)
    {
        using var response=await _httpClient.PostAsJsonAsync($"api/v1/companies/{companyId}/branches/{branchId}/ledger/chart/templates/import",chartTemplate,cancellationToken).ConfigureAwait(false);
        return await ReadPhaseTwoResponseAsync<ChartFileImportResult>(response,cancellationToken).ConfigureAwait(false);
    }
    private static async Task<T> ReadPhaseTwoResponseAsync<T>(HttpResponseMessage response,CancellationToken cancellationToken)
    {
        if(!response.IsSuccessStatusCode)
        {
            var content=await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {content}");
        }
        var obj=await response.Content.ReadFromJsonAsync<T>(cancellationToken:cancellationToken).ConfigureAwait(false);
        return obj ?? throw new InvalidOperationException("Empty server response.");
    }
}
