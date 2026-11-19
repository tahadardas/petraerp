import { useState, useEffect } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { useWorkspace } from "@/workspace/workspaceStore";
import { PetraButton, PetraLoadingSkeleton, PetraEmptyState, PetraErrorDetails, PetraDialog, PetraField } from "@/components/ui";
import { toast } from "@/components/ui/toastStore";

export interface AccountLedgerScreenProps {
  params?: Readonly<Record<string, string>> | undefined;
}

interface LedgerLine {
  journalId: string;
  lineId: string;
  accountingDate: string;
  postingSequence?: number | null;
  journalDescription: string;
  lineDescription: string;
  accountCode: string;
  costCenterCode?: string | null;
  baseDebit: string;
  baseCredit: string;
  runningBalance: string;
}

interface LedgerData {
  account: {
    id: string;
    code: string;
    nameArabic: string;
  };
  baseCurrencyCode: string;
  openingBalance: string;
  periodDebit: string;
  periodCredit: string;
  closingBalance: string;
  lines: LedgerLine[];
}

export default function AccountLedgerScreen({ params }: AccountLedgerScreenProps) {
  const initialAccountId = params?.accountId ?? "";
  const initialAccountCode = params?.accountCode ?? "";
  const initialAccountName = params?.accountName ?? "";

  const api = useApi();
  const { companyId, branchId } = useSession((s) => ({ companyId: s.companyId, branchId: s.branchId }));
  const openWorkspaceWindow = useWorkspace((s) => s.open);

  const [accountId, setAccountId] = useState(initialAccountId);
  const [ledger, setLedger] = useState<LedgerData | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<{ status?: number; message?: string } | null>(null);

  // Pre-execution dialog state
  const [optionsOpen, setOptionsOpen] = useState(!initialAccountId);
  const [fromDate, setFromDate] = useState("");
  const [toDate, setToDate] = useState("");
  const [sources, setSources] = useState("");
  const [consolidated, setConsolidated] = useState(false);

  const loadLedger = async (targetId: string = accountId) => {
    if (!companyId || !branchId || !targetId) {
      setOptionsOpen(true);
      return;
    }

    setLoading(true);
    setError(null);
    try {
      const res = await api.GET("/api/v1/companies/{companyId}/branches/{branchId}/ledger/reports/account-ledger/{accountId}", {
        params: {
          path: { companyId, branchId, accountId: targetId },
          query: {
            page: 1,
            pageSize: 500,
            ...(fromDate ? { from: fromDate } : {}),
            ...(toDate ? { to: toDate } : {}),
            ...(sources ? { sources } : {}),
            ...(consolidated ? { consolidated: true } : {}),
          },
        },
      });

      if (res.error) {
        setError({ status: res.response.status, message: "فشل استخراج دفتر الأستاذ من الخادم" });
      } else if (res.data) {
        setLedger(res.data as unknown as LedgerData);
      }
    } catch (err) {
      setError({ message: err instanceof Error ? err.message : "خطأ غير متوقع" });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    if (initialAccountId) {
      loadLedger(initialAccountId);
    }
  }, [companyId, branchId, initialAccountId]);

  const copyToExcel = () => {
    if (!ledger || ledger.lines.length === 0) return;
    const header = "التاريخ\tرقم القيد\tبيان القيد\tشرح السطر\tمدين\tدائن\tالرصيد الجاري";
    const rows = ledger.lines.map((l) =>
      `${l.accountingDate}\t${l.postingSequence ?? ""}\t${l.journalDescription}\t${l.lineDescription}\t${l.baseDebit}\t${l.baseCredit}\t${l.runningBalance}`
    );
    const tsv = [header, ...rows].join("\r\n");
    navigator.clipboard?.writeText(tsv).then(() => {
      toast.success("تم نسخ دفتر الأستاذ إلى الحافظة بصيغة Excel (TSV)");
    });
  };

  const openJournal = (journalId: string, postingSeq?: number | null) => {
    openWorkspaceWindow({
      key: `journal:${journalId}`,
      screen: "journal-entry",
      title: `قيد #${postingSeq ?? journalId.substring(0, 6)}`,
      params: { journalId },
    });
  };

  return (
    <div style={{ display: "flex", flexDirection: "column", height: "100%", background: "var(--bg-app)" }}>
      {/* Options Dialog before execution */}
      <PetraDialog
        open={optionsOpen}
        onClose={() => setOptionsOpen(false)}
        title="خيارات دفتر الأستاذ (Account Ledger)"
        width={480}
        actions={
          <>
            <PetraButton variant="secondary" onClick={() => setOptionsOpen(false)}>
              إلغاء
            </PetraButton>
            <PetraButton
              variant="primary"
              disabled={!accountId}
              onClick={() => {
                setOptionsOpen(false);
                loadLedger();
              }}
            >
              عرض التقرير
            </PetraButton>
          </>
        }
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "var(--space-3)" }}>
          <PetraField label="معرّف الحساب (Account ID)" required>
            <input
              type="text"
              value={accountId}
              onChange={(e) => setAccountId(e.target.value)}
              placeholder="أدخل معرّف الحساب أو افتحه من دليل الحسابات"
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

          <PetraField label="مصدر الحركات (Source)">
            <select
              value={sources}
              onChange={(e) => setSources(e.target.value)}
              style={{
                height: "var(--control-height)",
                padding: "0 var(--space-2)",
                borderRadius: "var(--radius)",
                border: "1px solid var(--border)",
                background: "var(--bg-input)",
                color: "var(--text)",
              }}
            >
              <option value="">جميع المصادر (All Sources)</option>
              <option value="JOURNAL">القيود اليومية فقط (General Journals)</option>
              <option value="SALES_INVOICE">فواتير المبيعات فقط (Sales Invoices)</option>
              <option value="PURCHASE_INVOICE">فواتير المشتريات فقط (Purchase Invoices)</option>
              <option value="VOUCHER">سندات الخزينة والصندوق (Treasury Vouchers)</option>
            </select>
          </PetraField>

          <label style={{ display: "flex", alignItems: "center", gap: "var(--space-2)", cursor: "pointer", fontSize: "var(--fs-body)" }}>
            <input
              type="checkbox"
              checked={consolidated}
              onChange={(e) => setConsolidated(e.target.checked)}
            />
            <span>تضمين الحسابات الفرعية التابعة (Consolidated)</span>
          </label>

          <div
            style={{
              padding: "var(--space-2)",
              background: "var(--bg-sunken)",
              borderRadius: "var(--radius)",
              fontSize: "var(--fs-caption)",
              color: "var(--text-muted)",
            }}
          >
            📋 فلترة المصادر (Sources) مؤجلة وفق توثيق BACKEND_GAP لحين دعمها في الخادم.
          </div>
        </div>
      </PetraDialog>

      {/* Top Bar */}
      <div
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          padding: "var(--space-3) var(--space-4)",
          background: "var(--bg-surface)",
          borderBottom: "1px solid var(--border)",
          gap: "var(--space-3)",
          flexWrap: "wrap",
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: "var(--space-2)" }}>
          <h2 style={{ margin: 0, fontSize: "var(--fs-title)", fontWeight: 700 }}>
            دفتر الأستاذ: {ledger?.account.nameArabic ?? initialAccountName ?? "—"}
          </h2>
          {ledger?.account.code || initialAccountCode ? (
            <span className="numeric" style={{ fontWeight: 600, color: "var(--primary)" }}>
              [{ledger?.account.code ?? initialAccountCode}]
            </span>
          ) : null}
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: "var(--space-2)" }}>
          <PetraButton variant="secondary" size="sm" onClick={() => setOptionsOpen(true)}>
            خيارات التقرير ⚙
          </PetraButton>
          <PetraButton variant="secondary" size="sm" onClick={() => loadLedger()} loading={loading}>
            تحديث
          </PetraButton>
          <PetraButton variant="secondary" size="sm" onClick={copyToExcel} disabled={!ledger || ledger.lines.length === 0}>
            نسخ إلى Excel
          </PetraButton>
        </div>
      </div>

      {/* KPI Cards */}
      {ledger ? (
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(4, 1fr)",
            gap: "var(--space-3)",
            padding: "var(--space-3) var(--space-4)",
            background: "var(--bg-surface)",
            borderBottom: "1px solid var(--border)",
          }}
        >
          <div style={{ padding: "var(--space-2)", background: "var(--bg-sunken)", borderRadius: "var(--radius)" }}>
            <span style={{ fontSize: "var(--fs-caption)", color: "var(--text-muted)" }}>الرصيد الافتتاحي</span>
            <div className="numeric" style={{ fontSize: 16, fontWeight: 700 }}>
              {ledger.openingBalance} {ledger.baseCurrencyCode}
            </div>
          </div>
          <div style={{ padding: "var(--space-2)", background: "var(--bg-sunken)", borderRadius: "var(--radius)" }}>
            <span style={{ fontSize: "var(--fs-caption)", color: "var(--text-muted)" }}>مجموع حركات المدين</span>
            <div className="numeric" style={{ fontSize: 16, fontWeight: 700, color: "var(--success)" }}>
              {ledger.periodDebit} {ledger.baseCurrencyCode}
            </div>
          </div>
          <div style={{ padding: "var(--space-2)", background: "var(--bg-sunken)", borderRadius: "var(--radius)" }}>
            <span style={{ fontSize: "var(--fs-caption)", color: "var(--text-muted)" }}>مجموع حركات الدائن</span>
            <div className="numeric" style={{ fontSize: 16, fontWeight: 700, color: "var(--danger)" }}>
              {ledger.periodCredit} {ledger.baseCurrencyCode}
            </div>
          </div>
          <div style={{ padding: "var(--space-2)", background: "var(--bg-sunken)", borderRadius: "var(--radius)" }}>
            <span style={{ fontSize: "var(--fs-caption)", color: "var(--text-muted)" }}>الرصيد الختامي للأستاذ</span>
            <div className="numeric" style={{ fontSize: 16, fontWeight: 700, color: "var(--primary)" }}>
              {ledger.closingBalance} {ledger.baseCurrencyCode}
            </div>
          </div>
        </div>
      ) : null}

      {/* Movements Grid */}
      <div style={{ flex: 1, overflow: "auto", padding: "var(--space-4)" }}>
        {loading ? (
          <PetraLoadingSkeleton rows={8} />
        ) : error ? (
          <PetraErrorDetails status={error.status} message={error.message} onRetry={() => loadLedger()} />
        ) : !ledger || ledger.lines.length === 0 ? (
          <PetraEmptyState title="لا توجد حركات" description="لا توجد حركات مسجلة لهذا الحساب في الفترة المحددة." />
        ) : (
          <div
            style={{
              background: "var(--bg-surface)",
              border: "1px solid var(--border)",
              borderRadius: "var(--radius)",
              overflow: "hidden",
            }}
          >
            <div
              style={{
                display: "grid",
                gridTemplateColumns: "110px 100px 180px 1fr 120px 120px 130px",
                padding: "var(--space-2) var(--space-4)",
                background: "var(--bg-sunken)",
                fontWeight: 600,
                borderBottom: "1px solid var(--border)",
                fontSize: "var(--fs-caption)",
              }}
            >
              <span>التاريخ</span>
              <span>رقم القيد</span>
              <span>بيان القيد</span>
              <span>شرح الحركة</span>
              <span style={{ textAlign: "end" }}>مدين</span>
              <span style={{ textAlign: "end" }}>دائن</span>
              <span style={{ textAlign: "end" }}>الرصيد الجاري</span>
            </div>

            {ledger.lines.map((line, idx) => (
              <div
                key={idx}
                onDoubleClick={() => openJournal(line.journalId, line.postingSequence)}
                title="انقر نقرًا مزدوجًا لفتح القيد المحاسبي"
                style={{
                  display: "grid",
                  gridTemplateColumns: "110px 100px 180px 1fr 120px 120px 130px",
                  padding: "var(--space-2) var(--space-4)",
                  borderBottom: "1px solid var(--divider)",
                  fontSize: "var(--fs-body)",
                  alignItems: "center",
                  cursor: "pointer",
                }}
              >
                <span className="numeric">{line.accountingDate}</span>
                <span className="numeric" style={{ color: "var(--primary)", fontWeight: 600 }}>
                  #{line.postingSequence ?? "—"}
                </span>
                <span style={{ overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                  {line.journalDescription}
                </span>
                <span style={{ overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                  {line.lineDescription}
                </span>
                <span className="numeric" style={{ color: line.baseDebit && line.baseDebit !== "0" ? "var(--success)" : undefined }}>
                  {line.baseDebit || "—"}
                </span>
                <span className="numeric" style={{ color: line.baseCredit && line.baseCredit !== "0" ? "var(--danger)" : undefined }}>
                  {line.baseCredit || "—"}
                </span>
                <span className="numeric" style={{ fontWeight: 700 }}>
                  {line.runningBalance}
                </span>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
