import { useState, useEffect } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { useWorkspace } from "@/workspace/workspaceStore";
import { PetraButton, PetraLoadingSkeleton, PetraEmptyState, PetraErrorDetails, PetraDialog, PetraField } from "@/components/ui";
import { toast } from "@/components/ui/toastStore";

interface TrialBalanceRow {
  accountId: string;
  accountCode: string;
  accountNameArabic: string;
  level: number;
  hasChildren: boolean;
  openingDebit: string;
  openingCredit: string;
  periodDebit: string;
  periodCredit: string;
  endingDebit: string;
  endingCredit: string;
}

interface TrialBalanceData {
  from?: string | null;
  to: string;
  baseCurrencyCode: string;
  rows: TrialBalanceRow[];
  totalOpeningDebit: string;
  totalOpeningCredit: string;
  totalPeriodDebit: string;
  totalPeriodCredit: string;
  totalEndingDebit: string;
  totalEndingCredit: string;
  isBalanced: boolean;
}

export default function TrialBalanceScreen() {
  const api = useApi();
  const { companyId, branchId } = useSession((s) => ({ companyId: s.companyId, branchId: s.branchId }));
  const openWorkspaceWindow = useWorkspace((s) => s.open);

  const [data, setData] = useState<TrialBalanceData | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<{ status?: number; message?: string } | null>(null);

  const [optionsOpen, setOptionsOpen] = useState(false);
  const [toDate, setToDate] = useState(new Date().toISOString().substring(0, 10));
  const [includeZeroRows, setIncludeZeroRows] = useState(false);
  const [maximumLevel, setMaximumLevel] = useState(4);

  const loadTrialBalance = async () => {
    if (!companyId || !branchId) return;

    setLoading(true);
    setError(null);
    try {
      const res = await api.GET("/api/v1/companies/{companyId}/branches/{branchId}/ledger/reports/trial-balance", {
        params: {
          path: { companyId, branchId },
          query: {
            to: toDate,
            includeZeroRows,
            maximumLevel,
          },
        },
      });

      if (res.error) {
        setError({ status: res.response.status, message: "فشل استخراج ميزان المراجعة من الخادم" });
      } else if (res.data) {
        setData(res.data as unknown as TrialBalanceData);
      }
    } catch (err) {
      setError({ message: err instanceof Error ? err.message : "خطأ غير متوقع" });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadTrialBalance();
  }, [companyId, branchId]);

  const copyToExcel = () => {
    if (!data || data.rows.length === 0) return;
    const header = "رمز الحساب\tاسم الحساب\tافتتاحي مدين\tافتتاحي دائن\tحركة مدين\tحركة دائن\tختامي مدين\tختامي دائن";
    const rows = data.rows.map((r) =>
      `${r.accountCode}\t${r.accountNameArabic}\t${r.openingDebit}\t${r.openingCredit}\t${r.periodDebit}\t${r.periodCredit}\t${r.endingDebit}\t${r.endingCredit}`
    );
    const tsv = [header, ...rows].join("\r\n");
    navigator.clipboard?.writeText(tsv).then(() => {
      toast.success("تم نسخ ميزان المراجعة إلى الحافظة بصيغة Excel (TSV)");
    });
  };

  const openLedger = (row: TrialBalanceRow) => {
    openWorkspaceWindow({
      key: `ledger:${row.accountId}`,
      screen: "account-ledger",
      title: `دفتر أستاذ: ${row.accountNameArabic}`,
      params: { accountId: row.accountId, accountCode: row.accountCode, accountName: row.accountNameArabic },
    });
  };

  return (
    <div style={{ display: "flex", flexDirection: "column", height: "100%", background: "var(--bg-app)" }}>
      {/* Options Dialog */}
      <PetraDialog
        open={optionsOpen}
        onClose={() => setOptionsOpen(false)}
        title="خيارات ميزان المراجعة"
        width={440}
        actions={
          <>
            <PetraButton variant="secondary" onClick={() => setOptionsOpen(false)}>
              إلغاء
            </PetraButton>
            <PetraButton
              variant="primary"
              onClick={() => {
                setOptionsOpen(false);
                loadTrialBalance();
              }}
            >
              تطبيق وعرض
            </PetraButton>
          </>
        }
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "var(--space-3)" }}>
          <PetraField label="حتى تاريخ" required>
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

          <PetraField label="أقصى مستوى في الشجرة">
            <select
              value={maximumLevel}
              onChange={(e) => setMaximumLevel(parseInt(e.target.value, 10))}
              style={{
                height: "var(--control-height)",
                padding: "0 var(--space-2)",
                borderRadius: "var(--radius)",
                border: "1px solid var(--border)",
                background: "var(--bg-input)",
                color: "var(--text)",
              }}
            >
              <option value={1}>المستوى 1 (الحسابات الرئيسية)</option>
              <option value={2}>المستوى 2</option>
              <option value={3}>المستوى 3</option>
              <option value={4}>المستوى 4 (الحسابات التفصيلية)</option>
              <option value={5}>المستوى 5 (كافة التفرعات)</option>
            </select>
          </PetraField>

          <label style={{ display: "flex", alignItems: "center", gap: "var(--space-2)", cursor: "pointer", fontSize: "var(--fs-body)" }}>
            <input
              type="checkbox"
              checked={includeZeroRows}
              onChange={(e) => setIncludeZeroRows(e.target.checked)}
            />
            <span>إظهار الحسابات ذات الرصيد الصفري والحركات المعدومة</span>
          </label>
        </div>
      </PetraDialog>

      {/* Top Header */}
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
        <div style={{ display: "flex", alignItems: "center", gap: "var(--space-3)" }}>
          <h2 style={{ margin: 0, fontSize: "var(--fs-title)", fontWeight: 700 }}>
            ميزان المراجعة العام (Trial Balance)
          </h2>
          {data ? (
            <span style={{ fontSize: "var(--fs-caption)", color: "var(--text-muted)" }}>
              حتى تاريخ {data.to} [{data.baseCurrencyCode}]
            </span>
          ) : null}
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: "var(--space-2)" }}>
          <PetraButton variant="secondary" size="sm" onClick={() => setOptionsOpen(true)}>
            خيارات الميزان ⚙
          </PetraButton>
          <PetraButton variant="secondary" size="sm" onClick={loadTrialBalance} loading={loading}>
            تحديث
          </PetraButton>
          <PetraButton variant="secondary" size="sm" onClick={copyToExcel} disabled={!data || data.rows.length === 0}>
            نسخ إلى Excel
          </PetraButton>
        </div>
      </div>

      {/* Main Table */}
      <div style={{ flex: 1, overflow: "auto", padding: "var(--space-4)" }}>
        {loading ? (
          <PetraLoadingSkeleton rows={10} />
        ) : error ? (
          <PetraErrorDetails status={error.status} message={error.message} onRetry={loadTrialBalance} />
        ) : !data || data.rows.length === 0 ? (
          <PetraEmptyState title="لا توجد بيانات" description="لا توجد حسابات مسجلة لميزان المراجعة." />
        ) : (
          <div
            style={{
              background: "var(--bg-surface)",
              border: "1px solid var(--border)",
              borderRadius: "var(--radius)",
              overflow: "hidden",
            }}
          >
            {/* Header Columns */}
            <div
              style={{
                display: "grid",
                gridTemplateColumns: "110px 1.5fr repeat(6, 110px)",
                padding: "var(--space-2) var(--space-4)",
                background: "var(--bg-sunken)",
                fontWeight: 600,
                borderBottom: "1px solid var(--border)",
                fontSize: "var(--fs-caption)",
                alignItems: "center",
              }}
            >
              <span>رمز الحساب</span>
              <span>اسم الحساب</span>
              <span style={{ textAlign: "end" }}>افتتاحي مدين</span>
              <span style={{ textAlign: "end" }}>افتتاحي دائن</span>
              <span style={{ textAlign: "end" }}>حركة مدين</span>
              <span style={{ textAlign: "end" }}>حركة دائن</span>
              <span style={{ textAlign: "end" }}>ختامي مدين</span>
              <span style={{ textAlign: "end" }}>ختامي دائن</span>
            </div>

            {/* Rows */}
            {data.rows.map((row) => {
              const indent = (row.level - 1) * 12;
              return (
                <div
                  key={row.accountId}
                  onDoubleClick={() => openLedger(row)}
                  title="انقر نقرًا مزدوجًا لفتح دفتر أستاذ الحساب"
                  style={{
                    display: "grid",
                    gridTemplateColumns: "110px 1.5fr repeat(6, 110px)",
                    padding: "var(--space-2) var(--space-4)",
                    borderBottom: "1px solid var(--divider)",
                    alignItems: "center",
                    fontSize: "var(--fs-body)",
                    cursor: "pointer",
                    background: row.hasChildren ? "var(--bg-sunken)" : "transparent",
                    fontWeight: row.hasChildren ? 600 : 400,
                  }}
                >
                  <span className="numeric" style={{ color: "var(--primary)" }}>
                    {row.accountCode}
                  </span>
                  <span style={{ paddingInlineStart: indent }}>
                    {row.accountNameArabic}
                  </span>
                  <span className="numeric">{row.openingDebit || "—"}</span>
                  <span className="numeric">{row.openingCredit || "—"}</span>
                  <span className="numeric" style={{ color: "var(--success)" }}>{row.periodDebit || "—"}</span>
                  <span className="numeric" style={{ color: "var(--danger)" }}>{row.periodCredit || "—"}</span>
                  <span className="numeric" style={{ fontWeight: 600 }}>{row.endingDebit || "—"}</span>
                  <span className="numeric" style={{ fontWeight: 600 }}>{row.endingCredit || "—"}</span>
                </div>
              );
            })}
          </div>
        )}
      </div>

      {/* Totals Bar */}
      {data ? (
        <footer
          style={{
            display: "grid",
            gridTemplateColumns: "110px 1.5fr repeat(6, 110px)",
            padding: "var(--space-2) var(--space-4)",
            background: "var(--header-bg)",
            color: "var(--header-text)",
            borderTop: "1px solid var(--border)",
            fontSize: "var(--fs-caption)",
            fontWeight: 700,
            alignItems: "center",
          }}
        >
          <span>المجاميع العامة:</span>
          <span>
            {data.isBalanced ? "✓ ميزان المراجعة متوازن تماماً" : "✕ يوجد عدم توازن"}
          </span>
          <span className="numeric" style={{ textAlign: "end" }}>{data.totalOpeningDebit}</span>
          <span className="numeric" style={{ textAlign: "end" }}>{data.totalOpeningCredit}</span>
          <span className="numeric" style={{ textAlign: "end" }}>{data.totalPeriodDebit}</span>
          <span className="numeric" style={{ textAlign: "end" }}>{data.totalPeriodCredit}</span>
          <span className="numeric" style={{ textAlign: "end" }}>{data.totalEndingDebit}</span>
          <span className="numeric" style={{ textAlign: "end" }}>{data.totalEndingCredit}</span>
        </footer>
      ) : null}
    </div>
  );
}
