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
      { id: "new-sales-invoice", label: "menu.sales_invoices", screen: "sales-invoice" },
    ],
  },
  {
    id: "vouchers",
    label: "menu.vouchers",
    children: [
      { id: "treasury-vouchers", label: "menu.treasury", screen: "treasury-vouchers" },
      { id: "new-receipt", label: "menu.treasury", screen: "treasury-voucher", params: { kind: "Receipt" } },
      { id: "new-payment", label: "menu.treasury", screen: "treasury-voucher", params: { kind: "Payment" } },
    ],
  },
  { id: "warehouses", label: "menu.warehouses" },
  {
    id: "reports",
    label: "menu.reports",
    children: [
      { id: "trial-balance", label: "menu.trial_balance", screen: "trial-balance" },
      { id: "account-ledger", label: "menu.account_ledger", screen: "account-ledger" },
    ],
  },
  { id: "maintenance", label: "menu.maintenance" },
  {
    id: "settings",
    label: "menu.settings",
    children: [
      { id: "patterns", label: "menu.patterns", screen: "pattern-manager" },
    ],
  },
];
