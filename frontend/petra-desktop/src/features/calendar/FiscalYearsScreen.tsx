import { useState, useEffect } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { PetraButton, PetraLoadingSkeleton, PetraEmptyState, PetraErrorDetails, PetraDialog, PetraField } from "@/components/ui";
import { toast } from "@/components/ui/toastStore";

export interface FiscalYearsScreenProps {
  params?: Readonly<Record<string, string>> | undefined;
}

type PeriodState = "Open" | "SoftClosed" | "Closed";

interface AccountingPeriod {
  id: string;
  fiscalYearId: string;
  code: string;
  rangeStart: string;
  rangeEnd: string;
  state: PeriodState;
  version: string;
}

interface FiscalYear {
  id: string;
  companyId: string;
  code: string;
  rangeStart: string;
  rangeEnd: string;
  periods: AccountingPeriod[];
  version: string;
}

export default function FiscalYearsScreen({ params: _params }: FiscalYearsScreenProps) {
  const api = useApi();
  const { companyId, branchId } = useSession((s) => ({ companyId: s.companyId, branchId: s.branchId }));

  const [years, setYears] = useState<FiscalYear[]>([]);
  const [selectedYearId, setSelectedYearId] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<{ status?: number; message?: string } | null>(null);

  // Create Fiscal Year Dialog
  const [createOpen, setCreateOpen] = useState(false);
  const [newCode, setNewCode] = useState(() => `${new Date().getFullYear()}`);
  const [newStart, setNewStart] = useState(() => `${new Date().getFullYear()}-01-01`);
  const [newEnd, setNewEnd] = useState(() => `${new Date().getFullYear()}-12-31`);
  const [creating, setCreating] = useState(false);

  // Transition Dialog (Close, SoftClose, Reopen)
  const [transitionPeriod, setTransitionPeriod] = useState<AccountingPeriod | null>(null);
  const [transitionAction, setTransitionAction] = useState<"soft-close" | "close" | "reopen">("soft-close");
  const [transitionReason, setTransitionReason] = useState("");
  const [transitioning, setTransitioning] = useState(false);

  const loadYears = async () => {
    if (!companyId || !branchId) return;

    setLoading(true);
    setError(null);
    try {
      const res = await api.GET("/api/v1/companies/{companyId}/branches/{branchId}/ledger/calendar/fiscal-years", {
        params: {
          path: { companyId, branchId },
        },
      });

      if (res.error) {
        setError({ status: res.response.status, message: "فشل تحميل السنوات والفترات المالية" });
      } else if (res.data) {
        const fetched = res.data as unknown as FiscalYear[];
        setYears(fetched);
        if (!selectedYearId && fetched.length > 0 && fetched[0]) {
          setSelectedYearId(fetched[0].id);
        }
      }
    } catch (err) {
      setError({ message: err instanceof Error ? err.message : "خطأ غير متوقع" });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadYears();
  }, [companyId, branchId]);

  const handleCreateYear = async () => {
    if (!companyId || !branchId || !newCode.trim() || !newStart || !newEnd) {
      toast.error("يرجى ملء جميع بيانات السنة المالية");
      return;
    }

    setCreating(true);
    try {
      const res = await api.POST("/api/v1/companies/{companyId}/branches/{branchId}/ledger/calendar/fiscal-years", {
        params: {
          path: { companyId, branchId },
        },
        body: {
          code: newCode.trim(),
          start: newStart,
          end: newEnd,
        },
      });

      if (res.error) {
        toast.error("فشل إنشاء السنة المالية");
      } else {
        toast.success(`تم إنشاء السنة المالية ${newCode} بنجاح`);
        setCreateOpen(false);
        await loadYears();
      }
    } catch {
      toast.error("خطأ أثناء الاتصال بالخادم");
    } finally {
      setCreating(false);
    }
  };

  const handleExecuteTransition = async () => {
    if (!companyId || !branchId || !transitionPeriod || !transitionReason.trim()) {
      toast.error("يرجى إدخال سبب العملية");
      return;
    }

    setTransitioning(true);
    try {
      let res;
      if (transitionAction === "soft-close") {
        res = await api.POST("/api/v1/companies/{companyId}/branches/{branchId}/ledger/calendar/periods/{periodId}/soft-close", {
          params: { path: { companyId, branchId, periodId: transitionPeriod.id } },
          body: { expectedVersion: transitionPeriod.version, reason: transitionReason.trim() },
        });
      } else if (transitionAction === "close") {
        res = await api.POST("/api/v1/companies/{companyId}/branches/{branchId}/ledger/calendar/periods/{periodId}/close", {
          params: { path: { companyId, branchId, periodId: transitionPeriod.id } },
          body: { expectedVersion: transitionPeriod.version, reason: transitionReason.trim() },
        });
      } else {
        res = await api.POST("/api/v1/companies/{companyId}/branches/{branchId}/ledger/calendar/periods/{periodId}/reopen", {
          params: { path: { companyId, branchId, periodId: transitionPeriod.id } },
          body: { expectedVersion: transitionPeriod.version, reason: transitionReason.trim() },
        });
      }

      if (res.error) {
        toast.error("فشل تنفيذ عملية تحويل حالة الفترة");
      } else {
        toast.success(`تم تحديث حالة الفترة ${transitionPeriod.code} بنجاح`);
        setTransitionPeriod(null);
        setTransitionReason("");
        await loadYears();
      }
    } catch {
      toast.error("خطأ أثناء الاتصال بالخادم");
    } finally {
      setTransitioning(false);
    }
  };

  const selectedYear = years.find((y) => y.id === selectedYearId);

  const getPeriodBadge = (st: PeriodState) => {
    switch (st) {
      case "Open":
        return { label: "مفتوحة", bg: "var(--color-success-bg, #e6f4ea)", color: "var(--color-success)" };
      case "SoftClosed":
        return { label: "إغلاق أولي", bg: "var(--color-warning-bg, #fef7e0)", color: "var(--color-warning)" };
      case "Closed":
        return { label: "مغلقة نهائياً", bg: "var(--color-danger-bg, #fce8e6)", color: "var(--color-danger)" };
    }
  };

  return (
    <div style={{ display: "flex", flexDirection: "column", height: "100%", background: "var(--bg-app)" }}>
      {/* Create Year Dialog */}
      <PetraDialog
        open={createOpen}
        onClose={() => setCreateOpen(false)}
        title="إنشاء سنة مالية جديدة"
        width={450}
        actions={
          <>
            <PetraButton variant="secondary" onClick={() => setCreateOpen(false)}>
              إلغاء
            </PetraButton>
            <PetraButton variant="primary" disabled={creating} onClick={handleCreateYear}>
              {creating ? "جاري الإنشاء..." : "إنشاء السنة"}
            </PetraButton>
          </>
        }
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "var(--space-3)" }}>
          <PetraField label="رمز السنة المالية (Code)" required>
            <input
              type="text"
              value={newCode}
              onChange={(e) => setNewCode(e.target.value)}
              placeholder="مثال: 2026"
              style={{
                height: "var(--control-height)",
                padding: "0 var(--space-2)",
                borderRadius: "var(--radius)",
                border: "1px solid var(--border)",
                background: "var(--bg-input)",
                color: "var(--text)",
                fontFamily: "monospace",
              }}
            />
          </PetraField>

          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "var(--space-2)" }}>
            <PetraField label="تاريخ البداية" required>
              <input
                type="date"
                value={newStart}
                onChange={(e) => setNewStart(e.target.value)}
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

            <PetraField label="تاريخ النهاية" required>
              <input
                type="date"
                value={newEnd}
                onChange={(e) => setNewEnd(e.target.value)}
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
        </div>
      </PetraDialog>

      {/* Transition Period Dialog */}
      <PetraDialog
        open={!!transitionPeriod}
        onClose={() => setTransitionPeriod(null)}
        title={
          transitionAction === "soft-close"
            ? `إغلاق أولي للفترة (${transitionPeriod?.code})`
            : transitionAction === "close"
            ? `إغلاق نهائي للفترة (${transitionPeriod?.code})`
            : `إعادة فتح الفترة (${transitionPeriod?.code})`
        }
        width={450}
        actions={
          <>
            <PetraButton variant="secondary" onClick={() => setTransitionPeriod(null)}>
              إلغاء
            </PetraButton>
            <PetraButton
              variant={transitionAction === "close" ? "danger" : "primary"}
              disabled={transitioning || !transitionReason.trim()}
              onClick={handleExecuteTransition}
            >
              {transitioning ? "جاري المعالجة..." : "تأكيد"}
            </PetraButton>
          </>
        }
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "var(--space-3)" }}>
          <PetraField label="سبب الإجراء أو المبرر الإداري" required>
            <textarea
              value={transitionReason}
              onChange={(e) => setTransitionReason(e.target.value)}
              rows={3}
              placeholder="اكتب سبب إغلاق أو فتح الفترة..."
              style={{
                padding: "var(--space-2)",
                borderRadius: "var(--radius)",
                border: "1px solid var(--border)",
                background: "var(--bg-input)",
                color: "var(--text)",
                resize: "vertical",
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
            السنوات والفترات المالية (Fiscal Years & Accounting Periods)
          </h2>
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: "var(--space-2)" }}>
          <PetraButton variant="secondary" size="sm" onClick={() => setCreateOpen(true)}>
            إنشاء سنة مالية
          </PetraButton>
          <PetraButton variant="primary" size="sm" onClick={loadYears} disabled={loading}>
            {loading ? "جاري التحميل..." : "تحديث"}
          </PetraButton>
        </div>
      </div>

      {/* Two-Pane Layout */}
      <div style={{ flex: 1, display: "grid", gridTemplateColumns: "300px 1fr", overflow: "hidden" }}>
        {/* Left List of Years */}
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
            قائمة السنوات المالية
          </div>

          <div style={{ flex: 1, overflow: "auto" }}>
            {loading ? (
              <PetraLoadingSkeleton rows={5} />
            ) : error ? (
              <PetraErrorDetails status={error.status} message={error.message} onRetry={loadYears} />
            ) : years.length === 0 ? (
              <PetraEmptyState title="لا توجد سنوات مالية" description="قم بإنشاء السنة المالية الأولى للشركة." />
            ) : (
              years.map((y) => (
                <div
                  key={y.id}
                  onClick={() => setSelectedYearId(y.id)}
                  style={{
                    padding: "var(--space-2) var(--space-3)",
                    borderBottom: "1px solid var(--border-light)",
                    cursor: "pointer",
                    background: selectedYearId === y.id ? "var(--bg-sunken)" : "transparent",
                    borderRight: selectedYearId === y.id ? "3px solid var(--primary)" : "3px solid transparent",
                  }}
                >
                  <strong style={{ fontSize: "var(--fs-body)", fontFamily: "monospace" }}>{y.code}</strong>
                  <div style={{ fontSize: "var(--fs-caption)", color: "var(--text-muted)", marginTop: "2px" }}>
                    {y.rangeStart} إلى {y.rangeEnd}
                  </div>
                  <div style={{ fontSize: "var(--fs-caption)", color: "var(--primary)", marginTop: "2px" }}>
                    {y.periods.length} فترات محاسبية
                  </div>
                </div>
              ))
            )}
          </div>
        </div>

        {/* Right Periods Table */}
        <div style={{ flex: 1, overflow: "auto", padding: "var(--space-3)" }}>
          {selectedYear ? (
            <div
              style={{
                background: "var(--bg-surface)",
                borderRadius: "var(--radius)",
                border: "1px solid var(--border)",
                overflow: "hidden",
              }}
            >
              <div
                style={{
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "space-between",
                  padding: "var(--space-2) var(--space-3)",
                  background: "var(--bg-sunken)",
                  borderBottom: "1px solid var(--border)",
                }}
              >
                <div style={{ display: "flex", alignItems: "center", gap: "var(--space-2)" }}>
                  <h3 style={{ margin: 0, fontSize: "var(--fs-title)" }}>
                    فترات السنة المالية {selectedYear.code}
                  </h3>
                  <span style={{ fontSize: "var(--fs-caption)", color: "var(--text-muted)" }}>
                    ({selectedYear.rangeStart} إلى {selectedYear.rangeEnd})
                  </span>
                </div>
              </div>

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
                    <th style={{ padding: "var(--space-2) var(--space-3)" }}>رمز الفترة</th>
                    <th style={{ padding: "var(--space-2) var(--space-3)" }}>تاريخ البداية</th>
                    <th style={{ padding: "var(--space-2) var(--space-3)" }}>تاريخ النهاية</th>
                    <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "center" }}>الحالة</th>
                    <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "center" }}>إجراءات الإغلاق والفتح</th>
                  </tr>
                </thead>
                <tbody>
                  {selectedYear.periods.map((p, idx) => {
                    const badge = getPeriodBadge(p.state);
                    return (
                      <tr
                        key={p.id}
                        style={{
                          borderBottom: "1px solid var(--border-light)",
                          background: idx % 2 === 1 ? "var(--bg-sunken)" : "transparent",
                        }}
                      >
                        <td style={{ padding: "var(--space-2) var(--space-3)", fontFamily: "monospace", fontWeight: 700 }}>
                          {p.code}
                        </td>
                        <td style={{ padding: "var(--space-2) var(--space-3)", fontFamily: "monospace" }}>
                          {p.rangeStart}
                        </td>
                        <td style={{ padding: "var(--space-2) var(--space-3)", fontFamily: "monospace" }}>
                          {p.rangeEnd}
                        </td>
                        <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "center" }}>
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
                        </td>
                        <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "center" }}>
                          <div style={{ display: "inline-flex", gap: "var(--space-1)" }}>
                            {p.state === "Open" && (
                              <>
                                <PetraButton
                                  variant="secondary"
                                  size="sm"
                                  onClick={() => {
                                    setTransitionPeriod(p);
                                    setTransitionAction("soft-close");
                                  }}
                                >
                                  إغلاق أولي
                                </PetraButton>
                                <PetraButton
                                  variant="danger"
                                  size="sm"
                                  onClick={() => {
                                    setTransitionPeriod(p);
                                    setTransitionAction("close");
                                  }}
                                >
                                  إغلاق نهائي
                                </PetraButton>
                              </>
                            )}

                            {p.state === "SoftClosed" && (
                              <>
                                <PetraButton
                                  variant="primary"
                                  size="sm"
                                  onClick={() => {
                                    setTransitionPeriod(p);
                                    setTransitionAction("reopen");
                                  }}
                                >
                                  إعادة فتح
                                </PetraButton>
                                <PetraButton
                                  variant="danger"
                                  size="sm"
                                  onClick={() => {
                                    setTransitionPeriod(p);
                                    setTransitionAction("close");
                                  }}
                                >
                                  إغلاق نهائي
                                </PetraButton>
                              </>
                            )}

                            {p.state === "Closed" && (
                              <PetraButton
                                variant="secondary"
                                size="sm"
                                onClick={() => {
                                  setTransitionPeriod(p);
                                  setTransitionAction("reopen");
                                }}
                              >
                                إعادة فتح
                              </PetraButton>
                            )}
                          </div>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          ) : (
            <PetraEmptyState title="حدد سنة مالية" description="اختر سنة مالية من القائمة لعرض وإدارة فتراتها المحاسبية." />
          )}
        </div>
      </div>
    </div>
  );
}
