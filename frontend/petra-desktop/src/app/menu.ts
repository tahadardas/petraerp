import type { MessageKey } from "@/i18n/messages";

export interface MenuItem {
  id: string;
  label: MessageKey;
  /** Screen opened by the item (must exist in the screen registry). */
  screen?: string;
  params?: Record<string, string>;
  /** Permission code required to show the item; enforced again by the API. */
  permission?: string;
  children?: readonly MenuItem[];
}

/**
 * Top-level menus mapped to production business screens supported by the API.
 */
export const topMenus: readonly MenuItem[] = [
  {
    id: "accounting",
    label: "menu.accounting",
    children: [
      { id: "accounts", label: "menu.accounts", screen: "accounts" },
      { id: "cost-centers", label: "menu.cost_centers", screen: "cost-centers" },
      { id: "journals", label: "menu.journals", screen: "journals" },
      { id: "new-journal", label: "menu.journals", screen: "journal-entry" },
      { id: "parties", label: "menu.parties", screen: "parties" },
    ],
  },
  {
    id: "invoices",
    label: "menu.invoices",
    children: [
      { id: "sales-invoices", label: "menu.sales_invoices", screen: "sales-invoices" },
      { id: "purchase-invoices", label: "menu.purchase_invoices", screen: "purchase-invoices" },
    ],
  },
  {
    id: "vouchers",
    label: "menu.vouchers",
    children: [
      { id: "treasury-vouchers", label: "menu.treasury", screen: "treasury-vouchers" },
      { id: "new-receipt", label: "menu.treasury", screen: "treasury-voucher", params: { kind: "Receipt" } },
      { id: "new-payment", label: "menu.treasury", screen: "treasury-voucher", params: { kind: "Payment" } },
      { id: "commercial-papers", label: "menu.commercial_papers", screen: "commercial-papers" },
      { id: "bank-reconciliation", label: "menu.bank_reconciliation", screen: "bank-reconciliation" },
      { id: "open-items", label: "menu.open_items", screen: "open-items" },
    ],
  },
  {
    id: "warehouses",
    label: "menu.warehouses",
    children: [
      { id: "items", label: "menu.items", screen: "items" },
      { id: "warehouses-list", label: "menu.warehouses_list", screen: "warehouses" },
      { id: "units", label: "menu.units", screen: "units" },
      { id: "inventory-operations", label: "menu.inventory_operations", screen: "inventory-operations" },
    ],
  },
  {
    id: "reports",
    label: "menu.reports",
    children: [
      { id: "general-journal", label: "menu.general_journal", screen: "general-journal" },
      { id: "trial-balance", label: "menu.trial_balance", screen: "trial-balance" },
      { id: "account-ledger", label: "menu.account_ledger", screen: "account-ledger" },
      { id: "financial-reports", label: "menu.financial_reports", screen: "financial-reports" },
      { id: "aging-report", label: "menu.aging_report", screen: "aging-report" },
      { id: "sales-analysis", label: "menu.sales_analysis", screen: "sales-analysis" },
      { id: "purchase-analysis", label: "menu.purchase_analysis", screen: "purchase-analysis" },
      { id: "inventory-valuation", label: "menu.inventory_valuation", screen: "inventory-valuation" },
      { id: "inventory-movements", label: "menu.inventory_movements", screen: "inventory-movements" },
    ],
  },
  {
    id: "maintenance",
    label: "menu.maintenance",
    children: [
      { id: "security-audit", label: "menu.security_audit", screen: "security-audit" },
    ],
  },
  {
    id: "settings",
    label: "menu.settings",
    children: [
      { id: "branches", label: "menu.branches", screen: "branches" },
      { id: "currencies", label: "menu.currencies", screen: "currencies" },
      { id: "users", label: "menu.users", screen: "users" },
      { id: "roles", label: "menu.roles", screen: "roles" },
      { id: "patterns", label: "menu.patterns", screen: "pattern-manager" },
    ],
  },
];
