import { useState, useEffect } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { useWorkspace } from "@/workspace/workspaceStore";
import { PetraButton, PetraLoadingSkeleton, PetraEmptyState, PetraErrorDetails, PetraDialog, PetraField } from "@/components/ui";
import { toast } from "@/components/ui/toastStore";

export interface OpeningBalancesScreenProps {
  params?: Readonly<Record<string, string>> | undefined;
}

interface OpeningBalanceLine {
  id?: string;
  accountId: string;
  accountCode: string;
  accountNameArabic: string;
  currencyCode: string;
  debitOriginal: string;
  creditOriginal: string;
  exchangeRate: string;
  debitBase: string;
  creditBase: string;
  description?: string | null;
}

interface OpeningBalanceBatch {
  id: string;
  companyId: string;
  fiscalYearId: string;
  openingDate: string;
  sourceType: string;
  originalCurrency: string;
  baseCurrency: string;
  approvalState: number; // 0: Draft, 1: Approved, 2: Posted, 3: Reversed
  postedJournalId?: string | null;
  reversalJournalId?: string | null;
  totalDebitBase: string;
  totalCreditBase: string;
  isBalanced: boolean;
  versionBase64: string;
  lines: OpeningBalanceLine[];
}

export default function OpeningBalancesScreen({ params: _params }: OpeningBalancesScreenProps) {
  const api = useApi();
  const { companyId, branchId } = useSession((s) => ({ companyId: s.companyId, branchId: s.branchId }));
  const openWorkspaceWindow = useWorkspace((s) => s.open);

  const [batches, setBatches] = useState<OpeningBalanceBatch[]>([]);
  const [selectedBatchId, setSelectedBatchId] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<{ status?: number; message?: string } | null>(null);

  // Create Batch Dialog
  const [createOpen, setCreateOpen] = useState(false);
  const [openingDate, setOpeningDate] = useState(() => `${new Date().getFullYear()}-01-01`);
  const [sourceType, setSourceType] = useState("Manual");
  const [baseCurrency, setBaseCurrency] = useState("JOD");
  const [creating, setCreating] = useState(false);

  const [actionInProgress, setActionInProgress] = useState(false);

  const loadBatches = async () => {
    if (!companyId || !branchId) return;

    setLoading(true);
    setError(null);
    try {
      const res = await api.GET("/api/v1/companies/{companyId}/branches/{branchId}/opening-balances", {
        params: {
          path: { companyId, branchId },
        },
      });

      if (res.error) {
        setError({ status: res.response.status, message: "فشل تحميل حزم الأرصدة الافتتاحية" });
      } else if (res.data) {
        const fetched = res.data as unknown as OpeningBalanceBatch[];
        setBatches(fetched);
        if (!selectedBatchId && fetched.length > 0 && fetched[0]) {
          setSelectedBatchId(fetched[0].id);
        }
      }
    } catch (err) {
      setError({ message: err instanceof Error ? err.message : "خطأ غير متوقع" });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadBatches();
  }, [companyId, branchId]);

  const handleCreateBatch = async () => {
    if (!companyId || !branchId) return;

    setCreating(true);
    try {
      const res = await api.POST("/api/v1/companies/{companyId}/branches/{branchId}/opening-balances", {
        params: {
          path: { companyId, branchId },
        },
        body: {
          openingDate,
          sourceType,
          originalCurrency: baseCurrency.toUpperCase(),
          baseCurrency: baseCurrency.toUpperCase(),
          idempotencyKey: `opb-${Date.now()}-${Math.random().toString(36).substring(2, 7)}`,
          lines: [],
        },
      });

      if (res.error) {
        toast.error("فشل إنشاء حزمة الأرصدة الافتتاحية");
      } else {
        toast.success("تم إنشاء حزمة الأرصدة الافتتاحية بنجاح");
        setCreateOpen(false);
        await loadBatches();
      }
    } catch {
      toast.error("خطأ أثناء الاتصال بالخادم");
    } finally {
      setCreating(false);
    }
  };

  const handleApprove = async (batch: OpeningBalanceBatch) => {
    if (!companyId || !branchId) return;

    setActionInProgress(true);
    try {
      const res = await api.POST("/api/v1/companies/{companyId}/branches/{branchId}/opening-balances/{batchId}/approve", {
        params: { path: { companyId, branchId, batchId: batch.id } },
        body: { reason: "اعتماد حزمة الأرصدة الافتتاحية" },
      });

      if (res.error) {
        toast.error("فشل اعتماد الأرصدة الافتتاحية: تأكد من توازن القيد");
      } else {
        toast.success("تم اعتماد حزمة الأرصدة الافتتاحية بنجاح");
        await loadBatches();
      }
    } catch {
      toast.error("خطأ أثناء الاعتماد");
    } finally {
      setActionInProgress(false);
    }
  };

  const handlePost = async (batch: OpeningBalanceBatch) => {
    if (!companyId || !branchId) return;

    setActionInProgress(true);
    try {
      const res = await api.POST("/api/v1/companies/{companyId}/branches/{branchId}/opening-balances/{batchId}/post", {
        params: { path: { companyId, branchId, batchId: batch.id } },
        body: { reason: "ترحيل الأرصدة الافتتاحية للسنة المالية" },
      });

      if (res.error) {
        toast.error("فشل ترحيل الأرصدة الافتتاحية");
      } else {
        toast.success("تم ترحيل الأرصدة الافتتاحية إلى دفتر الأستاذ العام بنجاح");
        await loadBatches();
      }
    } catch {
      toast.error("خطأ أثناء الترحيل");
    } finally {
      setActionInProgress(false);
    }
  };

  const handleReverse = async (batch: OpeningBalanceBatch) => {
    if (!companyId || !branchId) return;

    setActionInProgress(true);
    try {
      const res = await api.POST("/api/v1/companies/{companyId}/branches/{branchId}/opening-balances/{batchId}/reverse", {
        params: { path: { companyId, branchId, batchId: batch.id } },
        body: { reason: "عكس الأرصدة الافتتاحية" },
      });

      if (res.error) {
        toast.error("فشل عكس الأرصدة الافتتاحية");
      } else {
        toast.success("تم عكس ترحيل الأرصدة الافتتاحية بنجاح");
        await loadBatches();
      }
    } catch {
      toast.error("خطأ أثناء العكس");
    } finally {
      setActionInProgress(false);
    }
  };

  const selectedBatch = batches.find((b) => b.id === selectedBatchId);

  const getApprovalStateText = (st: number) => {
    switch (st) {
      case 0:
        return { label: "مسودة", bg: "var(--bg-sunken)", color: "var(--text-muted)" };
      case 1:
        return { label: "معتمد", bg: "var(--color-warning-bg, #fef7e0)", color: "var(--color-warning)" };
      case 2:
        return { label: "مرحّل", bg: "var(--color-success-bg, #e6f4ea)", color: "var(--color-success)" };
      case 3:
        return { label: "معكوس", bg: "var(--color-danger-bg, #fce8e6)", color: "var(--color-danger)" };
      default:
        return { label: "غير معروف", bg: "var(--bg-sunken)", color: "var(--text-muted)" };
    }
  };

  const copyToExcel = () => {
    if (!selectedBatch?.lines || selectedBatch.lines.length === 0) return;
    const header = "رمز الحساب\tاسم الحساب\tالعملة\tمدين بالأصل\tدائن بالأصل\tسعر الصرف\tمدين بالأساس\tدائن بالأساس\tالبيان";
    const rows = selectedBatch.lines.map((l) =>
      `${l.accountCode}\t${l.accountNameArabic}\t${l.currencyCode}\t${l.debitOriginal}\t${l.creditOriginal}\t${l.exchangeRate}\t${l.debitBase}\t${l.creditBase}\t${l.description ?? ""}`
    );
    navigator.clipboard?.writeText([header, ...rows].join("\r\n")).then(() => {
      toast.success("تم نسخ الأرصدة الافتتاحية إلى الحافظة (TSV)");
    });
  };

  return (
    <div style={{ display: "flex", flexDirection: "column", height: "100%", background: "var(--bg-app)" }}>
      {/* Create Batch Dialog */}
      <PetraDialog
        open={createOpen}
        onClose={() => setCreateOpen(false)}
        title="إنشاء حزمة أرصدة افتتاحية جديدة"
        width={450}
        actions={
          <>
            <PetraButton variant="secondary" onClick={() => setCreateOpen(false)}>
              إلغاء
            </PetraButton>
            <PetraButton variant="primary" disabled={creating} onClick={handleCreateBatch}>
              {creating ? "جاري الإنشاء..." : "إنشاء الحزمة"}
            </PetraButton>
          </>
        }
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "var(--space-3)" }}>
          <PetraField label="تاريخ الرصيد الافتتاحي" required>
            <input
              type="date"
              value={openingDate}
              onChange={(e) => setOpeningDate(e.target.value)}
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

          <PetraField label="مصدر الرصيد الافتتاحي">
            <select
              value={sourceType}
              onChange={(e) => setSourceType(e.target.value)}
              style={{
                height: "var(--control-height)",
                padding: "0 var(--space-2)",
                borderRadius: "var(--radius)",
                border: "1px solid var(--border)",
                background: "var(--bg-input)",
                color: "var(--text)",
              }}
            >
              <option value="Manual">إدخال يدوي مباشر (Manual)</option>
              <option value="Migration">استيراد وتدوير من سنة سابقة (Migration)</option>
              <option value="External">استيراد من نظام خارجي (External)</option>
            </select>
          </PetraField>

          <PetraField label="عملة الأساس">
            <input
              type="text"
              value={baseCurrency}
              onChange={(e) => setBaseCurrency(e.target.value)}
              maxLength={3}
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
            قيد الأرصدة الافتتاحية (Opening Balances)
          </h2>
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: "var(--space-2)" }}>
          <PetraButton variant="secondary" size="sm" onClick={() => setCreateOpen(true)}>
            حزمة جديدة
          </PetraButton>
          <PetraButton variant="secondary" size="sm" onClick={copyToExcel} disabled={!selectedBatch?.lines.length}>
            نسخ إلى Excel
          </PetraButton>
          <PetraButton variant="primary" size="sm" onClick={loadBatches} disabled={loading}>
            {loading ? "جاري التحميل..." : "تحديث"}
          </PetraButton>
        </div>
      </div>

      {/* Two-Pane Body */}
      <div style={{ flex: 1, display: "grid", gridTemplateColumns: "300px 1fr", overflow: "hidden" }}>
        {/* Left List of Batches */}
        <div
          style={{
            background: "var(--bg-surface)",
            borderLeft: "1px solid var(--border)",
            display: "flex",
            flexDirection: "column",
            overflow: "auto",
          }}
        >
          <div
            style={{
              padding: "var(--space-2) var(--space-3)",
              background: "var(--bg-sunken)",
              borderBottom: "1px solid var(--border)",
              fontWeight: 700,
              fontSize: "var(--fs-caption)",
              color: "var(--text-muted)",
            }}
          >
            حزم الأرصدة الافتتاحية
          </div>

          <div style={{ flex: 1, overflow: "auto" }}>
            {loading ? (
              <PetraLoadingSkeleton rows={5} />
            ) : error ? (
              <PetraErrorDetails status={error.status} message={error.message} onRetry={loadBatches} />
            ) : batches.length === 0 ? (
              <PetraEmptyState title="لا توجد حزم أرصدة افتتاحية" description="قم بإنشاء حزمة لإدخال الأرصدة الافتتاحية." />
            ) : (
              batches.map((b) => {
                const badge = getApprovalStateText(b.approvalState);
                return (
                  <div
                    key={b.id}
                    onClick={() => setSelectedBatchId(b.id)}
                    style={{
                      padding: "var(--space-2) var(--space-3)",
                      borderBottom: "1px solid var(--border-light)",
                      cursor: "pointer",
                      background: selectedBatchId === b.id ? "var(--bg-sunken)" : "transparent",
                      borderRight: selectedBatchId === b.id ? "3px solid var(--primary)" : "3px solid transparent",
                    }}
                  >
                    <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between" }}>
                      <strong style={{ fontFamily: "monospace" }}>{b.openingDate}</strong>
                      <span
                        style={{
                          padding: "2px 8px",
                          borderRadius: "var(--radius)",
                          fontSize: "var(--fs-caption)",
                          background: badge.bg,
                          color: badge.color,
                        }}
                      >
                        {badge.label}
                      </span>
                    </div>
                    <div style={{ fontSize: "var(--fs-caption)", color: "var(--text-muted)", marginTop: "4px" }}>
                      المصدر: {b.sourceType} · {b.lines.length} حساب
                    </div>
                  </div>
                );
              })
            )}
          </div>
        </div>

        {/* Right Batch Detail */}
        <div style={{ display: "flex", flexDirection: "column", overflow: "hidden" }}>
          {selectedBatch ? (
            <>
              {/* Metrics & Actions Bar */}
              <div
                style={{
                  display: "grid",
                  gridTemplateColumns: "repeat(5, 1fr)",
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
                    {selectedBatch.totalDebitBase} {selectedBatch.baseCurrency}
                  </strong>
                </div>

                <div style={{ background: "var(--bg-sunken)", padding: "var(--space-2)", borderRadius: "var(--radius)" }}>
                  <span style={{ color: "var(--text-muted)", display: "block" }}>إجمالي الدائن بالأساس</span>
                  <strong style={{ fontSize: "var(--fs-body)", fontFamily: "monospace" }}>
                    {selectedBatch.totalCreditBase} {selectedBatch.baseCurrency}
                  </strong>
                </div>

                <div style={{ background: "var(--bg-sunken)", padding: "var(--space-2)", borderRadius: "var(--radius)" }}>
                  <span style={{ color: "var(--text-muted)", display: "block" }}>توازن القيد الافتتاحي</span>
                  <strong
                    style={{
                      fontSize: "var(--fs-body)",
                      color: selectedBatch.isBalanced ? "var(--color-success)" : "var(--color-danger)",
                    }}
                  >
                    {selectedBatch.isBalanced ? "متوازن ✓" : "غير متوازن ⚠"}
                  </strong>
                </div>

                <div style={{ background: "var(--bg-sunken)", padding: "var(--space-2)", borderRadius: "var(--radius)" }}>
                  <span style={{ color: "var(--text-muted)", display: "block" }}>أصل القيد المرحل</span>
                  {selectedBatch.postedJournalId ? (
                    <button
                      type="button"
                      onClick={() => {
                        openWorkspaceWindow({
                          key: `journal:${selectedBatch.postedJournalId}`,
                          screen: "journal-entry",
                          title: "القيد الافتتاحي المرحل",
                          params: { journalId: selectedBatch.postedJournalId! },
                        });
                      }}
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
                      عرض القيد #{selectedBatch.postedJournalId.substring(0, 8)}
                    </button>
                  ) : (
                    <span>لم يرحل بعد</span>
                  )}
                </div>

                <div style={{ display: "flex", alignItems: "center", justifyContent: "flex-end", gap: "var(--space-2)" }}>
                  {selectedBatch.approvalState === 0 && (
                    <PetraButton
                      variant="primary"
                      size="sm"
                      disabled={!selectedBatch.isBalanced || actionInProgress}
                      onClick={() => handleApprove(selectedBatch)}
                    >
                      اعتماد الحزمة
                    </PetraButton>
                  )}

                  {selectedBatch.approvalState === 1 && (
                    <PetraButton
                      variant="primary"
                      size="sm"
                      disabled={actionInProgress}
                      onClick={() => handlePost(selectedBatch)}
                    >
                      ترحيل للأستاذ العام
                    </PetraButton>
                  )}

                  {selectedBatch.approvalState === 2 && (
                    <PetraButton
                      variant="danger"
                      size="sm"
                      disabled={actionInProgress}
                      onClick={() => handleReverse(selectedBatch)}
                    >
                      عكس الترحيل
                    </PetraButton>
                  )}
                </div>
              </div>

              {/* Lines Table */}
              <div style={{ flex: 1, overflow: "auto", padding: "var(--space-3)" }}>
                {selectedBatch.lines.length === 0 ? (
                  <PetraEmptyState title="لا توجد أسطر حسابات في هذه الحزمة" description="أدخل أسطر الأرصدة الافتتاحية للمطابقة." />
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
                          <th style={{ padding: "var(--space-2) var(--space-3)" }}>رمز الحساب</th>
                          <th style={{ padding: "var(--space-2) var(--space-3)" }}>اسم الحساب</th>
                          <th style={{ padding: "var(--space-2) var(--space-3)" }}>العملة</th>
                          <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left" }}>مدين بالأصل</th>
                          <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left" }}>دائن بالأصل</th>
                          <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left" }}>مدين بالأساس</th>
                          <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left" }}>دائن بالأساس</th>
                          <th style={{ padding: "var(--space-2) var(--space-3)" }}>البيان</th>
                        </tr>
                      </thead>
                      <tbody>
                        {selectedBatch.lines.map((l, idx) => (
                          <tr
                            key={l.id ?? idx}
                            style={{
                              borderBottom: "1px solid var(--border-light)",
                              background: idx % 2 === 1 ? "var(--bg-sunken)" : "transparent",
                            }}
                          >
                            <td style={{ padding: "var(--space-2) var(--space-3)", fontFamily: "monospace", fontWeight: 700 }}>
                              {l.accountCode}
                            </td>
                            <td style={{ padding: "var(--space-2) var(--space-3)", fontWeight: 600 }}>
                              {l.accountNameArabic}
                            </td>
                            <td style={{ padding: "var(--space-2) var(--space-3)", fontFamily: "monospace" }}>
                              {l.currencyCode}
                            </td>
                            <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left", fontFamily: "monospace" }}>
                              {l.debitOriginal}
                            </td>
                            <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left", fontFamily: "monospace" }}>
                              {l.creditOriginal}
                            </td>
                            <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left", fontFamily: "monospace", fontWeight: 700 }}>
                              {l.debitBase}
                            </td>
                            <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left", fontFamily: "monospace", fontWeight: 700 }}>
                              {l.creditBase}
                            </td>
                            <td style={{ padding: "var(--space-2) var(--space-3)", color: "var(--text-muted)" }}>
                              {l.description ?? "—"}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </div>
            </>
          ) : (
            <PetraEmptyState title="حدد حزمة أرصدة افتتاحية" description="اختر حزمة لعرض تفاصيلها أو أنشئ حزمة جديدة." />
          )}
        </div>
      </div>
    </div>
  );
}
