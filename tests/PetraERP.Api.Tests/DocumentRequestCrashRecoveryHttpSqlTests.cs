using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.CompanyOptions;
using PetraERP.Contracts.DocumentProcessing;
using PetraERP.Contracts.Sales;

namespace PetraERP.Api.Tests;

/// <summary>
/// P01 crash-state acceptance on real SQL Server: a key bound durably but whose document never committed (crash between
/// "key accepted" and "document committed") must complete exactly once on an identical retry and must conflict on any
/// different payload, creating nothing.
/// </summary>
public sealed partial class LedgerManagementHttpSqlTests
{
    private static Guid DerivedSalesDocumentId(Guid companyId, Guid branchId, string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"PetraERP.SaveAndProcess.create:SalesInvoice:{companyId:N}:{branchId:N}:{key.Trim()}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static async Task SimulateBindWithoutDocumentAsync(ApiSqlHost host, string sourceKey, string orphanKey)
    {
        using var annual = host.AnnualContext();
        var source = await annual.Database.SqlQuery<string>(
            $"SELECT [RequestHashHex] AS [Value] FROM [audit].[DocumentRequestBindings] WHERE [IdempotencyKey]={sourceKey}")
            .SingleAsync(Cancellation).ConfigureAwait(true);
        var actor = await annual.Database.SqlQuery<Guid>(
            $"SELECT [ActorUserId] AS [Value] FROM [audit].[DocumentRequestBindings] WHERE [IdempotencyKey]={sourceKey}")
            .SingleAsync(Cancellation).ConfigureAwait(true);
        var documentId = DerivedSalesDocumentId(host.CompanyId, host.BranchId, orphanKey);
        var now = DateTimeOffset.UtcNow;
        await annual.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO [audit].[DocumentRequestBindings]
            ([CompanyId],[BranchId],[DocumentKind],[IdempotencyKey],[DocumentId],[ActorUserId],[RequestHashHex],[RegisteredAtUtc])
            VALUES ({host.CompanyId},{host.BranchId},'SalesInvoice',{orphanKey},{documentId},{actor},{source},{now})", Cancellation).ConfigureAwait(true);
    }

    [Theory(Explicit = true)]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "SqlServer")]
    public async Task KeyBoundBeforeCrashCompletesOnceOnIdenticalRetryAndConflictsOnChangedRequest(bool changedRetry)
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var client = host.Factory.CreateClient();
            var token = (await ApiSqlHost.LoginAsync(client, ApiSqlHost.AdminUser, ApiSqlHost.AdminPassword).ConfigureAwait(true)).AccessToken;
            var fixture = await PrepareNumberedSalesAsync(host, client, token).ConfigureAwait(true);
            var root = $"/api/v1/companies/{host.CompanyId}/branches/{host.BranchId}";
            await SetPostingModeAsync(host, client, token, root, DocumentPostingMode.ApproveAndPost).ConfigureAwait(true);
            var original = DuplicatedItemSale(fixture);
            var sourceKey = $"crash-src-{Guid.NewGuid():N}";
            var first = await SendAsync<DocumentProcessingResultDto>(host, client, HttpMethod.Post, root + "/sales/invoices/save-and-process", token,
                new SaveAndProcessSalesInvoiceRequest { Document = original, IdempotencyKey = sourceKey }).ConfigureAwait(true);
            Assert.True(first.Status == HttpStatusCode.Created, $"HTTP {(int)first.Status} / {first.Code}");
            var before = await CountsAsync(host).ConfigureAwait(true);

            var orphanKey = $"crash-orphan-{Guid.NewGuid():N}";
            await SimulateBindWithoutDocumentAsync(host, sourceKey, orphanKey).ConfigureAwait(true);
            Assert.Equal(before, await CountsAsync(host).ConfigureAwait(true));

            var retryDocument = changedRetry ? Mutate(original, "one-cent-price") : original;
            var retry = await SendAsync<DocumentProcessingResultDto>(host, client, HttpMethod.Post, root + "/sales/invoices/save-and-process", token,
                new SaveAndProcessSalesInvoiceRequest { Document = retryDocument, IdempotencyKey = orphanKey }).ConfigureAwait(true);

            if (changedRetry)
            {
                Assert.False(retry.Status is HttpStatusCode.Created or HttpStatusCode.OK, $"changed retry accepted ({(int)retry.Status}).");
                Assert.Equal(RequestKeyConflict, retry.Code);
                Assert.Equal(before, await CountsAsync(host).ConfigureAwait(true));
            }
            else
            {
                Assert.True(retry.Status is HttpStatusCode.Created or HttpStatusCode.OK, $"HTTP {(int)retry.Status} / {retry.Code}");
                Assert.Equal(DerivedSalesDocumentId(host.CompanyId, host.BranchId, orphanKey), retry.Body!.DocumentId);
                Assert.Equal("Posted", retry.Body.Outcome);
                Assert.Equal((before.Invoices + 1, before.Journals + 1), await CountsAsync(host).ConfigureAwait(true));
                var again = await SendAsync<DocumentProcessingResultDto>(host, client, HttpMethod.Post, root + "/sales/invoices/save-and-process", token,
                    new SaveAndProcessSalesInvoiceRequest { Document = original, IdempotencyKey = orphanKey }).ConfigureAwait(true);
                Assert.Equal(retry.Body.DocumentId, again.Body!.DocumentId);
                Assert.Equal((before.Invoices + 1, before.Journals + 1), await CountsAsync(host).ConfigureAwait(true));
            }
        }
    }
}
