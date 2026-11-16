using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PetraERP.ApiClient;

namespace PetraERP.Desktop.Accounting;

public sealed partial class ChartOfAccountsViewModel
{
    [ObservableProperty] private string importTemplateCode="USER-CHART";
    [ObservableProperty] private int importTemplateVersion=1;
    [ObservableProperty] private string importTemplateSource="User supplied; not verified as an official national standard";
    [ObservableProperty] private string importValidationReport=string.Empty;
    private ChartFilePreview? templatePreview;
    private bool CanPreviewTemplate => CanManage && Idle && !IsEditorOpen;
    private bool CanImportTemplate => CanPreviewTemplate && templatePreview?.CanImport==true;
    partial void OnImportTemplateCodeChanged(string value)=>ClearTemplatePreview();
    partial void OnImportTemplateVersionChanged(int value)=>ClearTemplatePreview();
    partial void OnImportTemplateSourceChanged(string value)=>ClearTemplatePreview();
    private void ClearTemplatePreview()
    {
        templatePreview=null;ImportValidationReport=string.Empty;
        ImportTemplateCommand.NotifyCanExecuteChanged();
    }
    [RelayCommand(CanExecute=nameof(CanPreviewTemplate))]
    private async Task PreviewTemplateFileAsync()
    {
        var picker=new OpenFileDialog { Filter="Chart files (*.csv;*.xlsx)|*.csv;*.xlsx",CheckFileExists=true,Multiselect=false };
        if(picker.ShowDialog()!=true) return;
        await RunAsync(async token=>
        {
            using var file=File.OpenRead(picker.FileName);
            var id=new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("PetraERP.UserChart:"+ImportTemplateCode.Trim().ToUpperInvariant()))[..16]);
            var metadata=JsonSerializer.Serialize(new {TemplateId=id,TemplateCode=ImportTemplateCode.Trim(),TemplateNameArabic="دليل حسابات مستورد",TemplateNameEnglish="Imported chart",CountryCode="ZZ",TemplateVersion=ImportTemplateVersion,TemplateSource=ImportTemplateSource.Trim(),Accounts=Array.Empty<object>(),DefaultAccountRoles=new Dictionary<string,string>()});
            templatePreview=await client.PreviewTemplateFileAsync(CompanyId,BranchId,picker.FileName,file,metadata,token).ConfigureAwait(true);
            ImportValidationReport=templatePreview.CanImport
                ? "المعاينة صالحة؛ لم يتم إنشاء حسابات أو أرصدة بعد."+Environment.NewLine+templatePreview.Template.GetRawText()
                : string.Join(Environment.NewLine,templatePreview.Errors);
            ImportTemplateCommand.NotifyCanExecuteChanged();
        }).ConfigureAwait(true);
    }
    [RelayCommand(CanExecute=nameof(CanImportTemplate))]
    private async Task ImportTemplateAsync()
    {
        if(templatePreview is not {CanImport:true} preview) return;
        await RunAsync(async token=>
        {
            var result=await client.ImportTemplateAsync(CompanyId,BranchId,preview.Template,token).ConfigureAwait(true);
            ImportValidationReport=$"الحسابات المستوردة: {result.AccountsImported}; مسبقاً: {result.AlreadyImported}";
            templatePreview=null;ImportTemplateCommand.NotifyCanExecuteChanged();
            await ReloadAsync(null,token).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }
}
