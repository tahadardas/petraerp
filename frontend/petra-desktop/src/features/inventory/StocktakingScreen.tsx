import { useState, useEffect } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { useWorkspace } from "@/workspace/workspaceStore";
import {
  PetraButton,
  PetraField,
  PetraErrorDetails,
} from "@/components/ui";
import { LookupPicker, type LookupItem } from "@/components/lookup/LookupPicker";
import { toast } from "@/components/ui/toastStore";
import type { components } from "@/services/api/schema";

export interface StocktakingScreenProps {
  params?: Readonly<Record<string, string>> | undefined;
}

interface WarehouseItem {
  id?: string;
  code?: string;
  nameArabic?: string;
}

type StockAdjustmentDto = components["schemas"]["StockAdjustmentDto"];
type StockAdjustmentLineDto = components["schemas"]["StockAdjustmentLineDto"];

interface CountLine {
  id: string;
  item: LookupItem | null;
  unitId: string;
  unitCode: string;
  countedQuantity: string;
  unitCost: string;
}

export default function StocktakingScreen({ params: _params }: StocktakingScreenProps) {
  const api = useApi();
  const { companyId, branchId } = useSession((s) => ({ companyId: s.companyId, branchId: s.branchId }));
  const openWorkspaceWindow = useWorkspace((s) => s.open);

  const [warehouses, setWarehouses] = useState<WarehouseItem[]>([]);
  const [selectedWarehouseId, setSelectedWarehouseId] = useState<string>("");
  const [adjustmentDate, setAdjustmentDate] = useState<string>(
    new Date().toISOString().split("T")[0] ?? "2026-11-19"
  );
  const [reason, setReason] = useState<string>("جرد دوري للمستودع ومطابقة الأرصدة الفعلية");
  const [notes, setNotes] = useState<string>("");

  const [inventoryAccount, setInventoryAccount] = useState<LookupItem | null>(null);
  const [offsetAccount, setOffsetAccount] = useState<LookupItem | null>(null);

  const [lines, setLines] = useState<CountLine[]>([
    {
      id: "line-1",
      item: null,
      unitId: "",
      unitCode: "",
      countedQuantity: "1.00",
      unitCost: "0.00",
    },
  ]);

  const [isLoadingWarehouses, setIsLoadingWarehouses] = useState(false);
  const [isSavingDraft, setIsSavingDraft] = useState(false);
  const [isApproving, setIsApproving] = useState(false);
  const [currentAdjustment, setCurrentAdjustment] = useState<StockAdjustmentDto | null>(null);
  const [error, setError] = useState<{ status?: number; message: string } | null>(null);

  useEffect(() => {
    if (!companyId || !branchId) return;
    const fetchWarehouses = async () => {
      setIsLoadingWarehouses(true);
      try {
        const res = await api.GET(
          "/api/v1/companies/{companyId}/branches/{branchId}/inventory/warehouses",
          {
            params: { path: { companyId, branchId } },
          }
        );
        if (res.data) {
          setWarehouses(res.data);
          if (res.data.length > 0 && !selectedWarehouseId) {
            setSelectedWarehouseId(res.data[0]?.id ?? "");
          }
        }
      } catch {
        toast.error("فشل جلب قائمة المستودعات");
      } finally {
        setIsLoadingWarehouses(false);
      }
    };
    fetchWarehouses();
  }, [companyId, branchId]);

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

  const searchItems = async (query: string, signal: AbortSignal): Promise<readonly LookupItem[]> => {
    if (!companyId || !branchId) return [];
    try {
      const res = await api.GET(
        "/api/v1/companies/{companyId}/branches/{branchId}/inventory/items/lookup",
        {
          params: {
            path: { companyId, branchId },
            query: { search: query, maxResults: 20 },
          },
          signal,
        }
      );
      const items = res.data ?? [];
      return items.map((it) => ({
        id: it.id ?? "",
        code: it.code ?? "",
        name: it.nameArabic ?? "",
      }));
    } catch {
      return [];
    }
  };

  const addLine = () => {
    setLines([
      ...lines,
      {
        id: `line-${Date.now()}`,
        item: null,
        unitId: "",
        unitCode: "",
        countedQuantity: "1.00",
        unitCost: "0.00",
      },
    ]);
  };

  const removeLine = (id: string) => {
    if (lines.length === 1) {
      toast.warning("يجب أن يحتوي مستند الجرد على بند واحد على الأقل");
      return;
    }
    setLines(lines.filter((l) => l.id !== id));
  };

  const updateLine = (id: string, patch: Partial<CountLine>) => {
    setLines(lines.map((l) => (l.id === id ? { ...l, ...patch } : l)));
  };

  const handleSaveDraft = async () => {
    if (!companyId || !branchId || !selectedWarehouseId) {
      toast.warning("يرجى اختيار المستودع قبل حفظ الجرد");
      return;
    }
    if (!reason.trim()) {
      toast.warning("يرجى إدخال سبب ومبرر الجرد والتسوية");
      return;
    }

    const validLines = lines.filter((l) => l.item?.id && Number(l.countedQuantity) >= 0);
    if (validLines.length === 0) {
      toast.warning("يرجى إضافة مادة واحدة على الأقل بالكمية المجرودة");
      return;
    }

    setIsSavingDraft(true);
    setError(null);

    try {
      const res = await api.POST(
        "/api/v1/companies/{companyId}/branches/{branchId}/inventory/adjustments",
        {
          params: { path: { companyId, branchId } },
          body: {
            warehouseId: selectedWarehouseId,
            adjustmentDateUtc: `${adjustmentDate}T00:00:00Z`,
            reason,
            notes: notes || null,
            inventoryAccountId: inventoryAccount?.id || null,
            offsetAccountId: offsetAccount?.id || null,
            lines: validLines.map((l) => ({
              itemId: l.item!.id,
              countedQuantity: l.countedQuantity,
              unitCost: l.unitCost || "0.00",
              ...(l.unitId ? { unitId: l.unitId } : {}),
            })),
          },
        }
      );

      if (res.error || !res.data) {
        setError({
          status: res.response?.status,
          message: (res.error as { title?: string; detail?: string })?.detail ?? "فشل حفظ مسودة الجرد",
        });
        return;
      }

      setCurrentAdjustment(res.data);
      toast.success(`تم حفظ مسودة الجرد رقم ${res.data.adjustmentNumber ?? "الجديد"} بنجاح`);
    } catch (err) {
      setError({
        message: err instanceof Error ? err.message : "خطأ غير متوقع أثناء حفظ المسودة",
      });
    } finally {
      setIsSavingDraft(false);
    }
  };

  const handleApprove = async () => {
    if (!companyId || !branchId || !currentAdjustment?.id) {
      toast.warning("يرجى حفظ مسودة الجرد أولاً قبل الاعتماد");
      return;
    }

    setIsApproving(true);
    try {
      const res = await api.POST(
        "/api/v1/companies/{companyId}/branches/{branchId}/inventory/adjustments/{adjustmentId}/approve",
        {
          params: {
            path: {
              companyId,
              branchId,
              adjustmentId: currentAdjustment.id,
            },
          },
          body: {
            expectedVersion: currentAdjustment.version ?? "1",
            idempotencyKey: `adj-appr-${currentAdjustment.id}-${Date.now()}`,
            reason: "اعتماد وترحيل فروقات الجرد الفعلي للمخزون",
          },
        }
      );

      if (res.error || !res.data) {
        toast.error((res.error as { detail?: string })?.detail ?? "فشل اعتماد وترحيل الجرد");
        return;
      }

      setCurrentAdjustment(res.data);
      toast.success("تم اعتماد الجرد وترحيل فوارق المخزون والأستاذ العام بنجاح");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "خطأ أثناء اعتماد الجرد");
    } finally {
      setIsApproving(false);
    }
  };

  const exportTsv = () => {
    const headers = ["رمز المادة", "اسم المادة", "الكمية المجرودة", "تكلفة الوحدة"];
    const rows = lines.map((l) => [
      l.item?.code ?? "",
      l.item?.name ?? "",
      l.countedQuantity,
      l.unitCost,
    ]);

    const tsvContent = [headers.join("\t"), ...rows.map((row) => row.join("\t"))].join("\n");
    const blob = new Blob(["\uFEFF" + tsvContent], { type: "text/tab-separated-values;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `stocktaking-${adjustmentDate}.tsv`;
    a.click();
    URL.revokeObjectURL(url);
    toast.success("تم تصدير بنود الجرد بنجاح");
  };

  return (
    <div className="stocktaking-screen" style={{ padding: "16px", height: "100%", display: "flex", flexDirection: "column", gap: "16px", overflowY: "auto" }}>
      {/* Header */}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", borderBottom: "1px solid var(--border)", paddingBottom: "12px" }}>
        <div>
          <h2 style={{ margin: 0, fontSize: "1.25rem", color: "var(--header-bg)" }}>
            الجرد الفعلي وتسوية المخزون (Stocktaking & Physical Adjustment)
          </h2>
          <p style={{ margin: "4px 0 0", fontSize: "0.85rem", color: "var(--text-muted)" }}>
            إدخال الكميات الفعلية المجرودة وحساب فوارق الجرد وترحيل حركات التسوية المخزنية والمحاسبية.
          </p>
        </div>
        <div style={{ display: "flex", gap: "8px" }}>
          <PetraButton variant="secondary" onClick={exportTsv} size="sm">
            تصدير TSV
          </PetraButton>
          <PetraButton
            variant="secondary"
            onClick={handleSaveDraft}
            disabled={isSavingDraft || currentAdjustment?.status === "Approved"}
            size="sm"
          >
            {isSavingDraft ? "جارٍ الحفظ..." : "حفظ مسودة الجرد (F9)"}
          </PetraButton>
          <PetraButton
            variant="primary"
            onClick={handleApprove}
            disabled={!currentAdjustment?.id || currentAdjustment.status === "Approved" || isApproving}
            size="sm"
          >
            {isApproving ? "جارٍ الاعتماد..." : "اعتماد وترحيل الفوارق (Ctrl+S)"}
          </PetraButton>
        </div>
      </div>

      {/* Status banner if already approved */}
      {currentAdjustment && (
        <div
          style={{
            background: currentAdjustment.status === "Approved" ? "#E8F5E9" : "#FEF3C7",
            border: `1px solid ${currentAdjustment.status === "Approved" ? "#A5D6A7" : "#FDE68A"}`,
            borderRadius: "4px",
            padding: "12px",
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
          }}
        >
          <div>
            <div
              style={{
                fontWeight: "bold",
                color: currentAdjustment.status === "Approved" ? "#2E7D32" : "#92400E",
                fontSize: "0.95rem",
              }}
            >
              مستند الجرد رقم: {currentAdjustment.adjustmentNumber} ({currentAdjustment.status === "Approved" ? "مرحل ومعتمد" : "مسودة غير مرحلة"})
            </div>
            <div style={{ fontSize: "0.8rem", color: "var(--text-muted)", marginTop: "4px" }}>
              عدد بنود الجرد: {currentAdjustment.lines?.length ?? 0} | المستودع: {warehouses.find((w) => w.id === currentAdjustment.warehouseId)?.nameArabic ?? "المحدد"}
            </div>
          </div>
          {currentAdjustment.status === "Approved" && (
            <span style={{ padding: "4px 8px", background: "#2E7D32", color: "#FFFFFF", borderRadius: "3px", fontSize: "0.8rem", fontWeight: "bold" }}>
              ✓ تم ترحيل حركات الفوارق بنجاح
            </span>
          )}
        </div>
      )}

      {/* Main Parameters Panel */}
      <div style={{ background: "var(--bg-surface)", border: "1px solid var(--border)", borderRadius: "4px", padding: "12px", display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))", gap: "12px" }}>
        <PetraField label="المستودع المراد جرده">
          <select
            className="petra-select"
            value={selectedWarehouseId}
            onChange={(e) => setSelectedWarehouseId(e.target.value)}
            disabled={currentAdjustment?.status === "Approved" || isLoadingWarehouses}
            style={{ width: "100%", padding: "5px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
          >
            {warehouses.map((w) => (
              <option key={w.id} value={w.id}>
                {w.code} - {w.nameArabic}
              </option>
            ))}
          </select>
        </PetraField>

        <PetraField label="تاريخ الجرد الفعلي">
          <input
            type="date"
            className="petra-input"
            value={adjustmentDate}
            onChange={(e) => setAdjustmentDate(e.target.value)}
            disabled={currentAdjustment?.status === "Approved"}
            style={{ width: "100%", padding: "5px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
          />
        </PetraField>

        <PetraField label="حساب بضاعة المخزون (اختياري)">
          <LookupPicker
            ariaLabel="حساب بضاعة المخزون"
            placeholder="حساب بضاعة المخزون الافتراضي..."
            search={searchAccounts}
            value={inventoryAccount}
            onChange={setInventoryAccount}
          />
        </PetraField>

        <PetraField label="حساب فروقات الجرد والتسوية (اختياري)">
          <LookupPicker
            ariaLabel="حساب فروقات الجرد"
            placeholder="حساب فروقات وتسوية الجرد..."
            search={searchAccounts}
            value={offsetAccount}
            onChange={setOffsetAccount}
          />
        </PetraField>

        <div style={{ gridColumn: "1 / -1", display: "grid", gridTemplateColumns: "1fr 1fr", gap: "12px" }}>
          <PetraField label="سبب ومبرر الجرد الفعلي (إلزامي للتدقيق)">
            <input
              type="text"
              className="petra-input"
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              disabled={currentAdjustment?.status === "Approved"}
              style={{ width: "100%", padding: "5px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
            />
          </PetraField>
          <PetraField label="ملاحظات إضافية (اختياري)">
            <input
              type="text"
              className="petra-input"
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              disabled={currentAdjustment?.status === "Approved"}
              style={{ width: "100%", padding: "5px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
            />
          </PetraField>
        </div>
      </div>

      {/* Counted Lines Table */}
      <div style={{ flex: 1, display: "flex", flexDirection: "column", gap: "8px" }}>
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
          <h3 style={{ margin: 0, fontSize: "1rem", color: "var(--header-bg)" }}>
            بنود الجرد والكميات المادية ({lines.length})
          </h3>
          {currentAdjustment?.status !== "Approved" && (
            <PetraButton variant="secondary" size="sm" onClick={addLine}>
              + إضافة بند جرد جديد
            </PetraButton>
          )}
        </div>

        {error && <PetraErrorDetails status={error.status} message={error.message} onRetry={handleSaveDraft} />}

        {/* If approved, show lines from approved response */}
        {currentAdjustment?.lines && currentAdjustment.lines.length > 0 ? (
          <div style={{ flex: 1, overflowX: "auto", border: "1px solid var(--border)", borderRadius: "4px", background: "var(--bg-surface)" }}>
            <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.85rem", textAlign: "right" }}>
              <thead>
                <tr style={{ background: "var(--header-bg)", color: "#FFFFFF" }}>
                  <th style={{ padding: "8px" }}>رمز المادة</th>
                  <th style={{ padding: "8px" }}>اسم المادة</th>
                  <th style={{ padding: "8px" }}>الوحدة</th>
                  <th style={{ padding: "8px" }}>الرصيد الدفتري (النظام)</th>
                  <th style={{ padding: "8px" }}>الكمية المجرودة (الفعلية)</th>
                  <th style={{ padding: "8px" }}>فارق الجرد</th>
                  <th style={{ padding: "8px" }}>تكلفة الوحدة</th>
                </tr>
              </thead>
              <tbody>
                {currentAdjustment.lines.map((l: StockAdjustmentLineDto, idx: number) => {
                  const diff = Number(l.differenceQuantity);
                  return (
                    <tr
                      key={l.id || idx}
                      style={{
                        borderBottom: "1px solid var(--border)",
                        background: idx % 2 === 0 ? "transparent" : "var(--bg-app)",
                      }}
                    >
                      <td style={{ padding: "8px", fontWeight: "bold" }}>
                        <span
                          style={{ color: "var(--primary)", cursor: "pointer", textDecoration: "underline" }}
                          onClick={() => {
                            if (l.itemId) {
                              openWorkspaceWindow({
                                key: `inventory-movements-${l.itemId}`,
                                title: `حركة مادة: ${l.itemCode}`,
                                screen: "inventory-movements",
                                params: { itemId: l.itemId },
                              });
                            }
                          }}
                        >
                          {l.itemCode}
                        </span>
                      </td>
                      <td style={{ padding: "8px" }}>{l.itemName}</td>
                      <td style={{ padding: "8px" }}>{l.unitCode}</td>
                      <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)" }}>{l.systemQuantity}</td>
                      <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)", fontWeight: "bold" }}>{l.countedQuantity}</td>
                      <td
                        style={{
                          padding: "8px",
                          fontFamily: "var(--font-mono, monospace)",
                          fontWeight: "bold",
                          color: diff > 0 ? "#2E7D32" : diff < 0 ? "#C62828" : "inherit",
                        }}
                      >
                        {l.differenceQuantity} {diff > 0 ? "(زيادة)" : diff < 0 ? "(عجز)" : "(مطابق)"}
                      </td>
                      <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)" }}>{l.unitCost}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        ) : (
          <div style={{ flex: 1, overflowX: "auto", border: "1px solid var(--border)", borderRadius: "4px", background: "var(--bg-surface)" }}>
            <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.85rem", textAlign: "right" }}>
              <thead>
                <tr style={{ background: "var(--header-bg)", color: "#FFFFFF" }}>
                  <th style={{ padding: "8px", width: "40px" }}>#</th>
                  <th style={{ padding: "8px", minWidth: "220px" }}>المادة المجرودة</th>
                  <th style={{ padding: "8px", width: "140px" }}>الكمية المجرودة</th>
                  <th style={{ padding: "8px", width: "140px" }}>تكلفة الوحدة التقديرية</th>
                  <th style={{ padding: "8px", width: "60px", textAlign: "center" }}>إجراء</th>
                </tr>
              </thead>
              <tbody>
                {lines.map((l, idx) => (
                  <tr
                    key={l.id}
                    style={{
                      borderBottom: "1px solid var(--border)",
                      background: idx % 2 === 0 ? "transparent" : "var(--bg-app)",
                    }}
                  >
                    <td style={{ padding: "8px", textAlign: "center", color: "var(--text-muted)" }}>{idx + 1}</td>
                    <td style={{ padding: "6px 8px" }}>
                      <LookupPicker
                        ariaLabel={`مادة السطر ${idx + 1}`}
                        placeholder="ابحث عن المادة بالاسم أو الرمز..."
                        search={searchItems}
                        value={l.item}
                        onChange={(it) => updateLine(l.id, { item: it })}
                      />
                    </td>
                    <td style={{ padding: "6px 8px" }}>
                      <input
                        type="text"
                        className="petra-input"
                        value={l.countedQuantity}
                        onChange={(e) => updateLine(l.id, { countedQuantity: e.target.value })}
                        style={{ width: "100%", padding: "5px 8px", fontFamily: "var(--font-mono, monospace)", textAlign: "center", border: "1px solid var(--border)", borderRadius: "3px" }}
                      />
                    </td>
                    <td style={{ padding: "6px 8px" }}>
                      <input
                        type="text"
                        className="petra-input"
                        value={l.unitCost}
                        onChange={(e) => updateLine(l.id, { unitCost: e.target.value })}
                        style={{ width: "100%", padding: "5px 8px", fontFamily: "var(--font-mono, monospace)", textAlign: "center", border: "1px solid var(--border)", borderRadius: "3px" }}
                      />
                    </td>
                    <td style={{ padding: "6px 8px", textAlign: "center" }}>
                      <PetraButton
                        variant="secondary"
                        size="sm"
                        onClick={() => removeLine(l.id)}
                        disabled={lines.length === 1}
                      >
                        حذف
                      </PetraButton>
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
