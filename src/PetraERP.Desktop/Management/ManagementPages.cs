using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Management;

/// <summary>Groups the branch / warehouse / bulletin / numbering / inter-company / report-centre pages so the shell registry stays small.</summary>
public sealed class ManagementPages(BranchesViewModel branches, WarehousesViewModel warehouses, RateBulletinViewModel rateBulletin, DocumentNumbersViewModel documentNumbers,
    InterCompanyViewModel interCompany, BranchComparisonViewModel branchComparison, ReportsCenterViewModel reportsCenter, AnalyticalReportViewModel analytical, BankReconciliationViewModel bankReconciliation)
{
    public BranchesViewModel Branches { get; } = branches;
    public WarehousesViewModel Warehouses { get; } = warehouses;
    public RateBulletinViewModel RateBulletin { get; } = rateBulletin;
    public DocumentNumbersViewModel DocumentNumbers { get; } = documentNumbers;
    public InterCompanyViewModel InterCompany { get; } = interCompany;
    public BranchComparisonViewModel BranchComparison { get; } = branchComparison;
    public ReportsCenterViewModel ReportsCenter { get; } = reportsCenter;

    public AnalyticalReportViewModel Analytical { get; } = analytical;
    public BankReconciliationViewModel BankReconciliation { get; } = bankReconciliation;

    public IReadOnlyList<IShellPage> Pages => [Branches, Warehouses, RateBulletin, DocumentNumbers, InterCompany, BranchComparison, ReportsCenter, Analytical, BankReconciliation];
}
