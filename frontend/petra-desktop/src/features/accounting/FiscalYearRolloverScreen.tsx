import { useState, useEffect } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { useWorkspace } from "@/workspace/workspaceStore";
import {
  PetraButton,
  PetraField,
  PetraDialog,
  PetraEmptyState,
  PetraLoadingSkeleton,
  PetraErrorDetails,
} from "@/components/ui";
import { LookupPicker, type LookupItem } from "@/components/lookup/LookupPicker";
import { toast } from "@/components/ui/toastStore";
import type { components } from "@/services/api/schema";

export interface FiscalYearRolloverScreenProps {
  params?: Readonly<Record<string, string>> | undefined;
}

interface FiscalYear {
  id: string;
  code: string;
  rangeStart: string;
  rangeEnd: string;
}

type RolloverPreviewResponse = components["schemas"]["RolloverPreviewResponse"];
type RolloverAccountLine = components["schemas"]["RolloverAccountLineResponse"];
type RolloverResultResponse = components["schemas"]["RolloverResultResponse"];

export default function FiscalYearRolloverScreen({ params: _params }: FiscalYearRolloverScreenProps) {
  const api = useApi();
  const { companyId, branchId } = useSession((s) => ({ companyId: s.companyId, branchId: s.branchId }));
  const openWorkspaceWindow = useWorkspace((s) => s.open);

  const [fiscalYears, setFiscalYears] = useState<FiscalYear[]>([]);
  const [sourceFiscalYearId, setSourceFiscalYearId] = useState<string>("");
  const [targetFiscalYearId, setTargetFiscalYearId] = useState<string>("");
  const [retainedEarningsAccount, setRetainedEarningsAccount] = useState<LookupItem | null>(null);

  const [isLoadingYears, setIsLoadingYears] = useState(false);
  const [isPreviewing, setIsPreviewing] = useState(false);
  const [previewData, setPreviewData] = useState<RolloverPreviewResponse | null>(null);
  const [error, setError] = useState<{ status?: number; message: string } | null>(null);

  // Execution dialog state
  const [isExecOpen, setIsExecOpen] = useState(false);
  const [reason, setReason] = useState<string>("إغلاق السنة المالية وترحيل الأرصدة الافتتاحية والذمم والمخزون للسنة الجديدة");
  const [isExecuting, setIsExecuting] = useState(false);
  const [lastResult, setLastResult] = useState<RolloverResultResponse | null>(null);

  useEffect(() => {
    if (!companyId || !branchId) return;
    const fetchYears = async () => {
      setIsLoadingYears(true);
      try {
        const res = await api.GET(
          "/api/v1/companies/{companyId}/branches/{branchId}/ledger/calendar/fiscal-years",
          {
            params: { path: { companyId, branchId } },
          }
        );
        if (res.data) {
          const list = res.data as unknown as FiscalYear[];
          setFiscalYears(list);
          if (list.length >= 2) {
            // Pick most recent two as source and target
            setSourceFiscalYearId(list[0]?.id ?? "");
            setTargetFiscalYearId(list[1]?.id ?? "");
          } else if (list.length === 1) {
            setSourceFiscalYearId(list[0]?.id ?? "");
          }
        }
      } catch (err) {
        toast.error("فشل تحميل قائمة السنوات المالية");
      } finally {
        setIsLoadingYears(false);
      }
    };
    fetchYears();
  }, [companyId, branchId]);

  const searchAccounts = async (query: string, signal: AbortSignal): Promise<readonly LookupItem[]> => {
    if (!companyId) return [];
    try {
      const res = await api.GET(
        "/api/v1/companies/{companyId}/branches/{branchId}/ledger/accounts",
        {
          params: { path: { companyId, branchId: companyId } },
          signal,
        }
      );
      const accounts = res.data ?? [];
      const q = query.toLowerCase();
      return accounts
        .filter((a) => !q || a.code?.toLowerCase().includes(q) || a.nameArabic?.toLowerCase().includes(q))
        .map((a) => ({
          id: a.id ?? "",
          code: a.code ?? "",
          name: a.nameArabic ?? "",
        }));
    } catch {
      return [];
    }
  };

  const handlePreview = async () => {
    if (!companyId || !sourceFiscalYearId || !targetFiscalYearId) {
      toast.warning("يرجى اختيار السنة المصدر والسنة الهدف");
      return;
    }
    if (sourceFiscalYearId === targetFiscalYearId) {
      toast.warning("لا يمكن تدوير السنة المالية إلى نفس السنة");
      return;
    }

    setIsPreviewing(true);
    setError(null);
    setLastResult(null);

    try {
      const res = await api.POST("/api/v1/workspaces/rollover/preview", {
        body: {
          companyId,
          sourceFiscalYearId,
          targetFiscalYearId,
          retainedEarningsAccountId: retainedEarningsAccount?.id ?? null,
        },
      });

      if (res.error || !res.data) {
        setError({
          status: res.response?.status,
          message: (res.error as { title?: string; detail?: string })?.detail ?? "فشل معاينة تدوير السنة المالية",
        });
        return;
      }

      setPreviewData(res.data);
      toast.success("تمت معاينة تدوير السنة المالية وحساب صافي الدخل بنجاح");
    } catch (err) {
      setError({
        message: err instanceof Error ? err.message : "خطأ غير متوقع أثناء المعاينة",
      });
    } finally {
      setIsPreviewing(false);
    }
  };

  const handleExecute = async () => {
    if (!companyId || !previewData) return;
    if (!reason.trim()) {
      toast.warning("يرجى إدخال سبب تنفيذ التدوير المالي");
      return;
    }

    setIsExecuting(true);
    try {
      const res = await api.POST("/api/v1/workspaces/rollover/execute", {
        body: {
          companyId,
          sourceFiscalYearId: previewData.sourceFiscalYearId,
          targetFiscalYearId: previewData.targetFiscalYearId,
          retainedEarningsAccountId: previewData.retainedEarningsAccountId,
          sourceFingerprint: previewData.sourceFingerprint,
          reason,
          idempotencyKey: `rollover-${previewData.sourceFiscalYearId}-${Date.now()}`,
        },
      });

      if (res.error || !res.data) {
        toast.error((res.error as { title?: string; detail?: string })?.detail ?? "فشل تنفيذ تدوير السنة المالية");
        return;
      }

      setLastResult(res.data);
      setIsExecOpen(false);
      toast.success("تم تنفيذ التدوير المالي وتوليد الأرصدة الافتتاحية بنجاح");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "خطأ أثناء تنفيذ التدوير");
    } finally {
      setIsExecuting(false);
    }
  };

  const exportTsv = () => {
    if (!previewData || !previewData.accountLines || previewData.accountLines.length === 0) {
      toast.warning("لا توجد بيانات للتصدير");
      return;
    }

    const headers = [
      "رمز الحساب",
      "اسم الحساب",
      "النوع",
      "الرصيد المدين المقفل",
      "الرصيد الدائن المقفل",
      "العملة",
    ];

    const rows = previewData.accountLines.map((l: RolloverAccountLine) => [
      l.accountCode,
      l.accountNameArabic,
      l.accountType,
      l.closingDebit,
      l.closingCredit,
      l.currencyCode,
    ]);

    const tsvContent = [headers.join("\t"), ...rows.map((row) => row.join("\t"))].join("\n");
    const blob = new Blob(["\uFEFF" + tsvContent], { type: "text/tab-separated-values;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `rollover-preview-${previewData.sourceYearCode}-to-${previewData.targetYearCode}.tsv`;
    a.click();
    URL.revokeObjectURL(url);
    toast.success("تم تصدير تقرير التدوير المالي بنجاح");
  };

  return (
    <div className="fiscal-year-rollover-screen" style={{ padding: "16px", height: "100%", display: "flex", flexDirection: "column", gap: "16px", overflowY: "auto" }}>
      {/* Header */}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", borderBottom: "1px solid var(--border)", paddingBottom: "12px" }}>
        <div>
          <h2 style={{ margin: 0, fontSize: "1.25rem", color: "var(--header-bg)" }}>
            تدوير السنة المالية (Fiscal Year Rollover)
          </h2>
          <p style={{ margin: "4px 0 0", fontSize: "0.85rem", color: "var(--text-muted)" }}>
            إغلاق السنة الحالية وحساب صافي الأرباح/الخسائر وتدوير الأرصدة والذمم المفتوحة وبضاعة المخزون إلى السنة الهدف.
          </p>
        </div>
        <div style={{ display: "flex", gap: "8px" }}>
          <PetraButton variant="secondary" onClick={exportTsv} disabled={!previewData} size="sm">
            تصدير TSV
          </PetraButton>
          <PetraButton variant="secondary" onClick={handlePreview} disabled={isPreviewing || isLoadingYears} size="sm">
            {isPreviewing ? "جارٍ المعاينة..." : "معاينة التدوير (F5)"}
          </PetraButton>
          <PetraButton
            variant="primary"
            onClick={() => setIsExecOpen(true)}
            disabled={!previewData || isExecuting}
            size="sm"
          >
            اعتماد وتنفيذ التدوير
          </PetraButton>
        </div>
      </div>

      {/* Filter panel */}
      <div style={{ background: "var(--bg-surface)", border: "1px solid var(--border)", borderRadius: "4px", padding: "12px", display: "flex", flexWrap: "wrap", gap: "16px", alignItems: "flex-end" }}>
        <PetraField label="السنة المالية المصدر (السنة المراد إغلاقها)">
          <select
            className="petra-select"
            value={sourceFiscalYearId}
            onChange={(e) => setSourceFiscalYearId(e.target.value)}
            style={{ minWidth: "160px", padding: "5px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
          >
            <option value="">-- اختر السنة المصدر --</option>
            {fiscalYears.map((fy) => (
              <option key={fy.id} value={fy.id}>
                السنة: {fy.code} ({fy.rangeStart} إلى {fy.rangeEnd})
              </option>
            ))}
          </select>
        </PetraField>

        <PetraField label="السنة المالية الهدف (السنة الجديدة المستقبلة)">
          <select
            className="petra-select"
            value={targetFiscalYearId}
            onChange={(e) => setTargetFiscalYearId(e.target.value)}
            style={{ minWidth: "160px", padding: "5px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
          >
            <option value="">-- اختر السنة الهدف --</option>
            {fiscalYears.map((fy) => (
              <option key={fy.id} value={fy.id}>
                السنة: {fy.code} ({fy.rangeStart} إلى {fy.rangeEnd})
              </option>
            ))}
          </select>
        </PetraField>

        <div style={{ width: "280px" }}>
          <PetraField label="حساب الأرباح المدورة / المحتجزة (اختياري)">
            <LookupPicker
              ariaLabel="حساب الأرباح المحتجزة"
              placeholder="ابحث عن حساب الأرباح المدورة..."
              search={searchAccounts}
              value={retainedEarningsAccount}
              onChange={setRetainedEarningsAccount}
            />
          </PetraField>
        </div>
      </div>

      {/* Execution Success Banner */}
      {lastResult && (
        <div style={{ background: "#E8F5E9", border: "1px solid #A5D6A7", borderRadius: "4px", padding: "12px", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
          <div>
            <div style={{ fontWeight: "bold", color: "#2E7D32", fontSize: "0.95rem" }}>
              تم تنفيذ تدوير السنة المالية بنجاح: {lastResult.message}
            </div>
            <div style={{ fontSize: "0.8rem", color: "#1B5E20", marginTop: "4px" }}>
              حزمة الأرصدة الافتتاحية: {lastResult.openingBalanceBatchId} | الذمم المدورة: {lastResult.openItemsCreated} | حركات المخزون: {lastResult.stockMovementsCreated}
            </div>
          </div>
          {lastResult.openingJournalId && (
            <PetraButton
              variant="secondary"
              size="sm"
              onClick={() => {
                openWorkspaceWindow({
                  key: `journal-${lastResult.openingJournalId}`,
                  title: `قيد افتتاحي للسنة الجديدة`,
                  screen: "journal-entry",
                  params: { id: lastResult.openingJournalId },
                });
              }}
            >
              عرض القيد الافتتاحي
            </PetraButton>
          )}
        </div>
      )}

      {/* Preview KPI Cards */}
      {previewData && (
        <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))", gap: "12px" }}>
          <div style={{ background: "var(--bg-surface)", border: "1px solid var(--border)", padding: "12px", borderRadius: "4px" }}>
            <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>إجمالي الإيرادات المقفلة</div>
            <div style={{ fontSize: "1.3rem", fontWeight: "bold", fontFamily: "var(--font-mono, monospace)", color: "#2E7D32" }}>
              {previewData.totalRevenues}
            </div>
          </div>
          <div style={{ background: "var(--bg-surface)", border: "1px solid var(--border)", padding: "12px", borderRadius: "4px" }}>
            <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>إجمالي المصروفات المقفلة</div>
            <div style={{ fontSize: "1.3rem", fontWeight: "bold", fontFamily: "var(--font-mono, monospace)", color: "#C62828" }}>
              {previewData.totalExpenses}
            </div>
          </div>
          <div style={{ background: Number(previewData.netIncome) >= 0 ? "#E8F5E9" : "#FFEBEE", border: "1px solid var(--border)", padding: "12px", borderRadius: "4px" }}>
            <div style={{ fontSize: "0.8rem", color: Number(previewData.netIncome) >= 0 ? "#2E7D32" : "#C62828" }}>صافي الدخل (أرباح/خسائر النشاط)</div>
            <div style={{ fontSize: "1.3rem", fontWeight: "bold", fontFamily: "var(--font-mono, monospace)", color: Number(previewData.netIncome) >= 0 ? "#2E7D32" : "#C62828" }}>
              {previewData.netIncome}
            </div>
          </div>
          <div style={{ background: previewData.isTrialBalanceBalanced ? "#F0FDF4" : "#FEF2F2", border: "1px solid var(--border)", padding: "12px", borderRadius: "4px" }}>
            <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>توازن ميزان المراجعة المقفل</div>
            <div style={{ fontSize: "1rem", fontWeight: "bold", color: previewData.isTrialBalanceBalanced ? "#16A34A" : "#DC2626", marginTop: "4px" }}>
              {previewData.isTrialBalanceBalanced ? "✓ ميزان المراجعة متوازن تماماً" : "⚠ غير متوازن! يوجد فرق"}
            </div>
            <div style={{ fontSize: "0.75rem", fontFamily: "var(--font-mono, monospace)", color: "var(--text-muted)", marginTop: "2px" }}>
              مدين: {previewData.totalBalanceSheetDebit} | دائن: {previewData.totalBalanceSheetCredit}
            </div>
          </div>
          <div style={{ background: "var(--bg-surface)", border: "1px solid var(--border)", padding: "12px", borderRadius: "4px" }}>
            <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>الذمم المفتوحة المدورة</div>
            <div style={{ fontSize: "1.15rem", fontWeight: "bold", color: "var(--primary)" }}>
              {previewData.openItemsCount} بند ({previewData.openItemsTotalAmount})
            </div>
          </div>
          <div style={{ background: "var(--bg-surface)", border: "1px solid var(--border)", padding: "12px", borderRadius: "4px" }}>
            <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>بضاعة آخر المدة المدورة</div>
            <div style={{ fontSize: "1.15rem", fontWeight: "bold", color: "var(--header-bg)" }}>
              {previewData.stockItemsCount} مادة ({previewData.stockTotalValue})
            </div>
          </div>
        </div>
      )}

      {/* Main Grid */}
      {isPreviewing ? (
        <PetraLoadingSkeleton rows={8} />
      ) : error ? (
        <PetraErrorDetails status={error.status} message={error.message} onRetry={handlePreview} />
      ) : !previewData ? (
        <PetraEmptyState title="اختر السنة المصدر والسنة الهدف ثم اضغط 'معاينة التدوير' لفحص حسابات الإقفال والأرصدة" />
      ) : (
        <div style={{ flex: 1, overflowX: "auto", border: "1px solid var(--border)", borderRadius: "4px", background: "var(--bg-surface)" }}>
          <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.85rem", textAlign: "right" }}>
            <thead>
              <tr style={{ background: "var(--header-bg)", color: "#FFFFFF" }}>
                <th style={{ padding: "8px" }}>رمز الحساب</th>
                <th style={{ padding: "8px" }}>اسم الحساب</th>
                <th style={{ padding: "8px" }}>الرصيد المدين المقفل</th>
                <th style={{ padding: "8px" }}>الرصيد الدائن المقفل</th>
                <th style={{ padding: "8px" }}>العملة</th>
              </tr>
            </thead>
            <tbody>
              {previewData.accountLines.map((line: RolloverAccountLine, idx: number) => (
                <tr
                  key={line.accountId || idx}
                  style={{
                    borderBottom: "1px solid var(--border)",
                    background: idx % 2 === 0 ? "transparent" : "var(--bg-app)",
                  }}
                >
                  <td style={{ padding: "8px", fontWeight: "bold" }}>
                    <span
                      style={{ color: "var(--primary)", cursor: "pointer", textDecoration: "underline" }}
                      onClick={() => {
                        openWorkspaceWindow({
                          key: `account-ledger-${line.accountId}`,
                          title: `دفتر أستاذ: ${line.accountCode}`,
                          screen: "account-ledger",
                          params: { accountId: line.accountId },
                        });
                      }}
                    >
                      {line.accountCode}
                    </span>
                  </td>
                  <td style={{ padding: "8px" }}>{line.accountNameArabic}</td>
                  <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)" }}>{line.closingDebit}</td>
                  <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)" }}>{line.closingCredit}</td>
                  <td style={{ padding: "8px" }}>{line.currencyCode}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* Confirmation Dialog */}
      <PetraDialog
        open={isExecOpen}
        onClose={() => setIsExecOpen(false)}
        title="تأكيد تنفيذ تدوير السنة المالية"
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "16px" }}>
          <div style={{ background: "#FFFBEB", border: "1px solid #FDE68A", borderRadius: "4px", padding: "12px", fontSize: "0.85rem", color: "#92400E" }}>
            <strong>تحذير أمان مالي:</strong> هذه العملية ستقوم بإقفال حسابات الأرباح والخسائر ونقل صافي الدخل، وإنشاء حزمة أرصدة افتتاحية معتمدة وقيد افتتاحي في السنة الهدف <strong>{previewData?.targetYearCode}</strong>، بالإضافة لتدوير كافة فواتير الذمم غير المسددة وبضاعة المخزون.
          </div>

          <PetraField label="سبب ومبرر التدوير المالي (إلزامي للتدقيق والامتثال)">
            <textarea
              className="petra-input"
              rows={3}
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              style={{ width: "100%", padding: "6px 8px", border: "1px solid var(--border)", borderRadius: "3px", resize: "vertical" }}
            />
          </PetraField>

          <div style={{ display: "flex", justifyContent: "flex-end", gap: "8px", marginTop: "8px" }}>
            <PetraButton variant="secondary" onClick={() => setIsExecOpen(false)}>
              إلغاء
            </PetraButton>
            <PetraButton variant="primary" onClick={handleExecute} disabled={isExecuting}>
              {isExecuting ? "جارٍ تنفيذ التدوير..." : "تأكيد وبدء التدوير المالي"}
            </PetraButton>
          </div>
        </div>
      </PetraDialog>
    </div>
  );
}
