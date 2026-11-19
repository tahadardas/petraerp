import { usePreferences, type Language } from "@/design-system/preferences";

/** UI strings only. Accounting captions that depend on a pattern come from the API (effective pattern), not from here. */
export const messages = {
  ar: {
    "app.title": "Petra ERP",
    "menu.accounting": "المحاسبة",
    "menu.parties": "الأطراف",
    "menu.invoices": "الفواتير",
    "menu.vouchers": "السندات",
    "menu.warehouses": "المستودعات",
    "menu.patterns": "أنماط المستندات",
    "menu.reports": "التقارير",
    "menu.maintenance": "الصيانة",
    "menu.settings": "الإعدادات",
    "menu.accounts": "دليل الحسابات",
    "menu.journals": "القيود اليومية",
    "menu.sales_invoices": "فواتير المبيعات",
    "menu.treasury": "سندات القبض والدفع",
    "menu.trial_balance": "ميزان المراجعة",
    "menu.account_ledger": "دفتر الأستاذ",
    "workspace.empty": "لا توجد نوافذ مفتوحة. اختر من القوائم العلوية أو اضغط Ctrl+K للبحث.",
    "workspace.close": "إغلاق",
    "workspace.unsaved": "تغييرات غير محفوظة",
    "home.title": "الصفحة الرئيسية",
    "home.body": "نظام النوافذ والقوائم جاهز. الشاشات المحاسبية تُبنى شاشة واحدة في كل خطوة فوق واجهة API.",
    "pref.theme": "السمة",
    "pref.light": "فاتح",
    "pref.dark": "داكن",
    "pref.language": "اللغة",
  },
  en: {
    "app.title": "Petra ERP",
    "menu.accounting": "Accounting",
    "menu.parties": "Parties",
    "menu.invoices": "Invoices",
    "menu.vouchers": "Vouchers",
    "menu.warehouses": "Warehouses",
    "menu.patterns": "Document Patterns",
    "menu.reports": "Reports",
    "menu.maintenance": "Maintenance",
    "menu.settings": "Settings",
    "menu.accounts": "Chart of Accounts",
    "menu.journals": "Journal Entries",
    "menu.sales_invoices": "Sales Invoices",
    "menu.treasury": "Receipt & Payment Vouchers",
    "menu.trial_balance": "Trial Balance",
    "menu.account_ledger": "Account Ledger",
    "workspace.empty": "No open windows. Pick one from the top menu or press Ctrl+K to search.",
    "workspace.close": "Close",
    "workspace.unsaved": "Unsaved changes",
    "home.title": "Home",
    "home.body": "The window system and menus are ready. Accounting screens are built one at a time on top of the API.",
    "pref.theme": "Theme",
    "pref.light": "Light",
    "pref.dark": "Dark",
    "pref.language": "Language",
  },
} as const;

export type MessageKey = keyof (typeof messages)["ar"];

export function translate(language: Language, key: MessageKey): string {
  return messages[language][key];
}

export function useT(): (key: MessageKey) => string {
  const language = usePreferences((s) => s.language);
  return (key) => translate(language, key);
}
