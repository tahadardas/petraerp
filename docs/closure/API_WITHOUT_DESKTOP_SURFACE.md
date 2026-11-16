# عمليات API بلا واجهة Desktop مكتشفة (2026-11-15)

مستخرج آليًا من `API_ROUTE_MAPPING.csv` (تتبّع نصي): عملية لم يُعثر لها على غلاف عميل Desktop ولا على استدعاء مباشر من View Model. **قد تكون مُستهلَكة من CLI/وحدات خارجية/جهة أخرى، أو غير مُعرَّضة في الواجهة بعد**؛ لا يُستنتج أنها معطّلة.

العدد: **21** من 298.

## AccountingReconciliationEndpoints.cs
- `GET /api/v1/companies/{companyId}/branches/{branchId}/reconciliation/export/json` — ExportAccountingReconciliationJson
- `GET /api/v1/companies/{companyId}/branches/{branchId}/reconciliation/export/csv` — ExportAccountingReconciliationCsv

## BranchAdminEndpoints.cs
- `GET /api/v1/companies/{companyId}/branches/{branchId}/branch-admin/tree` — GetCompanyBranchTree
- `GET /api/v1/companies/{companyId}/branches/{branchId}/branch-admin/{target}` — GetBranchCard
- `POST /api/v1/companies/{companyId}/branches/{branchId}/branch-admin/{target}/sync-central` — SyncBranchToCentral

## CommercialPaperEndpoints.cs
- `GET /api/v1/companies/{companyId}/branches/{branchId}/treasury/commercial-papers/{paperId}` — GetCommercialPaper

## LedgerMasterDataEndpoints.cs
- `GET /api/v1/companies/{companyId}/branches/{branchId}/ledger/chart/accounts/deletions` — GetDeletedAccountsAudit
- `GET /api/v1/companies/{companyId}/branches/{branchId}/ledger/calendar/fiscal-years/{fiscalYearId}` — GetFiscalYear

## ModuleEndpoints.cs
- `GET /api/v1/modules` — ListModules
- `POST /api/v1/modules/{moduleId}/enable` — EnableModule
- `POST /api/v1/modules/{moduleId}/disable` — DisableModule
- `POST /api/v1/companies/{companyId}/branches/{branchId}/modules/{moduleId}/commands/{commandKey}` — ExecuteModuleCommand

## OpenItemEndpoints.cs
- `GET /api/v1/companies/{companyId}/branches/{branchId}/open-items` — ListOpenItems
- `POST /api/v1/companies/{companyId}/branches/{branchId}/open-items` — CreateOpenItem
- `GET /api/v1/companies/{companyId}/branches/{branchId}/open-items/party-balances` — GetPartyBalancesSummary
- `GET /api/v1/companies/{companyId}/branches/{branchId}/open-items/{openItemId}` — GetOpenItem

## OpeningBalanceEndpoints.cs
- `POST /api/v1/companies/{companyId}/branches/{branchId}/opening-balances` — CreateOpeningBalanceDraft
- `GET /api/v1/companies/{companyId}/branches/{branchId}/opening-balances/{batchId}` — GetOpeningBalanceBatch

## PlatformEndpoints.cs
- `GET /api/v1/system/write-gate` — GetWriteGateStatus

## WorkspaceSelectionEndpoints.cs
- `GET /api/v1/workspaces/migration/plan` — GetMigrationPlan
- `POST /api/v1/workspaces/migration/execute` — ExecuteMigration
