import { useState, useEffect } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { PetraButton, PetraLoadingSkeleton, PetraEmptyState, PetraErrorDetails, PetraDialog, PetraField } from "@/components/ui";
import { toast } from "@/components/ui/toastStore";

export interface BankReconciliationScreenProps {
  params?: Readonly<Record<string, string>> | undefined;
}

interface ReconciliationSummary {
  id: string;
  accountId: string;
  accountCode: string;
  statementDate: string;
  statementBalance: string;
  difference: string;
  status: string;
}

interface ReconciliationLine {
  journalLineId: string;
  journalId: string;
  accountingDate: string;
  postingSequence?: number | null;
  description: string;
  amount: string;
  cleared: boolean;
}

interface ReconciliationDetail {
  id: string;
  branchId: string;
  accountId: string;
  accountCode: string;
  statementDate: string;
  statementBalance: string;
  priorClearedBalance: string;
  clearedInThisReconciliation: string;
  difference: string;
  bookBalanceAtStatementDate: string;
  status: string;
  version: string;
  lines: ReconciliationLine[];
}

export default function BankReconciliationScreen({ params: _params }: BankReconciliationScreenProps) {
  const api = useApi();
  const { companyId, branchId } = useSession((s) => ({ companyId: s.companyId, branchId: s.branchId }));

  const [list, setList] = useState<ReconciliationSummary[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [activeReconciliation, setActiveReconciliation] = useState<ReconciliationDetail | null>(null);

  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<{ status?: number; message?: string } | null>(null);

  // New reconciliation dialog
  const [startOpen, setStartOpen] = useState(false);
  const [bankAccountId, setBankAccountId] = useState("");
  const [statementDate, setStatementDate] = useState(() => new Date().toISOString().substring(0, 10));
  const [statementBalance, setStatementBalance] = useState("");
  const [starting, setStarting] = useState(false);

  const [actionInProgress, setActionInProgress] = useState(false);

  const loadList = async () => {
    if (!companyId || !branchId) return;

    setLoading(true);
    setError(null);
    try {
      const res = await api.GET("/api/v1/companies/{companyId}/branches/{branchId}/treasury/bank-reconciliations", {
        params: {
          path: { companyId, branchId },
        },
      });

      if (res.error) {
        setError({ status: res.response.status, message: "فشل تحميل قائمة التسويات البنكية" });
      } else if (res.data) {
        const items = res.data as unknown as ReconciliationSummary[];
        setList(items);
        if (!selectedId && items.length > 0 && items[0]) {
          setSelectedId(items[0].id);
        }
      }
    } catch (err) {
      setError({ message: err instanceof Error ? err.message : "خطأ غير متوقع" });
    } finally {
      setLoading(false);
    }
  };

  const loadDetail = async (id: string) => {
    if (!companyId || !branchId) return;

    try {
      const res = await api.GET("/api/v1/companies/{companyId}/branches/{branchId}/treasury/bank-reconciliations/{reconciliationId}", {
        params: {
          path: { companyId, branchId, reconciliationId: id },
        },
      });

      if (res.data) {
        setActiveReconciliation(res.data as unknown as ReconciliationDetail);
      }
    } catch {
      toast.error("فشل تحميل تفاصيل التسوية البنكية");
    }
  };

  useEffect(() => {
    loadList();
  }, [companyId, branchId]);

  useEffect(() => {
    if (selectedId) {
      loadDetail(selectedId);
    } else {
      setActiveReconciliation(null);
    }
  }, [selectedId]);

  const handleStartReconciliation = async () => {
    if (!companyId || !branchId || !bankAccountId.trim() || !statementBalance.trim()) {
      toast.error("يرجى إدخال حساب البنك ورصيد كشف الحساب");
      return;
    }

    setStarting(true);
    try {
      const res = await api.POST("/api/v1/companies/{companyId}/branches/{branchId}/treasury/bank-reconciliations", {
        params: {
          path: { companyId, branchId },
        },
        body: {
          accountId: bankAccountId.trim(),
          statementDate: statementDate,
          statementBalance: statementBalance.trim(),
        },
      });

      if (res.error) {
        toast.error("فشل بدء تسوية بنكية جديدة");
      } else if (res.data) {
        toast.success("تم فتح مذكرة التسوية البنكية بنجاح");
        setStartOpen(false);
        setBankAccountId("");
        setStatementBalance("");
        await loadList();
      }
    } catch {
      toast.error("خطأ أثناء الاتصال بالخادم");
    } finally {
      setStarting(false);
    }
  };

  const toggleLineCleared = async (line: ReconciliationLine) => {
    if (!companyId || !branchId || !activeReconciliation) return;

    const newCleared = !line.cleared;
    try {
      const res = await api.POST("/api/v1/companies/{companyId}/branches/{branchId}/treasury/bank-reconciliations/{reconciliationId}/lines", {
        params: {
          path: { companyId, branchId, reconciliationId: activeReconciliation.id },
        },
        body: {
          journalLineIds: [line.journalLineId],
          cleared: newCleared,
          expectedVersion: activeReconciliation.version,
        },
      });

      if (res.error) {
        toast.error("فشل تحديث حالة مطابقة السطر");
      } else {
        await loadDetail(activeReconciliation.id);
      }
    } catch {
      toast.error("خطأ في الاتصال أثناء تحديث المطابقة");
    }
  };

  const handleComplete = async () => {
    if (!companyId || !branchId || !activeReconciliation) return;

    setActionInProgress(true);
    try {
      const res = await api.POST("/api/v1/companies/{companyId}/branches/{branchId}/treasury/bank-reconciliations/{reconciliationId}/complete", {
        params: {
          path: { companyId, branchId, reconciliationId: activeReconciliation.id },
        },
        body: {
          expectedVersion: activeReconciliation.version,
        },
      });

      if (res.error) {
        toast.error("تعذر إتمام التسوية: تأكد من تصفير الفارق بين كشف الحساب والدفاتر");
      } else {
        toast.success("تم إتمام واعتماد التسوية البنكية بنجاح");
        await loadDetail(activeReconciliation.id);
        await loadList();
      }
    } catch {
      toast.error("خطأ أثناء إتمام التسوية");
    } finally {
      setActionInProgress(false);
    }
  };

  const handleDiscard = async () => {
    if (!companyId || !branchId || !activeReconciliation) return;

    setActionInProgress(true);
    try {
      const res = await api.POST("/api/v1/companies/{companyId}/branches/{branchId}/treasury/bank-reconciliations/{reconciliationId}/discard", {
        params: {
          path: { companyId, branchId, reconciliationId: activeReconciliation.id },
        },
        body: {
          expectedVersion: activeReconciliation.version,
        },
      });

      if (res.error) {
        toast.error("فشل إلغاء التسوية");
      } else {
        toast.success("تم إلغاء التسوية بنجاح");
        setSelectedId(null);
        await loadList();
      }
    } catch {
      toast.error("خطأ أثناء إلغاء التسوية");
    } finally {
      setActionInProgress(false);
    }
  };

  const copyToExcel = () => {
    if (!activeReconciliation || activeReconciliation.lines.length === 0) return;
    const header = "المطابقة\tالتاريخ\tرقم القيد\tالبيان\tالمبلغ";
    const rows = activeReconciliation.lines.map((l) =>
      `${l.cleared ? "مطابق" : "معلق"}\t${l.accountingDate}\t${l.postingSequence ?? ""}\t${l.description}\t${l.amount}`
    );
    navigator.clipboard?.writeText([header, ...rows].join("\r\n")).then(() => {
      toast.success("تم نسخ أسطر التسوية البنكية إلى الحافظة (TSV)");
    });
  };

  const isBalanced = activeReconciliation && (activeReconciliation.difference === "0" || activeReconciliation.difference === "0.00" || activeReconciliation.difference === "0.0000");

  return (
    <div style={{ display: "flex", flexDirection: "column", height: "100%", background: "var(--bg-app)" }}>
      {/* Start Reconciliation Dialog */}
      <PetraDialog
        open={startOpen}
        onClose={() => setStartOpen(false)}
        title="بدء مذكرة تسوية بنكية جديدة"
        width={450}
        actions={
          <>
            <PetraButton variant="secondary" onClick={() => setStartOpen(false)}>
              إلغاء
            </PetraButton>
            <PetraButton variant="primary" disabled={starting} onClick={handleStartReconciliation}>
              {starting ? "جاري الفتح..." : "بدء التسوية"}
            </PetraButton>
          </>
        }
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "var(--space-3)" }}>
          <PetraField label="معرّف حساب البنك (Bank Account ID)" required>
            <input
              type="text"
              value={bankAccountId}
              onChange={(e) => setBankAccountId(e.target.value)}
              placeholder="UUID لحساب البنك"
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

          <PetraField label="تاريخ كشف الحساب البنكي" required>
            <input
              type="date"
              value={statementDate}
              onChange={(e) => setStatementDate(e.target.value)}
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

          <PetraField label="الرصيد الفعلي في كشف البنك (Statement Balance)" required>
            <input
              type="text"
              value={statementBalance}
              onChange={(e) => setStatementBalance(e.target.value)}
              placeholder="0.00"
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
            التسوية والمطابقة البنكية (Bank Reconciliation)
          </h2>
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: "var(--space-2)" }}>
          <PetraButton variant="secondary" size="sm" onClick={() => setStartOpen(true)}>
            مذكرة تسوية جديدة
          </PetraButton>
          <PetraButton variant="secondary" size="sm" onClick={copyToExcel} disabled={!activeReconciliation?.lines.length}>
            نسخ إلى Excel
          </PetraButton>
          <PetraButton variant="primary" size="sm" onClick={loadList} disabled={loading}>
            {loading ? "جاري التحميل..." : "تحديث"}
          </PetraButton>
        </div>
      </div>

      {/* Two-Pane Body */}
      <div style={{ flex: 1, display: "grid", gridTemplateColumns: "320px 1fr", overflow: "hidden" }}>
        {/* Left List of Reconciliations */}
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
            سجل مذكرات التسوية البنكية
          </div>

          <div style={{ flex: 1, overflow: "auto" }}>
            {loading ? (
              <PetraLoadingSkeleton rows={6} />
            ) : error ? (
              <PetraErrorDetails status={error.status} message={error.message} onRetry={loadList} />
            ) : list.length === 0 ? (
              <PetraEmptyState title="لا توجد تسويات بنكية" description="ابدأ بفتح مذكرة تسوية لمطابقة حساب بنكي." />
            ) : (
              list.map((item) => (
                <div
                  key={item.id}
                  onClick={() => setSelectedId(item.id)}
                  style={{
                    padding: "var(--space-2) var(--space-3)",
                    borderBottom: "1px solid var(--border-light)",
                    cursor: "pointer",
                    background: selectedId === item.id ? "var(--bg-sunken)" : "transparent",
                    borderRight: selectedId === item.id ? "3px solid var(--primary)" : "3px solid transparent",
                  }}
                >
                  <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between" }}>
                    <strong style={{ fontFamily: "monospace" }}>{item.accountCode}</strong>
                    <span
                      style={{
                        padding: "2px 8px",
                        borderRadius: "var(--radius)",
                        fontSize: "var(--fs-caption)",
                        background: item.status === "Completed" ? "var(--color-success-bg, #e6f4ea)" : "var(--color-warning-bg, #fef7e0)",
                        color: item.status === "Completed" ? "var(--color-success)" : "var(--color-warning)",
                      }}
                    >
                      {item.status}
                    </span>
                  </div>
                  <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", fontSize: "var(--fs-caption)", color: "var(--text-muted)", marginTop: "4px" }}>
                    <span>{item.statementDate}</span>
                    <span style={{ fontFamily: "monospace" }}>الفارق: {item.difference}</span>
                  </div>
                </div>
              ))
            )}
          </div>
        </div>

        {/* Right Active Reconciliation Details */}
        <div style={{ display: "flex", flexDirection: "column", overflow: "hidden" }}>
          {activeReconciliation ? (
            <>
              {/* Summary Metrics Bar */}
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
                  <span style={{ color: "var(--text-muted)", display: "block" }}>رصيد كشف البنك</span>
                  <strong style={{ fontSize: "var(--fs-body)", fontFamily: "monospace" }}>
                    {activeReconciliation.statementBalance}
                  </strong>
                </div>

                <div style={{ background: "var(--bg-sunken)", padding: "var(--space-2)", borderRadius: "var(--radius)" }}>
                  <span style={{ color: "var(--text-muted)", display: "block" }}>رصيد الدفاتر</span>
                  <strong style={{ fontSize: "var(--fs-body)", fontFamily: "monospace" }}>
                    {activeReconciliation.bookBalanceAtStatementDate}
                  </strong>
                </div>

                <div style={{ background: "var(--bg-sunken)", padding: "var(--space-2)", borderRadius: "var(--radius)" }}>
                  <span style={{ color: "var(--text-muted)", display: "block" }}>المطابق في هذه الجلسة</span>
                  <strong style={{ fontSize: "var(--fs-body)", fontFamily: "monospace" }}>
                    {activeReconciliation.clearedInThisReconciliation}
                  </strong>
                </div>

                <div style={{ background: "var(--bg-sunken)", padding: "var(--space-2)", borderRadius: "var(--radius)" }}>
                  <span style={{ color: "var(--text-muted)", display: "block" }}>فارق المطابقة</span>
                  <strong
                    style={{
                      fontSize: "var(--fs-body)",
                      fontFamily: "monospace",
                      color: isBalanced ? "var(--color-success)" : "var(--color-danger)",
                    }}
                  >
                    {activeReconciliation.difference}
                  </strong>
                </div>

                <div style={{ display: "flex", alignItems: "center", justifyContent: "flex-end", gap: "var(--space-2)" }}>
                  {activeReconciliation.status !== "Completed" && (
                    <>
                      <PetraButton
                        variant="primary"
                        size="sm"
                        disabled={!isBalanced || actionInProgress}
                        onClick={handleComplete}
                      >
                        اعتماد التسوية
                      </PetraButton>
                      <PetraButton
                        variant="danger"
                        size="sm"
                        disabled={actionInProgress}
                        onClick={handleDiscard}
                      >
                        إلغاء
                      </PetraButton>
                    </>
                  )}
                </div>
              </div>

              {/* Lines Table */}
              <div style={{ flex: 1, overflow: "auto", padding: "var(--space-3)" }}>
                {activeReconciliation.lines.length === 0 ? (
                  <PetraEmptyState title="لا توجد حركات بنكية مفتوحة للمطابقة" description="جميع حركات الحساب مطابقة أو لا توجد حركات في الفترة." />
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
                          <th style={{ padding: "var(--space-2) var(--space-3)", width: "50px", textAlign: "center" }}>
                            مطابقة
                          </th>
                          <th style={{ padding: "var(--space-2) var(--space-3)" }}>التاريخ</th>
                          <th style={{ padding: "var(--space-2) var(--space-3)" }}>رقم القيد</th>
                          <th style={{ padding: "var(--space-2) var(--space-3)" }}>بيان الحركة</th>
                          <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left" }}>المبلغ</th>
                        </tr>
                      </thead>
                      <tbody>
                        {activeReconciliation.lines.map((line, idx) => (
                          <tr
                            key={line.journalLineId}
                            style={{
                              borderBottom: "1px solid var(--border-light)",
                              background: line.cleared ? "var(--color-success-bg, #f0fdf4)" : idx % 2 === 1 ? "var(--bg-sunken)" : "transparent",
                            }}
                          >
                            <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "center" }}>
                              <input
                                type="checkbox"
                                checked={line.cleared}
                                disabled={activeReconciliation.status === "Completed"}
                                onChange={() => toggleLineCleared(line)}
                                style={{ cursor: "pointer" }}
                              />
                            </td>
                            <td style={{ padding: "var(--space-2) var(--space-3)", fontFamily: "monospace" }}>
                              {line.accountingDate}
                            </td>
                            <td style={{ padding: "var(--space-2) var(--space-3)", fontFamily: "monospace" }}>
                              {line.postingSequence ?? "—"}
                            </td>
                            <td style={{ padding: "var(--space-2) var(--space-3)" }}>{line.description}</td>
                            <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left", fontFamily: "monospace", fontWeight: 700 }}>
                              {line.amount}
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
            <PetraEmptyState title="اختر مذكرة تسوية بنكية" description="حدد مذكرة من القائمة أو أنشئ مذكرة تسوية جديدة لمطابقة حركات البنك." />
          )}
        </div>
      </div>
    </div>
  );
}
