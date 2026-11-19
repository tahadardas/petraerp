import { useState, useEffect } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { useWorkspace } from "@/workspace/workspaceStore";
import { PetraButton, PetraLoadingSkeleton, PetraEmptyState, PetraErrorDetails, PetraField } from "@/components/ui";
import { toast } from "@/components/ui/toastStore";
import type { components } from "@/services/api/schema";

export interface BranchComparisonScreenProps {
  params?: Readonly<Record<string, string>> | undefined;
}

type BranchTrialBalance = components["schemas"]["BranchTrialBalanceResponse"];
type BranchTrialBalanceRow = components["schemas"]["BranchTrialBalanceRowDto"];
type BranchDto = components["schemas"]["BranchTrialBalanceBranchDto"];

export default function BranchComparisonScreen({ params: _params }: BranchComparisonScreenProps) {
  const api = useApi();
  const { companyId, branchId } = useSession((s) => ({ companyId: s.companyId, branchId: s.branchId }));
  const openWorkspaceWindow = useWorkspace((s) => s.open);

  const [fromDate, setFromDate] = useState<string>("");
  const [toDate, setToDate] = useState<string>("");
  const [eliminateClearing, setEliminateClearing] = useState<boolean>(true);
  const [searchAccount, setSearchAccount] = useState<string>("");

  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<{ status?: number; message?: string } | null>(null);
  const [data, setData] = useState<BranchTrialBalance | null>(null);

  const loadReport = async () => {
    if (!companyId || !branchId) return;

    setIsLoading(true);
    setError(null);

    try {
      const res = await api.GET(
        "/api/v1/companies/{companyId}/branches/{branchId}/branch-reports/trial-balance",
        {
          params: {
            path: { companyId, branchId },
            query: {
              ...(fromDate ? { from: fromDate } : {}),
              ...(toDate ? { to: toDate } : {}),
              eliminate: eliminateClearing,
            },
          },
        }
      );

      if (res.error || !res.data) {
        setError({
          status: res.response?.status,
          message: (res.error as { title?: string; detail?: string })?.detail ?? "فشل تحميل ميزان مقارنة الفروع",
        });
        return;
      }

      setData(res.data);
      toast.success("تم تحديث ميزان المقارنة المجمع للفروع بنجاح");
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
      void loadReport();
    }
  }, [companyId, branchId, eliminateClearing]);

  const branches: BranchDto[] = data?.branches ?? [];
  const rows: BranchTrialBalanceRow[] = data?.rows ?? [];

  const filteredRows = rows.filter((r) => {
    if (!searchAccount.trim()) return true;
    const q = searchAccount.toLowerCase();
    return (
      r.accountCode?.toLowerCase().includes(q) ||
      r.accountName?.toLowerCase().includes(q)
    );
  });

  const exportTsv = () => {
    if (filteredRows.length === 0) {
      toast.warning("لا توجد بيانات للتصدير");
      return;
    }

    const headers = [
      "رمز الحساب",
      "اسم الحساب",
      "حساب وسيط تسوية",
      ...branches.map((b) => `رصيد (${b.name ?? b.code ?? "فرع"})`),
      "إجمالي الافتتاحي",
      "إجمالي المدين",
      "إجمالي الدائن",
      "إجمالي الختامي",
      "الختامي المجمع",
    ];

    const tsvRows = filteredRows.map((r) => {
      const branchValues = branches.map((b) => {
        const col = r.byBranch?.find((c) => c.branchId === b.branchId);
        return col?.closing ?? "0.00";
      });

      return [
        r.accountCode ?? "",
        r.accountName ?? "",
        r.isInterBranchClearing ? "نعم" : "لا",
        ...branchValues,
        r.totalOpening ?? "0.00",
        r.totalDebit ?? "0.00",
        r.totalCredit ?? "0.00",
        r.totalClosing ?? "0.00",
        r.consolidatedClosing ?? "0.00",
      ];
    });

    const tsvContent = [headers.join("\t"), ...tsvRows.map((row) => row.join("\t"))].join("\n");
    const blob = new Blob(["\uFEFF" + tsvContent], { type: "text/tab-separated-values;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `branch-comparison-trial-balance-${new Date().toISOString().slice(0, 10)}.tsv`;
    a.click();
    URL.revokeObjectURL(url);
    toast.success("تم تصدير ميزان مقارنة الفروع بصيغة Excel/TSV");
  };

  return (
    <div className="branch-comparison-screen" style={{ padding: "16px", height: "100%", display: "flex", flexDirection: "column", gap: "16px", overflowY: "auto" }}>
      {/* Header */}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", borderBottom: "1px solid var(--border)", paddingBottom: "12px" }}>
        <div>
          <h2 style={{ margin: 0, fontSize: "1.25rem", color: "var(--header-bg)" }}>
            ميزان المراجعة المجمع ومقارنة الفروع (Branch Comparison & Trial Balance)
          </h2>
          <p style={{ margin: "4px 0 0", fontSize: "0.85rem", color: "var(--text-muted)" }}>
            عرض ومقارنة أرصدة الحركات والأرصدة الختامية عبر كل فروع الشركة واستبعاد الحسابات الوسيطة المشتركة آلياً.
          </p>
        </div>
        <div style={{ display: "flex", gap: "8px" }}>
          <PetraButton variant="secondary" onClick={exportTsv} disabled={filteredRows.length === 0} size="sm">
            تصدير Excel/TSV
          </PetraButton>
          <PetraButton variant="primary" onClick={loadReport} disabled={isLoading} size="sm">
            {isLoading ? "جارٍ التحميل..." : "تحديث التقرير (F5)"}
          </PetraButton>
        </div>
      </div>

      {/* Filter Bar */}
      <div style={{ background: "var(--bg-surface)", border: "1px solid var(--border)", borderRadius: "4px", padding: "12px", display: "flex", flexWrap: "wrap", gap: "16px", alignItems: "flex-end" }}>
        <PetraField label="من تاريخ">
          <input
            type="date"
            className="petra-input"
            value={fromDate}
            onChange={(e) => setFromDate(e.target.value)}
            style={{ padding: "5px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
          />
        </PetraField>
        <PetraField label="إلى تاريخ">
          <input
            type="date"
            className="petra-input"
            value={toDate}
            onChange={(e) => setToDate(e.target.value)}
            style={{ padding: "5px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
          />
        </PetraField>
        <PetraField label="بحث بالحساب">
          <input
            type="text"
            className="petra-input"
            placeholder="رمز أو اسم الحساب..."
            value={searchAccount}
            onChange={(e) => setSearchAccount(e.target.value)}
            style={{ width: "220px", padding: "5px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
          />
        </PetraField>
        <label style={{ display: "flex", alignItems: "center", gap: "6px", fontSize: "0.85rem", cursor: "pointer", paddingBottom: "6px" }}>
          <input
            type="checkbox"
            checked={eliminateClearing}
            onChange={(e) => setEliminateClearing(e.target.checked)}
          />
          استبعاد حسابات التسوية بين الفروع (Eliminate Inter-Branch Clearing)
        </label>
      </div>

      {/* KPI Totals */}
      {data && (
        <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))", gap: "12px" }}>
          <div style={{ background: "var(--bg-surface)", border: "1px solid var(--border)", padding: "12px", borderRadius: "4px" }}>
            <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>إجمالي المدين المجمع</div>
            <div style={{ fontSize: "1.35rem", fontWeight: "bold", fontFamily: "var(--font-mono, monospace)", color: "var(--header-bg)" }}>
              {data.totalDebit ?? "0.00"} {data.baseCurrencyCode}
            </div>
          </div>
          <div style={{ background: "var(--bg-surface)", border: "1px solid var(--border)", padding: "12px", borderRadius: "4px" }}>
            <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>إجمالي الدائن المجمع</div>
            <div style={{ fontSize: "1.35rem", fontWeight: "bold", fontFamily: "var(--font-mono, monospace)", color: "var(--header-bg)" }}>
              {data.totalCredit ?? "0.00"} {data.baseCurrencyCode}
            </div>
          </div>
          <div style={{ background: "var(--bg-surface)", border: "1px solid var(--border)", padding: "12px", borderRadius: "4px" }}>
            <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>الختامي المجمع</div>
            <div style={{ fontSize: "1.35rem", fontWeight: "bold", fontFamily: "var(--font-mono, monospace)", color: "var(--primary)" }}>
              {data.consolidatedClosingSum ?? "0.00"} {data.baseCurrencyCode}
            </div>
          </div>
          <div style={{
            background: data.interBranchResidual && data.interBranchResidual !== "0" && data.interBranchResidual !== "0.00" ? "#FFEBEE" : "#E8F5E9",
            border: `1px solid ${data.interBranchResidual && data.interBranchResidual !== "0" && data.interBranchResidual !== "0.00" ? "#EF9A9A" : "#A5D6A7"}`,
            padding: "12px",
            borderRadius: "4px",
          }}>
            <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>فارق التسوية المتبقي بين الفروع</div>
            <div style={{
              fontSize: "1.35rem",
              fontWeight: "bold",
              fontFamily: "var(--font-mono, monospace)",
              color: data.interBranchResidual && data.interBranchResidual !== "0" && data.interBranchResidual !== "0.00" ? "#C62828" : "#2E7D32",
            }}>
              {data.interBranchResidual ?? "0.00"}
            </div>
          </div>
        </div>
      )}

      {/* Multi-Branch Grid */}
      {isLoading ? (
        <PetraLoadingSkeleton rows={10} />
      ) : error ? (
        <PetraErrorDetails status={error.status} message={error.message} onRetry={loadReport} />
      ) : filteredRows.length === 0 ? (
        <PetraEmptyState title="لا توجد بيانات لميزان المراجعة المقارن" />
      ) : (
        <div style={{ flex: 1, overflowX: "auto", border: "1px solid var(--border)", borderRadius: "4px", background: "var(--bg-surface)" }}>
          <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.85rem", textAlign: "right", whiteSpace: "nowrap" }}>
            <thead>
              <tr style={{ background: "var(--header-bg)", color: "#FFFFFF" }}>
                <th style={{ padding: "8px", position: "sticky", right: 0, background: "var(--header-bg)", zIndex: 1 }}>رمز الحساب</th>
                <th style={{ padding: "8px", position: "sticky", right: "100px", background: "var(--header-bg)", zIndex: 1 }}>اسم الحساب</th>
                {branches.map((b) => (
                  <th key={b.branchId} style={{ padding: "8px", textAlign: "center" }}>
                    {b.name ?? b.code ?? "فرع"}
                  </th>
                ))}
                <th style={{ padding: "8px" }}>إجمالي الافتتاحي</th>
                <th style={{ padding: "8px" }}>إجمالي المدين</th>
                <th style={{ padding: "8px" }}>إجمالي الدائن</th>
                <th style={{ padding: "8px" }}>إجمالي الختامي</th>
                <th style={{ padding: "8px", background: "#004D40" }}>الختامي المجمع</th>
              </tr>
            </thead>
            <tbody>
              {filteredRows.map((r, idx) => (
                <tr
                  key={r.accountId || idx}
                  style={{
                    borderBottom: "1px solid var(--border)",
                    background: r.isInterBranchClearing ? "#FFF8E1" : idx % 2 === 0 ? "transparent" : "var(--bg-app)",
                  }}
                >
                  <td style={{ padding: "8px", fontWeight: "bold", position: "sticky", right: 0, background: "inherit" }}>
                    <span
                      style={{ color: "var(--primary)", cursor: "pointer", textDecoration: "underline" }}
                      onClick={() => {
                        if (r.accountId) {
                          openWorkspaceWindow({
                            key: `account-ledger-${r.accountId}`,
                            title: `دفتر أستاذ: ${r.accountCode}`,
                            screen: "account-ledger",
                            params: { accountId: r.accountId },
                          });
                        }
                      }}
                    >
                      {r.accountCode}
                    </span>
                  </td>
                  <td style={{ padding: "8px", position: "sticky", right: "100px", background: "inherit" }}>
                    {r.accountName}
                    {r.isInterBranchClearing && (
                      <span style={{ fontSize: "0.7rem", color: "#F57F17", marginRight: "6px" }}>(تسوية فروع)</span>
                    )}
                  </td>
                  {branches.map((b) => {
                    const col = r.byBranch?.find((c) => c.branchId === b.branchId);
                    const val = col?.closing ?? "0.00";
                    return (
                      <td key={b.branchId} style={{ padding: "8px", textAlign: "center", fontFamily: "var(--font-mono, monospace)" }}>
                        {val}
                      </td>
                    );
                  })}
                  <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)" }}>{r.totalOpening ?? "0.00"}</td>
                  <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)", color: "#1B5E20" }}>{r.totalDebit ?? "0.00"}</td>
                  <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)", color: "#B71C1C" }}>{r.totalCredit ?? "0.00"}</td>
                  <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)", fontWeight: "bold" }}>{r.totalClosing ?? "0.00"}</td>
                  <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)", fontWeight: "bold", background: "rgba(0, 150, 136, 0.08)", color: "#00695C" }}>
                    {r.consolidatedClosing ?? "0.00"}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
