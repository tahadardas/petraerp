import { useState, useEffect } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { useWorkspace } from "@/workspace/workspaceStore";
import { PetraButton, PetraLoadingSkeleton, PetraEmptyState, PetraErrorDetails, PetraDialog, PetraField } from "@/components/ui";
import { toast } from "@/components/ui/toastStore";

export interface OpenItemsSettlementScreenProps {
  params?: Readonly<Record<string, string>> | undefined;
}

interface OpenItemDto {
  id: string;
  companyId: string;
  branchId: string;
  partyId: string;
  partyNameArabic: string;
  partyType: number; // 1: Customer, 2: Supplier
  originDocumentNumber?: string | null;
  originDocumentDateUtc: string;
  dueDateUtc?: string | null;
  currencyCode: string;
  originalAmount: string;
  settledAmount: string;
  outstandingAmount: string;
  exchangeRate: string;
  originalBaseCarryingAmount: string;
  currentBaseCarryingAmount: string;
  accountCode: string;
  state: number; // 0: Open, 1: PartiallySettled, 2: FullySettled, etc.
}

interface PartyBalanceDto {
  partyId: string;
  partyNameArabic: string;
  partyType: number;
  currencyCode: string;
  totalOriginal: string;
  totalSettled: string;
  totalOutstanding: string;
  totalBaseCarrying: string;
}

