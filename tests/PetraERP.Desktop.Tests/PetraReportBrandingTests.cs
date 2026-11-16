using System.IO;
using System.Threading;
using System.Windows.Threading;
using PetraERP.Desktop.Reporting;
using PetraERP.Reporting.OpenSource;

namespace PetraERP.Desktop.Tests;

[Collection(WpfApplicationGroup.Name)]
public sealed class PetraReportBrandingTests
{
    [Fact]
    public async Task BrandingStoreSavesAndRestoresPerCompanyConfiguration()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "petra_branding_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PetraReportBrandingStore(tempDir);
            var companyId = Guid.NewGuid();

            var branding = new PetraReportBranding
            {
                CompanyId = companyId,
                CompanyNameArabic = "شركة بترا للصناعات",
                CompanyNameEnglish = "Petra Industries Co.",
                TaxNumber = "123456789",
                CommercialRegistration = "987654",
                Address = "دمشق - الميدان",
                Phone = "+963 11 1234567",
                ShowSignatures = true,
                PreparedByTitle = "المحاسب الأول",
                ReviewedByTitle = "مدير التدقيق",
                ApprovedByTitle = "المدير العام",
            };

            await store.SaveAsync(branding, TestContext.Current.CancellationToken);
            var loaded = await store.LoadAsync(companyId, "Fallback Name", TestContext.Current.CancellationToken);

            Assert.Equal("شركة بترا للصناعات", loaded.CompanyNameArabic);
            Assert.Equal("Petra Industries Co.", loaded.CompanyNameEnglish);
            Assert.Equal("123456789", loaded.TaxNumber);
            Assert.Equal("987654", loaded.CommercialRegistration);
            Assert.True(loaded.ShowSignatures);
            Assert.Equal("المحاسب الأول", loaded.PreparedByTitle);
            Assert.Equal("مدير التدقيق", loaded.ReviewedByTitle);
            Assert.Equal("المدير العام", loaded.ApprovedByTitle);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PageBuilderWithBrandingRendersDocumentSuccessfully()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    var document = CreateDocument();
                    var companyId = Guid.NewGuid();
                    var branding = new PetraReportBranding
                    {
                        CompanyId = companyId,
                        CompanyNameArabic = "شركة الفجر للتجارة",
                        ShowSignatures = true,
                        ShowTaxNumber = true,
                        TaxNumber = "TAX-9988",
                        CommercialRegistration = "CR-1122",
                    };

                    var fixedDoc = await ProfessionalReportPageBuilder.BuildAsync(document, true, CancellationToken.None, branding).ConfigureAwait(true);
                    Assert.NotNull(fixedDoc);
                    Assert.True(fixedDoc.DocumentPaginator.PageCount >= 1);
                    completion.TrySetResult();
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
                finally
                {
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            }));
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    private static ReportDocument CreateDocument() => new(
        "Ledger", "دفتر الأستاذ", "شركة الفجر للتجارة", "2026", "SYP",
        [
            new ReportColumn("Date", "التاريخ", ReportValueKind.Date),
            new ReportColumn("Description", "البيان"),
            new ReportColumn("Debit", "مدين", ReportValueKind.Amount, 2),
            new ReportColumn("Credit", "دائن", ReportValueKind.Amount, 2),
        ],
        [
            new Dictionary<string, object?> { ["Date"] = "2026-01-01", ["Description"] = "حركة افتتاحية", ["Debit"] = 50000m, ["Credit"] = 0m },
        ],
        summaries: [new ReportSummaryValue("Closing", "الرصيد الفعلي", 50000m, "SYP")]);
}
