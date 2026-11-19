import { useState, useEffect } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { PetraButton, PetraLoadingSkeleton, PetraEmptyState, PetraErrorDetails, PetraDialog, PetraField } from "@/components/ui";
import { toast } from "@/components/ui/toastStore";

export interface InventoryMovementsScreenProps {
  params?: Readonly<Record<string, string>> | undefined;
}

interface InventoryMovementLine {
  dateUtc: string;
  kind?: string;
  documentType: string;
  documentId?: string | null;
  documentNumber?: string | null;
  warehouseCode: string;
  quantity: string;
  value: string;
  balanceQuantity: string;
  balanceValue: string;
}

interface InventoryMovementData {
  itemId: string;
  itemCode: string;
  itemName: string;
  baseUnitCode: string;
  from?: string;
  to?: string;
  openingQuantity?: string;
  openingValue?: string;
  inQuantity?: string;
  outQuantity?: string;
  closingQuantity?: string;
  closingValue?: string;
  lines?: InventoryMovementLine[];
}

export default function InventoryMovementsScreen({ params }: InventoryMovementsScreenProps) {
  const initialItemId = params?.itemId ?? "";
  const initialItemCode = params?.itemCode ?? "";
  const initialItemName = params?.itemName ?? "";

  const api = useApi();
  const { companyId, branchId } = useSession((s) => ({ companyId: s.companyId, branchId: s.branchId }));

  const [itemId, setItemId] = useState(initialItemId);
  const [fromDate, setFromDate] = useState("");
  const [toDate, setToDate] = useState("");
  const [optionsOpen, setOptionsOpen] = useState(!initialItemId);

  const [data, setData] = useState<InventoryMovementData | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<{ status?: number; message?: string } | null>(null);

  const loadMovements = async (targetId: string = itemId) => {
    if (!companyId || !branchId || !targetId) {
      setOptionsOpen(true);
      return;
    }

    setLoading(true);
    setError(null);
    try {
      const res = await api.GET("/api/v1/companies/{companyId}/branches/{branchId}/reports/inventory/movements/{itemId}", {
        params: {
          path: { companyId, branchId, itemId: targetId },
          query: {
            ...(fromDate ? { from: fromDate } : {}),
            ...(toDate ? { to: toDate } : {}),
          },
        },
      });

      if (res.error) {
        setError({ status: res.response.status, message: "فشل استخراج كشف حركة المادة" });
      } else if (res.data) {
        setData(res.data as unknown as InventoryMovementData);
      }
    } catch (err) {
      setError({ message: err instanceof Error ? err.message : "خطأ غير متوقع" });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    if (initialItemId) {
      loadMovements(initialItemId);
    }
  }, [companyId, branchId, initialItemId]);

  const copyToExcel = () => {
    if (!data?.lines || data.lines.length === 0) return;
    const header = "التاريخ\tالمستودع\tنوع المستند\tرقم المستند\tالكمية\tالقيمة\tالرصيد التراكمي\tقيمة الرصيد";
    const rows = data.lines.map((l) =>
      `${l.dateUtc.substring(0, 10)}\t${l.warehouseCode}\t${l.documentType}\t${l.documentNumber ?? ""}\t${l.quantity}\t${l.value}\t${l.balanceQuantity}\t${l.balanceValue}`
    );
    navigator.clipboard?.writeText([header, ...rows].join("\r\n")).then(() => {
      toast.success("تم نسخ كشف حركة المادة إلى الحافظة (TSV)");
    });
  };

  return (
    <div style={{ display: "flex", flexDirection: "column", height: "100%", background: "var(--bg-app)" }}>
      {/* Options Dialog */}
      <PetraDialog
        open={optionsOpen}
        onClose={() => setOptionsOpen(false)}
        title="خيارات كشف حركة المادة (Stock Card)"
        width={450}
        actions={
          <>
            <PetraButton variant="secondary" onClick={() => setOptionsOpen(false)}>
              إلغاء
            </PetraButton>
            <PetraButton
              variant="primary"
              disabled={!itemId.trim()}
              onClick={() => {
                setOptionsOpen(false);
                loadMovements();
              }}
            >
              عرض التقرير
            </PetraButton>
          </>
        }
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "var(--space-3)" }}>
          <PetraField label="معرّف المادة (Item ID)" required>
            <input
              type="text"
              value={itemId}
              onChange={(e) => setItemId(e.target.value)}
              placeholder="UUID للمادة"
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
        <div style={{ display: "flex", alignItems: "center", gap: "var(--space-2)" }}>
          <h2 style={{ margin: 0, fontSize: "var(--fs-title)", fontWeight: 700 }}>
            كشف حركة مادة: {data?.itemName ?? initialItemName ?? "تحديد مادة"}
          </h2>
          {(data?.itemCode ?? initialItemCode) && (
            <span style={{ fontFamily: "monospace", color: "var(--primary)", fontWeight: 700 }}>
              ({data?.itemCode ?? initialItemCode})
            </span>
          )}
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: "var(--space-2)" }}>
          <PetraButton variant="secondary" size="sm" onClick={() => setOptionsOpen(true)}>
            اختيار مادة / فترة
          </PetraButton>
          <PetraButton variant="secondary" size="sm" onClick={copyToExcel} disabled={!data?.lines?.length}>
            نسخ إلى Excel
          </PetraButton>
          <PetraButton variant="primary" size="sm" onClick={() => loadMovements()} disabled={loading}>
            {loading ? "جاري التحميل..." : "تحديث"}
          </PetraButton>
        </div>
      </div>

      {/* Summary KPI Bar */}
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
            <span style={{ color: "var(--text-muted)", display: "block" }}>الرصيد الافتتاحي</span>
            <strong style={{ fontSize: "var(--fs-body)", fontFamily: "monospace" }}>
              {data.openingQuantity ?? "0"} {data.baseUnitCode}
            </strong>
          </div>

          <div style={{ background: "var(--bg-sunken)", padding: "var(--space-2)", borderRadius: "var(--radius)" }}>
            <span style={{ color: "var(--text-muted)", display: "block" }}>قيمة الافتتاحي</span>
            <strong style={{ fontSize: "var(--fs-body)", fontFamily: "monospace" }}>
              {data.openingValue ?? "0.00"}
            </strong>
          </div>

          <div style={{ background: "var(--bg-sunken)", padding: "var(--space-2)", borderRadius: "var(--radius)" }}>
            <span style={{ color: "var(--text-muted)", display: "block" }}>مجموع الوارد (+)</span>
            <strong style={{ fontSize: "var(--fs-body)", fontFamily: "monospace", color: "var(--color-success)" }}>
              +{data.inQuantity ?? "0"}
            </strong>
          </div>

          <div style={{ background: "var(--bg-sunken)", padding: "var(--space-2)", borderRadius: "var(--radius)" }}>
            <span style={{ color: "var(--text-muted)", display: "block" }}>مجموع الصادر (-)</span>
            <strong style={{ fontSize: "var(--fs-body)", fontFamily: "monospace", color: "var(--color-danger)" }}>
              -{data.outQuantity ?? "0"}
            </strong>
          </div>

          <div style={{ background: "var(--bg-sunken)", padding: "var(--space-2)", borderRadius: "var(--radius)" }}>
            <span style={{ color: "var(--text-muted)", display: "block" }}>الرصيد الختامي</span>
            <strong style={{ fontSize: "var(--fs-body)", fontFamily: "monospace", color: "var(--primary)" }}>
              {data.closingQuantity ?? "0"} {data.baseUnitCode}
            </strong>
          </div>

          <div style={{ background: "var(--bg-sunken)", padding: "var(--space-2)", borderRadius: "var(--radius)" }}>
            <span style={{ color: "var(--text-muted)", display: "block" }}>قيمة الرصيد الختامي</span>
            <strong style={{ fontSize: "var(--fs-body)", fontFamily: "monospace", color: "var(--primary)" }}>
              {data.closingValue ?? "0.00"}
            </strong>
          </div>
        </div>
      )}

      {/* Main Table */}
      <div style={{ flex: 1, overflow: "auto", padding: "var(--space-3)" }}>
        {loading ? (
          <PetraLoadingSkeleton rows={10} />
        ) : error ? (
          <PetraErrorDetails status={error.status} message={error.message} onRetry={() => loadMovements()} />
        ) : !data?.lines || data.lines.length === 0 ? (
          <PetraEmptyState title="لا توجد حركات مخزنية للمادة" description="لم تسجل أي حركة وارد أو صادر لهذه المادة في الفترة المحددة." />
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
                  <th style={{ padding: "var(--space-2) var(--space-3)" }}>التاريخ</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)" }}>المستودع</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)" }}>نوع الحركة</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)" }}>رقم المستند</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "center" }}>الكمية</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left" }}>القيمة</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "center" }}>رصيد الكمية</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left" }}>قيمة الرصيد</th>
                </tr>
              </thead>
              <tbody>
                {data.lines.map((l, idx) => {
                  const isPositive = !l.quantity.startsWith("-") && l.quantity !== "0";
                  return (
                    <tr
                      key={idx}
                      style={{
                        borderBottom: "1px solid var(--border-light)",
                        background: idx % 2 === 1 ? "var(--bg-sunken)" : "transparent",
                      }}
                    >
                      <td style={{ padding: "var(--space-2) var(--space-3)", fontFamily: "monospace" }}>
                        {l.dateUtc.substring(0, 10)}
                      </td>
                      <td style={{ padding: "var(--space-2) var(--space-3)" }}>{l.warehouseCode}</td>
                      <td style={{ padding: "var(--space-2) var(--space-3)", fontWeight: 600 }}>{l.documentType}</td>
                      <td style={{ padding: "var(--space-2) var(--space-3)", fontFamily: "monospace" }}>
                        {l.documentNumber ?? "—"}
                      </td>
                      <td
                        style={{
                          padding: "var(--space-2) var(--space-3)",
                          textAlign: "center",
                          fontFamily: "monospace",
                          fontWeight: 700,
                          color: isPositive ? "var(--color-success)" : "var(--color-danger)",
                        }}
                      >
                        {isPositive ? `+${l.quantity}` : l.quantity}
                      </td>
                      <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left", fontFamily: "monospace" }}>
                        {l.value}
                      </td>
                      <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "center", fontFamily: "monospace", fontWeight: 700 }}>
                        {l.balanceQuantity}
                      </td>
                      <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left", fontFamily: "monospace", fontWeight: 700, color: "var(--primary)" }}>
                        {l.balanceValue}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}
