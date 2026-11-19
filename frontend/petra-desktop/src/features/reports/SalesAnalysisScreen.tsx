import { useState, useEffect } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { useWorkspace } from "@/workspace/workspaceStore";
import { PetraButton, PetraLoadingSkeleton, PetraEmptyState, PetraErrorDetails, PetraDialog, PetraField } from "@/components/ui";
import { toast } from "@/components/ui/toastStore";

export interface SalesAnalysisScreenProps {
  params?: Readonly<Record<string, string>> | undefined;
}

type GroupingType = "Pattern" | "Customer" | "Item";

interface SalesAnalysisRow {
  keyId?: string | null;
  keyCode: string;
  keyName: string;
  unitCode?: string | null;
  quantity: string;
  documentCount: number;
  netBase: string;
  additionsBase: string;
  taxBase: string;
  totalBase: string;
}

interface SalesAnalysisData {
  from?: string;
  to?: string;
  grouping?: GroupingType;
  baseCurrency?: string;
  rows?: SalesAnalysisRow[];
  netBase?: string;
  additionsBase?: string;
  taxBase?: string;
  totalBase?: string;
  glRevenueBase?: string | null;
  revenueDifference?: string | null;
}

export default function SalesAnalysisScreen({ params: _params }: SalesAnalysisScreenProps) {
  const api = useApi();
  const { companyId, branchId } = useSession((s) => ({ companyId: s.companyId, branchId: s.branchId }));
  const openWorkspaceWindow = useWorkspace((s) => s.open);

  const [grouping, setGrouping] = useState<GroupingType>("Customer");
  const [fromDate, setFromDate] = useState("");
  const [toDate, setToDate] = useState("");
  const [optionsOpen, setOptionsOpen] = useState(false);

  const [data, setData] = useState<SalesAnalysisData | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<{ status?: number; message?: string } | null>(null);

  const loadReport = async () => {
    if (!companyId || !branchId) return;

    setLoading(true);
    setError(null);
    try {
      const res = await api.GET("/api/v1/companies/{companyId}/branches/{branchId}/reports/sales-analysis", {
        params: {
          path: { companyId, branchId },
          query: {
            groupBy: grouping,
            ...(fromDate ? { from: fromDate } : {}),
            ...(toDate ? { to: toDate } : {}),
          },
        },
      });

      if (res.error) {
        setError({ status: res.response.status, message: "فشل استخراج تقرير تحليل المبيعات من الخادم" });
      } else if (res.data) {
        setData(res.data as unknown as SalesAnalysisData);
      }
    } catch (err) {
      setError({ message: err instanceof Error ? err.message : "خطأ غير متوقع أثناء تحميل التقرير" });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadReport();
  }, [companyId, branchId, grouping]);

  const copyToExcel = () => {
    if (!data?.rows || data.rows.length === 0) return;
    const header = "الرمز\tالاسم\tالوحدة\tالكمية\tعدد الفواتير\tالصافي\tالإضافات\tالضريبة\tالإجمالي";
    const rows = data.rows.map((r) =>
      `${r.keyCode}\t${r.keyName}\t${r.unitCode ?? ""}\t${r.quantity}\t${r.documentCount}\t${r.netBase}\t${r.additionsBase}\t${r.taxBase}\t${r.totalBase}`
    );
    const tsv = [header, ...rows].join("\r\n");
    navigator.clipboard?.writeText(tsv).then(() => {
      toast.success("تم نسخ جدول تحليل المبيعات إلى الحافظة بصيغة Excel (TSV)");
    });
  };

  return (
    <div style={{ display: "flex", flexDirection: "column", height: "100%", background: "var(--bg-app)" }}>
      {/* Dialog for filtering options */}
      <PetraDialog
        open={optionsOpen}
        onClose={() => setOptionsOpen(false)}
        title="خيارات تقرير تحليل المبيعات"
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
                loadReport();
              }}
            >
              تطبيق الفلاتر
            </PetraButton>
          </>
        }
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "var(--space-3)" }}>
          <PetraField label="تجميع التقرير حسب (Group By)">
            <select
              value={grouping}
              onChange={(e) => setGrouping(e.target.value as GroupingType)}
              style={{
                height: "var(--control-height)",
                padding: "0 var(--space-2)",
                borderRadius: "var(--radius)",
                border: "1px solid var(--border)",
                background: "var(--bg-input)",
                color: "var(--text)",
              }}
            >
              <option value="Customer">حسب العملاء (By Customer)</option>
              <option value="Item">حسب المواد والأصناف (By Item)</option>
              <option value="Pattern">حسب أنماط الفواتير (By Pattern)</option>
            </select>
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
        </div>
      </PetraDialog>

      {/* Header bar */}
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
            تحليل المبيعات (Sales Analysis)
          </h2>
          <div style={{ display: "flex", gap: "var(--space-1)" }}>
            <PetraButton
              variant={grouping === "Customer" ? "primary" : "secondary"}
              size="sm"
              onClick={() => setGrouping("Customer")}
            >
              حسب العملاء
            </PetraButton>
            <PetraButton
              variant={grouping === "Item" ? "primary" : "secondary"}
              size="sm"
              onClick={() => setGrouping("Item")}
            >
              حسب المواد
            </PetraButton>
            <PetraButton
              variant={grouping === "Pattern" ? "primary" : "secondary"}
              size="sm"
              onClick={() => setGrouping("Pattern")}
            >
              حسب الأنماط
            </PetraButton>
          </div>
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: "var(--space-2)" }}>
          <PetraButton variant="secondary" size="sm" onClick={() => setOptionsOpen(true)}>
            خيارات التقرير
          </PetraButton>
          <PetraButton variant="secondary" size="sm" onClick={copyToExcel} disabled={!data?.rows?.length}>
            نسخ إلى Excel
          </PetraButton>
          <PetraButton variant="primary" size="sm" onClick={loadReport} disabled={loading}>
            {loading ? "جاري التحميل..." : "تحديث"}
          </PetraButton>
        </div>
      </div>

      {/* Reconciliation & KPI Bar */}
      {data && (
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(6, 1fr)",
            gap: "var(--space-2)",
            padding: "var(--space-2) var(--space-3)",
            background: "var(--bg-surface)",
            borderBottom: "1px solid var(--border)",
            fontSize: "var(--fs-caption)",
          }}
        >
          <div style={{ background: "var(--bg-sunken)", padding: "var(--space-2)", borderRadius: "var(--radius)" }}>
            <span style={{ color: "var(--text-muted)", display: "block" }}>إجمالي الصافي</span>
            <strong style={{ fontSize: "var(--fs-body)", fontFamily: "monospace" }}>{data.netBase ?? "0.00"} {data.baseCurrency}</strong>
          </div>
          <div style={{ background: "var(--bg-sunken)", padding: "var(--space-2)", borderRadius: "var(--radius)" }}>
            <span style={{ color: "var(--text-muted)", display: "block" }}>إجمالي الإضافات</span>
            <strong style={{ fontSize: "var(--fs-body)", fontFamily: "monospace" }}>{data.additionsBase ?? "0.00"} {data.baseCurrency}</strong>
          </div>
          <div style={{ background: "var(--bg-sunken)", padding: "var(--space-2)", borderRadius: "var(--radius)" }}>
            <span style={{ color: "var(--text-muted)", display: "block" }}>إجمالي الضريبة</span>
            <strong style={{ fontSize: "var(--fs-body)", fontFamily: "monospace" }}>{data.taxBase ?? "0.00"} {data.baseCurrency}</strong>
          </div>
          <div style={{ background: "var(--bg-sunken)", padding: "var(--space-2)", borderRadius: "var(--radius)" }}>
            <span style={{ color: "var(--text-muted)", display: "block" }}>الإجمالي العام</span>
            <strong style={{ fontSize: "var(--fs-body)", color: "var(--primary)", fontFamily: "monospace" }}>{data.totalBase ?? "0.00"} {data.baseCurrency}</strong>
          </div>
          <div style={{ background: "var(--bg-sunken)", padding: "var(--space-2)", borderRadius: "var(--radius)" }}>
            <span style={{ color: "var(--text-muted)", display: "block" }}>إيراد دفتر الأستاذ (GL)</span>
            <strong style={{ fontSize: "var(--fs-body)", fontFamily: "monospace" }}>{data.glRevenueBase ?? "—"}</strong>
          </div>
          <div style={{ background: "var(--bg-sunken)", padding: "var(--space-2)", borderRadius: "var(--radius)" }}>
            <span style={{ color: "var(--text-muted)", display: "block" }}>فارق المطابقة مع GL</span>
            <strong style={{
              fontSize: "var(--fs-body)",
              color: data.revenueDifference && data.revenueDifference !== "0.00" && data.revenueDifference !== "0" ? "var(--color-danger)" : "var(--color-success)",
              fontFamily: "monospace"
            }}>
              {data.revenueDifference ?? "0.00"}
            </strong>
          </div>
        </div>
      )}

      {/* Main Content */}
      <div style={{ flex: 1, overflow: "auto", padding: "var(--space-3)" }}>
        {loading ? (
          <PetraLoadingSkeleton rows={10} />
        ) : error ? (
          <PetraErrorDetails status={error.status} message={error.message} onRetry={loadReport} />
        ) : !data?.rows || data.rows.length === 0 ? (
          <PetraEmptyState title="لا توجد مبيعات مرحلة في الفترة المحددة" description="تأكد من تحديد نطاق التاريخ المناسب أو ترحيل فواتير المبيعات." />
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
                  <th style={{ padding: "var(--space-2) var(--space-3)" }}>الرمز</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)" }}>
                    {grouping === "Customer" ? "اسم العميل" : grouping === "Item" ? "اسم المادة" : "نمط الفاتورة"}
                  </th>
                  {grouping === "Item" && <th style={{ padding: "var(--space-2) var(--space-3)" }}>الوحدة</th>}
                  <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "center" }}>الكمية</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "center" }}>عدد الفواتير</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left" }}>الصافي</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left" }}>الإضافات</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left" }}>الضريبة</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left" }}>الإجمالي</th>
                </tr>
              </thead>
              <tbody>
                {data.rows.map((row, idx) => (
                  <tr
                    key={row.keyId ?? idx}
                    style={{
                      borderBottom: "1px solid var(--border-light)",
                      background: idx % 2 === 1 ? "var(--bg-sunken)" : "transparent",
                    }}
                  >
                    <td style={{ padding: "var(--space-2) var(--space-3)", fontFamily: "monospace", fontWeight: 600 }}>
                      {row.keyCode}
                    </td>
                    <td style={{ padding: "var(--space-2) var(--space-3)" }}>
                      {grouping === "Customer" && row.keyId ? (
                        <button
                          type="button"
                          onClick={() => {
                            openWorkspaceWindow({
                              key: `statement:${row.keyId}`,
                              screen: "party-statement",
                              title: `كشف حساب: ${row.keyName}`,
                              params: { partyId: row.keyId!, partyName: row.keyName },
                            });
                          }}
                          style={{
                            background: "none",
                            border: "none",
                            padding: 0,
                            color: "var(--primary)",
                            cursor: "pointer",
                            textAlign: "right",
                            fontWeight: 600,
                            textDecoration: "underline",
                          }}
                        >
                          {row.keyName}
                        </button>
                      ) : (
                        row.keyName
                      )}
                    </td>
                    {grouping === "Item" && (
                      <td style={{ padding: "var(--space-2) var(--space-3)", color: "var(--text-muted)" }}>
                        {row.unitCode ?? "—"}
                      </td>
                    )}
                    <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "center", fontFamily: "monospace" }}>
                      {row.quantity}
                    </td>
                    <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "center", fontFamily: "monospace" }}>
                      {row.documentCount}
                    </td>
                    <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left", fontFamily: "monospace" }}>
                      {row.netBase}
                    </td>
                    <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left", fontFamily: "monospace" }}>
                      {row.additionsBase}
                    </td>
                    <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left", fontFamily: "monospace" }}>
                      {row.taxBase}
                    </td>
                    <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left", fontFamily: "monospace", fontWeight: 700, color: "var(--primary)" }}>
                      {row.totalBase}
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
