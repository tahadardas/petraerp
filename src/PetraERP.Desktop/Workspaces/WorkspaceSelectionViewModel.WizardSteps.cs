using System.Globalization;
using PetraERP.ApiClient;

namespace PetraERP.Desktop.Workspaces;

public sealed partial class WorkspaceSelectionViewModel
{
    /// <summary>Validates the current step and explains the first problem; the server validates everything again.</summary>
    private bool StepIsValid()
    {
        var error = Wizard switch
        {
            WorkspaceWizardKind.Company => WizardStep switch
            {
                0 => CompanyInfoError(),
                1 => System.Text.RegularExpressions.Regex.IsMatch(NewDatabaseName, "^[A-Za-z][A-Za-z0-9_]{0,127}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant) ? null : Text("اسم قاعدة SQL غير صالح.", "Invalid SQL database name."),
                3 => string.IsNullOrEmpty(AdministratorPassword) ? Text("أكد كلمة مرور المدير الحالي.", "Confirm the current administrator password.") : null,
                4 => SelectedAccountTemplate is null ? Text("اختر قالباً مسجلاً.", "Choose a registered template.") : null,
                2 => NewCompanyCurrency is null ? Text("اختر العملة الأساسية من القائمة.", "Choose the base currency from the list.") : null,
                5 => CompanyYearError(),
                _ => null,
            },
            WorkspaceWizardKind.FiscalYear => WizardStep == 0 ? YearInfoError() : null,
            _ => null,
        };
        StepError = error ?? string.Empty;
        return error is null;
    }

    private string? CompanyInfoError()
    {
        var code = NewCompanyCode.Trim();
        if (code.Length is < 2 or > 16 || !code.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            return Text("الرمز من 2 إلى 16 حرفاً لاتينياً أو رقماً (مثل DARDAS).", "Code: 2–16 Latin letters or digits (for example DARDAS).");
        if (AllWorkspaces.Any(w => string.Equals(w.CompanyCode, code, StringComparison.OrdinalIgnoreCase)))
            return Text("يوجد ملف شركة بهذا الرمز.", "A company file with this code already exists.");
        if (string.IsNullOrWhiteSpace(NewCompanyNameArabic)) return Text("الاسم العربي مطلوب.", "The Arabic name is required.");
        return null;
    }

    private string? CompanyYearError()
    {
        if (NewCompanyYear is < 2000 or > 2100) return Text("سنة غير صالحة.", "Invalid year.");
        if (NewCompanyStart is not { } start || NewCompanyEnd is not { } end || end < start)
            return Text("تاريخ النهاية يجب أن يكون بعد تاريخ البداية.", "The end date must be after the start date.");
        if (end > start.AddYears(1).AddDays(-1)) return Text("السنة المالية لا تتجاوز 12 شهراً.", "A fiscal year cannot exceed 12 months.");
        return null;
    }

    private string? YearInfoError()
    {
        if (SelectedWorkspace is null) return Text("اختر ملف الشركة أولاً.", "Select the company file first.");
        if (NewYearNumber <= SelectedWorkspace.YearNumber) return Text("السنة الجديدة يجب أن تكون بعد السنة الحالية.", "The new year must follow the current one.");
        if (AllWorkspaces.Any(w => w.CompanyId == SelectedWorkspace.CompanyId && w.YearNumber == NewYearNumber))
            return Text("هذه السنة موجودة لهذه الشركة.", "This year already exists for the company.");
        return string.IsNullOrWhiteSpace(NewYearCode) ? Text("رمز السنة مطلوب.", "The year code is required.") : null;
    }

    private string CompanyReview() => string.Join(Environment.NewLine, new[]
    {
        Text("الرمز: ", "Code: ") + NewCompanyCode.Trim().ToUpperInvariant(),
        Text("الاسم العربي: ", "Arabic name: ") + NewCompanyNameArabic.Trim(),
        Text("الاسم الإنجليزي: ", "English name: ") + (string.IsNullOrWhiteSpace(NewCompanyNameEnglish) ? "—" : NewCompanyNameEnglish.Trim()),
        Text("العملة الأساسية: ", "Base currency: ") + (NewCompanyCurrency is { } c ? c.Code + " · " + Text(c.NameArabic, c.NameEnglish) : "—"),
        Text("السنة المالية: ", "Fiscal year: ") + NewCompanyYear.ToString(CultureInfo.InvariantCulture),
        // Separate lines: a date range inside Arabic text is reordered by the bidi algorithm.
        Text("من: ", "From: ") + NewCompanyStart?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Text("إلى: ", "To: ") + NewCompanyEnd?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Text("لا يمكن تغيير العملة الأساسية بعد ترحيل أي حركة.", "The base currency cannot change once anything is posted."),
    });

    private string YearReview() => string.Join(Environment.NewLine, new[]
    {
        Text("الشركة: ", "Company: ") + SelectedWorkspace?.CompanyNameArabic,
        Text("السنة: ", "Year: ") + NewYearNumber.ToString(CultureInfo.InvariantCulture) + " · " + NewYearCode.Trim().ToUpperInvariant(),
        Text("نسخ دليل الحسابات: ", "Copy chart of accounts: ") + YesNo(CopyChartOfAccounts),
        Text("نسخ المستودعات: ", "Copy warehouses: ") + YesNo(CopyWarehouses),
        Text("نسخ أنماط المستندات: ", "Copy document patterns: ") + YesNo(CopyPatterns),
    });

    private string YesNo(bool value) => value ? Text("نعم", "Yes") : Text("لا", "No");

    partial void OnNewCompanyYearChanged(int value)
    {
        if (value is < 2000 or > 2100) return;
        NewCompanyStart = new DateTime(value, 1, 1); NewCompanyEnd = new DateTime(value, 12, 31);
    }

    partial void OnNewCompanyCodeChanged(string value)
    {
        if (value.Length > 0 && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')) NewDatabaseName = $"Petra_{value}_{NewCompanyYear}";
        NotifyWizardCommands();
    }
    partial void OnNewCompanyNameArabicChanged(string value) => NotifyWizardCommands();
    partial void OnNewCompanyCurrencyChanged(SupportedCurrencyResponse? value) => NotifyWizardCommands();
    partial void OnNewCompanyStartChanged(DateTime? value) => NotifyWizardCommands();
    partial void OnNewCompanyEndChanged(DateTime? value) => NotifyWizardCommands();
    partial void OnNewYearNumberChanged(int value) { NewYearCode = $"FY{value}"; NotifyWizardCommands(); }
    partial void OnNewYearCodeChanged(string value) => NotifyWizardCommands();

    private async Task FinishAsync()
    {
        var created = Wizard;
        Guid? target = null;
        var ok = await operation.RunAsync(async () =>
        {
            if (created == WorkspaceWizardKind.Company)
            {
                var result = await client.CreateCompanyWorkspaceAsync(new CreateCompanyWorkspaceRequest
                {
                    DatabaseName = NewDatabaseName.Trim(), ChartTemplateCode = SelectedAccountTemplate!.TemplateCode,
                    AdministratorPassword = AdministratorPassword,
                    Code = NewCompanyCode.Trim().ToUpperInvariant(),
                    NameArabic = NewCompanyNameArabic.Trim(),
                    NameEnglish = string.IsNullOrWhiteSpace(NewCompanyNameEnglish) ? null : NewCompanyNameEnglish.Trim(),
                    BaseCurrencyCode = NewCompanyCurrency!.Code,
                    InitialYear = NewCompanyYear,
                    StartDate = DateOnly.FromDateTime(NewCompanyStart!.Value),
                    EndDate = DateOnly.FromDateTime(NewCompanyEnd!.Value),
                }, CancellationToken.None).ConfigureAwait(true);
                target = result.FiscalYearId;
            }
            else if (SelectedWorkspace is { } current)
            {
                var result = await client.CreateFiscalYearWorkspaceAsync(new CreateFiscalYearWorkspaceRequest
                {
                    SourceFiscalYearId = current.FiscalYearId,
                    CompanyId = current.CompanyId, YearNumber = NewYearNumber, YearCode = NewYearCode.Trim().ToUpperInvariant(),
                    StartDate = new DateOnly(NewYearNumber, 1, 1), EndDate = new DateOnly(NewYearNumber, 12, 31),
                    CopyChartOfAccounts = CopyChartOfAccounts, CopyWarehouses = CopyWarehouses, CopyPatterns = CopyPatterns,
                }, CancellationToken.None).ConfigureAwait(true);
                target = result.FiscalYearId;
            }
        }).ConfigureAwait(true);
        AdministratorPassword = string.Empty;
        if (!ok) { StepError = Text("تعذر الإنشاء؛ راجع الرسالة أدناه. لم يتغير شيء في المعالج.", "Creation failed; see the message below. The wizard kept your entries."); return; }
        Wizard = WorkspaceWizardKind.None;
        await LoadWorkspacesAsync().ConfigureAwait(true);
        if (target is { } id) SelectedWorkspace = AllWorkspaces.FirstOrDefault(w => w.FiscalYearId == id) ?? SelectedWorkspace;
        StatusMessage = created == WorkspaceWizardKind.FiscalYear
            ? "تم نقل البيانات المرجعية فقط. لم يتم تدوير الأرصدة المالية والمخزنية."
            : Text("تم الإنشاء. انقر مرتين على الملف لفتحه.", "Created. Double-click the file to open it.");
    }
}
