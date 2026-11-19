import { useState, useEffect } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { usePreferences } from "@/design-system/preferences";
import { PetraButton, PetraLoadingSkeleton, PetraErrorDetails, PetraField } from "@/components/ui";
import { toast } from "@/components/ui/toastStore";
import type { components } from "@/services/api/schema";

export interface PreferencesScreenProps {
  params?: Readonly<Record<string, string>> | undefined;
}

type WorkspaceState = components["schemas"]["WorkspaceStateResponse"];

export default function PreferencesScreen({ params: _params }: PreferencesScreenProps) {
  const api = useApi();
  const { companyId, branchId } = useSession((s) => ({ companyId: s.companyId, branchId: s.branchId }));
  const { theme, language, density, setTheme, setLanguage, setDensity } = usePreferences();

  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<{ status?: number; message?: string } | null>(null);
  const [serverVersion, setServerVersion] = useState<string>("1");
  const [favoriteCommands, setFavoriteCommands] = useState<string[]>([]);
  const [favoriteReports, setFavoriteReports] = useState<string[]>([]);
  const [isSaving, setIsSaving] = useState(false);

  // Available options
  const AVAILABLE_REPORTS = [
    { key: "general-journal", label: "دفتر اليومية العام (General Journal)" },
    { key: "account-ledger", label: "دفتر الأستاذ (Account Ledger)" },
    { key: "trial-balance", label: "ميزان المراجعة (Trial Balance)" },
    { key: "financial-statements", label: "القوائم المالية الختامية (Financial Statements)" },
    { key: "party-statement", label: "كشف حساب الأطراف (Party Statement)" },
    { key: "aging-report", label: "أعمار الذمم والديون (Aging Report)" },
    { key: "sales-analysis", label: "تحليل المبيعات (Sales Analysis)" },
    { key: "purchase-analysis", label: "تحليل المشتريات (Purchase Analysis)" },
    { key: "inventory-valuation", label: "تقييم بضاعة آخر المدة (Inventory Valuation)" },
    { key: "branch-comparison", label: "مقارنة الفروع والميزان المجمع (Branch Comparison)" },
  ];

  const AVAILABLE_COMMANDS = [
    { key: "journal-entry", label: "قيد يومية جديد" },
    { key: "sales-invoice-card", label: "فاتورة مبيعات جديدة" },
    { key: "purchase-invoice-card", label: "فاتورة مشتريات جديدة" },
    { key: "treasury-voucher-card", label: "سند قبض / دفع جديد" },
    { key: "parties", label: "دليل الأطراف والعملاء" },
    { key: "items", label: "دليل المواد والمخزون" },
    { key: "batch-posting", label: "الترحيل الآلي الجماعي" },
    { key: "reconciliation", label: "المطابقة المحاسبية الشاملة" },
  ];

  const loadPreferences = async () => {
    if (!companyId || !branchId) return;

    setIsLoading(true);
    setError(null);

    try {
      const res = await api.GET(
        "/api/v1/companies/{companyId}/branches/{branchId}/workspace/preferences",
        {
          params: {
            path: { companyId, branchId },
          },
        }
      );

      if (res.error || !res.data) {
        setError({
          status: res.response?.status,
          message: (res.error as { title?: string; detail?: string })?.detail ?? "فشل تحميل تفضيلات بيئة العمل",
        });
        return;
      }

      const data: WorkspaceState = res.data;
      setServerVersion(data.version ?? "1");
      if (data.preferences) {
        if (data.preferences.theme === "light" || data.preferences.theme === "dark") {
          setTheme(data.preferences.theme);
        }
        if (data.preferences.language === "ar" || data.preferences.language === "en") {
          setLanguage(data.preferences.language);
        }
        setFavoriteCommands(data.preferences.favoriteCommands ?? []);
        setFavoriteReports(data.preferences.favoriteReports ?? []);
      }
    } catch (err) {
      setError({
        message: err instanceof Error ? err.message : "خطأ غير متوقع",
      });
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    if (companyId && branchId) {
      void loadPreferences();
    }
  }, [companyId, branchId]);

  const toggleFavoriteCommand = (key: string) => {
    setFavoriteCommands((prev) =>
      prev.includes(key) ? prev.filter((k) => k !== key) : [...prev, key]
    );
  };

  const toggleFavoriteReport = (key: string) => {
    setFavoriteReports((prev) =>
      prev.includes(key) ? prev.filter((k) => k !== key) : [...prev, key]
    );
  };

  const handleSave = async () => {
    if (!companyId || !branchId) return;

    setIsSaving(true);
    try {
      const res = await api.PUT(
        "/api/v1/companies/{companyId}/branches/{branchId}/workspace/preferences",
        {
          params: {
            path: { companyId, branchId },
          },
          body: {
            version: serverVersion,
            preferences: {
              language,
              theme,
              favoriteCommands,
              favoriteReports,
              grids: [],
            },
          },
        }
      );

      if (res.error || !res.data) {
        toast.error((res.error as { title?: string; detail?: string })?.detail ?? "فشل حفظ التفضيلات");
        return;
      }

      setServerVersion(res.data.version ?? String(Number(serverVersion) + 1));
      toast.success("تم حفظ وتطبيق التفضيلات بنجاح");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "خطأ أثناء الحفظ");
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <div className="preferences-screen" style={{ padding: "16px", height: "100%", display: "flex", flexDirection: "column", gap: "16px", overflowY: "auto", maxWidth: "900px" }}>
      {/* Header */}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", borderBottom: "1px solid var(--border)", paddingBottom: "12px" }}>
        <div>
          <h2 style={{ margin: 0, fontSize: "1.25rem", color: "var(--header-bg)" }}>
            تفضيلات المستخدم وخيارات بيئة العمل (User & Workspace Preferences)
          </h2>
          <p style={{ margin: "4px 0 0", fontSize: "0.85rem", color: "var(--text-muted)" }}>
            تخصيص المظهر، اللغة، الكثافة، الأوامر والتقارير المفضلة والمزامنة السحابية مع الخادم.
          </p>
        </div>
        <div style={{ display: "flex", gap: "8px" }}>
          <PetraButton variant="secondary" onClick={loadPreferences} disabled={isLoading} size="sm">
            إعادة تحميل
          </PetraButton>
          <PetraButton variant="primary" onClick={handleSave} disabled={isSaving} size="sm">
            {isSaving ? "جارٍ الحفظ..." : "حفظ التفضيلات"}
          </PetraButton>
        </div>
      </div>

      {isLoading ? (
        <PetraLoadingSkeleton rows={8} />
      ) : error ? (
        <PetraErrorDetails status={error.status} message={error.message} onRetry={loadPreferences} />
      ) : (
        <div style={{ display: "flex", flexDirection: "column", gap: "20px" }}>
          {/* Section 1: Appearance & Display */}
          <div style={{ background: "var(--bg-surface)", border: "1px solid var(--border)", borderRadius: "4px", padding: "16px" }}>
            <h3 style={{ margin: "0 0 12px", fontSize: "1rem", color: "var(--header-bg)", borderBottom: "1px solid var(--border)", paddingBottom: "8px" }}>
              المظهر وطريقة العرض (Appearance & Display)
            </h3>
            <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))", gap: "16px" }}>
              <PetraField label="لغة الواجهة (Interface Language)">
                <div style={{ display: "flex", gap: "8px" }}>
                  <button
                    type="button"
                    onClick={() => setLanguage("ar")}
                    style={{
                      flex: 1,
                      padding: "8px",
                      borderRadius: "4px",
                      border: "1px solid var(--border)",
                      background: language === "ar" ? "var(--primary)" : "transparent",
                      color: language === "ar" ? "#FFFFFF" : "var(--text)",
                      cursor: "pointer",
                      fontWeight: language === "ar" ? "bold" : "normal",
                    }}
                  >
                    العربية (RTL)
                  </button>
                  <button
                    type="button"
                    onClick={() => setLanguage("en")}
                    style={{
                      flex: 1,
                      padding: "8px",
                      borderRadius: "4px",
                      border: "1px solid var(--border)",
                      background: language === "en" ? "var(--primary)" : "transparent",
                      color: language === "en" ? "#FFFFFF" : "var(--text)",
                      cursor: "pointer",
                      fontWeight: language === "en" ? "bold" : "normal",
                    }}
                  >
                    English (LTR)
                  </button>
                </div>
              </PetraField>

              <PetraField label="السمة البصرية (Color Theme)">
                <div style={{ display: "flex", gap: "8px" }}>
                  <button
                    type="button"
                    onClick={() => setTheme("light")}
                    style={{
                      flex: 1,
                      padding: "8px",
                      borderRadius: "4px",
                      border: "1px solid var(--border)",
                      background: theme === "light" ? "var(--primary)" : "transparent",
                      color: theme === "light" ? "#FFFFFF" : "var(--text)",
                      cursor: "pointer",
                      fontWeight: theme === "light" ? "bold" : "normal",
                    }}
                  >
                    فاتح (Petra Calm)
                  </button>
                  <button
                    type="button"
                    onClick={() => setTheme("dark")}
                    style={{
                      flex: 1,
                      padding: "8px",
                      borderRadius: "4px",
                      border: "1px solid var(--border)",
                      background: theme === "dark" ? "var(--primary)" : "transparent",
                      color: theme === "dark" ? "#FFFFFF" : "var(--text)",
                      cursor: "pointer",
                      fontWeight: theme === "dark" ? "bold" : "normal",
                    }}
                  >
                    داكن (Dark Enterprise)
                  </button>
                </div>
              </PetraField>

              <PetraField label="كثافة الجداول والمربعات (Grid Density)">
                <div style={{ display: "flex", gap: "8px" }}>
                  <button
                    type="button"
                    onClick={() => setDensity("compact")}
                    style={{
                      flex: 1,
                      padding: "8px",
                      borderRadius: "4px",
                      border: "1px solid var(--border)",
                      background: density === "compact" ? "var(--primary)" : "transparent",
                      color: density === "compact" ? "#FFFFFF" : "var(--text)",
                      cursor: "pointer",
                      fontWeight: density === "compact" ? "bold" : "normal",
                    }}
                  >
                    كثيف محاسبي (Compact)
                  </button>
                  <button
                    type="button"
                    onClick={() => setDensity("comfortable")}
                    style={{
                      flex: 1,
                      padding: "8px",
                      borderRadius: "4px",
                      border: "1px solid var(--border)",
                      background: density === "comfortable" ? "var(--primary)" : "transparent",
                      color: density === "comfortable" ? "#FFFFFF" : "var(--text)",
                      cursor: "pointer",
                      fontWeight: density === "comfortable" ? "bold" : "normal",
                    }}
                  >
                    مريح (Comfortable)
                  </button>
                </div>
              </PetraField>
            </div>
          </div>

          {/* Section 2: Favorite Commands */}
          <div style={{ background: "var(--bg-surface)", border: "1px solid var(--border)", borderRadius: "4px", padding: "16px" }}>
            <h3 style={{ margin: "0 0 12px", fontSize: "1rem", color: "var(--header-bg)", borderBottom: "1px solid var(--border)", paddingBottom: "8px" }}>
              الأوامر السريعة المفضلة (Favorite Commands in Ctrl+K)
            </h3>
            <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))", gap: "10px" }}>
              {AVAILABLE_COMMANDS.map((cmd) => {
                const isFav = favoriteCommands.includes(cmd.key);
                return (
                  <label
                    key={cmd.key}
                    style={{
                      display: "flex",
                      alignItems: "center",
                      gap: "8px",
                      padding: "8px",
                      borderRadius: "4px",
                      border: "1px solid var(--border)",
                      background: isFav ? "#F0FDF4" : "transparent",
                      cursor: "pointer",
                      fontSize: "0.85rem",
                    }}
                  >
                    <input
                      type="checkbox"
                      checked={isFav}
                      onChange={() => toggleFavoriteCommand(cmd.key)}
                    />
                    {cmd.label}
                  </label>
                );
              })}
            </div>
          </div>

          {/* Section 3: Favorite Reports */}
          <div style={{ background: "var(--bg-surface)", border: "1px solid var(--border)", borderRadius: "4px", padding: "16px" }}>
            <h3 style={{ margin: "0 0 12px", fontSize: "1rem", color: "var(--header-bg)", borderBottom: "1px solid var(--border)", paddingBottom: "8px" }}>
              التقارير المفضلة (Favorite Reports in Command Palette & Home)
            </h3>
            <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(250px, 1fr))", gap: "10px" }}>
              {AVAILABLE_REPORTS.map((rep) => {
                const isFav = favoriteReports.includes(rep.key);
                return (
                  <label
                    key={rep.key}
                    style={{
                      display: "flex",
                      alignItems: "center",
                      gap: "8px",
                      padding: "8px",
                      borderRadius: "4px",
                      border: "1px solid var(--border)",
                      background: isFav ? "#F0FDF4" : "transparent",
                      cursor: "pointer",
                      fontSize: "0.85rem",
                    }}
                  >
                    <input
                      type="checkbox"
                      checked={isFav}
                      onChange={() => toggleFavoriteReport(rep.key)}
                    />
                    {rep.label}
                  </label>
                );
              })}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
