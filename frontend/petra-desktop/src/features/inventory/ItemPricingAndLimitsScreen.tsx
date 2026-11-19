import { useState, useEffect } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { useWorkspace } from "@/workspace/workspaceStore";
import {
  PetraButton,
  PetraField,
  PetraEmptyState,
  PetraLoadingSkeleton,
  PetraErrorDetails,
} from "@/components/ui";
import { toast } from "@/components/ui/toastStore";
import type { components } from "@/services/api/schema";

export interface ItemPricingAndLimitsScreenProps {
  params?: Readonly<Record<string, string>> | undefined;
}

type ItemGroupDto = components["schemas"]["ItemGroupDto"];
type AdjustItemPricesResponse = components["schemas"]["AdjustItemPricesResponse"];
type ItemSummaryDto = components["schemas"]["ItemSummaryDto"];

export default function ItemPricingAndLimitsScreen({ params: _params }: ItemPricingAndLimitsScreenProps) {
  const api = useApi();
  const { companyId, branchId } = useSession((s) => ({ companyId: s.companyId, branchId: s.branchId }));
  const openWorkspaceWindow = useWorkspace((s) => s.open);

  const [activeTab, setActiveTab] = useState<"pricing" | "limits">("pricing");

  // Tab 1: Pricing Tool State
  const [groups, setGroups] = useState<ItemGroupDto[]>([]);
  const [selectedGroupId, setSelectedGroupId] = useState<string>("");
  const [priceListCode, setPriceListCode] = useState<string>("");
  const [percent, setPercent] = useState<string>("5.00");
  const [roundingScale, setRoundingScale] = useState<number>(2);

  const [isLoadingGroups, setIsLoadingGroups] = useState(false);
  const [isAdjusting, setIsAdjusting] = useState(false);
  const [pricingResult, setPricingResult] = useState<AdjustItemPricesResponse | null>(null);

  // Tab 2: Stock Limits State
  const [items, setItems] = useState<ItemSummaryDto[]>([]);
  const [isLoadingItems, setIsLoadingItems] = useState(false);
  const [itemsError, setItemsError] = useState<{ status?: number; message: string } | null>(null);
  const [statusFilter, setStatusFilter] = useState<"all" | "critical" | "reorder" | "overstock">("all");
  const [searchQuery, setSearchQuery] = useState<string>("");

  useEffect(() => {
    if (!companyId || !branchId) return;

    const fetchGroups = async () => {
      setIsLoadingGroups(true);
      try {
        const res = await api.GET(
          "/api/v1/companies/{companyId}/branches/{branchId}/inventory/groups",
          {
            params: { path: { companyId, branchId } },
          }
        );
        if (res.data) {
          setGroups(res.data);
        }
      } catch {
        toast.error("فشل جلب مجموعات المواد");
      } finally {
        setIsLoadingGroups(false);
      }
    };

    fetchGroups();
  }, [companyId, branchId]);

  const fetchItems = async () => {
    if (!companyId || !branchId) return;
    setIsLoadingItems(true);
    setItemsError(null);
    try {
      const res = await api.GET(
        "/api/v1/companies/{companyId}/branches/{branchId}/inventory/items",
        {
          params: {
            path: { companyId, branchId },
            query: { page: 1, pageSize: 200 },
          },
        }
      );
      if (res.data?.items) {
        setItems(res.data.items);
      } else {
        setItems([]);
      }
    } catch (err) {
      setItemsError({
        message: err instanceof Error ? err.message : "فشل جلب قائمة المواد وحدود المخزون",
      });
    } finally {
      setIsLoadingItems(false);
    }
  };

  useEffect(() => {
    if (activeTab === "limits") {
      fetchItems();
    }
  }, [companyId, branchId, activeTab]);

  const handleAdjustPrices = async (previewOnly: boolean) => {
    if (!companyId || !branchId) return;
    if (!percent || isNaN(Number(percent))) {
      toast.warning("يرجى إدخال نسبة تعديل مئوية صحيحة (موجبة للزيادة أو سالبة للخصم)");
      return;
    }

    setIsAdjusting(true);
    setPricingResult(null);

    try {
      const res = await api.POST(
        "/api/v1/companies/{companyId}/branches/{branchId}/inventory/price-adjustments",
        {
          params: { path: { companyId, branchId } },
          body: {
            percent,
            roundingScale,
            previewOnly,
            groupId: selectedGroupId || null,
            priceListCode: priceListCode.trim() || null,
          },
        }
      );

      if (res.error || !res.data) {
        toast.error((res.error as { detail?: string })?.detail ?? "فشل تعديل أسعار المواد");
        return;
      }

      setPricingResult(res.data);
      if (previewOnly) {
        toast.success(`معاينة: سيتم تعديل أسعار ${res.data.itemCount} مادة (${res.data.rowCount} سجل سعر)`);
      } else {
        toast.success(`تم بنجاح تحديث وتطبيق أسعار ${res.data.itemCount} مادة (${res.data.rowCount} سجل سعر)`);
      }
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "خطأ أثناء تعديل الأسعار");
    } finally {
      setIsAdjusting(false);
    }
  };

  const getStockStatus = (it: ItemSummaryDto) => {
    const current = Number(it.currentStock ?? "0");
    const min = it.minStock ? Number(it.minStock) : null;
    const max = it.maxStock ? Number(it.maxStock) : null;
    const reorder = it.reorderPoint ? Number(it.reorderPoint) : null;

    if (min !== null && current < min) {
      return { code: "critical", label: "تحت الحد الأدنى (حرج)", color: "#C62828", bg: "#FFEBEE" };
    }
    if (reorder !== null && current <= reorder) {
      return { code: "reorder", label: "نقطة إعادة الطلب", color: "#D97706", bg: "#FEF3C7" };
    }
    if (max !== null && current > max) {
      return { code: "overstock", label: "فائض تخزين", color: "#1D4ED8", bg: "#EFF6FF" };
    }
    return { code: "safe", label: "رصيد آمن", color: "#2E7D32", bg: "#E8F5E9" };
  };

  const filteredItems = items.filter((it) => {
    const q = searchQuery.toLowerCase();
    const matchesSearch =
      !q || it.code?.toLowerCase().includes(q) || it.nameArabic?.toLowerCase().includes(q);
    if (!matchesSearch) return false;

    const status = getStockStatus(it).code;
    if (statusFilter === "critical") return status === "critical";
    if (statusFilter === "reorder") return status === "reorder" || status === "critical";
    if (statusFilter === "overstock") return status === "overstock";
    return true;
  });

  const exportLimitsTsv = () => {
    const headers = [
      "رمز المادة",
      "اسم المادة",
      "المجموعة",
      "الرصيد الفعلي الحالي",
      "الحد الأدنى",
      "نقطة إعادة الطلب",
      "الحد الأعلى",
      "حالة الأمان",
    ];
    const rows = filteredItems.map((it) => {
      const st = getStockStatus(it);
      return [
        it.code ?? "",
        it.nameArabic ?? "",
        it.groupName ?? "",
        it.currentStock,
        it.minStock ?? "—",
        it.reorderPoint ?? "—",
        it.maxStock ?? "—",
        st.label,
      ];
    });

    const tsvContent = [headers.join("\t"), ...rows.map((row) => row.join("\t"))].join("\n");
    const blob = new Blob(["\uFEFF" + tsvContent], { type: "text/tab-separated-values;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `item-stock-limits-${Date.now()}.tsv`;
    a.click();
    URL.revokeObjectURL(url);
    toast.success("تم تصدير تقرير حدود المخزون بنجاح");
  };

  return (
    <div className="item-pricing-limits-screen" style={{ padding: "16px", height: "100%", display: "flex", flexDirection: "column", gap: "16px", overflowY: "auto" }}>
      {/* Header */}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", borderBottom: "1px solid var(--border)", paddingBottom: "12px" }}>
        <div>
          <h2 style={{ margin: 0, fontSize: "1.25rem", color: "var(--header-bg)" }}>
            أداة تسعير المواد وحدود المخزون (Item Pricing & Stock Limits)
          </h2>
          <p style={{ margin: "4px 0 0", fontSize: "0.85rem", color: "var(--text-muted)" }}>
            تعديل وتحديث أسعار المواد بنسب مئوية مجمعة، ومراقبة الحدود الدنيا ونقاط إعادة الطلب للمخزون.
          </p>
        </div>
      </div>

      {/* Tabs */}
      <div style={{ display: "flex", gap: "8px", borderBottom: "1px solid var(--border)" }}>
        <button
          className="petra-tab-btn"
          style={{
            padding: "8px 16px",
            border: "none",
            borderBottom: activeTab === "pricing" ? "3px solid var(--primary)" : "none",
            background: "transparent",
            color: activeTab === "pricing" ? "var(--primary)" : "var(--text-muted)",
            fontWeight: activeTab === "pricing" ? "bold" : "normal",
            cursor: "pointer",
          }}
          onClick={() => setActiveTab("pricing")}
        >
          أداة تعديل الأسعار المجمعة (INV-07)
        </button>
        <button
          className="petra-tab-btn"
          style={{
            padding: "8px 16px",
            border: "none",
            borderBottom: activeTab === "limits" ? "3px solid var(--primary)" : "none",
            background: "transparent",
            color: activeTab === "limits" ? "var(--primary)" : "var(--text-muted)",
            fontWeight: activeTab === "limits" ? "bold" : "normal",
            cursor: "pointer",
          }}
          onClick={() => setActiveTab("limits")}
        >
          مراقبة حدود ونقاط إعادة الطلب (INV-08)
        </button>
      </div>

      {/* TAB 1: Pricing Tool */}
      {activeTab === "pricing" && (
        <div style={{ display: "flex", flexDirection: "column", gap: "16px", maxWidth: "800px" }}>
          <div style={{ background: "var(--bg-surface)", border: "1px solid var(--border)", borderRadius: "4px", padding: "16px", display: "flex", flexDirection: "column", gap: "16px" }}>
            <h3 style={{ margin: 0, fontSize: "1rem", color: "var(--header-bg)" }}>
              إعدادات تعديل وتحديث الأسعار
            </h3>
            <p style={{ margin: 0, fontSize: "0.85rem", color: "var(--text-muted)" }}>
              تسمح هذه الأداة برفع أو خفض أسعار المواد بنسبة مئوية محددة لكافة المواد النشطة أو لمجموعة معينة، مع إمكانية المعاينة الجافة قبل الاعتماد الفعلي.
            </p>

            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "16px" }}>
              <PetraField label="مجموعة المواد (اختياري - افتراضياً كافة المواد)">
                <select
                  className="petra-select"
                  value={selectedGroupId}
                  onChange={(e) => setSelectedGroupId(e.target.value)}
                  disabled={isLoadingGroups || isAdjusting}
                  style={{ width: "100%", padding: "6px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
                >
                  <option value="">كافة مجموعات المواد</option>
                  {groups.map((g) => (
                    <option key={g.id} value={g.id}>
                      {g.code} - {g.nameArabic}
                    </option>
                  ))}
                </select>
              </PetraField>

              <PetraField label="رمز قائمة الأسعار (اختياري)">
                <input
                  type="text"
                  className="petra-input"
                  placeholder="مثال: WHOLESALE أو فارغ للكل"
                  value={priceListCode}
                  onChange={(e) => setPriceListCode(e.target.value)}
                  disabled={isAdjusting}
                  style={{ width: "100%", padding: "6px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
                />
              </PetraField>

              <PetraField label="نسبة التعديل المئوية % (موجبة للزيادة / سالبة للخصم)">
                <input
                  type="text"
                  className="petra-input"
                  placeholder="مثال: 5.00 للزيادة، -10.00 للخصم"
                  value={percent}
                  onChange={(e) => setPercent(e.target.value)}
                  disabled={isAdjusting}
                  style={{ width: "100%", padding: "6px 8px", fontSize: "0.85rem", fontFamily: "var(--font-mono, monospace)", border: "1px solid var(--border)", borderRadius: "3px" }}
                />
              </PetraField>

              <PetraField label="عدد المنازل العشرية للتقريب">
                <select
                  className="petra-select"
                  value={roundingScale}
                  onChange={(e) => setRoundingScale(Number(e.target.value))}
                  disabled={isAdjusting}
                  style={{ width: "100%", padding: "6px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
                >
                  <option value={0}>0 (أقرب عدد صحيح)</option>
                  <option value={1}>1 منزلة عشرية</option>
                  <option value={2}>2 منزلتان عشريتان (قياسي)</option>
                  <option value={3}>3 منازل عشرية</option>
                  <option value={4}>4 منازل عشرية</option>
                </select>
              </PetraField>
            </div>

            <div style={{ display: "flex", gap: "8px", justifyContent: "flex-end", marginTop: "8px" }}>
              <PetraButton
                variant="secondary"
                onClick={() => handleAdjustPrices(true)}
                disabled={isAdjusting}
              >
                {isAdjusting ? "جارٍ المعاينة..." : "معاينة التأثير جافاً (Preview)"}
              </PetraButton>
              <PetraButton
                variant="primary"
                onClick={() => handleAdjustPrices(false)}
                disabled={isAdjusting}
              >
                {isAdjusting ? "جارٍ التطبيق..." : "تطبيق وحفظ الأسعار الجديدة"}
              </PetraButton>
            </div>
          </div>

          {/* Pricing Result Card */}
          {pricingResult && (
            <div
              style={{
                background: pricingResult.applied ? "#E8F5E9" : "#FEF3C7",
                border: `1px solid ${pricingResult.applied ? "#A5D6A7" : "#FDE68A"}`,
                borderRadius: "4px",
                padding: "16px",
                display: "flex",
                flexDirection: "column",
                gap: "8px",
              }}
            >
              <div style={{ fontWeight: "bold", color: pricingResult.applied ? "#2E7D32" : "#92400E", fontSize: "1rem" }}>
                {pricingResult.applied
                  ? "✓ تم تطبيق وحفظ تعديل الأسعار بنجاح في قاعدة البيانات"
                  : "ℹ نتيجة المعاينة الجافة (لم يتم الحفظ في قاعدة البيانات)"}
              </div>
              <div style={{ display: "flex", gap: "24px", fontSize: "0.9rem" }}>
                <div>
                  <strong>عدد المواد المتأثرة:</strong> {pricingResult.itemCount} مادة
                </div>
                <div>
                  <strong>عدد سجلات الأسعار المعدلة:</strong> {pricingResult.rowCount} سجل
                </div>
                <div>
                  <strong>حالة التطبيق:</strong> {pricingResult.applied ? "مطبقة فعلياً" : "معاينة فقط"}
                </div>
              </div>
            </div>
          )}
        </div>
      )}

      {/* TAB 2: Stock Limits Monitor */}
      {activeTab === "limits" && (
        <div style={{ display: "flex", flexDirection: "column", gap: "12px", flex: 1 }}>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", flexWrap: "wrap", gap: "8px" }}>
            <div style={{ display: "flex", gap: "12px", alignItems: "center" }}>
              <input
                type="text"
                className="petra-input"
                placeholder="بحث عن مادة..."
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                style={{ width: "200px", padding: "5px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
              />
              <PetraField label="تصفية حسب حالة الأمان">
                <select
                  className="petra-select"
                  value={statusFilter}
                  onChange={(e) => setStatusFilter(e.target.value as any)}
                  style={{ width: "160px", padding: "5px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
                >
                  <option value="all">كافة الحالات ({items.length})</option>
                  <option value="reorder">تجاوز نقطة الطلب</option>
                  <option value="critical">تحت الحد الأدنى (حرج)</option>
                  <option value="overstock">فائض تخزين</option>
                </select>
              </PetraField>
            </div>
            <div style={{ display: "flex", gap: "8px" }}>
              <PetraButton variant="secondary" onClick={exportLimitsTsv} disabled={filteredItems.length === 0} size="sm">
                تصدير TSV
              </PetraButton>
              <PetraButton variant="secondary" onClick={fetchItems} disabled={isLoadingItems} size="sm">
                تحديث الأرصدة
              </PetraButton>
            </div>
          </div>

          {isLoadingItems ? (
            <PetraLoadingSkeleton rows={8} />
          ) : itemsError ? (
            <PetraErrorDetails status={itemsError.status} message={itemsError.message} onRetry={fetchItems} />
          ) : filteredItems.length === 0 ? (
            <PetraEmptyState title="لا توجد مواد مطابقة لشروط الفلترة المحددة" />
          ) : (
            <div style={{ flex: 1, overflowX: "auto", border: "1px solid var(--border)", borderRadius: "4px", background: "var(--bg-surface)" }}>
              <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.85rem", textAlign: "right" }}>
                <thead>
                  <tr style={{ background: "var(--header-bg)", color: "#FFFFFF" }}>
                    <th style={{ padding: "8px" }}>رمز المادة</th>
                    <th style={{ padding: "8px" }}>اسم المادة</th>
                    <th style={{ padding: "8px" }}>المجموعة</th>
                    <th style={{ padding: "8px" }}>الرصيد الفعلي الحالي</th>
                    <th style={{ padding: "8px" }}>الحد الأدنى (Min)</th>
                    <th style={{ padding: "8px" }}>نقطة إعادة الطلب</th>
                    <th style={{ padding: "8px" }}>الحد الأعلى (Max)</th>
                    <th style={{ padding: "8px", textAlign: "center" }}>مؤشر الأمان</th>
                  </tr>
                </thead>
                <tbody>
                  {filteredItems.map((it, idx) => {
                    const st = getStockStatus(it);
                    return (
                      <tr
                        key={it.id || idx}
                        style={{
                          borderBottom: "1px solid var(--border)",
                          background: idx % 2 === 0 ? "transparent" : "var(--bg-app)",
                        }}
                      >
                        <td style={{ padding: "8px", fontWeight: "bold" }}>
                          <span
                            style={{ color: "var(--primary)", cursor: "pointer", textDecoration: "underline" }}
                            onClick={() => {
                              if (it.id) {
                                openWorkspaceWindow({
                                  key: `inventory-movements-${it.id}`,
                                  title: `حركة مادة: ${it.code}`,
                                  screen: "inventory-movements",
                                  params: { itemId: it.id },
                                });
                              }
                            }}
                          >
                            {it.code}
                          </span>
                        </td>
                        <td style={{ padding: "8px" }}>{it.nameArabic}</td>
                        <td style={{ padding: "8px", color: "var(--text-muted)" }}>{it.groupName || "—"}</td>
                        <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)", fontWeight: "bold" }}>
                          {it.currentStock} {it.baseUnitSymbol ?? ""}
                        </td>
                        <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)" }}>
                          {it.minStock || "—"}
                        </td>
                        <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)", color: "#D97706", fontWeight: "bold" }}>
                          {it.reorderPoint || "—"}
                        </td>
                        <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)" }}>
                          {it.maxStock || "—"}
                        </td>
                        <td style={{ padding: "8px", textAlign: "center" }}>
                          <span
                            style={{
                              padding: "2px 8px",
                              borderRadius: "3px",
                              fontSize: "0.75rem",
                              fontWeight: "bold",
                              background: st.bg,
                              color: st.color,
                            }}
                          >
                            {st.label}
                          </span>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
