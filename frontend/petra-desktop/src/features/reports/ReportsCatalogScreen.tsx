import { useState, useEffect } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { useWorkspace } from "@/workspace/workspaceStore";
import { PetraButton, PetraLoadingSkeleton, PetraEmptyState, PetraErrorDetails, PetraField } from "@/components/ui";
import { toast } from "@/components/ui/toastStore";
import type { components } from "@/services/api/schema";

export interface ReportsCatalogScreenProps {
  params?: Readonly<Record<string, string>> | undefined;
}

type CatalogResponse = components["schemas"]["ProfessionalReportCatalogResponse"];
type ReportDef = components["schemas"]["ProfessionalReportDefinitionDto"];
type ReportExecutionResponse = components["schemas"]["ProfessionalReportExecutionResponse"];
type ReportRow = components["schemas"]["ProfessionalReportRowDto"];

export default function ReportsCatalogScreen({ params: _params }: ReportsCatalogScreenProps) {
  const api = useApi();
  const { companyId, branchId } = useSession((s) => ({ companyId: s.companyId, branchId: s.branchId }));
  const openWorkspaceWindow = useWorkspace((s) => s.open);

  const [catalog, setCatalog] = useState<CatalogResponse | null>(null);
  const [selectedReportKind, setSelectedReportKind] = useState<string | null>(null);
  const [isLoadingCatalog, setIsLoadingCatalog] = useState(false);
  const [catalogError, setCatalogError] = useState<{ status?: number; message?: string } | null>(null);

  // Execution parameters
  const [selectedFiscalYearId, setSelectedFiscalYearId] = useState<string>("");
  const [fromDate, setFromDate] = useState<string>(new Date().toISOString().slice(0, 10));
  const [toDate, setToDate] = useState<string>(new Date().toISOString().slice(0, 10));
  const [currencyCode, setCurrencyCode] = useState<string>("");

  // Execution state
  const [isExecuting, setIsExecuting] = useState(false);
  const [executionResult, setExecutionResult] = useState<ReportExecutionResponse | null>(null);

  const loadCatalog = async () => {
    if (!companyId || !branchId) return;

    setIsLoadingCatalog(true);
    setCatalogError(null);

    try {
      const res = await api.GET(
        "/api/v1/companies/{companyId}/branches/{branchId}/reporting/catalog",
        {
          params: {
            path: { companyId, branchId },
          },
        }
      );

      if (res.error || !res.data) {
        setCatalogError({
          status: res.response?.status,
          message: (res.error as { title?: string; detail?: string })?.detail ?? "فشل تحميل دليل التقارير المحاسبية",
        });
        return;
      }

      setCatalog(res.data);
      const reports = res.data.reports ?? [];
      if (reports.length > 0 && !selectedReportKind) {
        setSelectedReportKind(reports[0]?.kind ?? null);
      }
      if (res.data.fiscalYears && res.data.fiscalYears.length > 0) {
        const fy = res.data.fiscalYears[0];
        if (fy) {
          setSelectedFiscalYearId(fy.id);
          if (fy.from) setFromDate(fy.from);
          if (fy.to) setToDate(fy.to);
        }
      }
    } catch (err) {
      setCatalogError({
        message: err instanceof Error ? err.message : "خطأ غير متوقع",
      });
    } finally {
      setIsLoadingCatalog(false);
    }
  };

  useEffect(() => {
    if (companyId && branchId) {
      void loadCatalog();
    }
  }, [companyId, branchId]);

  const reports: ReportDef[] = catalog?.reports ?? [];
  const selectedReport = reports.find((r) => r.kind === selectedReportKind) ?? null;

  const handleExecute = async () => {
    if (!companyId || !branchId || !selectedReport) return;
    if (!selectedFiscalYearId) {
      toast.warning("يرجى اختيار السنة المالية");
      return;
    }

    setIsExecuting(true);
    setExecutionResult(null);

    try {
      const res = await api.POST(
        "/api/v1/companies/{companyId}/branches/{branchId}/reporting/execute",
        {
          params: {
            path: { companyId, branchId },
          },
          body: {
            kind: selectedReport.kind,
            fiscalYearId: selectedFiscalYearId,
            from: fromDate,
            to: toDate,
            ...(currencyCode ? { currencyCode } : {}),
          },
        }
      );

      if (res.error || !res.data) {
        toast.error((res.error as { title?: string; detail?: string })?.detail ?? "فشل تنفيذ التقرير المالي");
        return;
      }

      setExecutionResult(res.data);
      toast.success(`تم تنفيذ التقرير: ${res.data.rows?.length ?? 0} سطر مالي`);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "خطأ أثناء تنفيذ التقرير");
    } finally {
      setIsExecuting(false);
    }
  };

  const rows: ReportRow[] = executionResult?.rows ?? [];

  const exportTsv = () => {
    if (rows.length === 0) {
      toast.warning("لا توجد بيانات تقرير للتصدير");
      return;
    }

    const headers = [
      "التاريخ",
      "تسلسل القيد",
      "المصدر",
      "رقم المستند",
      "رمز الحساب",
      "اسم الحساب",
      "الطرف",
      "البيان والشرح",
      "مدين",
      "دائن",
      "الرصيد الفعلي",
      "العملة",
    ];

    const tsvRows = rows.map((r) => [
      r.date ?? "",
      r.postingSequence != null ? String(r.postingSequence) : "",
      r.source ?? "",
      r.documentNumber ?? "",
      r.accountCode ?? "",
      r.accountName ?? "",
      r.partyName ?? "",
      r.description ?? "",
      r.debit ?? "0.00",
      r.credit ?? "0.00",
      r.actualBalance ?? "0.00",
      r.currencyCode ?? "",
    ]);

    const tsvContent = [headers.join("\t"), ...tsvRows.map((row) => row.join("\t"))].join("\n");
    const blob = new Blob(["\uFEFF" + tsvContent], { type: "text/tab-separated-values;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `report-${selectedReport?.kind || "export"}-${fromDate}.tsv`;
    a.click();
    URL.revokeObjectURL(url);
    toast.success("تم تصدير التقرير بنجاح");
  };

  return (
    <div className="reports-catalog-screen" style={{ padding: "16px", height: "100%", display: "flex", flexDirection: "column", gap: "16px", overflowY: "auto" }}>
      {/* Header */}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", borderBottom: "1px solid var(--border)", paddingBottom: "12px" }}>
        <div>
          <h2 style={{ margin: 0, fontSize: "1.25rem", color: "var(--header-bg)" }}>
            مركز التقارير المحاسبية والتنفيذ الفوري (Reports Catalog & Engine)
          </h2>
          <p style={{ margin: "4px 0 0", fontSize: "0.85rem", color: "var(--text-muted)" }}>
            استعراض دليل التقارير الخادمية المعتمدة وتشغيل التقارير المجمعة والتفصيلية وفق الصلاحيات والسنوات المالية.
          </p>
        </div>
        <div style={{ display: "flex", gap: "8px" }}>
          <PetraButton variant="secondary" onClick={loadCatalog} disabled={isLoadingCatalog} size="sm">
            تحديث الدليل
          </PetraButton>
          <PetraButton variant="secondary" onClick={exportTsv} disabled={rows.length === 0} size="sm">
            تصدير TSV
          </PetraButton>
        </div>
      </div>

      {isLoadingCatalog ? (
        <PetraLoadingSkeleton rows={8} />
      ) : catalogError ? (
        <PetraErrorDetails status={catalogError.status} message={catalogError.message} onRetry={loadCatalog} />
      ) : (
        <div style={{ flex: 1, display: "grid", gridTemplateColumns: "360px 1fr", gap: "16px", minHeight: 0 }}>
          {/* Right Pane: Reports List */}
          <div style={{ border: "1px solid var(--border)", borderRadius: "4px", background: "var(--bg-surface)", display: "flex", flexDirection: "column" }}>
            <div style={{ padding: "10px 12px", borderBottom: "1px solid var(--border)", fontWeight: "bold", color: "var(--header-bg)", fontSize: "0.9rem" }}>
              دليل التقارير المحاسبية ({reports.length})
            </div>
            <div style={{ flex: 1, overflowY: "auto" }}>
              {reports.length === 0 ? (
                <PetraEmptyState title="لا توجد تقارير متاحة" />
              ) : (
                reports.map((rep) => {
                  const isSelected = rep.kind === selectedReportKind;
                  return (
                    <div
                      key={rep.kind}
                      onClick={() => {
                        setSelectedReportKind(rep.kind);
                        setExecutionResult(null);
                      }}
                      style={{
                        padding: "12px",
                        borderBottom: "1px solid var(--border)",
                        background: isSelected ? "#F0FDF4" : "transparent",
                        borderRight: isSelected ? "4px solid var(--primary)" : "4px solid transparent",
                        cursor: "pointer",
                      }}
                    >
                      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                        <div style={{ fontWeight: "bold", fontSize: "0.85rem", color: "var(--header-bg)" }}>
                          {rep.nameArabic}
                        </div>
                        <span
                          style={{
                            fontSize: "0.7rem",
                            padding: "2px 6px",
                            borderRadius: "3px",
                            background: "#ECEFF1",
                            color: "#37474F",
                          }}
                        >
                          {rep.kind}
                        </span>
                      </div>
                      <div style={{ fontSize: "0.75rem", color: "var(--text-muted)", marginTop: "4px" }}>
                        {rep.nameEnglish}
                      </div>
                      <div style={{ fontSize: "0.7rem", color: "var(--primary)", marginTop: "2px" }}>
                        مصادر: {rep.sources?.length ?? 0} | حقول: {rep.fields?.length ?? 0}
                      </div>
                    </div>
                  );
                })
              )}
            </div>
          </div>

          {/* Left Pane: Parameters & Execution View */}
          {selectedReport ? (
            <div style={{ border: "1px solid var(--border)", borderRadius: "4px", background: "var(--bg-surface)", padding: "16px", display: "flex", flexDirection: "column", gap: "16px", overflowY: "auto" }}>
              {/* Report Header */}
              <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", borderBottom: "1px solid var(--border)", paddingBottom: "10px" }}>
                <div>
                  <h3 style={{ margin: 0, fontSize: "1.05rem", color: "var(--header-bg)" }}>
                    {selectedReport.nameArabic} ({selectedReport.nameEnglish})
                  </h3>
                  <div style={{ fontSize: "0.8rem", color: "var(--text-muted)", marginTop: "4px" }}>
                    رمز التقرير: <code>{selectedReport.kind}</code> | الحد الأقصى للأسطر: {catalog?.maximumRows ?? 100000}
                  </div>
                </div>
                <PetraButton
                  variant="primary"
                  size="sm"
                  onClick={handleExecute}
                  disabled={isExecuting}
                >
                  {isExecuting ? "جارٍ التنفيذ..." : "تشغيل التقرير المالي"}
                </PetraButton>
              </div>

              {/* Execution Filters Bar */}
              <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(180px, 1fr))", gap: "12px", background: "var(--bg-app)", padding: "12px", borderRadius: "4px" }}>
                <PetraField label="السنة المالية">
                  <select
                    className="petra-select"
                    value={selectedFiscalYearId}
                    onChange={(e) => {
                      setSelectedFiscalYearId(e.target.value);
                      const fy = catalog?.fiscalYears?.find((f) => f.id === e.target.value);
                      if (fy?.from) setFromDate(fy.from);
                      if (fy?.to) setToDate(fy.to);
                    }}
                    style={{ width: "100%", padding: "5px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
                  >
                    {catalog?.fiscalYears?.map((fy) => (
                      <option key={fy.id} value={fy.id}>
                        سنة {fy.code} ({fy.from} إلى {fy.to})
                      </option>
                    ))}
                  </select>
                </PetraField>

                <PetraField label="من تاريخ">
                  <input
                    type="date"
                    className="petra-input"
                    value={fromDate}
                    onChange={(e) => setFromDate(e.target.value)}
                    style={{ width: "100%", padding: "5px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
                  />
                </PetraField>

                <PetraField label="إلى تاريخ">
                  <input
                    type="date"
                    className="petra-input"
                    value={toDate}
                    onChange={(e) => setToDate(e.target.value)}
                    style={{ width: "100%", padding: "5px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
                  />
                </PetraField>

                <PetraField label="العملة (اختياري)">
                  <input
                    type="text"
                    className="petra-input"
                    placeholder="SAR"
                    value={currencyCode}
                    onChange={(e) => setCurrencyCode(e.target.value)}
                    style={{ width: "100%", padding: "5px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
                  />
                </PetraField>
              </div>

              {/* Execution Results */}
              {isExecuting ? (
                <PetraLoadingSkeleton rows={8} />
              ) : executionResult ? (
                <div style={{ flex: 1, display: "flex", flexDirection: "column", gap: "8px" }}>
                  <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                    <div style={{ fontWeight: "bold", fontSize: "0.9rem", color: "var(--header-bg)" }}>
                      نتائج التقرير المنفذة ({rows.length} سطر)
                    </div>
                    {executionResult.summary && (
                      <div style={{ fontSize: "0.85rem", fontFamily: "var(--font-mono, monospace)", color: "var(--primary)" }}>
                        مدين الفترة: {executionResult.summary.actualPeriodDebit ?? "0.00"} | دائن الفترة: {executionResult.summary.actualPeriodCredit ?? "0.00"} | الختامي: {executionResult.summary.actualClosing ?? "0.00"}
                      </div>
                    )}
                  </div>

                  <div style={{ overflowX: "auto", border: "1px solid var(--border)", borderRadius: "4px" }}>
                    <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.85rem", textAlign: "right" }}>
                      <thead>
                        <tr style={{ background: "var(--header-bg)", color: "#FFFFFF" }}>
                          <th style={{ padding: "8px" }}>التاريخ</th>
                          <th style={{ padding: "8px" }}>تسلسل القيد</th>
                          <th style={{ padding: "8px" }}>المصدر</th>
                          <th style={{ padding: "8px" }}>رقم المستند</th>
                          <th style={{ padding: "8px" }}>رمز الحساب</th>
                          <th style={{ padding: "8px" }}>اسم الحساب</th>
                          <th style={{ padding: "8px" }}>البيان</th>
                          <th style={{ padding: "8px" }}>مدين</th>
                          <th style={{ padding: "8px" }}>دائن</th>
                          <th style={{ padding: "8px" }}>الرصيد الفعلي</th>
                        </tr>
                      </thead>
                      <tbody>
                        {rows.map((row, idx) => (
                          <tr key={row.rowId || idx} style={{ borderBottom: "1px solid var(--border)", background: idx % 2 === 0 ? "transparent" : "var(--bg-app)" }}>
                            <td style={{ padding: "8px" }}>{row.date ?? "—"}</td>
                            <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)" }}>
                              {row.postingSequence != null ? (
                                <span
                                  style={{ color: "var(--primary)", cursor: row.journalId ? "pointer" : "default", textDecoration: row.journalId ? "underline" : "none" }}
                                  onClick={() => {
                                    if (row.journalId) {
                                      openWorkspaceWindow({
                                        key: `journal-${row.journalId}`,
                                        title: `قيد: ${row.postingSequence}`,
                                        screen: "journal-entry",
                                        params: { id: row.journalId },
                                      });
                                    }
                                  }}
                                >
                                  {row.postingSequence}
                                </span>
                              ) : "—"}
                            </td>
                            <td style={{ padding: "8px" }}>{row.source ?? "—"}</td>
                            <td style={{ padding: "8px", fontWeight: "bold" }}>{row.documentNumber ?? "—"}</td>
                            <td style={{ padding: "8px" }}>
                              <span
                                style={{ color: "var(--primary)", cursor: row.accountId ? "pointer" : "default", textDecoration: row.accountId ? "underline" : "none" }}
                                onClick={() => {
                                  if (row.accountId) {
                                    openWorkspaceWindow({
                                      key: `account-ledger-${row.accountId}`,
                                      title: `دفتر أستاذ: ${row.accountCode}`,
                                      screen: "account-ledger",
                                      params: { accountId: row.accountId },
                                    });
                                  }
                                }}
                              >
                                {row.accountCode}
                              </span>
                            </td>
                            <td style={{ padding: "8px" }}>{row.accountName}</td>
                            <td style={{ padding: "8px", maxWidth: "250px" }}>{row.description}</td>
                            <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)", color: "#1B5E20" }}>{row.debit ?? "0.00"}</td>
                            <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)", color: "#B71C1C" }}>{row.credit ?? "0.00"}</td>
                            <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)", fontWeight: "bold" }}>{row.actualBalance ?? "0.00"}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>
              ) : (
                <PetraEmptyState title="حدد معايير التقرير واضغط 'تشغيل التقرير المالي' للحصول على النتائج الفورية" />
              )}
            </div>
          ) : (
            <PetraEmptyState title="اختر تقريراً من القائمة لعرض معاييره وتشغيله" />
          )}
        </div>
      )}
    </div>
  );
}
