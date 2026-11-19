import { useState, useId } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { useWorkspace } from "@/workspace/workspaceStore";
import { PetraButton, PetraLoadingSkeleton, PetraEmptyState, PetraErrorDetails, PetraDialog, PetraField } from "@/components/ui";
import { toast } from "@/components/ui/toastStore";
import type { components } from "@/services/api/schema";

export interface BatchPostingScreenProps {
  params?: Readonly<Record<string, string>> | undefined;
}

type BulkPostingDocumentType = components["schemas"]["BulkPostingDocumentType"];
type BulkPostingPreviewItem = components["schemas"]["BulkPostingPreviewItemDto"];
type BulkPostingClassificationCount = components["schemas"]["BulkPostingClassificationCountDto"];
type BulkPostingBatch = components["schemas"]["BulkPostingBatchDto"];
type BulkPostingItem = components["schemas"]["BulkPostingItemDto"];

const ALL_DOC_TYPES: { key: BulkPostingDocumentType; labelArabic: string }[] = [
  { key: "SalesInvoice", labelArabic: "فواتير المبيعات" },
  { key: "PurchaseInvoice", labelArabic: "فواتير المشتريات" },
  { key: "Voucher", labelArabic: "سندات الخزينة (قبض/دفع)" },
  { key: "Journal", labelArabic: "قيود اليومية العامة" },
  { key: "InventoryDocument", labelArabic: "مستندات المخزون والمناقلات" },
];

