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
  // Core Accounting Reports
  "account-ledger": lazy(() => import("@/features/reports/AccountLedgerScreen")),
  "trial-balance": lazy(() => import("@/features/reports/TrialBalanceScreen")),
  // Development gallery of the input engine (opened from the header in dev builds only).
  gallery: lazy(() => import("@/features/gallery/GalleryScreen")),
};
