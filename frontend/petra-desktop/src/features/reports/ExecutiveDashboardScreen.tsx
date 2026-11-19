import { useState, useEffect } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { useWorkspace } from "@/workspace/workspaceStore";
import {
  PetraButton,
  PetraField,
  PetraLoadingSkeleton,
  PetraErrorDetails,
} from "@/components/ui";
import { toast } from "@/components/ui/toastStore";

export interface ExecutiveDashboardScreenProps {
  params?: Readonly<Record<string, string>> | undefined;
}

export default function ExecutiveDashboardScreen({ params: _params }: ExecutiveDashboardScreenProps) {
  const api = useApi();
  const { companyId, branchId } = useSession((s) => ({ companyId: s.companyId, branchId: s.branchId }));
  const openWorkspaceWindow = useWorkspace((s) => s.open);

  const [asOfDate, setAsOfDate] = useState<string>(
    new Date().toISOString().split("T")[0] ?? "2026-11-19"
  );

  const [isLoading, setIsLoading] = useState<boolean>(false);
  const [error, setError] = useState<{ status?: number; message?: string } | null>(null);

  // Aggregated executive metrics
  const [metrics, setMetrics] = useState<{
    revenue: string;
    expenses: string;
    netIncome: string;
    inventoryValuation: string;
    receivables: string;
    payables: string;
    currency: string;
  } | null>(null);

  const loadExecutiveData = async () => {
    if (!companyId || !branchId) return;

    setIsLoading(true);
    setError(null);

    try {
      const fromDate = `${asOfDate.slice(0, 4)}-01-01`;

      // 1. Fetch Income Statement
      const incomeRes = await api.GET(
        "/api/v1/companies/{companyId}/branches/{branchId}/reports/financial/income-statement",
        {
          params: {
            path: { companyId, branchId },
            query: { from: fromDate, to: asOfDate },
          },
        }
      );

      // 2. Fetch Inventory Valuation
      const invRes = await api.GET(
        "/api/v1/companies/{companyId}/branches/{branchId}/reports/inventory/valuation",
        {
          params: {
            path: { companyId, branchId },
            query: { asOf: asOfDate },
          },
        }
      );

      // 3. Fetch Aging (Receivables/Payables)
      const agingCustRes = await api.GET(
        "/api/v1/companies/{companyId}/branches/{branchId}/reports/aging",
        {
          params: {
            path: { companyId, branchId },
            query: { asOf: asOfDate, kind: "Customer" },
          },
        }
      );

      const agingSuppRes = await api.GET(
        "/api/v1/companies/{companyId}/branches/{branchId}/reports/aging",
        {
          params: {
            path: { companyId, branchId },
            query: { asOf: asOfDate, kind: "Supplier" },
          },
        }
      );

      const revenue = incomeRes.data?.totalRevenue ?? "0.00";
      const expenses = incomeRes.data?.totalExpenses ?? "0.00";
      const netIncome = incomeRes.data?.netIncome ?? "0.00";
      const inventoryValuation = invRes.data?.totalValue ?? "0.00";
      const receivables = agingCustRes.data?.totals?.net ?? "0.00";
      const payables = agingSuppRes.data?.totals?.net ?? "0.00";

      setMetrics({
        revenue,
        expenses,
        netIncome,
        inventoryValuation,
        receivables,
        payables,
        currency: "SAR",
      });

      toast.success("تم تحديث المؤشرات التنفيذية للشركة بنجاح");
    } catch (err) {
      setError({
        message: err instanceof Error ? err.message : "خطأ أثناء تجميع البيانات التنفيذية",
      });
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    loadExecutiveData();
  }, [companyId, branchId]);

  const exportTsv = () => {
    if (!metrics) return;
    const headers = ["المؤشر المالي التنفيذي", "القيمة", "العملة"];
    const rows = [
      ["إجمالي الإيرادات", metrics.revenue, metrics.currency],
      ["إجمالي المصروفات", metrics.expenses, metrics.currency],
      ["صافي الربح / الدخل", metrics.netIncome, metrics.currency],
      ["تقييم بضاعة المخزون", metrics.inventoryValuation, metrics.currency],
      ["ذمم العملاء المدينة", metrics.receivables, metrics.currency],
      ["ذمم الموردين الدائنة", metrics.payables, metrics.currency],
    ];

    const tsvContent = [headers.join("\t"), ...rows.map((row) => row.join("\t"))].join("\n");
    const blob = new Blob(["\uFEFF" + tsvContent], { type: "text/tab-separated-values;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `executive-dashboard-${asOfDate}.tsv`;
    a.click();
    URL.revokeObjectURL(url);
    toast.success("تم تصدير المؤشرات التنفيذية بنجاح");
  };

  return (
    <div className="executive-dashboard-screen" style={{ padding: "16px", height: "100%", display: "flex", flexDirection: "column", gap: "16px", overflowY: "auto" }}>
      {/* Header */}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", borderBottom: "1px solid var(--border)", paddingBottom: "12px" }}>
        <div>
          <h2 style={{ margin: 0, fontSize: "1.25rem", color: "var(--header-bg)" }}>
            لوحة الإدارة والمؤشرات التنفيذية (Executive KPI Dashboard)
          </h2>
          <p style={{ margin: "4px 0 0", fontSize: "0.85rem", color: "var(--text-muted)" }}>
            نظرة شمولية تنفيذية على الأداء المالي، الربحية، السيولة، تقييم المخزون والذمم المدينة والدائنة.
          </p>
        </div>
        <div style={{ display: "flex", gap: "8px" }}>
          <PetraButton variant="secondary" onClick={exportTsv} disabled={!metrics} size="sm">
            تصدير TSV
          </PetraButton>
          <PetraButton variant="primary" onClick={loadExecutiveData} disabled={isLoading} size="sm">
            {isLoading ? "جارٍ التحديث..." : "تحديث المؤشرات (F5)"}
          </PetraButton>
        </div>
      </div>

      {/* Filter panel */}
      <div style={{ background: "var(--bg-surface)", border: "1px solid var(--border)", borderRadius: "4px", padding: "12px", display: "flex", gap: "16px", alignItems: "flex-end" }}>
        <PetraField label="حتى تاريخ (As Of Date)">
          <input
            type="date"
            className="petra-input"
            value={asOfDate}
            onChange={(e) => setAsOfDate(e.target.value)}
            style={{ padding: "5px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
          />
        </PetraField>
      </div>

      {isLoading ? (
        <PetraLoadingSkeleton rows={10} />
      ) : error ? (
        <PetraErrorDetails status={error.status} message={error.message} onRetry={loadExecutiveData} />
      ) : metrics ? (
        <div style={{ display: "flex", flexDirection: "column", gap: "20px" }}>
          {/* Top KPI Cards Grid */}
          <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))", gap: "16px" }}>
            {/* Revenue */}
            <div
              style={{
                background: "var(--bg-surface)",
                border: "1px solid var(--border)",
                borderRight: "4px solid #2E7D32",
                padding: "16px",
                borderRadius: "4px",
                cursor: "pointer",
              }}
              onClick={() => {
                openWorkspaceWindow({
                  key: "financial-reports",
                  title: "القوائم المالية الختامية",
                  screen: "financial-reports",
                });
              }}
            >
              <div style={{ fontSize: "0.85rem", color: "var(--text-muted)" }}>إجمالي الإيرادات (Revenues)</div>
              <div style={{ fontSize: "1.45rem", fontWeight: "bold", fontFamily: "var(--font-mono, monospace)", color: "#2E7D32", marginTop: "4px" }}>
                {metrics.revenue} <span style={{ fontSize: "0.85rem", fontWeight: "normal" }}>{metrics.currency}</span>
              </div>
              <div style={{ fontSize: "0.75rem", color: "var(--primary)", marginTop: "8px" }}>
                عرض قائمة الدخل ↗
              </div>
            </div>

            {/* Expenses */}
            <div
              style={{
                background: "var(--bg-surface)",
                border: "1px solid var(--border)",
                borderRight: "4px solid #C62828",
                padding: "16px",
                borderRadius: "4px",
                cursor: "pointer",
              }}
              onClick={() => {
                openWorkspaceWindow({
                  key: "financial-reports",
                  title: "القوائم المالية الختامية",
                  screen: "financial-reports",
                });
              }}
            >
              <div style={{ fontSize: "0.85rem", color: "var(--text-muted)" }}>إجمالي المصروفات (Expenses)</div>
              <div style={{ fontSize: "1.45rem", fontWeight: "bold", fontFamily: "var(--font-mono, monospace)", color: "#C62828", marginTop: "4px" }}>
                {metrics.expenses} <span style={{ fontSize: "0.85rem", fontWeight: "normal" }}>{metrics.currency}</span>
              </div>
              <div style={{ fontSize: "0.75rem", color: "var(--primary)", marginTop: "8px" }}>
                عرض تفاصيل المصروفات ↗
              </div>
            </div>

            {/* Net Income */}
            <div
              style={{
                background: Number(metrics.netIncome) >= 0 ? "#E8F5E9" : "#FFEBEE",
                border: "1px solid var(--border)",
                borderRight: `4px solid ${Number(metrics.netIncome) >= 0 ? "#16A34A" : "#DC2626"}`,
                padding: "16px",
                borderRadius: "4px",
              }}
            >
              <div style={{ fontSize: "0.85rem", color: Number(metrics.netIncome) >= 0 ? "#2E7D32" : "#C62828" }}>
                صافي الدخل والربح (Net Profit)
              </div>
              <div style={{ fontSize: "1.45rem", fontWeight: "bold", fontFamily: "var(--font-mono, monospace)", color: Number(metrics.netIncome) >= 0 ? "#16A34A" : "#DC2626", marginTop: "4px" }}>
                {metrics.netIncome} <span style={{ fontSize: "0.85rem", fontWeight: "normal" }}>{metrics.currency}</span>
              </div>
              <div style={{ fontSize: "0.75rem", color: "var(--text-muted)", marginTop: "8px" }}>
                {Number(metrics.netIncome) >= 0 ? "✓ أداء تشغيلي رابح" : "⚠ عجز في صافي النشاط"}
              </div>
            </div>

            {/* Inventory Valuation */}
            <div
              style={{
                background: "var(--bg-surface)",
                border: "1px solid var(--border)",
                borderRight: "4px solid var(--primary)",
                padding: "16px",
                borderRadius: "4px",
                cursor: "pointer",
              }}
              onClick={() => {
                openWorkspaceWindow({
                  key: "inventory-valuation",
                  title: "تقييم بضاعة آخر المدة",
                  screen: "inventory-valuation",
                });
              }}
            >
              <div style={{ fontSize: "0.85rem", color: "var(--text-muted)" }}>قيمة المخزون السلعي (Inventory)</div>
              <div style={{ fontSize: "1.45rem", fontWeight: "bold", fontFamily: "var(--font-mono, monospace)", color: "var(--primary)", marginTop: "4px" }}>
                {metrics.inventoryValuation} <span style={{ fontSize: "0.85rem", fontWeight: "normal" }}>{metrics.currency}</span>
              </div>
              <div style={{ fontSize: "0.75rem", color: "var(--primary)", marginTop: "8px" }}>
                عرض تقرير تقييم البضاعة ↗
              </div>
            </div>

            {/* Receivables */}
            <div
              style={{
                background: "var(--bg-surface)",
                border: "1px solid var(--border)",
                borderRight: "4px solid #D97706",
                padding: "16px",
                borderRadius: "4px",
                cursor: "pointer",
              }}
              onClick={() => {
                openWorkspaceWindow({
                  key: "aging-report",
                  title: "تقرير أعمار الذمم",
                  screen: "aging-report",
                });
              }}
            >
              <div style={{ fontSize: "0.85rem", color: "var(--text-muted)" }}>ذمم العملاء المدينة (Receivables)</div>
              <div style={{ fontSize: "1.45rem", fontWeight: "bold", fontFamily: "var(--font-mono, monospace)", color: "#D97706", marginTop: "4px" }}>
                {metrics.receivables} <span style={{ fontSize: "0.85rem", fontWeight: "normal" }}>{metrics.currency}</span>
              </div>
              <div style={{ fontSize: "0.75rem", color: "var(--primary)", marginTop: "8px" }}>
                أعمار الديون والتحصيل ↗
              </div>
            </div>

            {/* Payables */}
            <div
              style={{
                background: "var(--bg-surface)",
                border: "1px solid var(--border)",
                borderRight: "4px solid #6366F1",
                padding: "16px",
                borderRadius: "4px",
                cursor: "pointer",
              }}
              onClick={() => {
                openWorkspaceWindow({
                  key: "aging-report",
                  title: "تقرير أعمار الذمم",
                  screen: "aging-report",
                });
              }}
            >
              <div style={{ fontSize: "0.85rem", color: "var(--text-muted)" }}>ذمم الموردين الدائنة (Payables)</div>
              <div style={{ fontSize: "1.45rem", fontWeight: "bold", fontFamily: "var(--font-mono, monospace)", color: "#6366F1", marginTop: "4px" }}>
                {metrics.payables} <span style={{ fontSize: "0.85rem", fontWeight: "normal" }}>{metrics.currency}</span>
              </div>
              <div style={{ fontSize: "0.75rem", color: "var(--primary)", marginTop: "8px" }}>
                مستحقات الموردين ↗
              </div>
            </div>
          </div>

          {/* Quick Shortcuts Section */}
          <div style={{ background: "var(--bg-surface)", border: "1px solid var(--border)", borderRadius: "4px", padding: "16px" }}>
            <h3 style={{ margin: "0 0 12px", fontSize: "0.95rem", color: "var(--header-bg)" }}>
              الوصول المباشر للتقارير والتحليلات التنفيذية
            </h3>
            <div style={{ display: "flex", flexWrap: "wrap", gap: "8px" }}>
              <PetraButton
                variant="secondary"
                size="sm"
                onClick={() => openWorkspaceWindow({ key: "sales-analysis", title: "تحليل المبيعات", screen: "sales-analysis" })}
              >
                📊 تحليل المبيعات والعملاء
              </PetraButton>
              <PetraButton
                variant="secondary"
                size="sm"
                onClick={() => openWorkspaceWindow({ key: "purchase-analysis", title: "تحليل المشتريات", screen: "purchase-analysis" })}
              >
                📦 تحليل المشتريات والموردين
              </PetraButton>
              <PetraButton
                variant="secondary"
                size="sm"
                onClick={() => openWorkspaceWindow({ key: "trial-balance", title: "ميزان المراجعة", screen: "trial-balance" })}
              >
                ⚖ ميزان المراجعة العام
              </PetraButton>
              <PetraButton
                variant="secondary"
                size="sm"
                onClick={() => openWorkspaceWindow({ key: "reconciliation", title: "المطابقة المحاسبية", screen: "reconciliation" })}
              >
                🔍 فحص المطابقة الشاملة
              </PetraButton>
              <PetraButton
                variant="secondary"
                size="sm"
                onClick={() => openWorkspaceWindow({ key: "branch-comparison", title: "مقارنة الفروع", screen: "branch-comparison" })}
              >
                🏢 المقارنة بين الفروع
              </PetraButton>
            </div>
          </div>
        </div>
      ) : null}
    </div>
  );
}
