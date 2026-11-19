import { lazy, type ComponentType, type LazyExoticComponent } from "react";

export interface ScreenProps {
  params?: Readonly<Record<string, string>> | undefined;
}

type ScreenLoader = LazyExoticComponent<ComponentType<ScreenProps>>;

/**
 * Screen registry. Every screen is lazy-loaded so the first window opens fast; features add their screen here.
 * One implementation per workflow: a screen id maps to exactly one component.
 */
export const screens: Readonly<Record<string, ScreenLoader>> = {
  home: lazy(() => import("@/features/home/HomeScreen")),
  // Document Patterns
  "pattern-manager": lazy(() => import("@/features/patterns/PatternManagerScreen")),
  "pattern-card": lazy(() => import("@/features/patterns/PatternCardScreen")),
  // Chart of Accounts & General Ledger
  accounts: lazy(() => import("@/features/accounts/AccountsScreen")),
  "cost-centers": lazy(() => import("@/features/costcenters/CostCentersScreen")),
  "opening-balances": lazy(() => import("@/features/accounting/OpeningBalancesScreen")),
  "batch-posting": lazy(() => import("@/features/accounting/BatchPostingScreen")),
  "reconciliation": lazy(() => import("@/features/accounting/ReconciliationScreen")),
  "fiscal-years": lazy(() => import("@/features/calendar/FiscalYearsScreen")),
  journals: lazy(() => import("@/features/journals/JournalsScreen")),
  "journal-entry": lazy(() => import("@/features/journals/JournalEntryScreen")),
  // Unified Parties (Customers/Suppliers)
  parties: lazy(() => import("@/features/parties/PartiesScreen")),
  "party-statement": lazy(() => import("@/features/parties/PartyStatementScreen")),
  // Sales Operations
  "sales-invoices": lazy(() => import("@/features/sales/SalesInvoicesScreen")),
  "sales-invoice": lazy(() => import("@/features/sales/SalesInvoiceCardScreen")),
  // Treasury & Cash Operations
  "treasury-vouchers": lazy(() => import("@/features/treasury/TreasuryVouchersScreen")),
  "treasury-voucher": lazy(() => import("@/features/treasury/TreasuryVoucherCardScreen")),
  "commercial-papers": lazy(() => import("@/features/treasury/CommercialPapersScreen")),
  "bank-reconciliation": lazy(() => import("@/features/treasury/BankReconciliationScreen")),
  "open-items": lazy(() => import("@/features/treasury/OpenItemsSettlementScreen")),
  // Core Accounting Reports
  "account-ledger": lazy(() => import("@/features/reports/AccountLedgerScreen")),
  "general-journal": lazy(() => import("@/features/reports/GeneralJournalScreen")),
  "trial-balance": lazy(() => import("@/features/reports/TrialBalanceScreen")),
  "financial-reports": lazy(() => import("@/features/reports/FinancialStatementsScreen")),
  "aging-report": lazy(() => import("@/features/reports/AgingReportScreen")),
  "sales-analysis": lazy(() => import("@/features/reports/SalesAnalysisScreen")),
  "purchase-analysis": lazy(() => import("@/features/reports/PurchaseAnalysisScreen")),
  "inventory-valuation": lazy(() => import("@/features/reports/InventoryValuationScreen")),
  "inventory-movements": lazy(() => import("@/features/reports/InventoryMovementsScreen")),
  "reports-catalog": lazy(() => import("@/features/reports/ReportsCatalogScreen")),
  "branch-comparison": lazy(() => import("@/features/admin/BranchComparisonScreen")),
  // Inventory
  items: lazy(() => import("@/features/inventory/ItemsScreen")),
  warehouses: lazy(() => import("@/features/inventory/WarehousesScreen")),
  units: lazy(() => import("@/features/inventory/UnitsScreen")),
  "inventory-operations": lazy(() => import("@/features/inventory/InventoryOperationsScreen")),
  // Purchases
  "purchase-invoices": lazy(() => import("@/features/purchases/PurchaseInvoicesScreen")),
  "purchase-invoice-card": lazy(() => import("@/features/purchases/PurchaseInvoiceCardScreen")),
  // Currencies & Foreign Exchange
  currencies: lazy(() => import("@/features/currencies/CurrenciesScreen")),
  "fx-revaluation": lazy(() => import("@/features/currencies/FxRevaluationScreen")),
  // Administration & Audit
  branches: lazy(() => import("@/features/admin/BranchesScreen")),
  "inter-company": lazy(() => import("@/features/admin/InterCompanyScreen")),
  users: lazy(() => import("@/features/admin/UsersScreen")),
  roles: lazy(() => import("@/features/admin/RolesScreen")),
  "security-audit": lazy(() => import("@/features/admin/SecurityAuditScreen")),
  preferences: lazy(() => import("@/features/settings/PreferencesScreen")),
  // Development gallery of the input engine (opened from the header in dev builds only).
  gallery: lazy(() => import("@/features/gallery/GalleryScreen")),
};
