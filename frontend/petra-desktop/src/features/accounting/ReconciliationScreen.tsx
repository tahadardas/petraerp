import { useState, useEffect } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { useWorkspace } from "@/workspace/workspaceStore";
import { PetraButton, PetraLoadingSkeleton, PetraEmptyState, PetraErrorDetails } from "@/components/ui";
import { toast } from "@/components/ui/toastStore";
import type { components } from "@/services/api/schema";

export interface ReconciliationScreenProps {
  params?: Readonly<Record<string, string>> | undefined;
}

type ReconciliationReport = components["schemas"]["ReconciliationReportResponse"];

export default function ReconciliationScreen({ params: _params }: ReconciliationScreenProps) {
  const api = useApi();
  const { companyId, branchId } = useSession((s) => ({ companyId: s.companyId, branchId: s.branchId }));
  const openWorkspaceWindow = useWorkspace((s) => s.open);

  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<{ status?: number; message?: string } | null>(null);
  const [report, setReport] = useState<ReconciliationReport | null>(null);
  const [severityFilter, setSeverityFilter] = useState<string>("ALL");
  const [searchQuery, setSearchQuery] = useState<string>("");

  const runReconciliation = async () => {
    if (!companyId || !branchId) {
      toast.warning("يجب تحديد الشركة والفرع أولاً");
      return;
    }

    setIsLoading(true);
    setError(null);

    try {
      const res = await api.GET(
        "/api/v1/companies/{companyId}/branches/{branchId}/reconciliation",
        {
          params: {
            path: { companyId, branchId },
          },
        }
      );

      if (res.error || !res.data) {
        setError({
          status: res.response?.status,
          message: (res.error as { title?: string; detail?: string })?.detail ?? "فشل تشغيل المطابقة المحاسبية",
        });
        return;
      }

      setReport(res.data);
      if (res.data.hasBlockingDifferences) {
        toast.error(`اكتملت المطابقة: يوجد ${res.data.blockingChecks} فروقات محاسبية مانعة للإغلاق!`);
      } else {
        toast.success(`اكتملت المطابقة بنجاح: ${res.data.passedChecks} من أصل ${res.data.totalChecks} فحوصات سليمة تماماً`);
      }
    } catch (err) {
      setError({
        message: err instanceof Error ? err.message : "خطأ غير متوقع أثناء تشغيل المطابقة",
      });
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    if (companyId && branchId) {
      void runReconciliation();
    }
  }, [companyId, branchId]);

  const findings = report?.findings ?? [];

  const filteredFindings = findings.filter((f) => {
    if (severityFilter !== "ALL" && f.severity.toUpperCase() !== severityFilter) {
      return false;
    }
    if (searchQuery.trim()) {
      const q = searchQuery.toLowerCase();
      const codeMatch = f.checkCode?.toLowerCase().includes(q);
      const explMatch = f.explanation?.toLowerCase().includes(q);
      const actMatch = f.suggestedAction?.toLowerCase().includes(q);
      if (!codeMatch && !explMatch && !actMatch) return false;
    }
    return true;
  });

  const exportTsv = () => {
    if (findings.length === 0) {
      toast.warning("لا توجد بيانات مطابقة للتصدير");
      return;
    }

    const headers = [
      "رمز الفحص",
      "الخطورة",
      "العملة",
      "المبلغ الأصلي / المصدر",
      "المبلغ المحسوب",
      "الفارق المحاسبي",
      "الشرح والتفاصيل",
      "الإجراء المقترح",
      "مرجع الإثبات",
    ];

    const rows = findings.map((f) => [
      f.checkCode,
      f.severity,
      f.currencyCode ?? "",
      f.sourceAmount ?? "",
      f.calculatedAmount ?? "",
      f.difference ?? "",
      f.explanation,
      f.suggestedAction ?? "",
      f.evidenceReference ?? "",
    ]);

    const tsvContent = [headers.join("\t"), ...rows.map((row) => row.join("\t"))].join("\n");
    const blob = new Blob(["\uFEFF" + tsvContent], { type: "text/tab-separated-values;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `reconciliation-report-${new Date().toISOString().slice(0, 10)}.tsv`;
    a.click();
    URL.revokeObjectURL(url);
    toast.success("تم تصدير تقرير المطابقة بصيغة Excel/TSV");
  };

  return (
    <div className="reconciliation-screen" style={{ padding: "16px", height: "100%", display: "flex", flexDirection: "column", gap: "16px", overflowY: "auto" }}>
      {/* Header */}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", borderBottom: "1px solid var(--border)", paddingBottom: "12px" }}>
        <div>
          <h2 style={{ margin: 0, fontSize: "1.25rem", color: "var(--header-bg)" }}>
            فحص المطابقة المحاسبية متعدد الطبقات (Accounting Reconciliation)
          </h2>
          <p style={{ margin: "4px 0 0", fontSize: "0.85rem", color: "var(--text-muted)" }}>
            تشغيل الفحوصات المحاسبية الـ 14 الصارمة بين الأستاذ العام والدفاتر المساعدة والمخزون والقيود وفحص التوازن المانع للإغلاق.
          </p>
        </div>
        <div style={{ display: "flex", gap: "8px" }}>
          <PetraButton
            variant="secondary"
            onClick={exportTsv}
            disabled={!report || findings.length === 0}
            size="sm"
          >
            تصدير Excel/TSV
          </PetraButton>
          <PetraButton
            variant="primary"
            onClick={runReconciliation}
            disabled={isLoading}
            size="sm"
          >
            {isLoading ? "جارٍ الفحص..." : "إعادة تشغيل الفحص (F5)"}
          </PetraButton>
        </div>
      </div>

      {/* KPI Cards */}
      {report && (
        <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))", gap: "12px" }}>
          <div style={{ background: "var(--bg-surface)", border: "1px solid var(--border)", padding: "12px", borderRadius: "4px" }}>
            <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>إجمالي الفحوصات</div>
            <div style={{ fontSize: "1.5rem", fontWeight: "bold", color: "var(--header-bg)" }}>{report.totalChecks}</div>
          </div>
          <div style={{ background: "#E8F5E9", border: "1px solid #A5D6A7", padding: "12px", borderRadius: "4px" }}>
            <div style={{ fontSize: "0.8rem", color: "#2E7D32" }}>فحوصات سليمة تماماً</div>
            <div style={{ fontSize: "1.5rem", fontWeight: "bold", color: "#2E7D32" }}>{report.passedChecks}</div>
          </div>
          <div style={{ background: "#FFF8E1", border: "1px solid #FFE082", padding: "12px", borderRadius: "4px" }}>
            <div style={{ fontSize: "0.8rem", color: "#F57F17" }}>تحذيرات غير مانعة</div>
            <div style={{ fontSize: "1.5rem", fontWeight: "bold", color: "#F57F17" }}>{report.warningChecks}</div>
          </div>
          <div style={{
            background: report.blockingChecks > 0 ? "#FFEBEE" : "#F1F8E9",
            border: `1px solid ${report.blockingChecks > 0 ? "#EF9A9A" : "#C5E1A5"}`,
            padding: "12px",
            borderRadius: "4px",
          }}>
            <div style={{ fontSize: "0.8rem", color: report.blockingChecks > 0 ? "#C62828" : "#33691E" }}>
              فروقات مانعة للإغلاق
            </div>
            <div style={{ fontSize: "1.5rem", fontWeight: "bold", color: report.blockingChecks > 0 ? "#C62828" : "#33691E" }}>
              {report.blockingChecks}
            </div>
          </div>
        </div>
      )}

      {/* Filter and Search Bar */}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", gap: "12px", background: "var(--bg-surface)", padding: "10px 12px", border: "1px solid var(--border)", borderRadius: "4px" }}>
        <div style={{ display: "flex", gap: "8px", alignItems: "center" }}>
          <span style={{ fontSize: "0.85rem", color: "var(--text-muted)" }}>تصفية حسب النتيجة:</span>
          {[
            { key: "ALL", label: "الكل" },
            { key: "BLOCKING", label: "الفروقات المانعة" },
            { key: "WARNING", label: "تحذيرات" },
            { key: "PASSED", label: "سليم" },
          ].map((tab) => (
            <button
              key={tab.key}
              type="button"
              onClick={() => setSeverityFilter(tab.key)}
              style={{
                padding: "4px 10px",
                fontSize: "0.8rem",
                borderRadius: "3px",
                border: "1px solid var(--border)",
                cursor: "pointer",
                background: severityFilter === tab.key ? "var(--primary)" : "transparent",
                color: severityFilter === tab.key ? "#FFFFFF" : "var(--text)",
              }}
            >
              {tab.label}
            </button>
          ))}
        </div>

        <input
          type="text"
          className="petra-input"
          placeholder="بحث في رمز الفحص، الشرح، الإجراء..."
          value={searchQuery}
          onChange={(e) => setSearchQuery(e.target.value)}
          style={{ width: "260px", padding: "5px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
        />
      </div>

      {/* Main Table */}
      {isLoading ? (
        <PetraLoadingSkeleton rows={8} />
      ) : error ? (
        <PetraErrorDetails status={error.status} message={error.message} onRetry={runReconciliation} />
      ) : filteredFindings.length === 0 ? (
        <PetraEmptyState title={findings.length === 0 ? "لم يتم تشغيل المطابقة بعد" : "لا توجد نتائج مطابقة لخيارات التصفية"} />
      ) : (
        <div style={{ flex: 1, overflowX: "auto", border: "1px solid var(--border)", borderRadius: "4px", background: "var(--bg-surface)" }}>
          <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.85rem", textAlign: "right" }}>
            <thead>
              <tr style={{ background: "var(--header-bg)", color: "#FFFFFF" }}>
                <th style={{ padding: "8px" }}>رمز الفحص</th>
                <th style={{ padding: "8px" }}>الدرجة</th>
                <th style={{ padding: "8px" }}>مبلغ المصدر</th>
                <th style={{ padding: "8px" }}>المبلغ المحسوب</th>
                <th style={{ padding: "8px" }}>الفارق المحاسبي</th>
                <th style={{ padding: "8px" }}>العملة</th>
                <th style={{ padding: "8px" }}>الشرح والسبب</th>
                <th style={{ padding: "8px" }}>الإجراء المقترح</th>
                <th style={{ padding: "8px" }}>إجراءات</th>
              </tr>
            </thead>
            <tbody>
              {filteredFindings.map((f, idx) => {
                const isBlocking = f.severity.toUpperCase() === "BLOCKING";
                const isWarning = f.severity.toUpperCase() === "WARNING";
                const hasDiff = f.difference && f.difference !== "0" && f.difference !== "0.00";

                return (
                  <tr
                    key={f.reconciliationId || idx}
                    style={{
                      borderBottom: "1px solid var(--border)",
                      background: isBlocking ? "#FFF5F5" : isWarning ? "#FFFDF0" : idx % 2 === 0 ? "transparent" : "var(--bg-app)",
                    }}
                  >
                    <td style={{ padding: "8px", fontWeight: "bold" }}>{f.checkCode}</td>
                    <td style={{ padding: "8px" }}>
                      <span
                        style={{
                          padding: "2px 8px",
                          borderRadius: "3px",
                          fontSize: "0.75rem",
                          background: isBlocking ? "#FFCDD2" : isWarning ? "#FFE082" : "#C8E6C9",
                          color: isBlocking ? "#B71C1C" : isWarning ? "#E65100" : "#1B5E20",
                          fontWeight: "bold",
                        }}
                      >
                        {f.severity}
                      </span>
                    </td>
                    <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)" }}>{f.sourceAmount ?? "—"}</td>
                    <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)" }}>{f.calculatedAmount ?? "—"}</td>
                    <td
                      style={{
                        padding: "8px",
                        fontFamily: "var(--font-mono, monospace)",
                        fontWeight: hasDiff ? "bold" : "normal",
                        color: hasDiff ? "#C62828" : "inherit",
                      }}
                    >
                      {f.difference ?? "0.00"}
                    </td>
                    <td style={{ padding: "8px" }}>{f.currencyCode ?? "—"}</td>
                    <td style={{ padding: "8px", maxWidth: "280px" }}>{f.explanation}</td>
                    <td style={{ padding: "8px", color: "var(--text-muted)", maxWidth: "240px" }}>{f.suggestedAction || "—"}</td>
                    <td style={{ padding: "8px" }}>
                      {f.accountId && (
                        <button
                          type="button"
                          className="petra-button-link"
                          style={{ fontSize: "0.75rem", color: "var(--primary)", background: "none", border: "none", cursor: "pointer", textDecoration: "underline" }}
                          onClick={() => {
                            openWorkspaceWindow({
                              key: `account-ledger-${f.accountId}`,
                              title: "دفتر الأستاذ",
                              screen: "account-ledger",
                              params: { accountId: f.accountId! },
                            });
                          }}
                        >
                          عرض الأستاذ
                        </button>
                      )}
                      {f.partyId && (
                        <button
                          type="button"
                          className="petra-button-link"
                          style={{ fontSize: "0.75rem", color: "var(--primary)", background: "none", border: "none", cursor: "pointer", textDecoration: "underline" }}
                          onClick={() => {
                            openWorkspaceWindow({
                              key: `party-statement-${f.partyId}`,
                              title: "كشف حساب الطرف",
                              screen: "party-statement",
                              params: { partyId: f.partyId! },
                            });
                          }}
                        >
                          كشف الحساب
                        </button>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
