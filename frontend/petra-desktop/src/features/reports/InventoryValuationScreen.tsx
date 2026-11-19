import { useState, useEffect } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { useWorkspace } from "@/workspace/workspaceStore";
import { PetraButton, PetraLoadingSkeleton, PetraEmptyState, PetraErrorDetails } from "@/components/ui";
import { toast } from "@/components/ui/toastStore";

export interface InventoryValuationScreenProps {
  params?: Readonly<Record<string, string>> | undefined;
}

interface InventoryValuationRow {
  itemId: string;
  itemCode: string;
  itemName: string;
  warehouseId?: string | null;
  warehouseCode?: string | null;
  warehouseName?: string | null;
  unitCode?: string | null;
  quantity: string;
  unitCostBase: string;
  totalValueBase: string;
}

interface InventoryValuationData {
  asOf?: string;
  baseCurrency?: string;
  rows?: InventoryValuationRow[];
  totalValue?: string;
  glInventoryBalance?: string;
  difference?: string;
}

export default function InventoryValuationScreen({ params: _params }: InventoryValuationScreenProps) {
  const api = useApi();
  const { companyId, branchId } = useSession((s) => ({ companyId: s.companyId, branchId: s.branchId }));
  const openWorkspaceWindow = useWorkspace((s) => s.open);

  const [asOfDate, setAsOfDate] = useState(() => new Date().toISOString().substring(0, 10));
  const [data, setData] = useState<InventoryValuationData | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<{ status?: number; message?: string } | null>(null);

  const loadValuation = async () => {
    if (!companyId || !branchId) return;

    setLoading(true);
    setError(null);
    try {
      const res = await api.GET("/api/v1/companies/{companyId}/branches/{branchId}/reports/inventory/valuation", {
        params: {
          path: { companyId, branchId },
          query: {
            ...(asOfDate ? { asOf: asOfDate } : {}),
          },
        },
      });

      if (res.error) {
        setError({ status: res.response.status, message: "فشل استخراج تقرير تقييم المخزون" });
      } else if (res.data) {
        setData(res.data as unknown as InventoryValuationData);
      }
    } catch (err) {
      setError({ message: err instanceof Error ? err.message : "خطأ غير متوقع" });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadValuation();
  }, [companyId, branchId]);

  const copyToExcel = () => {
    if (!data?.rows || data.rows.length === 0) return;
    const header = "رمز المادة\tاسم المادة\tالمستودع\tالوحدة\tالكمية\tمتوسط التكلفة\tالقيمة الإجمالية";
    const rows = data.rows.map((r) =>
      `${r.itemCode}\t${r.itemName}\t${r.warehouseName ?? ""}\t${r.unitCode ?? ""}\t${r.quantity}\t${r.unitCostBase}\t${r.totalValueBase}`
    );
    navigator.clipboard?.writeText([header, ...rows].join("\r\n")).then(() => {
      toast.success("تم نسخ تقرير بضاعة آخر المدة إلى الحافظة (TSV)");
    });
  };

  const openItemCard = (itemId: string, itemCode: string, itemName: string) => {
    openWorkspaceWindow({
      key: `item-movements:${itemId}`,
      screen: "inventory-movements",
      title: `حركة مادة: ${itemName}`,
      params: { itemId, itemCode, itemName },
    });
  };

  const isBalanced = data && (data.difference === "0" || data.difference === "0.00" || data.difference === "0.0000");

  return (
    <div style={{ display: "flex", flexDirection: "column", height: "100%", background: "var(--bg-app)" }}>
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
            تقييم بضاعة آخر المدة والمخزون (Inventory Valuation)
          </h2>
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: "var(--space-2)" }}>
          <div style={{ display: "flex", alignItems: "center", gap: "var(--space-1)" }}>
            <span style={{ fontSize: "var(--fs-caption)", color: "var(--text-muted)" }}>بتاريخ:</span>
            <input
              type="date"
              value={asOfDate}
              onChange={(e) => setAsOfDate(e.target.value)}
              style={{
                height: "28px",
                padding: "0 var(--space-2)",
                borderRadius: "var(--radius)",
                border: "1px solid var(--border)",
                background: "var(--bg-input)",
                color: "var(--text)",
              }}
            />
          </div>

          <PetraButton variant="secondary" size="sm" onClick={copyToExcel} disabled={!data?.rows?.length}>
            نسخ إلى Excel
          </PetraButton>
          <PetraButton variant="primary" size="sm" onClick={loadValuation} disabled={loading}>
            {loading ? "جاري التحميل..." : "تحديث التقييم"}
          </PetraButton>
        </div>
      </div>

      {/* KPI Reconciliation Bar */}
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
            <span style={{ color: "var(--text-muted)", display: "block" }}>إجمالي تقييم بضاعة المخزون</span>
            <strong style={{ fontSize: "var(--fs-body)", fontFamily: "monospace", color: "var(--primary)" }}>
              {data.totalValue ?? "0.00"} {data.baseCurrency}
            </strong>
          </div>

          <div style={{ background: "var(--bg-sunken)", padding: "var(--space-2)", borderRadius: "var(--radius)" }}>
            <span style={{ color: "var(--text-muted)", display: "block" }}>رصيد حسابات المخزون في GL</span>
            <strong style={{ fontSize: "var(--fs-body)", fontFamily: "monospace" }}>
              {data.glInventoryBalance ?? "—"} {data.baseCurrency}
            </strong>
          </div>

          <div style={{ background: "var(--bg-sunken)", padding: "var(--space-2)", borderRadius: "var(--radius)" }}>
            <span style={{ color: "var(--text-muted)", display: "block" }}>فارق المطابقة مع الأستاذ العام</span>
            <strong
              style={{
                fontSize: "var(--fs-body)",
                fontFamily: "monospace",
                color: isBalanced ? "var(--color-success)" : "var(--color-danger)",
              }}
            >
              {data.difference ?? "0.00"} {data.baseCurrency}
            </strong>
          </div>

          <div style={{ background: "var(--bg-sunken)", padding: "var(--space-2)", borderRadius: "var(--radius)" }}>
            <span style={{ color: "var(--text-muted)", display: "block" }}>عدد المواد في الجرد</span>
            <strong style={{ fontSize: "var(--fs-body)", fontFamily: "monospace" }}>
              {data.rows?.length ?? 0} مادة
            </strong>
          </div>
        </div>
      )}

      {/* Main Table */}
      <div style={{ flex: 1, overflow: "auto", padding: "var(--space-3)" }}>
        {loading ? (
          <PetraLoadingSkeleton rows={10} />
        ) : error ? (
          <PetraErrorDetails status={error.status} message={error.message} onRetry={loadValuation} />
        ) : !data?.rows || data.rows.length === 0 ? (
          <PetraEmptyState title="لا توجد بيانات بضاعة مسجلة" description="تأكد من وجود أرصدة مواد مرحلة في التاريخ المحدد." />
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
                  <th style={{ padding: "var(--space-2) var(--space-3)" }}>رمز المادة</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)" }}>اسم المادة</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)" }}>المستودع</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)" }}>الوحدة</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "center" }}>الكمية بالرصيد</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left" }}>متوسط التكلفة</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left" }}>القيمة الإجمالية</th>
                </tr>
              </thead>
              <tbody>
                {data.rows.map((row, idx) => (
                  <tr
                    key={row.itemId + (row.warehouseId ?? "")}
                    style={{
                      borderBottom: "1px solid var(--border-light)",
                      background: idx % 2 === 1 ? "var(--bg-sunken)" : "transparent",
                    }}
                  >
                    <td style={{ padding: "var(--space-2) var(--space-3)", fontFamily: "monospace", fontWeight: 700 }}>
                      <button
                        type="button"
                        onClick={() => openItemCard(row.itemId, row.itemCode, row.itemName)}
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
                        {row.itemCode}
                      </button>
                    </td>
                    <td style={{ padding: "var(--space-2) var(--space-3)", fontWeight: 600 }}>{row.itemName}</td>
                    <td style={{ padding: "var(--space-2) var(--space-3)", color: "var(--text-muted)" }}>
                      {row.warehouseName ?? "الكل"}
                    </td>
                    <td style={{ padding: "var(--space-2) var(--space-3)", color: "var(--text-muted)" }}>
                      {row.unitCode ?? "—"}
                    </td>
                    <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "center", fontFamily: "monospace", fontWeight: 600 }}>
                      {row.quantity}
                    </td>
                    <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left", fontFamily: "monospace" }}>
                      {row.unitCostBase}
                    </td>
                    <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left", fontFamily: "monospace", fontWeight: 700, color: "var(--primary)" }}>
                      {row.totalValueBase}
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