export default function BatchPostingScreen({ params: _params }: BatchPostingScreenProps) {
  const api = useApi();
  const { companyId, branchId } = useSession((s) => ({ companyId: s.companyId, branchId: s.branchId }));
  const openWorkspaceWindow = useWorkspace((s) => s.open);

  const defaultIdempotency = useId();

  // Filters
  const [selectedDocTypes, setSelectedDocTypes] = useState<BulkPostingDocumentType[]>([
    "SalesInvoice",
    "PurchaseInvoice",
    "Voucher",
    "Journal",
  ]);
  const [fromDate, setFromDate] = useState<string>("");
  const [toDate, setToDate] = useState<string>("");
  const [search, setSearch] = useState<string>("");

  // Preview state
  const [isLoadingPreview, setIsLoadingPreview] = useState(false);
  const [previewError, setPreviewError] = useState<{ status?: number; message?: string } | null>(null);
  const [previewTotals, setPreviewTotals] = useState<{
    totalExamined: number;
    readyToPost: number;
    needsApproval: number;
    ineligible: number;
  } | null>(null);
  const [classifications, setClassifications] = useState<BulkPostingClassificationCount[]>([]);
  const [previewItems, setPreviewItems] = useState<BulkPostingPreviewItem[]>([]);
  const [selectedDocIds, setSelectedDocIds] = useState<Set<string>>(new Set());

  // Execution state
  const [activeBatch, setActiveBatch] = useState<BulkPostingBatch | null>(null);
  const [batchResults, setBatchResults] = useState<BulkPostingItem[]>([]);
  const [isLoadingBatch, setIsLoadingBatch] = useState(false);
  const [isConfirmOpen, setIsConfirmOpen] = useState(false);
  const [postingReason, setPostingReason] = useState<string>("ترحيل آلي جماعي للوثائق المؤهلة");

  const toggleDocType = (dt: BulkPostingDocumentType) => {
    setSelectedDocTypes((prev) =>
      prev.includes(dt) ? prev.filter((t) => t !== dt) : [...prev, dt]
    );
  };

  const handlePreview = async () => {
    if (!companyId || !branchId) {
      toast.warning("يجب تحديد الشركة والفرع أولاً");
      return;
    }

    setIsLoadingPreview(true);
    setPreviewError(null);

    try {
      const res = await api.POST(
        "/api/v1/companies/{companyId}/branches/{branchId}/posting-batches/preview",
        {
          params: {
            path: { companyId, branchId },
          },
          body: {
            filter: {
              documentTypes: selectedDocTypes,
              ...(fromDate ? { fromDateUtc: new Date(fromDate).toISOString() } : {}),
              ...(toDate ? { toDateUtc: new Date(toDate).toISOString() } : {}),
              ...(search ? { search } : {}),
            },
          },
        }
      );

      if (res.error || !res.data) {
        setPreviewError({
          status: res.response?.status,
          message: (res.error as { title?: string; detail?: string })?.detail ?? "فشل جلب معاينة الترحيل",
        });
        return;
      }

      const data = res.data;
      setPreviewTotals({
        totalExamined: data.totalExamined ?? 0,
        readyToPost: data.readyToPost ?? 0,
        needsApproval: data.needsApproval ?? 0,
        ineligible: data.ineligible ?? 0,
      });
      setClassifications(data.classifications ?? []);
      const items = data.items ?? [];
      setPreviewItems(items);

      // Select all postable by default
      const postableIds = new Set<string>();
      items.forEach((it: BulkPostingPreviewItem) => {
        if (it.postable && it.documentId) {
          postableIds.add(it.documentId);
        }
      });
      setSelectedDocIds(postableIds);
      toast.success(`تم فحص ${data.totalExamined ?? 0} مستند: ${data.readyToPost ?? 0} مؤهل للترحيل`);
    } catch (err) {
      setPreviewError({
        message: err instanceof Error ? err.message : "خطأ غير متوقع أثناء المعاينة",
      });
    } finally {
      setIsLoadingPreview(false);
    }
  };

  const handleStartPosting = async () => {
    if (!companyId || !branchId) return;

    setIsLoadingBatch(true);
    setIsConfirmOpen(false);

    try {
      const selectedDocsPayload = Array.from(selectedDocIds)
        .map((id) => {
          const it = previewItems.find((p) => p.documentId === id);
          if (!it?.documentType) return null;
          return {
            documentType: it.documentType,
            documentId: id,
          };
        })
        .filter((d): d is { documentType: BulkPostingDocumentType; documentId: string } => d !== null);

      const res = await api.POST(
        "/api/v1/companies/{companyId}/branches/{branchId}/posting-batches",
        {
          params: {
            path: { companyId, branchId },
          },
          body: {
            idempotencyKey: `bulk-post-${Date.now()}-${defaultIdempotency.replace(/[:]/g, "")}`,
            confirmed: true,
            reason: postingReason,
            filter: {
              documentTypes: selectedDocTypes,
              ...(fromDate ? { fromDateUtc: new Date(fromDate).toISOString() } : {}),
              ...(toDate ? { toDateUtc: new Date(toDate).toISOString() } : {}),
              ...(search ? { search } : {}),
            },
            ...(selectedDocsPayload.length > 0 ? { documents: selectedDocsPayload } : {}),
          },
        }
      );

      if (res.error || !res.data) {
        toast.error((res.error as { title?: string; detail?: string })?.detail ?? "فشل بدء دفعة الترحيل");
        return;
      }

      const batch = res.data;
      setActiveBatch(batch);
      toast.success(`تم إنشاء دفعة الترحيل: ${batch.batchId?.slice(0, 8)}`);

      if (batch.batchId) {
        await loadBatchDetails(batch.batchId);
      }
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "خطأ أثناء تنفيذ الترحيل");
    } finally {
      setIsLoadingBatch(false);
    }
  };

  const loadBatchDetails = async (bId: string) => {
    if (!companyId || !branchId) return;

    try {
      const [batchRes, resultsRes] = await Promise.all([
        api.GET(
          "/api/v1/companies/{companyId}/branches/{branchId}/posting-batches/{batchId}",
          {
            params: {
              path: { companyId, branchId, batchId: bId },
            },
          }
        ),
        api.GET(
          "/api/v1/companies/{companyId}/branches/{branchId}/posting-batches/{batchId}/results",
          {
            params: {
              path: { companyId, branchId, batchId: bId },
              query: { page: 1, pageSize: 200 },
            },
          }
        ),
      ]);

      if (batchRes.data) {
        setActiveBatch(batchRes.data);
      }
      if (resultsRes.data?.items) {
        setBatchResults(resultsRes.data.items);
      }
    } catch {
      // silent refresh error
    }
  };

  const handleResume = async () => {
    if (!companyId || !branchId || !activeBatch?.batchId) return;

    try {
      const res = await api.POST(
        "/api/v1/companies/{companyId}/branches/{branchId}/posting-batches/{batchId}/resume",
        {
          params: {
            path: { companyId, branchId, batchId: activeBatch.batchId },
          },
          body: {
            reason: "استئناف ترحيل الدفعة",
          },
        }
      );

      if (res.error) {
        toast.error("فشل استئناف الترحيل");
        return;
      }

      toast.success("تم استئناف الترحيل بنجاح");
      await loadBatchDetails(activeBatch.batchId);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "خطأ أثناء الاستئناف");
    }
  };

  const toggleSelectAll = () => {
    if (selectedDocIds.size === previewItems.filter((i) => i.postable).length) {
      setSelectedDocIds(new Set());
    } else {
      const s = new Set<string>();
      previewItems.forEach((it) => {
        if (it.postable && it.documentId) s.add(it.documentId);
      });
      setSelectedDocIds(s);
    }
  };

  const exportResultsTsv = () => {
    if (batchResults.length === 0) {
      toast.warning("لا توجد نتائج للتصدير");
      return;
    }

    const headers = [
      "نوع المستند",
      "رقم المستند",
      "الطرف",
      "المبلغ",
      "العملة",
      "الحالة السابقة",
      "الحالة الجديدة",
      "رقم الترحيل",
      "نتيجة الترحيل",
      "السبب أو كود الفشل",
    ];

    const rows = batchResults.map((r) => [
      r.documentType ?? "",
      r.documentNumber ?? "",
      r.partyName ?? "",
      r.amount ?? "",
      r.currencyCode ?? "",
      r.previousStatus ?? "",
      r.newStatus ?? "",
      r.postingNumber ?? "",
      r.status ?? "",
      r.failureCode ? `${r.failureCode}: ${r.reason ?? ""}` : r.reason ?? "",
    ]);

    const tsvContent = [headers.join("\t"), ...rows.map((row) => row.join("\t"))].join("\n");
    const blob = new Blob(["\uFEFF" + tsvContent], { type: "text/tab-separated-values;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `posting-batch-${activeBatch?.batchId?.slice(0, 8) || "results"}.tsv`;
    a.click();
    URL.revokeObjectURL(url);
    toast.success("تم تصدير نتائج الترحيل بنجاح");
  };

  return (
    <div className="batch-posting-screen" style={{ padding: "16px", height: "100%", display: "flex", flexDirection: "column", gap: "16px", overflowY: "auto" }}>
      {/* Header */}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", borderBottom: "1px solid var(--border)", paddingBottom: "12px" }}>
        <div>
          <h2 style={{ margin: 0, fontSize: "1.25rem", color: "var(--header-bg)" }}>إدارة الترحيل الآلي الجماعي (Batch Posting Manager)</h2>
          <p style={{ margin: "4px 0 0", fontSize: "0.85rem", color: "var(--text-muted)" }}>
            فحص وتصنيف وترحيل فواتير المبيعات والمشتريات وسندات الخزينة وقيود اليومية آلياً مع التحقق المحاسبي الصارم.
          </p>
        </div>
        <div style={{ display: "flex", gap: "8px" }}>
          <PetraButton
            variant="secondary"
            onClick={handlePreview}
            disabled={isLoadingPreview || selectedDocTypes.length === 0}
            size="sm"
          >
            {isLoadingPreview ? "جارٍ الفحص..." : "معاينة المستندات المؤهلة (F5)"}
          </PetraButton>
          <PetraButton
            variant="primary"
            onClick={() => setIsConfirmOpen(true)}
            disabled={isLoadingBatch || (selectedDocIds.size === 0 && previewItems.length === 0)}
            size="sm"
          >
            بدء الترحيل الجماعي ({selectedDocIds.size})
          </PetraButton>
        </div>
      </div>

      {/* Filter panel */}
      <div style={{ background: "var(--bg-surface)", border: "1px solid var(--border)", borderRadius: "4px", padding: "12px", display: "flex", flexDirection: "column", gap: "12px" }}>
        <div style={{ fontSize: "0.85rem", fontWeight: "bold", color: "var(--header-bg)" }}>أنواع المستندات المشمولة بالترحيل:</div>
        <div style={{ display: "flex", flexWrap: "wrap", gap: "16px" }}>
          {ALL_DOC_TYPES.map((dt) => (
            <label key={dt.key} style={{ display: "flex", alignItems: "center", gap: "6px", fontSize: "0.85rem", cursor: "pointer" }}>
              <input
                type="checkbox"
                checked={selectedDocTypes.includes(dt.key)}
                onChange={() => toggleDocType(dt.key)}
              />
              {dt.labelArabic}
            </label>
          ))}
        </div>

        <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))", gap: "12px", marginTop: "4px" }}>
          <PetraField label="من تاريخ">
            <input
              type="date"
              className="petra-input"
              value={fromDate}
              onChange={(e) => setFromDate(e.target.value)}
              style={{ width: "100%", padding: "6px 8px", border: "1px solid var(--border)", borderRadius: "3px" }}
            />
          </PetraField>
          <PetraField label="إلى تاريخ">
            <input
              type="date"
              className="petra-input"
              value={toDate}
              onChange={(e) => setToDate(e.target.value)}
              style={{ width: "100%", padding: "6px 8px", border: "1px solid var(--border)", borderRadius: "3px" }}
            />
          </PetraField>
          <PetraField label="بحث (رقم مستند، طرف...)">
            <input
              type="text"
              className="petra-input"
              placeholder="اكتب للبحث..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              style={{ width: "100%", padding: "6px 8px", border: "1px solid var(--border)", borderRadius: "3px" }}
            />
          </PetraField>
        </div>
      </div>

      {/* Summary KPI Cards */}
      {previewTotals && (
        <div style={{ display: "grid", gridTemplateColumns: "repeat(4, 1fr)", gap: "12px" }}>
          <div style={{ background: "var(--bg-surface)", border: "1px solid var(--border)", padding: "12px", borderRadius: "4px" }}>
            <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>إجمالي المفحوص</div>
            <div style={{ fontSize: "1.5rem", fontWeight: "bold", color: "var(--header-bg)" }}>{previewTotals.totalExamined}</div>
          </div>
          <div style={{ background: "#E8F5E9", border: "1px solid #A5D6A7", padding: "12px", borderRadius: "4px" }}>
            <div style={{ fontSize: "0.8rem", color: "#2E7D32" }}>جاهز للترحيل الفوري</div>
            <div style={{ fontSize: "1.5rem", fontWeight: "bold", color: "#2E7D32" }}>{previewTotals.readyToPost}</div>
          </div>
          <div style={{ background: "#FFF8E1", border: "1px solid #FFE082", padding: "12px", borderRadius: "4px" }}>
            <div style={{ fontSize: "0.8rem", color: "#F57F17" }}>يتطلب اعتماداً مسبقاً</div>
            <div style={{ fontSize: "1.5rem", fontWeight: "bold", color: "#F57F17" }}>{previewTotals.needsApproval}</div>
          </div>
          <div style={{ background: "#FFEBEE", border: "1px solid #FFCDD2", padding: "12px", borderRadius: "4px" }}>
            <div style={{ fontSize: "0.8rem", color: "#C62828" }}>غير مؤهل للترحيل</div>
            <div style={{ fontSize: "1.5rem", fontWeight: "bold", color: "#C62828" }}>{previewTotals.ineligible}</div>
          </div>
        </div>
      )}

      {/* Classification pills */}
      {classifications.length > 0 && (
        <div style={{ display: "flex", flexWrap: "wrap", gap: "8px", alignItems: "center" }}>
          <span style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>تصنيفات الفحص:</span>
          {classifications.map((c, idx) => (
            <span
              key={idx}
              style={{
                fontSize: "0.75rem",
                padding: "3px 8px",
                borderRadius: "12px",
                background: c.classification === "ReadyForPosting" ? "#E8F5E9" : "#ECEFF1",
                color: c.classification === "ReadyForPosting" ? "#2E7D32" : "#37474F",
                border: "1px solid var(--border)",
              }}
            >
              {c.classification}: <strong>{c.count}</strong>
            </span>
          ))}
        </div>
      )}

      {/* Active Batch Banner (if running or completed) */}
      {activeBatch && (
        <div style={{ background: "#E0F2F1", border: "1px solid #80CBC4", borderRadius: "4px", padding: "12px", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
          <div>
            <div style={{ fontWeight: "bold", color: "#00695C", fontSize: "0.95rem" }}>
              حالة دفعة الترحيل الحالية: <span>{activeBatch.status}</span> ({activeBatch.batchId?.slice(0, 8)})
            </div>
            <div style={{ fontSize: "0.8rem", color: "#004D40", marginTop: "4px" }}>
              تم ترحيل: <strong>{activeBatch.posted ?? 0}</strong> من أصل <strong>{activeBatch.eligible ?? 0}</strong> مؤهل | تم تخطي: {activeBatch.skipped ?? 0} | فشل: {activeBatch.failed ?? 0}
            </div>
          </div>
          <div style={{ display: "flex", gap: "8px" }}>
            <PetraButton
              variant="secondary"
              size="sm"
              onClick={() => activeBatch.batchId && loadBatchDetails(activeBatch.batchId)}
            >
              تحديث النتائج
            </PetraButton>
            {(activeBatch.status === "CompletedWithFailures" || activeBatch.status === "Failed") && (
              <PetraButton variant="primary" size="sm" onClick={handleResume}>
                استئناف الترحيل
              </PetraButton>
            )}
            <PetraButton variant="secondary" size="sm" onClick={exportResultsTsv}>
              تصدير النتائج TSV
            </PetraButton>
          </div>
        </div>
      )}

      {/* Content Area: Preview items or Batch Results */}
      {isLoadingPreview ? (
        <PetraLoadingSkeleton rows={8} />
      ) : previewError ? (
        <PetraErrorDetails status={previewError.status} message={previewError.message} onRetry={handlePreview} />
      ) : previewItems.length === 0 && batchResults.length === 0 ? (
        <PetraEmptyState title="حدد أنواع المستندات واضغط 'معاينة المستندات المؤهلة' لبدء الترحيل" />
      ) : (
        <div style={{ flex: 1, display: "flex", flexDirection: "column", gap: "8px" }}>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
            <div style={{ fontSize: "0.9rem", fontWeight: "bold", color: "var(--header-bg)" }}>
              {batchResults.length > 0 ? `نتائج الترحيل المنفذة (${batchResults.length})` : `المستندات المعاينة (${previewItems.length})`}
            </div>
            {previewItems.length > 0 && batchResults.length === 0 && (
              <label style={{ fontSize: "0.8rem", display: "flex", alignItems: "center", gap: "4px", cursor: "pointer" }}>
                <input
                  type="checkbox"
                  checked={selectedDocIds.size > 0 && selectedDocIds.size === previewItems.filter((i) => i.postable).length}
                  onChange={toggleSelectAll}
                />
                تحديد كل المؤهل للترحيل ({selectedDocIds.size})
              </label>
            )}
          </div>

          <div style={{ overflowX: "auto", border: "1px solid var(--border)", borderRadius: "4px", background: "var(--bg-surface)" }}>
            <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.85rem", textAlign: "right" }}>
              <thead>
                <tr style={{ background: "var(--header-bg)", color: "#FFFFFF" }}>
                  {batchResults.length === 0 && <th style={{ padding: "8px", width: "40px", textAlign: "center" }}>اختيار</th>}
                  <th style={{ padding: "8px" }}>النوع</th>
                  <th style={{ padding: "8px" }}>رقم المستند</th>
                  <th style={{ padding: "8px" }}>التاريخ</th>
                  <th style={{ padding: "8px" }}>الطرف</th>
                  <th style={{ padding: "8px" }}>المبلغ</th>
                  <th style={{ padding: "8px" }}>العملة</th>
                  <th style={{ padding: "8px" }}>الحالة</th>
                  <th style={{ padding: "8px" }}>التصنيف / النتيجة</th>
                  <th style={{ padding: "8px" }}>السبب / الملاحظات</th>
                </tr>
              </thead>
              <tbody>
                {batchResults.length > 0
                  ? batchResults.map((r, idx) => (
                      <tr key={idx} style={{ borderBottom: "1px solid var(--border)", background: idx % 2 === 0 ? "transparent" : "var(--bg-app)" }}>
                        <td style={{ padding: "8px" }}>{r.documentType}</td>
                        <td style={{ padding: "8px", fontWeight: "bold" }}>{r.documentNumber || "—"}</td>
                        <td style={{ padding: "8px" }}>{r.documentDateUtc ? new Date(r.documentDateUtc).toLocaleDateString("ar-EG") : "—"}</td>
                        <td style={{ padding: "8px" }}>{r.partyName || "—"}</td>
                        <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)" }}>{r.amount || "0.00"}</td>
                        <td style={{ padding: "8px" }}>{r.currencyCode}</td>
                        <td style={{ padding: "8px" }}>
                          <span style={{
                            padding: "2px 6px",
                            borderRadius: "3px",
                            fontSize: "0.75rem",
                            background: r.status === "Posted" ? "#E8F5E9" : r.status === "Failed" ? "#FFEBEE" : "#FFF8E1",
                            color: r.status === "Posted" ? "#2E7D32" : r.status === "Failed" ? "#C62828" : "#F57F17",
                          }}>
                            {r.status}
                          </span>
                        </td>
                        <td style={{ padding: "8px" }}>
                          {r.postingNumber ? (
                            <span
                              style={{ color: "var(--primary)", cursor: "pointer", textDecoration: "underline" }}
                              onClick={() => {
                                if (r.journalEntryId) {
                                  openWorkspaceWindow({
                                    key: `journal-${r.journalEntryId}`,
                                    title: `قيد ترحيل: ${r.postingNumber}`,
                                    screen: "journal-entry",
                                    params: { id: r.journalEntryId },
                                  });
                                }
                              }}
                            >
                              قيد: {r.postingNumber}
                            </span>
                          ) : (
                            r.classification || "—"
                          )}
                        </td>
                        <td style={{ padding: "8px", color: r.failureCode ? "#C62828" : "var(--text-muted)" }}>
                          {r.failureCode ? `${r.failureCode}: ${r.reason ?? ""}` : r.reason || "—"}
                        </td>
                      </tr>
                    ))
                  : previewItems.map((item, idx) => {
                      const isSelected = item.documentId ? selectedDocIds.has(item.documentId) : false;
                      return (
                        <tr
                          key={idx}
                          style={{
                            borderBottom: "1px solid var(--border)",
                            background: isSelected ? "#F0FDF4" : idx % 2 === 0 ? "transparent" : "var(--bg-app)",
                          }}
                        >
                          <td style={{ padding: "8px", textAlign: "center" }}>
                            <input
                              type="checkbox"
                              disabled={!item.postable}
                              checked={isSelected}
                              onChange={() => {
                                if (!item.documentId) return;
                                const next = new Set(selectedDocIds);
                                if (next.has(item.documentId)) next.delete(item.documentId);
                                else next.add(item.documentId);
                                setSelectedDocIds(next);
                              }}
                            />
                          </td>
                          <td style={{ padding: "8px" }}>{item.documentType}</td>
                          <td style={{ padding: "8px", fontWeight: "bold" }}>{item.documentNumber || "—"}</td>
                          <td style={{ padding: "8px" }}>{item.documentDateUtc ? new Date(item.documentDateUtc).toLocaleDateString("ar-EG") : "—"}</td>
                          <td style={{ padding: "8px" }}>{item.partyName || "—"}</td>
                          <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)" }}>{item.amount || "0.00"}</td>
                          <td style={{ padding: "8px" }}>{item.currencyCode}</td>
                          <td style={{ padding: "8px" }}>{item.currentStatus}</td>
                          <td style={{ padding: "8px" }}>
                            <span style={{
                              padding: "2px 6px",
                              borderRadius: "3px",
                              fontSize: "0.75rem",
                              background: item.postable ? "#E8F5E9" : "#FFEBEE",
                              color: item.postable ? "#2E7D32" : "#C62828",
                            }}>
                              {item.classification}
                            </span>
                          </td>
                          <td style={{ padding: "8px", color: item.postable ? "var(--text-muted)" : "#C62828" }}>
                            {item.reason || "—"}
                          </td>
                        </tr>
                      );
                    })}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* Confirmation Dialog */}
      <PetraDialog
        open={isConfirmOpen}
        onClose={() => setIsConfirmOpen(false)}
        title="تأكيد الترحيل الآلي الجماعي"
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "16px" }}>
          <p style={{ margin: 0, fontSize: "0.9rem", color: "var(--text)" }}>
            سيتم ترحيل <strong>{selectedDocIds.size}</strong> مستنداً إلى دفتر الأستاذ العام وتوليد قيود اليومية ذات الأرقام الخادمية.
            العملية غير قابلة للتراجع المباشر إلا عبر قيود العكس.
          </p>

          <PetraField label="سبب / ملاحظة الترحيل">
            <input
              type="text"
              className="petra-input"
              value={postingReason}
              onChange={(e) => setPostingReason(e.target.value)}
              style={{ width: "100%", padding: "6px 8px", border: "1px solid var(--border)", borderRadius: "3px" }}
            />
          </PetraField>

          <div style={{ display: "flex", justifyContent: "flex-end", gap: "8px", marginTop: "8px" }}>
            <PetraButton variant="secondary" onClick={() => setIsConfirmOpen(false)}>
              إلغاء
            </PetraButton>
            <PetraButton variant="primary" onClick={handleStartPosting} disabled={isLoadingBatch}>
              {isLoadingBatch ? "جارٍ بدء الترحيل..." : "تأكيد وبدء الترحيل"}
            </PetraButton>
          </div>
        </div>
      </PetraDialog>
    </div>
  );
}
