using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PetraERP.Api.Endpoints;
using PetraERP.Api.Security;
using PetraERP.Application.GeneralLedger.Reconciliation;
using PetraERP.Application.Security;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Api.Tests;

public sealed class AccountingReconciliationContractTests
{
    [Fact]
    public async Task JsonExportHttpEndpointReturnsMappedStringAmounts()
    {
        var company = Guid.NewGuid();
        var branch = Guid.NewGuid();
        var year = Guid.NewGuid();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthorization(options => options.AddPolicy(PetraPolicies.Permission(PermissionCatalog.ReportsView), policy => policy.RequireAssertion(_ => true)));
        builder.Services.AddSingleton<IAccountingReconciliationService>(new StubReconciliationService(company, branch, year));
        builder.Services.AddScoped(_ => new AccountingDbContext(
            new DbContextOptionsBuilder<AccountingDbContext>().UseSqlServer(PetraApiFactory.UnreachableConnection).Options));
        builder.Services.AddScoped(services =>
        {
            var scope = new FinancialRequestScope();
            scope.Bind(services.GetRequiredService<AccountingDbContext>(), year);
            return scope;
        });
        await using var app = builder.Build();
        app.UseAuthorization();
        app.MapGroup("/api/v1").MapAccountingReconciliationEndpoints();
        await app.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        using var client = app.GetTestClient();
        using var response = await client.GetAsync($"/api/v1/companies/{company}/branches/{branch}/reconciliation/export/json", TestContext.Current.CancellationToken).ConfigureAwait(true);
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken).ConfigureAwait(true));
        Assert.Equal("1150.0000", document.RootElement.GetProperty("findings")[0].GetProperty("sourceAmount").GetString());
        Assert.Equal(company, document.RootElement.GetProperty("companyId").GetGuid());
    }

    [Fact]
    public void ReportJsonPreservesFinancialValuesAsDecimalStrings()
    {
        var company = Guid.NewGuid();
        var year = Guid.NewGuid();
        var finding = new ReconciliationFindingDto(Guid.NewGuid(), company, year, Guid.NewGuid(), "CHECK_01",
            null, null, null, null, null, "USD", 1150m, 1100m, -50m, "BLOCKING", "فرق محاسبي", null, null);
        var report = new ReconciliationReportDto(company, year, DateTimeOffset.UtcNow, 1, 0, 0, 1, true, [finding]);

        var json = JsonSerializer.Serialize(report.ToResponse(), JsonSerializerOptions.Web);
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement.GetProperty("findings")[0];

        Assert.Equal(JsonValueKind.String, row.GetProperty("sourceAmount").ValueKind);
        Assert.Equal("1150.0000", row.GetProperty("sourceAmount").GetString());
        Assert.Equal("-50.0000", row.GetProperty("difference").GetString());
        Assert.Equal("فرق محاسبي", row.GetProperty("explanation").GetString());
    }

    private sealed class StubReconciliationService(Guid company, Guid branch, Guid year) : IAccountingReconciliationService
    {
        public Task<ReconciliationReportDto> RunAllReconciliationsAsync(Guid companyId, Guid fiscalYearId, Guid? branchId, CancellationToken cancellationToken = default)
        {
            Assert.Equal(company, companyId);
            Assert.Equal(year, fiscalYearId);
            Assert.Equal(branch, branchId);
            var row = new ReconciliationFindingDto(Guid.NewGuid(), company, year, branch, "CHECK_01",
                null, null, null, null, null, "USD", 1150m, 1100m, -50m, "BLOCKING", "فرق محاسبي", null, null);
            return Task.FromResult(new ReconciliationReportDto(company, year, DateTimeOffset.UtcNow, 1, 0, 0, 1, true, [row]));
        }

        public Task<string> ExportReportToJsonAsync(Guid companyId, Guid fiscalYearId, Guid? branchId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The endpoint must use the mapped report contract.");

        public Task<string> ExportReportToCsvAsync(Guid companyId, Guid fiscalYearId, Guid? branchId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("CSV is outside this test.");
    }
}
