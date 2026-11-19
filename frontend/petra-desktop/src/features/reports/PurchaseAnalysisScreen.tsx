import { useState, useEffect } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { useWorkspace } from "@/workspace/workspaceStore";
import { PetraButton, PetraLoadingSkeleton, PetraEmptyState, PetraErrorDetails, PetraDialog, PetraField } from "@/components/ui";
import { toast } from "@/components/ui/toastStore";

export interface PurchaseAnalysisScreenProps {
  params?: Readonly<Record<string, string>> | undefined;
}

type PurchaseGroupingType = "Pattern" | "Supplier" | "Item";

interface PurchaseAnalysisRow {
  keyId?: string | null;
  keyCode: string;
  keyName: string;
  baseUnitCode?: string | null;
  baseQuantity: string;
  documentCount: number;
  netBase: string;
  taxBase: string;
  totalBase: string;
  stockCostBase: string;
}

interface PurchaseAnalysisData {
  from?: string;
  to?: string;
  grouping?: PurchaseGroupingType;
  baseCurrency?: string;
  rows?: PurchaseAnalysisRow[];
  netBase?: string;
  taxBase?: string;
  totalBase?: string;
  stockCostBase?: string;
}

export default function PurchaseAnalysisScreen({ params: _params }: PurchaseAnalysisScreenProps) {
  const api = useApi();
  const { companyId, branchId } = useSession((s) => ({ companyId: s.companyId, branchId: s.branchId }));
  const openWorkspaceWindow = useWorkspace((s) => s.open);

  const [grouping, setGrouping] = useState<PurchaseGroupingType>("Supplier");
  const [fromDate, setFromDate] = useState("");
  const [toDate, setToDate] = useState("");
  const [optionsOpen, setOptionsOpen] = useState(false);

  const [data, setData] = useState<PurchaseAnalysisData | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<{ status?: number; message?: string } | null>(null);

  const loadReport = async () => {
    if (!companyId || !branchId) return;

    setLoading(true);
    setError(null);
    try {
      const res = await api.GET("/api/v1/companies/{companyId}/branches/{branchId}/reports/purchase-analysis", {
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
        setError({ status: res.response.status, message: "فشل استخراج تقرير تحليل المشتريات من الخادم" });
      } else if (res.data) {
        setData(res.data as unknown as PurchaseAnalysisData);
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
    const header = "الرمز\tالاسم\tالوحدة\tالكمية\tعدد الفواتير\tالصافي\tالضريبة\tتكلفة المخزون\tالإجمالي";
    const rows = data.rows.map((r) =>
      `${r.keyCode}\t${r.keyName}\t${r.baseUnitCode ?? ""}\t${r.baseQuantity}\t${r.documentCount}\t${r.netBase}\t${r.taxBase}\t${r.stockCostBase}\t${r.totalBase}`
    );
    const tsv = [header, ...rows].join("\r\n");
    navigator.clipboard?.writeText(tsv).then(() => {
      toast.success("تم نسخ جدول تحليل المشتريات إلى الحافظة بصيغة Excel (TSV)");
    });
  };

  return (
    <div style={{ display: "flex", flexDirection: "column", height: "100%", background: "var(--bg-app)" }}>
      {/* Options Dialog */}
      <PetraDialog
        open={optionsOpen}
        onClose={() => setOptionsOpen(false)}
        title="خيارات تقرير تحليل المشتريات"
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
              onChange={(e) => setGrouping(e.target.value as PurchaseGroupingType)}
              style={{
                height: "var(--control-height)",
                padding: "0 var(--space-2)",
                borderRadius: "var(--radius)",
                border: "1px solid var(--border)",
                background: "var(--bg-input)",
                color: "var(--text)",
              }}
            >
              <option value="Supplier">حسب الموردين (By Supplier)</option>
              <option value="Item">حسب المواد والأصناف (By Item)</option>
              <option value="Pattern">حسب أنماط فواتير الشراء (By Pattern)</option>
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

      {/* Header Bar */}
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
            تحليل المشتريات (Purchase Analysis)
          </h2>
          <div style={{ display: "flex", gap: "var(--space-1)" }}>
            <PetraButton
              variant={grouping === "Supplier" ? "primary" : "secondary"}
              size="sm"
              onClick={() => setGrouping("Supplier")}
            >
              حسب الموردين
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

      {/* Summary KPI Bar */}
      {data && (
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(4, 1fr)",
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
            <span style={{ color: "var(--text-muted)", display: "block" }}>إجمالي الضريبة</span>
            <strong style={{ fontSize: "var(--fs-body)", fontFamily: "monospace" }}>{data.taxBase ?? "0.00"} {data.baseCurrency}</strong>
          </div>
          <div style={{ background: "var(--bg-sunken)", padding: "var(--space-2)", borderRadius: "var(--radius)" }}>
            <span style={{ color: "var(--text-muted)", display: "block" }}>تكلفة المخزون الكلية</span>
            <strong style={{ fontSize: "var(--fs-body)", fontFamily: "monospace" }}>{data.stockCostBase ?? "0.00"} {data.baseCurrency}</strong>
          </div>
          <div style={{ background: "var(--bg-sunken)", padding: "var(--space-2)", borderRadius: "var(--radius)" }}>
            <span style={{ color: "var(--text-muted)", display: "block" }}>الإجمالي العام للمشتريات</span>
            <strong style={{ fontSize: "var(--fs-body)", color: "var(--primary)", fontFamily: "monospace" }}>{data.totalBase ?? "0.00"} {data.baseCurrency}</strong>
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
          <PetraEmptyState title="لا توجد مشتريات مرحلة في الفترة المحددة" description="تأكد من ترحيل فواتير الشراء أو تغيير نطاق التواريخ." />
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
                    {grouping === "Supplier" ? "اسم المورد" : grouping === "Item" ? "اسم المادة" : "نمط الفاتورة"}
                  </th>
                  {grouping === "Item" && <th style={{ padding: "var(--space-2) var(--space-3)" }}>الوحدة</th>}
                  <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "center" }}>الكمية</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "center" }}>عدد الفواتير</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left" }}>الصافي</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left" }}>الضريبة</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left" }}>تكلفة المخزون</th>
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
                      {grouping === "Supplier" && row.keyId ? (
                        <button
                          type="button"
                          onClick={() => {
                            openWorkspaceWindow({
                              key: `statement:${row.keyId}`,
                              screen: "party-statement",
                              title: `كشف حساب مورد: ${row.keyName}`,
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
                        {row.baseUnitCode ?? "—"}
                      </td>
                    )}
                    <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "center", fontFamily: "monospace" }}>
                      {row.baseQuantity}
                    </td>
                    <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "center", fontFamily: "monospace" }}>
                      {row.documentCount}
                    </td>
                    <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left", fontFamily: "monospace" }}>
                      {row.netBase}
                    </td>
                    <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left", fontFamily: "monospace" }}>
                      {row.taxBase}
                    </td>
                    <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left", fontFamily: "monospace" }}>
                      {row.stockCostBase}
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
