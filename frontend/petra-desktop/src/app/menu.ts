import type { MessageKey } from "@/i18n/messages";

export interface MenuItem {
  id: string;
  label: MessageKey;
  /** Screen opened by the item (must exist in the screen registry). Groups without a screen are headers only. */
  screen?: string;
  /** Permission code required to show the item; enforced again by the API. */
  permission?: string;
}

/**
 * Top-level menus requested by the owner. They are the navigation skeleton only: a menu gets items when the screen behind it is
 * implemented against the real API (no placeholder commands).
 */
export const topMenus: readonly MenuItem[] = [
  { id: "accounting", label: "menu.accounting" },
  { id: "invoices", label: "menu.invoices" },
  { id: "vouchers", label: "menu.vouchers" },
  { id: "warehouses", label: "menu.warehouses" },
  { id: "reports", label: "menu.reports" },
  { id: "maintenance", label: "menu.maintenance" },
  { id: "settings", label: "menu.settings" },
];