export default function OpenItemsSettlementScreen({ params: _params }: OpenItemsSettlementScreenProps) {
  const api = useApi();
  const { companyId, branchId } = useSession((s) => ({ companyId: s.companyId, branchId: s.branchId }));
  const openWorkspaceWindow = useWorkspace((s) => s.open);

  const [viewMode, setViewMode] = useState<"items" | "balances">("items");
  const [partyTypeFilter, setPartyTypeFilter] = useState<number>(0); // 0: all, 1: customer, 2: supplier

  const [items, setItems] = useState<OpenItemDto[]>([]);
  const [balances, setBalances] = useState<PartyBalanceDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<{ status?: number; message?: string } | null>(null);

  // New Open Item Dialog
  const [createOpen, setCreateOpen] = useState(false);
  const [newPartyId, setNewPartyId] = useState("");
  const [newPartyType, setNewPartyType] = useState<number>(1);
  const [newCurrencyCode, setNewCurrencyCode] = useState("JOD");
  const [newOriginalAmount, setNewOriginalAmount] = useState("");
  const [newExchangeRate, setNewExchangeRate] = useState("1.00000000");
  const [newDocNumber, setNewDocNumber] = useState("");
  const [newAccountId, setNewAccountId] = useState("");
  const [saving, setSaving] = useState(false);

  const loadData = async () => {
    if (!companyId || !branchId) return;

    setLoading(true);
    setError(null);
    try {
      if (viewMode === "items") {
        const res = await api.GET("/api/v1/companies/{companyId}/branches/{branchId}/open-items", {
          params: {
            path: { companyId, branchId },
            query: {
              ...(partyTypeFilter > 0 ? { partyType: partyTypeFilter } : {}),
            },
          },
        });

        if (res.error) {
          setError({ status: res.response.status, message: "فشل تحميل الذمم المفتوحة" });
        } else if (res.data) {
          setItems(res.data as unknown as OpenItemDto[]);
        }
      } else {
        const res = await api.GET("/api/v1/companies/{companyId}/branches/{branchId}/open-items/party-balances", {
          params: {
            path: { companyId, branchId },
          },
        });

        if (res.error) {
          setError({ status: res.response.status, message: "فشل تحميل ملخص أرصدة الأطراف" });
        } else if (res.data) {
          setBalances(res.data as unknown as PartyBalanceDto[]);
        }
      }
    } catch (err) {
      setError({ message: err instanceof Error ? err.message : "خطأ غير متوقع" });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadData();
  }, [companyId, branchId, viewMode, partyTypeFilter]);

  const copyToExcel = () => {
    if (viewMode === "items") {
      if (items.length === 0) return;
      const header = "الطرف\tالنوع\tالمستند\tالتاريخ\tالاستحقاق\tالعملة\tالمبلغ الأصلي\tالمسدد\tالمتبقي\tالحساب";
      const rows = items.map((i) =>
        `${i.partyNameArabic}\t${i.partyType === 1 ? "عميل" : "مورد"}\t${i.originDocumentNumber ?? ""}\t${i.originDocumentDateUtc}\t${i.dueDateUtc ?? ""}\t${i.currencyCode}\t${i.originalAmount}\t${i.settledAmount}\t${i.outstandingAmount}\t${i.accountCode}`
      );
      navigator.clipboard?.writeText([header, ...rows].join("\r\n")).then(() => {
        toast.success("تم نسخ جدول الذمم المفتوحة إلى الحافظة (TSV)");
      });
    } else {
      if (balances.length === 0) return;
      const header = "الطرف\tالنوع\tالعملة\tإجمالي الأصلي\tإجمالي المسدد\tإجمالي المتبقي\tالقيمة بالعملة الأساسية";
      const rows = balances.map((b) =>
        `${b.partyNameArabic}\t${b.partyType === 1 ? "عميل" : "مورد"}\t${b.currencyCode}\t${b.totalOriginal}\t${b.totalSettled}\t${b.totalOutstanding}\t${b.totalBaseCarrying}`
      );
      navigator.clipboard?.writeText([header, ...rows].join("\r\n")).then(() => {
        toast.success("تم نسخ جدول أرصدة الأطراف إلى الحافظة (TSV)");
      });
    }
  };

  const handleCreateOpenItem = async () => {
    if (!companyId || !branchId || !newPartyId || !newAccountId || !newOriginalAmount) {
      toast.error("يرجى ملء جميع الحقول الإلزامية");
      return;
    }

    setSaving(true);
    try {
      const res = await api.POST("/api/v1/companies/{companyId}/branches/{branchId}/open-items", {
        params: {
          path: { companyId, branchId },
        },
        body: {
          branchId,
          partyId: newPartyId,
          partyType: newPartyType,
          currencyCode: newCurrencyCode.toUpperCase(),
          originalAmount: newOriginalAmount,
          exchangeRate: newExchangeRate,
          originType: 1, // manual / invoice
          accountId: newAccountId,
          originDocumentDateUtc: new Date().toISOString(),
          ...(newDocNumber ? { originDocumentNumber: newDocNumber } : {}),
        },
      });

      if (res.error) {
        toast.error("فشل حفظ إشعار الذمة المفتوحة");
      } else {
        toast.success("تم إنشاء الذمة المفتوحة بنجاح");
        setCreateOpen(false);
        loadData();
      }
    } catch {
      toast.error("خطأ أثناء الاتصال بالخادم");
    } finally {
      setSaving(false);
    }
  };

  const getStatusText = (state: number) => {
    switch (state) {
      case 0:
        return "مفتوحة";
      case 1:
        return "مسددة جزئياً";
      case 2:
        return "مسددة بالكامل";
      default:
        return "معلقة";
    }
  };

  return (
    <div style={{ display: "flex", flexDirection: "column", height: "100%", background: "var(--bg-app)" }}>
      {/* Create Dialog */}
      <PetraDialog
        open={createOpen}
        onClose={() => setCreateOpen(false)}
        title="إنشاء بند ذمة مفتوحة يدوي (Open Item)"
        width={480}
        actions={
          <>
            <PetraButton variant="secondary" onClick={() => setCreateOpen(false)}>
              إلغاء
            </PetraButton>
            <PetraButton variant="primary" disabled={saving} onClick={handleCreateOpenItem}>
              {saving ? "جاري الحفظ..." : "حفظ البند"}
            </PetraButton>
          </>
        }
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "var(--space-3)" }}>
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "var(--space-2)" }}>
            <PetraField label="نوع الطرف" required>
              <select
                value={newPartyType}
                onChange={(e) => setNewPartyType(Number(e.target.value))}
                style={{
                  height: "var(--control-height)",
                  padding: "0 var(--space-2)",
                  borderRadius: "var(--radius)",
                  border: "1px solid var(--border)",
                  background: "var(--bg-input)",
                  color: "var(--text)",
                }}
              >
                <option value={1}>عميل (Customer)</option>
                <option value={2}>مورد (Supplier)</option>
              </select>
            </PetraField>

            <PetraField label="معرّف الطرف (Party ID)" required>
              <input
                type="text"
                value={newPartyId}
                onChange={(e) => setNewPartyId(e.target.value)}
                placeholder="UUID للطرف"
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

          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "var(--space-2)" }}>
            <PetraField label="رقم المستند الأصل">
              <input
                type="text"
                value={newDocNumber}
                onChange={(e) => setNewDocNumber(e.target.value)}
                placeholder="مثال: INV-2026-001"
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

            <PetraField label="معرّف حساب الأستاذ" required>
              <input
                type="text"
                value={newAccountId}
                onChange={(e) => setNewAccountId(e.target.value)}
                placeholder="UUID لحساب الذمة"
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

          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr", gap: "var(--space-2)" }}>
            <PetraField label="المبلغ الأصلي" required>
              <input
                type="text"
                value={newOriginalAmount}
                onChange={(e) => setNewOriginalAmount(e.target.value)}
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

            <PetraField label="العملة" required>
              <input
                type="text"
                value={newCurrencyCode}
                onChange={(e) => setNewCurrencyCode(e.target.value)}
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

            <PetraField label="سعر الصرف" required>
              <input
                type="text"
                value={newExchangeRate}
                onChange={(e) => setNewExchangeRate(e.target.value)}
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
            تسوية الذمم والفواتير المفتوحة (Open Items & Settlements)
          </h2>
          <div style={{ display: "flex", gap: "var(--space-1)" }}>
            <PetraButton
              variant={viewMode === "items" ? "primary" : "secondary"}
              size="sm"
              onClick={() => setViewMode("items")}
            >
              الذمم المفتوحة
            </PetraButton>
            <PetraButton
              variant={viewMode === "balances" ? "primary" : "secondary"}
              size="sm"
              onClick={() => setViewMode("balances")}
            >
              ملخص أرصدة الأطراف
            </PetraButton>
          </div>
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: "var(--space-2)" }}>
          {viewMode === "items" && (
            <select
              value={partyTypeFilter}
              onChange={(e) => setPartyTypeFilter(Number(e.target.value))}
              style={{
                height: "var(--control-height)",
                padding: "0 var(--space-2)",
                borderRadius: "var(--radius)",
                border: "1px solid var(--border)",
                background: "var(--bg-input)",
                color: "var(--text)",
                fontSize: "var(--fs-caption)",
              }}
            >
              <option value={0}>جميع الأطراف (الكل)</option>
              <option value={1}>العملاء فقط</option>
              <option value={2}>الموردون فقط</option>
            </select>
          )}

          <PetraButton variant="secondary" size="sm" onClick={() => setCreateOpen(true)}>
            إضافة ذمة يدوية
          </PetraButton>
          <PetraButton variant="secondary" size="sm" onClick={copyToExcel}>
            نسخ إلى Excel
          </PetraButton>
          <PetraButton variant="primary" size="sm" onClick={loadData} disabled={loading}>
            {loading ? "جاري التحميل..." : "تحديث"}
          </PetraButton>
        </div>
      </div>

      {/* Main Table Area */}
      <div style={{ flex: 1, overflow: "auto", padding: "var(--space-3)" }}>
        {loading ? (
          <PetraLoadingSkeleton rows={10} />
        ) : error ? (
          <PetraErrorDetails status={error.status} message={error.message} onRetry={loadData} />
        ) : viewMode === "items" ? (
          items.length === 0 ? (
            <PetraEmptyState title="لا توجد ذمم مفتوحة غير مسددة" description="جميع الفواتير والمستندات مسددة بالكامل." />
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
                    <th style={{ padding: "var(--space-2) var(--space-3)" }}>الطرف</th>
                    <th style={{ padding: "var(--space-2) var(--space-3)" }}>النوع</th>
                    <th style={{ padding: "var(--space-2) var(--space-3)" }}>رقم المستند</th>
                    <th style={{ padding: "var(--space-2) var(--space-3)" }}>تاريخ المستند</th>
                    <th style={{ padding: "var(--space-2) var(--space-3)" }}>الاستحقاق</th>
                    <th style={{ padding: "var(--space-2) var(--space-3)" }}>العملة</th>
                    <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left" }}>المبلغ الأصلي</th>
                    <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left" }}>المسدد</th>
                    <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left" }}>المتبقي</th>
                    <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "center" }}>الحالة</th>
                    <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "center" }}>إجراء</th>
                  </tr>
                </thead>
                <tbody>
                  {items.map((item, idx) => (
                    <tr
                      key={item.id}
                      style={{
                        borderBottom: "1px solid var(--border-light)",
                        background: idx % 2 === 1 ? "var(--bg-sunken)" : "transparent",
                      }}
                    >
                      <td style={{ padding: "var(--space-2) var(--space-3)", fontWeight: 600 }}>
                        <button
                          type="button"
                          onClick={() => {
                            openWorkspaceWindow({
                              key: `statement:${item.partyId}`,
                              screen: "party-statement",
                              title: `كشف حساب: ${item.partyNameArabic}`,
                              params: { partyId: item.partyId, partyName: item.partyNameArabic },
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
                          {item.partyNameArabic}
                        </button>
                      </td>
                      <td style={{ padding: "var(--space-2) var(--space-3)", color: "var(--text-muted)" }}>
                        {item.partyType === 1 ? "عميل" : "مورد"}
                      </td>
                      <td style={{ padding: "var(--space-2) var(--space-3)", fontFamily: "monospace" }}>
                        {item.originDocumentNumber ?? "—"}
                      </td>
                      <td style={{ padding: "var(--space-2) var(--space-3)" }}>
                        {item.originDocumentDateUtc ? item.originDocumentDateUtc.substring(0, 10) : "—"}
                      </td>
                      <td style={{ padding: "var(--space-2) var(--space-3)", color: item.dueDateUtc ? "var(--color-danger)" : "inherit" }}>
                        {item.dueDateUtc ? item.dueDateUtc.substring(0, 10) : "—"}
                      </td>
                      <td style={{ padding: "var(--space-2) var(--space-3)", fontFamily: "monospace" }}>
                        {item.currencyCode}
                      </td>
                      <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left", fontFamily: "monospace" }}>
                        {item.originalAmount}
                      </td>
                      <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left", fontFamily: "monospace" }}>
                        {item.settledAmount}
                      </td>
                      <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left", fontFamily: "monospace", fontWeight: 700, color: "var(--color-danger)" }}>
                        {item.outstandingAmount}
                      </td>
                      <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "center" }}>
                        <span
                          style={{
                            padding: "2px 8px",
                            borderRadius: "var(--radius)",
                            fontSize: "var(--fs-caption)",
                            background: item.state === 2 ? "var(--color-success-bg, #e6f4ea)" : item.state === 1 ? "var(--color-warning-bg, #fef7e0)" : "var(--color-danger-bg, #fce8e6)",
                            color: item.state === 2 ? "var(--color-success)" : item.state === 1 ? "var(--color-warning)" : "var(--color-danger)",
                          }}
                        >
                          {getStatusText(item.state)}
                        </span>
                      </td>
                      <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "center" }}>
                        <PetraButton
                          variant="secondary"
                          size="sm"
                          onClick={() => {
                            openWorkspaceWindow({
                              key: `voucher-new:${item.partyId}`,
                              screen: item.partyType === 1 ? "receipt-voucher" : "payment-voucher",
                              title: `سند تسوية - ${item.partyNameArabic}`,
                              params: {
                                partyId: item.partyId,
                                amount: item.outstandingAmount,
                              },
                            });
                          }}
                        >
                          تسوية
                        </PetraButton>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )
        ) : balances.length === 0 ? (
          <PetraEmptyState title="لا توجد أرصدة أطراف مسجلة" description="تأكد من وجود حركات أطراف مسجلة." />
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
                  <th style={{ padding: "var(--space-2) var(--space-3)" }}>الطرف</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)" }}>النوع</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)" }}>العملة</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left" }}>إجمالي الأصلي</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left" }}>إجمالي المسدد</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left" }}>إجمالي المتبقي</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left" }}>القيمة بالعملة الأساسية</th>
                </tr>
              </thead>
              <tbody>
                {balances.map((b, idx) => (
                  <tr
                    key={b.partyId + b.currencyCode}
                    style={{
                      borderBottom: "1px solid var(--border-light)",
                      background: idx % 2 === 1 ? "var(--bg-sunken)" : "transparent",
                    }}
                  >
                    <td style={{ padding: "var(--space-2) var(--space-3)", fontWeight: 600 }}>
                      <button
                        type="button"
                        onClick={() => {
                          openWorkspaceWindow({
                            key: `statement:${b.partyId}`,
                            screen: "party-statement",
                            title: `كشف حساب: ${b.partyNameArabic}`,
                            params: { partyId: b.partyId, partyName: b.partyNameArabic },
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
                        {b.partyNameArabic}
                      </button>
                    </td>
                    <td style={{ padding: "var(--space-2) var(--space-3)", color: "var(--text-muted)" }}>
                      {b.partyType === 1 ? "عميل" : "مورد"}
                    </td>
                    <td style={{ padding: "var(--space-2) var(--space-3)", fontFamily: "monospace" }}>
                      {b.currencyCode}
                    </td>
                    <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left", fontFamily: "monospace" }}>
                      {b.totalOriginal}
                    </td>
                    <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left", fontFamily: "monospace" }}>
                      {b.totalSettled}
                    </td>
                    <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left", fontFamily: "monospace", fontWeight: 700, color: "var(--color-danger)" }}>
                      {b.totalOutstanding}
                    </td>
                    <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left", fontFamily: "monospace", fontWeight: 700 }}>
                      {b.totalBaseCarrying}
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
