import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useEffect, useState, useMemo } from "react";
import { applyPreferences, usePreferences } from "@/design-system/preferences";
import { useT } from "@/i18n/messages";
import { WorkspaceShell } from "@/workspace/WorkspaceShell";
import { useWorkspace } from "@/workspace/workspaceStore";
import { ApiProvider } from "@/services/api/ApiContext";
import {
  PetraToastContainer,
  PetraStatusBar,
  PetraCommandPalette,
  type CommandItem,
} from "@/components/ui";
import { WorkspaceContextModal } from "@/features/workspace/WorkspaceContextModal";
import AuthScreen from "@/features/auth/AuthScreen";
import { hasPermission, useSession } from "./permissions";
import { topMenus, type MenuItem } from "./menu";

function createQueryClient(): QueryClient {
  // Reference data is cached per workspace by query keys; aborted requests are cancelled by React Query's signal.
  return new QueryClient({ defaultOptions: { queries: { staleTime: 60_000, retry: 1, refetchOnWindowFocus: false } } });
}

export function App() {
  const [queryClient] = useState(createQueryClient);
  const { theme, language, density, setTheme, setLanguage } = usePreferences();
  const t = useT();
  const permissions = useSession((s) => s.permissions);
  const workspaceId = useSession((s) => s.workspaceId);
  const accessToken = useSession((s) => s.accessToken);
  const open = useWorkspace((s) => s.open);

  const [activeMenuId, setActiveMenuId] = useState<string | null>(null);
  const [contextModalOpen, setContextModalOpen] = useState(false);
  const [authModalOpen, setAuthModalOpen] = useState(false);

  useEffect(() => applyPreferences(document.documentElement, { theme, language, density }), [theme, language, density]);
  // Switching company/year must never show another workspace's cached data.
  useEffect(() => queryClient.clear(), [workspaceId, queryClient]);
  useEffect(() => {
    open({ key: "home", screen: "home", title: t("home.title") });
    // Opened once at start.
  }, []);

  const commandItems: CommandItem[] = useMemo(() => [
    { id: "cmd-accounts", title: "دليل الحسابات", category: "المحاسبة", screen: "accounts", shortcut: "F3" },
    { id: "cmd-cost-centers", title: "دليل مراكز الكلفة", category: "المحاسبة", screen: "cost-centers" },
    { id: "cmd-journals", title: "القيود اليومية العامة", category: "المحاسبة", screen: "journals" },
    { id: "cmd-new-journal", title: "قيد يومية جديد", category: "المحاسبة", screen: "journal-entry", shortcut: "Ctrl+N" },
    { id: "cmd-parties", title: "دليل الأطراف الموحد (الزبائن والموردون)", category: "الأطراف", screen: "parties" },
    { id: "cmd-sales-invoices", title: "فواتير المبيعات", category: "الفواتير", screen: "sales-invoices" },
    { id: "cmd-new-sales-invoice", title: "فاتورة مبيعات جديدة", category: "الفواتير", screen: "sales-invoice" },
    { id: "cmd-purchase-invoices", title: "فواتير المشتريات", category: "الفواتير", screen: "purchase-invoices" },
    { id: "cmd-new-purchase-invoice", title: "فاتورة مشتريات جديدة", category: "الفواتير", screen: "purchase-invoice-card" },
    { id: "cmd-items", title: "دليل المواد", category: "المستودعات", screen: "items" },
    { id: "cmd-warehouses", title: "دليل المستودعات", category: "المستودعات", screen: "warehouses" },
    { id: "cmd-units", title: "وحدات القياس", category: "المستودعات", screen: "units" },
    { id: "cmd-inventory-operations", title: "حركات ومستندات المستودعات", category: "المستودعات", screen: "inventory-operations" },
    { id: "cmd-treasury-vouchers", title: "سندات الصندوق (القبض والدفع)", category: "السندات", screen: "treasury-vouchers" },
    { id: "cmd-new-receipt", title: "سند قبض جديد", category: "السندات", screen: "treasury-voucher", params: { kind: "Receipt" } },
    { id: "cmd-new-payment", title: "سند دفع جديد", category: "السندات", screen: "treasury-voucher", params: { kind: "Payment" } },
    { id: "cmd-patterns", title: "إدارة أنماط المستندات", category: "الإعدادات", screen: "pattern-manager" },
    { id: "cmd-currencies", title: "إدارة العملات وأسعار الصرف", category: "الإعدادات", screen: "currencies" },
    { id: "cmd-users", title: "إدارة المستخدمين", category: "الإعدادات", screen: "users" },
    { id: "cmd-roles", title: "الأدوار والصلاحيات", category: "الإعدادات", screen: "roles" },
    { id: "cmd-security-audit", title: "سجل التدقيق والأمان", category: "الصيانة", screen: "security-audit" },
    { id: "cmd-trial-balance", title: "ميزان المراجعة العام", category: "التقارير", screen: "trial-balance" },
    { id: "cmd-account-ledger", title: "دفتر الأستاذ للحساب", category: "التقارير", screen: "account-ledger" },
    { id: "cmd-switch-context", title: "تبديل الشركة أو الفرع أو السنة المالية", category: "النظام", action: () => setContextModalOpen(true) },
  ], []);

  const handleMenuClick = (menu: MenuItem) => {
    if (menu.children && menu.children.length > 0) {
      setActiveMenuId((prev) => (prev === menu.id ? null : menu.id));
    } else if (menu.screen) {
      setActiveMenuId(null);
      open({
        key: menu.id,
        screen: menu.screen,
        title: t(menu.label),
        ...(menu.params ? { params: menu.params } : {}),
      });
    }
  };

  const handleSubItemClick = (sub: MenuItem) => {
    setActiveMenuId(null);
    if (sub.screen) {
      open({
        key: sub.id,
        screen: sub.screen,
        title: t(sub.label),
        ...(sub.params ? { params: sub.params } : {}),
      });
    }
  };

  return (
    <ApiProvider>
      <QueryClientProvider client={queryClient}>
        <div style={{ display: "flex", flexDirection: "column", height: "100%" }}>
          {/* Main Top Header */}
          <header
            style={{
              display: "flex",
              alignItems: "center",
              gap: "var(--space-4)",
              padding: "0 var(--space-4)",
              background: "var(--header-bg)",
              color: "var(--header-text)",
              borderBottom: "1px solid var(--border)",
              height: 44,
              userSelect: "none",
              position: "relative",
              zIndex: 100,
            }}
          >
            <strong style={{ fontSize: 16, fontWeight: 700, letterSpacing: "0.5px" }}>
              {t("app.title")}
            </strong>

            {/* Navigation Menus */}
            <nav aria-label="main" style={{ display: "flex", gap: "var(--space-1)", flex: 1 }}>
              {topMenus
                .filter((m) => hasPermission(permissions, m.permission))
                .map((m) => {
                  const isOpen = activeMenuId === m.id;
                  return (
                    <div key={m.id} style={{ position: "relative" }}>
                      <button
                        type="button"
                        onClick={() => handleMenuClick(m)}
                        aria-label={t(m.label)}
                        style={{
                          background: isOpen ? "rgba(255, 255, 255, 0.15)" : "none",
                          border: 0,
                          color: "inherit",
                          cursor: "pointer",
                          padding: "var(--space-1) var(--space-2)",
                          borderRadius: "var(--radius)",
                          fontSize: "var(--fs-body)",
                          fontWeight: 500,
                          fontFamily: "inherit",
                          display: "inline-flex",
                          alignItems: "center",
                          gap: 4,
                        }}
                      >
                        <span>{t(m.label)}</span>
                        {m.children ? <span aria-hidden="true" style={{ fontSize: 10 }}>▼</span> : null}
                      </button>

                      {/* Dropdown Menu */}
                      {isOpen && m.children ? (
                        <div
                          style={{
                            position: "absolute",
                            top: "100%",
                            insetInlineStart: 0,
                            background: "var(--bg-surface)",
                            color: "var(--text)",
                            border: "1px solid var(--border)",
                            borderRadius: "var(--radius)",
                            boxShadow: "0 8px 24px rgba(0, 0, 0, 0.2)",
                            minWidth: 200,
                            padding: "var(--space-1) 0",
                            zIndex: 1000,
                          }}
                        >
                          {m.children.map((sub) => (
                            <button
                              key={sub.id}
                              type="button"
                              onClick={() => handleSubItemClick(sub)}
                              style={{
                                display: "block",
                                width: "100%",
                                textAlign: "start",
                                padding: "var(--space-2) var(--space-3)",
                                background: "transparent",
                                border: 0,
                                color: "var(--text)",
                                cursor: "pointer",
                                fontSize: "var(--fs-body)",
                                fontFamily: "inherit",
                              }}
                              onMouseEnter={(e) => (e.currentTarget.style.background = "var(--bg-sunken)")}
                              onMouseLeave={(e) => (e.currentTarget.style.background = "transparent")}
                            >
                              {t(sub.label)}
                            </button>
                          ))}
                        </div>
                      ) : null}
                    </div>
                  );
                })}
            </nav>

            {/* Quick Command Palette Button */}
            <button
              type="button"
              onClick={() => {
                const event = new KeyboardEvent("keydown", { key: "k", ctrlKey: true });
                window.dispatchEvent(event);
              }}
              title="البحث الشامل والأوامر السريعة (Ctrl+K)"
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: "var(--space-2)",
                background: "rgba(255, 255, 255, 0.12)",
                border: "1px solid rgba(255, 255, 255, 0.2)",
                color: "inherit",
                borderRadius: "var(--radius)",
                padding: "4px var(--space-2)",
                fontSize: "var(--fs-caption)",
                cursor: "pointer",
                fontFamily: "inherit",
              }}
            >
              <span>🔍 بحث أوامر</span>
              <kbd style={{ background: "rgba(0, 0, 0, 0.25)", padding: "1px 4px", borderRadius: 3, fontSize: 10 }}>
                Ctrl+K
              </kbd>
            </button>

            {/* Auth / Login button */}
            <button
              type="button"
              onClick={() => setAuthModalOpen(true)}
              style={{
                background: "none",
                border: "1px solid rgba(255, 255, 255, 0.25)",
                color: "inherit",
                cursor: "pointer",
                padding: "3px var(--space-2)",
                borderRadius: "var(--radius)",
                fontSize: "var(--fs-caption)",
                fontFamily: "inherit",
              }}
            >
              {accessToken ? "الحساب 👤" : "تسجيل الدخول 🔑"}
            </button>

            {import.meta.env.DEV ? (
              <button
                type="button"
                onClick={() => open({ key: "gallery", screen: "gallery", title: "Components" })}
                style={{
                  background: "rgba(255, 255, 255, 0.1)",
                  border: 0,
                  color: "inherit",
                  cursor: "pointer",
                  padding: "var(--space-1) var(--space-2)",
                  borderRadius: "var(--radius)",
                  fontSize: "var(--fs-caption)",
                }}
              >
                Components
              </button>
            ) : null}

            <label style={{ fontSize: "var(--fs-caption)" }}>
              {t("pref.theme")}{" "}
              <select
                value={theme}
                onChange={(e) => setTheme(e.target.value === "dark" ? "dark" : "light")}
                style={{
                  background: "var(--bg-surface)",
                  color: "var(--text)",
                  border: "1px solid var(--border)",
                  borderRadius: 3,
                  padding: "1px 4px",
                }}
              >
                <option value="light">{t("pref.light")}</option>
                <option value="dark">{t("pref.dark")}</option>
              </select>
            </label>

            <label style={{ fontSize: "var(--fs-caption)" }}>
              {t("pref.language")}{" "}
              <select
                value={language}
                onChange={(e) => setLanguage(e.target.value === "en" ? "en" : "ar")}
                style={{
                  background: "var(--bg-surface)",
                  color: "var(--text)",
                  border: "1px solid var(--border)",
                  borderRadius: 3,
                  padding: "1px 4px",
                }}
              >
                <option value="ar">{"العربية"}</option>
                <option value="en">English</option>
              </select>
            </label>
          </header>

          {/* Main Multi-Document Workspace Shell */}
          <main style={{ flex: 1, minHeight: 0 }} onClick={() => setActiveMenuId(null)}>
            <WorkspaceShell />
          </main>

          {/* Footer Status Bar */}
          <PetraStatusBar onOpenContextSwitch={() => setContextModalOpen(true)} />

          {/* Toast Notification Container */}
          <PetraToastContainer />

          {/* Command Palette (Ctrl+K) */}
          <PetraCommandPalette items={commandItems} />

          {/* Workspace Context Switcher Modal (WS-01) */}
          <WorkspaceContextModal
            open={contextModalOpen}
            onClose={() => setContextModalOpen(false)}
          />

          {/* Auth Modal (AUTH-01) */}
          {authModalOpen ? (
            <div
              style={{
                position: "fixed",
                inset: 0,
                background: "rgba(0, 0, 0, 0.5)",
                display: "flex",
                alignItems: "center",
                justifyContent: "center",
                zIndex: 10000,
              }}
              onClick={() => setAuthModalOpen(false)}
            >
              <div onClick={(e) => e.stopPropagation()} style={{ width: 440 }}>
                <AuthScreen onSuccess={() => setAuthModalOpen(false)} />
              </div>
            </div>
          ) : null}
        </div>
      </QueryClientProvider>
    </ApiProvider>
  );
}
