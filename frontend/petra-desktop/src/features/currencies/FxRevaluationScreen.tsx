import { useState, useId } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { useWorkspace } from "@/workspace/workspaceStore";
import { PetraButton, PetraLoadingSkeleton, PetraEmptyState, PetraErrorDetails, PetraDialog, PetraField } from "@/components/ui";
import { LookupPicker, type LookupItem } from "@/components/lookup/LookupPicker";
import { toast } from "@/components/ui/toastStore";
import type { components } from "@/services/api/schema";

export interface FxRevaluationScreenProps {
  params?: Readonly<Record<string, string>> | undefined;
}

type FxPreviewResponse = components["schemas"]["FxRevaluationPreviewResponse"];
type FxItem = components["schemas"]["FxAccountRevaluationItemDto"];
type FxExecutionResponse = components["schemas"]["FxRevaluationExecutionResponse"];

export default function FxRevaluationScreen({ params: _params }: FxRevaluationScreenProps) {
  const api = useApi();
  const { companyId, branchId } = useSession((s) => ({ companyId: s.companyId, branchId: s.branchId }));
  const openWorkspaceWindow = useWorkspace((s) => s.open);
  const defaultIdempotency = useId();

  const [asOfDate, setAsOfDate] = useState<string>(new Date().toISOString().slice(0, 10));
  const [currencyCode, setCurrencyCode] = useState<string>("");
  const [overrideRate, setOverrideRate] = useState<string>("");

  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<{ status?: number; message?: string } | null>(null);
  const [previewData, setPreviewData] = useState<FxPreviewResponse | null>(null);
  const [selectedAccountIds, setSelectedAccountIds] = useState<Set<string>>(new Set());

  // Execution dialog state
  const [isExecOpen, setIsExecOpen] = useState(false);
  const [gainAccount, setGainAccount] = useState<LookupItem | null>(null);
  const [lossAccount, setLossAccount] = useState<LookupItem | null>(null);
  const [execDescription, setExecDescription] = useState<string>("قيد إعادة تقييم أرصدة العملات الأجنبية في نهاية الفترة");
  const [isExecuting, setIsExecuting] = useState(false);
  const [lastExecution, setLastExecution] = useState<FxExecutionResponse | null>(null);

  const searchAccounts = async (query: string, signal: AbortSignal): Promise<readonly LookupItem[]> => {
    if (!companyId || !branchId) return [];
    try {
      const res = await api.GET(
        "/api/v1/companies/{companyId}/branches/{branchId}/ledger/accounts",
        {
          params: { path: { companyId, branchId } },
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
    if (!companyId || !branchId) return;

    setIsLoading(true);
    setError(null);
    setLastExecution(null);

    try {
      const res = await api.POST(
        "/api/v1/companies/{companyId}/branches/{branchId}/currencies/fx-revaluation/preview",
        {
          params: {
            path: { companyId, branchId },
          },
          body: {
            asOfDate,
            ...(currencyCode ? { currencyCode } : {}),
            ...(overrideRate ? { overrideClosingRate: overrideRate } : {}),
          },
        }
      );

      if (res.error || !res.data) {
        setError({
          status: res.response?.status,
          message: (res.error as { title?: string; detail?: string })?.detail ?? "فشل معاينة إعادة التقييم",
        });
        return;
      }

      setPreviewData(res.data);
      const items = res.data.items ?? [];
      setSelectedAccountIds(new Set(items.map((i) => i.accountId)));
      toast.success(`تمت معاينة ${items.length} حساب بالعملات الأجنبية`);
    } catch (err) {
      setError({
        message: err instanceof Error ? err.message : "خطأ غير متوقع",
      });
    } finally {
      setIsLoading(false);
    }
  };

  const items: FxItem[] = previewData?.items ?? [];

  const toggleSelectAll = () => {
    if (selectedAccountIds.size === items.length) {
      setSelectedAccountIds(new Set());
    } else {
      setSelectedAccountIds(new Set(items.map((i) => i.accountId)));
    }
  };

  const handleExecute = async () => {
    if (!companyId || !branchId) return;
    if (!gainAccount?.id || !lossAccount?.id) {
      toast.warning("يرجى اختيار حساب أرباح فروقات الصرف وحساب خسائر فروقات الصرف");
      return;
    }

    setIsExecuting(true);
    try {
      const res = await api.POST(
        "/api/v1/companies/{companyId}/branches/{branchId}/currencies/fx-revaluation/execute",
        {
          params: {
            path: { companyId, branchId },
          },
          body: {
            asOfDate,
            gainAccountId: gainAccount.id,
            lossAccountId: lossAccount.id,
            idempotencyKey: `fx-rev-${Date.now()}-${defaultIdempotency.replace(/[:]/g, "")}`,
            ...(currencyCode ? { currencyCode } : {}),
            ...(overrideRate ? { overrideClosingRate: overrideRate } : {}),
            description: execDescription,
            selectedAccountIds: Array.from(selectedAccountIds),
          },
        }
      );

      if (res.error || !res.data) {
        toast.error((res.error as { title?: string; detail?: string })?.detail ?? "فشل توليد قيد إعادة التقييم");
        return;
      }

      setLastExecution(res.data);
      setIsExecOpen(false);
      toast.success(`تم توليد قيد إعادة التقييم بنجاح: ${res.data.journalEntryNumber}`);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "خطأ أثناء تنفيذ التقييم");
    } finally {
      setIsExecuting(false);
    }
  };

  const exportTsv = () => {
    if (items.length === 0) {
      toast.warning("لا توجد بيانات للتصدير");
      return;
    }

    const headers = [
      "رمز الحساب",
      "اسم الحساب",
      "العملة",
      "الرصيد بالعملة الأجنبية",
      "الرصيد الدفتري الحالي (أساس)",
      "سعر الصرف للإقفال",
      "الرصيد بعد إعادة التقييم (أساس)",
      "فارق التقييم غير المحقق",
      "نوع النتيجة",
    ];

    const rows = items.map((i) => [
      i.accountCode,
      i.accountNameArabic,
      i.currencyCode,
      i.foreignBalance,
      i.bookBaseBalance,
      i.closingRate,
      i.revaluedBaseBalance,
      i.unrealizedDifference,
      i.isGain ? "ربح" : "خسارة",
    ]);

    const tsvContent = [headers.join("\t"), ...rows.map((row) => row.join("\t"))].join("\n");
    const blob = new Blob(["\uFEFF" + tsvContent], { type: "text/tab-separated-values;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `fx-revaluation-${asOfDate}.tsv`;
    a.click();
    URL.revokeObjectURL(url);
    toast.success("تم تصدير تقرير إعادة التقييم بنجاح");
  };

  return (
    <div className="fx-revaluation-screen" style={{ padding: "16px", height: "100%", display: "flex", flexDirection: "column", gap: "16px", overflowY: "auto" }}>
      {/* Header */}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", borderBottom: "1px solid var(--border)", paddingBottom: "12px" }}>
        <div>
          <h2 style={{ margin: 0, fontSize: "1.25rem", color: "var(--header-bg)" }}>
            إعادة تقييم فروقات أسعار الصرف (FX Revaluation)
          </h2>
          <p style={{ margin: "4px 0 0", fontSize: "0.85rem", color: "var(--text-muted)" }}>
            حساب الفروقات غير المحققة للأرصدة بالعملات الأجنبية وتوليد قيد التسوية المحاسبي للأستاذ العام.
          </p>
        </div>
        <div style={{ display: "flex", gap: "8px" }}>
          <PetraButton variant="secondary" onClick={exportTsv} disabled={items.length === 0} size="sm">
            تصدير TSV
          </PetraButton>
          <PetraButton variant="secondary" onClick={handlePreview} disabled={isLoading} size="sm">
            {isLoading ? "جارٍ المعاينة..." : "معاينة الفروقات (F5)"}
          </PetraButton>
          <PetraButton
            variant="primary"
            onClick={() => setIsExecOpen(true)}
            disabled={selectedAccountIds.size === 0 || isLoading}
            size="sm"
          >
            توليد قيد التسوية ({selectedAccountIds.size})
          </PetraButton>
        </div>
      </div>

      {/* Filter panel */}
      <div style={{ background: "var(--bg-surface)", border: "1px solid var(--border)", borderRadius: "4px", padding: "12px", display: "flex", flexWrap: "wrap", gap: "16px", alignItems: "flex-end" }}>
        <PetraField label="تاريخ إعادة التقييم (As Of Date)">
          <input
            type="date"
            className="petra-input"
            value={asOfDate}
            onChange={(e) => setAsOfDate(e.target.value)}
            style={{ padding: "5px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
          />
        </PetraField>
        <PetraField label="تصفية حسب العملة (اختياري)">
          <input
            type="text"
            className="petra-input"
            placeholder="مثال: USD"
            value={currencyCode}
            onChange={(e) => setCurrencyCode(e.target.value)}
            style={{ width: "120px", padding: "5px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
          />
        </PetraField>
        <PetraField label="تجاوز سعر الصرف (اختياري)">
          <input
            type="text"
            className="petra-input"
            placeholder="سعر محدد..."
            value={overrideRate}
            onChange={(e) => setOverrideRate(e.target.value)}
            style={{ width: "130px", padding: "5px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
          />
        </PetraField>
      </div>

      {/* Execution Success Banner */}
      {lastExecution && (
        <div style={{ background: "#E8F5E9", border: "1px solid #A5D6A7", borderRadius: "4px", padding: "12px", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
          <div>
            <div style={{ fontWeight: "bold", color: "#2E7D32", fontSize: "0.95rem" }}>
              تم توليد قيد إعادة التقييم بنجاح: {lastExecution.journalEntryNumber}
            </div>
            <div style={{ fontSize: "0.8rem", color: "#1B5E20", marginTop: "4px" }}>
              عدد الحسابات: {lastExecution.revaluedAccountsCount} | أرباح: {lastExecution.totalGainAmount} | خسائر: {lastExecution.totalLossAmount} | صافي التقييم: {lastExecution.netRevaluationAmount}
            </div>
          </div>
          <PetraButton
            variant="secondary"
            size="sm"
            onClick={() => {
              openWorkspaceWindow({
                key: `journal-${lastExecution.journalEntryId}`,
                title: `قيد تقييم: ${lastExecution.journalEntryNumber}`,
                screen: "journal-entry",
                params: { id: lastExecution.journalEntryId },
              });
            }}
          >
            عرض القيد في تبويب جديد
          </PetraButton>
        </div>
      )}

      {/* KPI Cards */}
      {previewData && (
        <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))", gap: "12px" }}>
          <div style={{ background: "#E8F5E9", border: "1px solid #A5D6A7", padding: "12px", borderRadius: "4px" }}>
            <div style={{ fontSize: "0.8rem", color: "#2E7D32" }}>إجمالي الأرباح غير المحققة</div>
            <div style={{ fontSize: "1.35rem", fontWeight: "bold", fontFamily: "var(--font-mono, monospace)", color: "#2E7D32" }}>
              {previewData.totalUnrealizedGains} {previewData.baseCurrencyCode}
            </div>
          </div>
          <div style={{ background: "#FFEBEE", border: "1px solid #FFCDD2", padding: "12px", borderRadius: "4px" }}>
            <div style={{ fontSize: "0.8rem", color: "#C62828" }}>إجمالي الخسائر غير المحققة</div>
            <div style={{ fontSize: "1.35rem", fontWeight: "bold", fontFamily: "var(--font-mono, monospace)", color: "#C62828" }}>
              {previewData.totalUnrealizedLosses} {previewData.baseCurrencyCode}
            </div>
          </div>
          <div style={{ background: "var(--bg-surface)", border: "1px solid var(--border)", padding: "12px", borderRadius: "4px" }}>
            <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>صافي الفارق المحاسبي للتقييم</div>
            <div style={{ fontSize: "1.35rem", fontWeight: "bold", fontFamily: "var(--font-mono, monospace)", color: "var(--header-bg)" }}>
              {previewData.netDifference} {previewData.baseCurrencyCode}
            </div>
          </div>
          <div style={{ background: "var(--bg-surface)", border: "1px solid var(--border)", padding: "12px", borderRadius: "4px" }}>
            <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>عدد الحسابات المؤهلة</div>
            <div style={{ fontSize: "1.35rem", fontWeight: "bold", color: "var(--primary)" }}>
              {items.length}
            </div>
          </div>
        </div>
      )}

      {/* Main Table */}
      {isLoading ? (
        <PetraLoadingSkeleton rows={8} />
      ) : error ? (
        <PetraErrorDetails status={error.status} message={error.message} onRetry={handlePreview} />
      ) : items.length === 0 ? (
        <PetraEmptyState title="اضغط 'معاينة الفروقات' لجلب أرصدة الحسابات الأجنبية في التاريخ المحدد" />
      ) : (
        <div style={{ flex: 1, overflowX: "auto", border: "1px solid var(--border)", borderRadius: "4px", background: "var(--bg-surface)" }}>
          <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.85rem", textAlign: "right" }}>
            <thead>
              <tr style={{ background: "var(--header-bg)", color: "#FFFFFF" }}>
                <th style={{ padding: "8px", width: "40px", textAlign: "center" }}>
                  <input
                    type="checkbox"
                    checked={selectedAccountIds.size > 0 && selectedAccountIds.size === items.length}
                    onChange={toggleSelectAll}
                  />
                </th>
                <th style={{ padding: "8px" }}>رمز الحساب</th>
                <th style={{ padding: "8px" }}>اسم الحساب</th>
                <th style={{ padding: "8px" }}>العملة</th>
                <th style={{ padding: "8px" }}>الرصيد الأجنبي</th>
                <th style={{ padding: "8px" }}>الرصيد الدفتري الحالي</th>
                <th style={{ padding: "8px" }}>سعر الصرف</th>
                <th style={{ padding: "8px" }}>الرصيد بعد التقييم</th>
                <th style={{ padding: "8px" }}>الفارق غير المحقق</th>
                <th style={{ padding: "8px" }}>النوع</th>
              </tr>
            </thead>
            <tbody>
              {items.map((it, idx) => {
                const isSelected = selectedAccountIds.has(it.accountId);
                return (
                  <tr
                    key={it.accountId || idx}
                    style={{
                      borderBottom: "1px solid var(--border)",
                      background: isSelected ? "#F0FDF4" : idx % 2 === 0 ? "transparent" : "var(--bg-app)",
                    }}
                  >
                    <td style={{ padding: "8px", textAlign: "center" }}>
                      <input
                        type="checkbox"
                        checked={isSelected}
                        onChange={() => {
                          const next = new Set(selectedAccountIds);
                          if (next.has(it.accountId)) next.delete(it.accountId);
                          else next.add(it.accountId);
                          setSelectedAccountIds(next);
                        }}
                      />
                    </td>
                    <td style={{ padding: "8px", fontWeight: "bold" }}>
                      <span
                        style={{ color: "var(--primary)", cursor: "pointer", textDecoration: "underline" }}
                        onClick={() => {
                          openWorkspaceWindow({
                            key: `account-ledger-${it.accountId}`,
                            title: `دفتر أستاذ: ${it.accountCode}`,
                            screen: "account-ledger",
                            params: { accountId: it.accountId },
                          });
                        }}
                      >
                        {it.accountCode}
                      </span>
                    </td>
                    <td style={{ padding: "8px" }}>{it.accountNameArabic}</td>
                    <td style={{ padding: "8px" }}>{it.currencyCode}</td>
                    <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)" }}>{it.foreignBalance}</td>
                    <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)" }}>{it.bookBaseBalance}</td>
                    <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)" }}>{it.closingRate}</td>
                    <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)", fontWeight: "bold" }}>{it.revaluedBaseBalance}</td>
                    <td
                      style={{
                        padding: "8px",
                        fontFamily: "var(--font-mono, monospace)",
                        fontWeight: "bold",
                        color: it.isGain ? "#2E7D32" : "#C62828",
                      }}
                    >
                      {it.unrealizedDifference}
                    </td>
                    <td style={{ padding: "8px" }}>
                      <span
                        style={{
                          padding: "2px 6px",
                          borderRadius: "3px",
                          fontSize: "0.75rem",
                          background: it.isGain ? "#E8F5E9" : "#FFEBEE",
                          color: it.isGain ? "#2E7D32" : "#C62828",
                        }}
                      >
                        {it.isGain ? "أرباح صرف" : "خسائر صرف"}
                      </span>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      {/* Execution Dialog */}
      <PetraDialog
        open={isExecOpen}
        onClose={() => setIsExecOpen(false)}
        title="توليد قيد إعادة تقييم العملات الأجنبية"
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "16px" }}>
          <p style={{ margin: 0, fontSize: "0.85rem", color: "var(--text)" }}>
            سيتم توليد قيد يومية عام خادمي يوزع فروقات أسعار الصرف غير المحققة لـ <strong>{selectedAccountIds.size}</strong> حساباً
            إلى حسابي أرباح وخسائر فروقات أسعار الصرف المحددين أدناه.
          </p>

          <PetraField label="حساب أرباح فروقات الصرف (Unrealized FX Gain Account)">
            <LookupPicker
              ariaLabel="حساب أرباح فروقات الصرف"
              placeholder="ابحث عن حساب أرباح فروقات الصرف..."
              search={searchAccounts}
              value={gainAccount}
              onChange={setGainAccount}
            />
          </PetraField>

          <PetraField label="حساب خسائر فروقات الصرف (Unrealized FX Loss Account)">
            <LookupPicker
              ariaLabel="حساب خسائر فروقات الصرف"
              placeholder="ابحث عن حساب خسائر فروقات الصرف..."
              search={searchAccounts}
              value={lossAccount}
              onChange={setLossAccount}
            />
          </PetraField>

          <PetraField label="شرح وبيان القيد">
            <input
              type="text"
              className="petra-input"
              value={execDescription}
              onChange={(e) => setExecDescription(e.target.value)}
              style={{ width: "100%", padding: "6px 8px", border: "1px solid var(--border)", borderRadius: "3px" }}
            />
          </PetraField>

          <div style={{ display: "flex", justifyContent: "flex-end", gap: "8px", marginTop: "8px" }}>
            <PetraButton variant="secondary" onClick={() => setIsExecOpen(false)}>
              إلغاء
            </PetraButton>
            <PetraButton variant="primary" onClick={handleExecute} disabled={isExecuting}>
              {isExecuting ? "جارٍ توليد القيد..." : "تأكيد وتوليد القيد"}
            </PetraButton>
          </div>
        </div>
      </PetraDialog>
    </div>
  );
}
