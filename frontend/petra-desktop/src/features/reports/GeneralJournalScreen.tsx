import { useState, useEffect } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { useWorkspace } from "@/workspace/workspaceStore";
import { PetraButton, PetraLoadingSkeleton, PetraEmptyState, PetraErrorDetails, PetraDialog, PetraField } from "@/components/ui";
import { toast } from "@/components/ui/toastStore";

export interface GeneralJournalScreenProps {
  params?: Readonly<Record<string, string>> | undefined;
}

interface GeneralJournalLine {
  journalId: string;
  lineId: string;
  branchId: string;
  accountingDate: string;
  postingSequence?: number | null;
  state: string;
  journalDescription: string;
  lineDescription: string;
  accountId: string;
  accountCode: string;
  accountNameArabic: string;
  costCenterCode?: string | null;
  transactionCurrencyCode: string;
  transactionDebit: string;
  transactionCredit: string;
  baseDebit: string;
  baseCredit: string;
}

interface GeneralJournalData {
  lines: GeneralJournalLine[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalBaseDebit: string;
  totalBaseCredit: string;
  baseCurrencyCode: string;
}

export default function GeneralJournalScreen({ params: _params }: GeneralJournalScreenProps) {
  const api = useApi();
  const { companyId, branchId } = useSession((s) => ({ companyId: s.companyId, branchId: s.branchId }));
  const openWorkspaceWindow = useWorkspace((s) => s.open);

  const [fromDate, setFromDate] = useState("");
  const [toDate, setToDate] = useState("");
  const [search, setSearch] = useState("");
  const [consolidated, setConsolidated] = useState(false);
  const [optionsOpen, setOptionsOpen] = useState(false);

  const [data, setData] = useState<GeneralJournalData | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<{ status?: number; message?: string } | null>(null);

  const loadJournal = async () => {
    if (!companyId || !branchId) return;

    setLoading(true);
    setError(null);
    try {
      const res = await api.GET("/api/v1/companies/{companyId}/branches/{branchId}/ledger/reports/general-journal", {
        params: {
          path: { companyId, branchId },
          query: {
            page: 1,
            pageSize: 500,
            ...(fromDate ? { from: fromDate } : {}),
            ...(toDate ? { to: toDate } : {}),
            ...(search.trim() ? { search: search.trim() } : {}),
            ...(consolidated ? { consolidated: true } : {}),
          },
        },
      });

      if (res.error) {
        setError({ status: res.response.status, message: "فشل استخراج دفتر اليومية العام" });
      } else if (res.data) {
        setData(res.data as unknown as GeneralJournalData);
      }
    } catch (err) {
      setError({ message: err instanceof Error ? err.message : "خطأ غير متوقع" });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadJournal();
  }, [companyId, branchId]);

  const copyToExcel = () => {
    if (!data?.lines || data.lines.length === 0) return;
    const header = "التاريخ\tرقم القيد\tبيان القيد\tرمز الحساب\tاسم الحساب\tمركز الكلفة\tشرح السطر\tمدين\tدائن";
    const rows = data.lines.map((l) =>
      `${l.accountingDate}\t${l.postingSequence ?? ""}\t${l.journalDescription}\t${l.accountCode}\t${l.accountNameArabic}\t${l.costCenterCode ?? ""}\t${l.lineDescription}\t${l.baseDebit}\t${l.baseCredit}`
    );
    navigator.clipboard?.writeText([header, ...rows].join("\r\n")).then(() => {
      toast.success("تم نسخ دفتر اليومية إلى الحافظة بصيغة Excel (TSV)");
    });
  };

  const openJournalEntry = (journalId: string, seq?: number | null) => {
    openWorkspaceWindow({
      key: `journal:${journalId}`,
      screen: "journal-entry",
      title: `قيد #${seq ?? journalId.substring(0, 6)}`,
      params: { journalId },
    });
  };

  return (
    <div style={{ display: "flex", flexDirection: "column", height: "100%", background: "var(--bg-app)" }}>
      {/* Options Dialog */}
      <PetraDialog
        open={optionsOpen}
        onClose={() => setOptionsOpen(false)}
        title="خيارات دفتر اليومية العام"
        width={450}
        actions={
          <>
            <PetraButton variant="secondary" onClick={() => setOptionsOpen(false)}>
              إلغاء
            </PetraButton>
            <PetraButton
              variant="primary"
              onClick={() => {
                setOptionsOpen(false);
                loadJournal();
              }}
            >
              تطبيق الخيارات
            </PetraButton>
          </>
        }
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "var(--space-3)" }}>
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "var(--space-2)" }}>
            <PetraField label="من تاريخ">
              <input
                type="date"
                value={fromDate}
                onChange={(e) => setFromDate(e.target.value)}
                style={{
                  height: "var(--control-height)",
                  padding: "0 var(--space-2)",
                  borderRadius: "var(--radius)",
                  border: "1px solid var(--border)",
                  background: "var(--bg-input)",
                  color: "var(--text)",
                }}
              />
            </PetraField>

            <PetraField label="إلى تاريخ">
              <input
                type="date"
                value={toDate}
                onChange={(e) => setToDate(e.target.value)}
                style={{
                  height: "var(--control-height)",
                  padding: "0 var(--space-2)",
                  borderRadius: "var(--radius)",
                  border: "1px solid var(--border)",
                  background: "var(--bg-input)",
                  color: "var(--text)",
                }}
              />
            </PetraField>
          </div>

          <label style={{ display: "flex", alignItems: "center", gap: "var(--space-2)", cursor: "pointer", fontSize: "var(--fs-body)" }}>
            <input
              type="checkbox"
              checked={consolidated}
              onChange={(e) => setConsolidated(e.target.checked)}
            />
            <span>تضمين جميع فروع الشركة (Consolidated)</span>
          </label>
        </div>
      </PetraDialog>

      {/* Header */}
      <div
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          padding: "var(--space-2) var(--space-3)",
          background: "var(--bg-surface)",
          borderBottom: "1px solid var(--border)",
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: "var(--space-3)" }}>
          <h2 style={{ margin: 0, fontSize: "var(--fs-title)", fontWeight: 700 }}>
            دفتر اليومية العام (General Journal Book)
          </h2>
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: "var(--space-2)" }}>
          <input
            type="text"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter") loadJournal();
            }}
            placeholder="بحث في البيان والشرح... (Enter)"
            style={{
              height: "28px",
              padding: "0 var(--space-2)",
              borderRadius: "var(--radius)",
              border: "1px solid var(--border)",
              background: "var(--bg-input)",
              color: "var(--text)",
              width: "220px",
            }}
          />
          <PetraButton variant="secondary" size="sm" onClick={() => setOptionsOpen(true)}>
            خيارات الفترة
          </PetraButton>
          <PetraButton variant="secondary" size="sm" onClick={copyToExcel} disabled={!data?.lines.length}>
            نسخ إلى Excel
          </PetraButton>
          <PetraButton variant="primary" size="sm" onClick={loadJournal} disabled={loading}>
            {loading ? "جاري التحميل..." : "تحديث"}
          </PetraButton>
        </div>
      </div>

      {/* Summary KPI Bar */}
      {data && (
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(4, 1fr)",
            gap: "var(--space-2)",
            padding: "var(--space-2) var(--space-3)",
            background: "var(--bg-surface)",
            borderBottom: "1px solid var(--border)",
            fontSize: "var(--fs-caption)",
          }}
        >
          <div style={{ background: "var(--bg-sunken)", padding: "var(--space-2)", borderRadius: "var(--radius)" }}>
            <span style={{ color: "var(--text-muted)", display: "block" }}>إجمالي المدين بالأساس</span>
            <strong style={{ fontSize: "var(--fs-body)", fontFamily: "monospace" }}>
              {data.totalBaseDebit} {data.baseCurrencyCode}
            </strong>
          </div>

          <div style={{ background: "var(--bg-sunken)", padding: "var(--space-2)", borderRadius: "var(--radius)" }}>
            <span style={{ color: "var(--text-muted)", display: "block" }}>إجمالي الدائن بالأساس</span>
            <strong style={{ fontSize: "var(--fs-body)", fontFamily: "monospace" }}>
              {data.totalBaseCredit} {data.baseCurrencyCode}
            </strong>
          </div>

          <div style={{ background: "var(--bg-sunken)", padding: "var(--space-2)", borderRadius: "var(--radius)" }}>
            <span style={{ color: "var(--text-muted)", display: "block" }}>توازن اليومية</span>
            <strong
              style={{
                fontSize: "var(--fs-body)",
                color: data.totalBaseDebit === data.totalBaseCredit ? "var(--color-success)" : "var(--color-danger)",
              }}
            >
              {data.totalBaseDebit === data.totalBaseCredit ? "متوازن ✓" : "غير متوازن ⚠"}
            </strong>
          </div>

          <div style={{ background: "var(--bg-sunken)", padding: "var(--space-2)", borderRadius: "var(--radius)" }}>
            <span style={{ color: "var(--text-muted)", display: "block" }}>عدد القيود المعروضة</span>
            <strong style={{ fontSize: "var(--fs-body)", fontFamily: "monospace" }}>
              {data.lines.length} من أصل {data.totalCount}
            </strong>
          </div>
        </div>
      )}

      {/* Main Table */}
      <div style={{ flex: 1, overflow: "auto", padding: "var(--space-3)" }}>
        {loading ? (
          <PetraLoadingSkeleton rows={12} />
        ) : error ? (
          <PetraErrorDetails status={error.status} message={error.message} onRetry={loadJournal} />
        ) : !data?.lines || data.lines.length === 0 ? (
          <PetraEmptyState title="لا توجد أسطر قيود مسجلة" description="تأكد من تحديد نطاق التاريخ أو وجود قيود مرحلة." />
        ) : (
          <div
            style={{
              background: "var(--bg-surface)",
              borderRadius: "var(--radius)",
              border: "1px solid var(--border)",
              overflow: "hidden",
            }}
          >
            <table
              style={{
                width: "100%",
                borderCollapse: "collapse",
                textAlign: "right",
                fontSize: "var(--fs-body)",
              }}
            >
              <thead>
                <tr
                  style={{
                    background: "var(--bg-sunken)",
                    borderBottom: "2px solid var(--border)",
                    color: "var(--text-muted)",
                    fontSize: "var(--fs-caption)",
                  }}
                >
                  <th style={{ padding: "var(--space-2) var(--space-3)" }}>التاريخ</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)" }}>رقم القيد</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)" }}>بيان القيد</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)" }}>الحساب</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)" }}>مركز الكلفة</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)" }}>شرح السطر</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left" }}>مدين</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left" }}>دائن</th>
                </tr>
              </thead>
              <tbody>
                {data.lines.map((line, idx) => (
                  <tr
                    key={line.lineId}
                    style={{
                      borderBottom: "1px solid var(--border-light)",
                      background: idx % 2 === 1 ? "var(--bg-sunken)" : "transparent",
                    }}
                  >
                    <td style={{ padding: "var(--space-2) var(--space-3)", fontFamily: "monospace" }}>
                      {line.accountingDate}
                    </td>
                    <td style={{ padding: "var(--space-2) var(--space-3)", fontFamily: "monospace", fontWeight: 700 }}>
                      <button
                        type="button"
                        onClick={() => openJournalEntry(line.journalId, line.postingSequence)}
                        style={{
                          background: "none",
                          border: "none",
                          padding: 0,
                          color: "var(--primary)",
                          cursor: "pointer",
                          fontWeight: 700,
                          textDecoration: "underline",
                        }}
                      >
                        {line.postingSequence ?? "مسودة"}
                      </button>
                    </td>
                    <td style={{ padding: "var(--space-2) var(--space-3)", fontWeight: 600 }}>
                      {line.journalDescription}
                    </td>
                    <td style={{ padding: "var(--space-2) var(--space-3)" }}>
                      <span style={{ fontFamily: "monospace", color: "var(--primary)", fontWeight: 600, marginLeft: "4px" }}>
                        {line.accountCode}
                      </span>
                      <span>{line.accountNameArabic}</span>
                    </td>
                    <td style={{ padding: "var(--space-2) var(--space-3)", color: "var(--text-muted)" }}>
                      {line.costCenterCode ?? "—"}
                    </td>
                    <td style={{ padding: "var(--space-2) var(--space-3)", color: "var(--text-muted)" }}>
                      {line.lineDescription}
                    </td>
                    <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left", fontFamily: "monospace", fontWeight: line.baseDebit !== "0.00" && line.baseDebit !== "0" ? 700 : 400 }}>
                      {line.baseDebit}
                    </td>
                    <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left", fontFamily: "monospace", fontWeight: line.baseCredit !== "0.00" && line.baseCredit !== "0" ? 700 : 400 }}>
                      {line.baseCredit}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}
