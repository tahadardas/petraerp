import { useState } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { useWorkspace } from "@/workspace/workspaceStore";
import {
  PetraButton,
  PetraField,
  PetraEmptyState,
  PetraLoadingSkeleton,
  PetraErrorDetails,
} from "@/components/ui";
import { toast } from "@/components/ui/toastStore";
import type { components } from "@/services/api/schema";

export interface CurrencyBalanceReportScreenProps {
  params?: Readonly<Record<string, string>> | undefined;
}

type ProfessionalReportExecutionResponse = components["schemas"]["ProfessionalReportExecutionResponse"];

export default function CurrencyBalanceReportScreen({ params: _params }: CurrencyBalanceReportScreenProps) {
  const api = useApi();
  const { companyId, branchId, fiscalYear } = useSession((s) => ({
    companyId: s.companyId,
    branchId: s.branchId,
    fiscalYear: s.fiscalYear,
  }));
  const openWorkspaceWindow = useWorkspace((s) => s.open);

  const [fromDate, setFromDate] = useState<string>(`${new Date().getFullYear()}-01-01`);
  const [toDate, setToDate] = useState<string>(
    new Date().toISOString().split("T")[0] ?? `${new Date().getFullYear()}-12-31`
  );
  const [currencyCode, setCurrencyCode] = useState<string>("");
  const [onlyNonZero, setOnlyNonZero] = useState<boolean>(true);

  const [isLoading, setIsLoading] = useState<boolean>(false);
  const [error, setError] = useState<{ status?: number; message?: string } | null>(null);
  const [reportResult, setReportResult] = useState<ProfessionalReportExecutionResponse | null>(null);

  const handleRunReport = async () => {
    if (!companyId || !branchId) return;

    setIsLoading(true);
    setError(null);
    setReportResult(null);

    try {
      const res = await api.POST(
        "/api/v1/companies/{companyId}/branches/{branchId}/reporting/execute",
        {
          params: { path: { companyId, branchId } },
          body: {
            kind: "CurrencyBalance",
            fiscalYearId: fiscalYear ?? "00000000-0000-0000-0000-000000000000",
            from: fromDate,
            to: toDate,
            ...(currencyCode ? { currencyCode } : {}),
            onlyNonZero,
          },
        }
      );

      if (res.error || !res.data) {
        setError({
          status: res.response?.status,
          message: (res.error as { title?: string; detail?: string })?.detail ?? "فشل توليد تقرير ميزان العملات",
        });
        return;
      }

      setReportResult(res.data);
      toast.success(`تم استخراج ميزان العملات بنجاح (${res.data.rows?.length ?? 0} سطراً)`);
    } catch (err) {
      setError({
        message: err instanceof Error ? err.message : "خطأ غير متوقع أثناء استخراج التقرير",
      });
    } finally {
      setIsLoading(false);
    }
  };

  const headers = [
    "رمز الحساب",
    "اسم الحساب",
    "العملة",
    "مدين (أساس)",
    "دائن (أساس)",
    "الرصيد الفعلي",
    "مدين (عملة أجنبية)",
    "دائن (عملة أجنبية)",
    "عملة الحركة",
    "البيان",
  ];

  const exportTsv = () => {
    if (!reportResult?.rows || reportResult.rows.length === 0) {
      toast.warning("لا توجد بيانات للتصدير");
      return;
    }

    const rows = reportResult.rows.map((r) => [
      r.accountCode ?? "",
      r.accountName ?? "",
      r.currencyCode ?? "",
      r.debit ?? "0.00",
      r.credit ?? "0.00",
      r.actualBalance ?? "0.00",
      r.transactionDebit ?? "0.00",
      r.transactionCredit ?? "0.00",
      r.transactionCurrencyCode ?? "",
      r.description ?? "",
    ]);

    const tsvContent = [headers.join("\t"), ...rows.map((row) => row.join("\t"))].join("\n");
    const blob = new Blob(["\uFEFF" + tsvContent], { type: "text/tab-separated-values;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `currency-balance-${toDate}.tsv`;
    a.click();
    URL.revokeObjectURL(url);
    toast.success("تم تصدير ميزان العملات بنجاح");
  };

  const filteredRows = (reportResult?.rows ?? []).filter((r) => {
    if (!currencyCode) return true;
    const c = currencyCode.toLowerCase();
    return (
      (r.currencyCode ?? "").toLowerCase().includes(c) ||
      (r.transactionCurrencyCode ?? "").toLowerCase().includes(c)
    );
  });

  return (
    <div className="currency-balance-report-screen" style={{ padding: "16px", height: "100%", display: "flex", flexDirection: "column", gap: "16px", overflowY: "auto" }}>
      {/* Header */}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", borderBottom: "1px solid var(--border)", paddingBottom: "12px" }}>
        <div>
          <h2 style={{ margin: 0, fontSize: "1.2rem", color: "var(--header-bg)" }}>
            ميزان العملات (Currency Balance Report)
          </h2>
          <p style={{ margin: "4px 0 0", fontSize: "0.85rem", color: "var(--text-muted)" }}>
            عرض تحليلي لأرصدة الحسابات المقومة بالعملات الأجنبية مقابل المعادل المحلي بسعر الصرف المعتمد
          </p>
        </div>

        <div style={{ display: "flex", gap: "8px" }}>
          <PetraButton
            variant="secondary"
            onClick={exportTsv}
            disabled={!reportResult?.rows || reportResult.rows.length === 0}
          >
            تصدير TSV
          </PetraButton>
          <PetraButton variant="primary" onClick={handleRunReport} disabled={isLoading}>
            {isLoading ? "جاري الاستخراج..." : "عرض ميزان العملات"}
          </PetraButton>
        </div>
      </div>

      {/* Filter Parameters */}
      <div
        style={{
          background: "var(--bg-surface)",
          border: "1px solid var(--border)",
          borderRadius: "4px",
          padding: "16px",
          display: "grid",
          gridTemplateColumns: "repeat(auto-fit, minmax(180px, 1fr))",
          gap: "12px",
          alignItems: "end",
        }}
      >
        <PetraField label="من تاريخ">
          <input
            type="date"
            className="petra-input"
            value={fromDate}
            onChange={(e) => setFromDate(e.target.value)}
            style={{ width: "100%", padding: "6px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
          />
        </PetraField>

        <PetraField label="إلى تاريخ">
          <input
            type="date"
            className="petra-input"
            value={toDate}
            onChange={(e) => setToDate(e.target.value)}
            style={{ width: "100%", padding: "6px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
          />
        </PetraField>

        <PetraField label="فلترة العملة (اختياري)">
          <input
            type="text"
            className="petra-input"
            placeholder="مثال: USD, EUR..."
            value={currencyCode}
            onChange={(e) => setCurrencyCode(e.target.value.toUpperCase())}
            style={{ width: "100%", padding: "6px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
          />
        </PetraField>

        <div style={{ display: "flex", alignItems: "center", gap: "8px", paddingBottom: "8px" }}>
          <input
            type="checkbox"
            id="onlyNonZero"
            checked={onlyNonZero}
            onChange={(e) => setOnlyNonZero(e.target.checked)}
          />
          <label htmlFor="onlyNonZero" style={{ fontSize: "0.85rem", color: "var(--text)" }}>
            إخفاء الحسابات الصفرية
          </label>
        </div>
      </div>

      {/* Report Summary Cards */}
      {reportResult?.summary && (
        <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))", gap: "12px" }}>
          <div style={{ background: "var(--bg-surface)", border: "1px solid var(--border)", borderRadius: "4px", padding: "12px" }}>
            <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>إجمالي المدين (الفترة)</div>
            <div style={{ fontSize: "1.2rem", fontWeight: "bold", color: "#1E3A8A", fontFamily: "var(--font-mono, monospace)" }}>
              {reportResult.summary.actualPeriodDebit ?? "0.00"}
            </div>
          </div>
          <div style={{ background: "var(--bg-surface)", border: "1px solid var(--border)", borderRadius: "4px", padding: "12px" }}>
            <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>إجمالي الدائن (الفترة)</div>
            <div style={{ fontSize: "1.2rem", fontWeight: "bold", color: "#1E3A8A", fontFamily: "var(--font-mono, monospace)" }}>
              {reportResult.summary.actualPeriodCredit ?? "0.00"}
            </div>
          </div>
          <div style={{ background: "var(--bg-surface)", border: "1px solid var(--border)", borderRadius: "4px", padding: "12px" }}>
            <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>صافي الرصيد الختامي</div>
            <div style={{ fontSize: "1.2rem", fontWeight: "bold", color: "var(--primary)", fontFamily: "var(--font-mono, monospace)" }}>
              {reportResult.summary.actualClosing ?? "0.00"}
            </div>
          </div>
          <div style={{ background: "var(--bg-surface)", border: "1px solid var(--border)", borderRadius: "4px", padding: "12px" }}>
            <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>عدد الأسطر المالية</div>
            <div style={{ fontSize: "1.2rem", fontWeight: "bold" }}>
              {reportResult.summary.rowCount ?? filteredRows.length}
            </div>
          </div>
        </div>
      )}

      {/* Main Table / State */}
      {isLoading ? (
        <PetraLoadingSkeleton rows={10} />
      ) : error ? (
        <PetraErrorDetails status={error.status} message={error.message} onRetry={handleRunReport} />
      ) : !reportResult ? (
        <PetraEmptyState title="حدد الفترة واضغط 'عرض ميزان العملات' لاستخراج أرصدة العملات الأجنبية والمعادل المحلي" />
      ) : filteredRows.length === 0 ? (
        <PetraEmptyState title="لا توجد أرصدة عملات مطابقة للفلترة المحددة" />
      ) : (
        <div style={{ flex: 1, overflowX: "auto", border: "1px solid var(--border)", borderRadius: "4px", background: "var(--bg-surface)" }}>
          <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.85rem", textAlign: "right" }}>
            <thead>
              <tr style={{ background: "var(--header-bg)", color: "#FFFFFF" }}>
                {headers.map((h, idx) => (
                  <th key={idx} style={{ padding: "8px" }}>{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {filteredRows.map((row, rIdx) => (
                <tr
                  key={row.rowId || rIdx}
                  style={{
                    borderBottom: "1px solid var(--border)",
                    background: rIdx % 2 === 0 ? "transparent" : "var(--bg-app)",
                  }}
                >
                  <td style={{ padding: "8px", fontWeight: "bold" }}>
                    {row.accountId ? (
                      <span
                        style={{ color: "var(--primary)", cursor: "pointer", textDecoration: "underline" }}
                        onClick={() => {
                          openWorkspaceWindow({
                            key: `account-ledger-${row.accountId}`,
                            title: `دفتر أستاذ: ${row.accountCode}`,
                            screen: "account-ledger",
                            params: { accountId: row.accountId! },
                          });
                        }}
                      >
                        {row.accountCode}
                      </span>
                    ) : (
                      row.accountCode
                    )}
                  </td>
                  <td style={{ padding: "8px" }}>{row.accountName}</td>
                  <td style={{ padding: "8px" }}>{row.currencyCode}</td>
                  <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)" }}>{row.debit}</td>
                  <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)" }}>{row.credit}</td>
                  <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)", fontWeight: "bold" }}>{row.actualBalance}</td>
                  <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)" }}>{row.transactionDebit}</td>
                  <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)" }}>{row.transactionCredit}</td>
                  <td style={{ padding: "8px" }}>{row.transactionCurrencyCode}</td>
                  <td style={{ padding: "8px", color: "var(--text-muted)" }}>{row.description}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
