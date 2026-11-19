import { useState, useEffect } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { PetraButton, PetraLoadingSkeleton, PetraEmptyState, PetraErrorDetails, PetraDialog, PetraField } from "@/components/ui";
import { toast } from "@/components/ui/toastStore";
import type { components } from "@/services/api/schema";

export interface InterCompanyScreenProps {
  params?: Readonly<Record<string, string>> | undefined;
}

type InterCompanyLink = components["schemas"]["InterCompanyLinkDto"];
type ReconciliationResponse = components["schemas"]["InterCompanyReconciliationResponse"];

export default function InterCompanyScreen({ params: _params }: InterCompanyScreenProps) {
  const api = useApi();
  const { companyId, branchId } = useSession((s) => ({ companyId: s.companyId, branchId: s.branchId }));

  const [links, setLinks] = useState<InterCompanyLink[]>([]);
  const [selectedLinkId, setSelectedLinkId] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<{ status?: number; message?: string } | null>(null);

  // Reconciliation state
  const [asOfDate, setAsOfDate] = useState<string>(new Date().toISOString().slice(0, 10));
  const [reconciliation, setReconciliation] = useState<ReconciliationResponse | null>(null);
  const [isLoadingRecon, setIsLoadingRecon] = useState(false);

  // Add Link Dialog
  const [isAddOpen, setIsAddOpen] = useState(false);
  const [newCounterpartyCompanyId, setNewCounterpartyCompanyId] = useState("");
  const [newAccountCode, setNewAccountCode] = useState("");
  const [newCounterpartyAccountCode, setNewCounterpartyAccountCode] = useState("");
  const [newCurrencyCode, setNewCurrencyCode] = useState("SAR");
  const [newNote, setNewNote] = useState("");
  const [isSaving, setIsSaving] = useState(false);

  const loadLinks = async () => {
    if (!companyId || !branchId) return;

    setIsLoading(true);
    setError(null);

    try {
      const res = await api.GET(
        "/api/v1/companies/{companyId}/branches/{branchId}/inter-company/links",
        {
          params: {
            path: { companyId, branchId },
          },
        }
      );

      if (res.error || !res.data) {
        setError({
          status: res.response?.status,
          message: (res.error as { title?: string; detail?: string })?.detail ?? "فشل تحميل روابط الشركات الشقيقة",
        });
        return;
      }

      const items = res.data.items ?? [];
      setLinks(items);
      if (items.length > 0 && !selectedLinkId) {
        setSelectedLinkId(items[0]?.id ?? null);
      }
    } catch (err) {
      setError({
        message: err instanceof Error ? err.message : "خطأ غير متوقع",
      });
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    if (companyId && branchId) {
      void loadLinks();
    }
  }, [companyId, branchId]);

  const selectedLink = links.find((l) => l.id === selectedLinkId) ?? null;

  const runReconciliation = async (linkId: string) => {
    if (!companyId || !branchId) return;

    setIsLoadingRecon(true);
    setReconciliation(null);

    try {
      const res = await api.GET(
        "/api/v1/companies/{companyId}/branches/{branchId}/inter-company/links/{linkId}/reconciliation",
        {
          params: {
            path: { companyId, branchId, linkId },
            query: {
              ...(asOfDate ? { asOf: asOfDate } : {}),
            },
          },
        }
      );

      if (res.error || !res.data) {
        toast.error((res.error as { title?: string; detail?: string })?.detail ?? "فشل تشغيل مطابقة الحسابات المتقابلة");
        return;
      }

      setReconciliation(res.data);
      if (res.data.matched) {
        toast.success("الحسابات المتقابلة متطابقة تماماً (الفارق المحاسبي = 0)");
      } else {
        toast.warning(`يوجد فارق تسوية بين الشركتين: ${res.data.difference} ${res.data.currencyCode}`);
      }
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "خطأ أثناء المطابقة");
    } finally {
      setIsLoadingRecon(false);
    }
  };

  useEffect(() => {
    if (selectedLinkId) {
      void runReconciliation(selectedLinkId);
    }
  }, [selectedLinkId]);

  const handleToggleActive = async (link: InterCompanyLink) => {
    if (!companyId || !branchId || !link.id) return;

    try {
      const res = await api.PUT(
        "/api/v1/companies/{companyId}/branches/{branchId}/inter-company/links/{linkId}/active",
        {
          params: {
            path: { companyId, branchId, linkId: link.id },
          },
          body: {
            isActive: !link.isActive,
          },
        }
      );

      if (res.error) {
        toast.error("فشل تعديل حالة الرابط");
        return;
      }

      toast.success(link.isActive ? "تم تعطيل الرابط" : "تم تفعيل الرابط");
      await loadLinks();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "خطأ أثناء التعديل");
    }
  };

  const handleSaveLink = async () => {
    if (!companyId || !branchId) return;
    if (!newCounterpartyCompanyId || !newAccountCode || !newCounterpartyAccountCode) {
      toast.warning("يرجى ملء جميع الحقول المطلوبة");
      return;
    }

    setIsSaving(true);
    try {
      const res = await api.PUT(
        "/api/v1/companies/{companyId}/branches/{branchId}/inter-company/links",
        {
          params: {
            path: { companyId, branchId },
          },
          body: {
            counterpartyCompanyId: newCounterpartyCompanyId,
            accountCode: newAccountCode,
            counterpartyAccountCode: newCounterpartyAccountCode,
            currencyCode: newCurrencyCode,
            note: newNote || null,
          },
        }
      );

      if (res.error || !res.data) {
        toast.error((res.error as { title?: string; detail?: string })?.detail ?? "فشل حفظ الرابط المالي");
        return;
      }

      toast.success("تم حفظ الرابط المالي المشترك بنجاح");
      setIsAddOpen(false);
      setNewCounterpartyCompanyId("");
      setNewAccountCode("");
      setNewCounterpartyAccountCode("");
      setNewNote("");
      await loadLinks();
      if (res.data.id) {
        setSelectedLinkId(res.data.id);
      }
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "خطأ أثناء الحفظ");
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <div className="inter-company-screen" style={{ padding: "16px", height: "100%", display: "flex", flexDirection: "column", gap: "16px", overflowY: "auto" }}>
      {/* Header */}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", borderBottom: "1px solid var(--border)", paddingBottom: "12px" }}>
        <div>
          <h2 style={{ margin: 0, fontSize: "1.25rem", color: "var(--header-bg)" }}>
            تسوية ومطابقة الحسابات المشتركة بين الشركات (Inter-Company Settlement)
          </h2>
          <p style={{ margin: "4px 0 0", fontSize: "0.85rem", color: "var(--text-muted)" }}>
            ربط الحسابات المتقابلة بين الشركات الشقيقة ومطابقة الأرصدة تلقائياً وكشف الحركات غير المسواة.
          </p>
        </div>
        <div style={{ display: "flex", gap: "8px" }}>
          <PetraButton variant="secondary" onClick={loadLinks} disabled={isLoading} size="sm">
            تحديث الروابط
          </PetraButton>
          <PetraButton variant="primary" onClick={() => setIsAddOpen(true)} size="sm">
            + إنشاء رابط جديد
          </PetraButton>
        </div>
      </div>

      {isLoading ? (
        <PetraLoadingSkeleton rows={8} />
      ) : error ? (
        <PetraErrorDetails status={error.status} message={error.message} onRetry={loadLinks} />
      ) : (
        <div style={{ flex: 1, display: "grid", gridTemplateColumns: "350px 1fr", gap: "16px", minHeight: 0 }}>
          {/* Right Pane: Links List */}
          <div style={{ border: "1px solid var(--border)", borderRadius: "4px", background: "var(--bg-surface)", display: "flex", flexDirection: "column" }}>
            <div style={{ padding: "10px 12px", borderBottom: "1px solid var(--border)", fontWeight: "bold", color: "var(--header-bg)", fontSize: "0.9rem" }}>
              روابط الشركات الشقيقة ({links.length})
            </div>
            <div style={{ flex: 1, overflowY: "auto" }}>
              {links.length === 0 ? (
                <PetraEmptyState title="لا توجد روابط بين الشركات حالياً" />
              ) : (
                links.map((link) => {
                  const isSelected = link.id === selectedLinkId;
                  return (
                    <div
                      key={link.id}
                      onClick={() => setSelectedLinkId(link.id ?? null)}
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
                          {link.counterpartyCompanyName || "شركة شقيقة"}
                        </div>
                        <span
                          style={{
                            fontSize: "0.7rem",
                            padding: "2px 6px",
                            borderRadius: "3px",
                            background: link.isActive ? "#E8F5E9" : "#ECEFF1",
                            color: link.isActive ? "#2E7D32" : "#546E7A",
                          }}
                        >
                          {link.isActive ? "نشط" : "معطل"}
                        </span>
                      </div>
                      <div style={{ fontSize: "0.8rem", color: "var(--text-muted)", marginTop: "4px" }}>
                        حسابنا: <strong style={{ color: "var(--text)" }}>{link.accountCode}</strong> ⇄ مقابل: <strong style={{ color: "var(--text)" }}>{link.counterpartyAccountCode}</strong>
                      </div>
                      <div style={{ fontSize: "0.75rem", color: "var(--text-muted)", marginTop: "2px" }}>
                        العملة: {link.currencyCode}
                      </div>
                    </div>
                  );
                })
              )}
            </div>
          </div>

          {/* Left Pane: Link Details & Reconciliation */}
          {selectedLink ? (
            <div style={{ border: "1px solid var(--border)", borderRadius: "4px", background: "var(--bg-surface)", padding: "16px", display: "flex", flexDirection: "column", gap: "16px", overflowY: "auto" }}>
              {/* Link Details Bar */}
              <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", borderBottom: "1px solid var(--border)", paddingBottom: "12px" }}>
                <div>
                  <h3 style={{ margin: 0, fontSize: "1.05rem", color: "var(--header-bg)" }}>
                    مطابقة الرابط: {selectedLink.counterpartyCompanyName || "الشركة الشقيقة"}
                  </h3>
                  <div style={{ fontSize: "0.8rem", color: "var(--text-muted)", marginTop: "4px" }}>
                    حسابنا المحلي: {selectedLink.accountCode} | حساب الطرف المقابل: {selectedLink.counterpartyAccountCode} | العملة: {selectedLink.currencyCode}
                  </div>
                </div>
                <div style={{ display: "flex", gap: "8px" }}>
                  <PetraButton
                    variant="secondary"
                    size="sm"
                    onClick={() => handleToggleActive(selectedLink)}
                  >
                    {selectedLink.isActive ? "تعطيل الرابط" : "تفعيل الرابط"}
                  </PetraButton>
                </div>
              </div>

              {/* As Of Date & Run Bar */}
              <div style={{ display: "flex", gap: "12px", alignItems: "flex-end", background: "var(--bg-app)", padding: "10px", borderRadius: "4px" }}>
                <PetraField label="تاريخ المطابقة (As Of Date)">
                  <input
                    type="date"
                    className="petra-input"
                    value={asOfDate}
                    onChange={(e) => setAsOfDate(e.target.value)}
                    style={{ padding: "5px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
                  />
                </PetraField>
                <PetraButton
                  variant="primary"
                  size="sm"
                  onClick={() => selectedLink.id && runReconciliation(selectedLink.id)}
                  disabled={isLoadingRecon}
                >
                  {isLoadingRecon ? "جارٍ المطابقة..." : "إعادة تشغيل المطابقة"}
                </PetraButton>
              </div>

              {/* Reconciliation Status & KPI Cards */}
              {isLoadingRecon ? (
                <PetraLoadingSkeleton rows={6} />
              ) : reconciliation ? (
                <div style={{ display: "flex", flexDirection: "column", gap: "16px" }}>
                  <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr", gap: "12px" }}>
                    {/* This Side */}
                    <div style={{ background: "#F8FAFC", border: "1px solid var(--border)", padding: "12px", borderRadius: "4px" }}>
                      <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
                        رصيد شركتنا ({reconciliation.thisSide?.databaseName ?? "شركتنا"})
                      </div>
                      <div style={{ fontSize: "1.25rem", fontWeight: "bold", fontFamily: "var(--font-mono, monospace)", color: "var(--header-bg)", marginTop: "4px" }}>
                        {reconciliation.thisSide?.balance ?? "0.00"} {reconciliation.currencyCode}
                      </div>
                      <div style={{ fontSize: "0.75rem", color: "var(--text-muted)", marginTop: "2px" }}>
                        حساب: {selectedLink.accountCode}
                      </div>
                    </div>

                    {/* Counterparty Side */}
                    <div style={{ background: "#F8FAFC", border: "1px solid var(--border)", padding: "12px", borderRadius: "4px" }}>
                      <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
                        رصيد الطرف المقابل ({selectedLink.counterpartyCompanyName ?? reconciliation.counterpartySide?.databaseName ?? "الشركة الشقيقة"})
                      </div>
                      <div style={{ fontSize: "1.25rem", fontWeight: "bold", fontFamily: "var(--font-mono, monospace)", color: "var(--header-bg)", marginTop: "4px" }}>
                        {reconciliation.counterpartySide?.balance ?? "0.00"} {reconciliation.currencyCode}
                      </div>
                      <div style={{ fontSize: "0.75rem", color: "var(--text-muted)", marginTop: "2px" }}>
                        حساب: {selectedLink.counterpartyAccountCode}
                      </div>
                    </div>

                    {/* Difference / Match */}
                    <div style={{
                      background: reconciliation.matched ? "#E8F5E9" : "#FFEBEE",
                      border: `1px solid ${reconciliation.matched ? "#A5D6A7" : "#EF9A9A"}`,
                      padding: "12px",
                      borderRadius: "4px",
                    }}>
                      <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>حالة المطابقة والفارق</div>
                      <div style={{
                        fontSize: "1.25rem",
                        fontWeight: "bold",
                        fontFamily: "var(--font-mono, monospace)",
                        color: reconciliation.matched ? "#2E7D32" : "#C62828",
                        marginTop: "4px",
                      }}>
                        {reconciliation.matched ? "متطابق تماماً (0.00)" : `فارق: ${reconciliation.difference ?? "0.00"}`}
                      </div>
                      {reconciliation.warningCode && (
                        <div style={{ fontSize: "0.75rem", color: "#C62828", marginTop: "2px" }}>
                          تحذير: {reconciliation.warningCode}
                        </div>
                      )}
                    </div>
                  </div>

                  {/* Unmatched Movements (if any) */}
                  {reconciliation.matching && (
                    <div style={{ display: "flex", flexDirection: "column", gap: "12px" }}>
                      <div style={{ fontWeight: "bold", fontSize: "0.9rem", color: "var(--header-bg)" }}>
                        تفاصيل الحركات غير المسواة:
                      </div>

                      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "12px" }}>
                        {/* Unmatched This */}
                        <div style={{ border: "1px solid var(--border)", borderRadius: "4px", padding: "8px" }}>
                          <div style={{ fontSize: "0.8rem", fontWeight: "bold", color: "#37474F", marginBottom: "6px" }}>
                            حركات غير مسواة طرفنا ({reconciliation.matching.unmatchedThis?.length ?? 0}):
                          </div>
                          {(reconciliation.matching.unmatchedThis?.length ?? 0) === 0 ? (
                            <div style={{ fontSize: "0.8rem", color: "#2E7D32" }}>لا توجد حركات معلقة</div>
                          ) : (
                            reconciliation.matching.unmatchedThis?.map((m, i) => (
                              <div key={i} style={{ fontSize: "0.8rem", padding: "4px 0", borderBottom: "1px solid #ECEFF1", display: "flex", justifyContent: "space-between" }}>
                                <span>{m.description || "حركة قيد"} ({m.date})</span>
                                <strong style={{ fontFamily: "var(--font-mono, monospace)" }}>{m.amount}</strong>
                              </div>
                            ))
                          )}
                        </div>

                        {/* Unmatched Counterparty */}
                        <div style={{ border: "1px solid var(--border)", borderRadius: "4px", padding: "8px" }}>
                          <div style={{ fontSize: "0.8rem", fontWeight: "bold", color: "#37474F", marginBottom: "6px" }}>
                            حركات غير مسواة الطرف المقابل ({reconciliation.matching.unmatchedCounterparty?.length ?? 0}):
                          </div>
                          {(reconciliation.matching.unmatchedCounterparty?.length ?? 0) === 0 ? (
                            <div style={{ fontSize: "0.8rem", color: "#2E7D32" }}>لا توجد حركات معلقة</div>
                          ) : (
                            reconciliation.matching.unmatchedCounterparty?.map((m, i) => (
                              <div key={i} style={{ fontSize: "0.8rem", padding: "4px 0", borderBottom: "1px solid #ECEFF1", display: "flex", justifyContent: "space-between" }}>
                                <span>{m.description || "حركة قيد"} ({m.date})</span>
                                <strong style={{ fontFamily: "var(--font-mono, monospace)" }}>{m.amount}</strong>
                              </div>
                            ))
                          )}
                        </div>
                      </div>
                    </div>
                  )}
                </div>
              ) : null}
            </div>
          ) : (
            <PetraEmptyState title="اختر رابطاً من القائمة لعرض تفاصيل المطابقة" />
          )}
        </div>
      )}

      {/* Add Link Dialog */}
      <PetraDialog
        open={isAddOpen}
        onClose={() => setIsAddOpen(false)}
        title="إنشاء رابط مالي بين الشركات الشقيقة"
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "12px" }}>
          <PetraField label="معرف الشركة المقابلة (Company UUID)">
            <input
              type="text"
              className="petra-input"
              value={newCounterpartyCompanyId}
              onChange={(e) => setNewCounterpartyCompanyId(e.target.value)}
              placeholder="00000000-0000-0000-0000-000000000000"
              style={{ width: "100%", padding: "6px 8px", border: "1px solid var(--border)", borderRadius: "3px" }}
            />
          </PetraField>

          <PetraField label="رمز الحساب في شركتنا (Account Code)">
            <input
              type="text"
              className="petra-input"
              value={newAccountCode}
              onChange={(e) => setNewAccountCode(e.target.value)}
              placeholder="مثال: 120501"
              style={{ width: "100%", padding: "6px 8px", border: "1px solid var(--border)", borderRadius: "3px" }}
            />
          </PetraField>

          <PetraField label="رمز الحساب المقابل في الشركة الشقيقة">
            <input
              type="text"
              className="petra-input"
              value={newCounterpartyAccountCode}
              onChange={(e) => setNewCounterpartyAccountCode(e.target.value)}
              placeholder="مثال: 220501"
              style={{ width: "100%", padding: "6px 8px", border: "1px solid var(--border)", borderRadius: "3px" }}
            />
          </PetraField>

          <PetraField label="العملة المشتركة للرابط">
            <input
              type="text"
              className="petra-input"
              value={newCurrencyCode}
              onChange={(e) => setNewCurrencyCode(e.target.value)}
              placeholder="SAR"
              style={{ width: "100%", padding: "6px 8px", border: "1px solid var(--border)", borderRadius: "3px" }}
            />
          </PetraField>

          <PetraField label="ملاحظات">
            <input
              type="text"
              className="petra-input"
              value={newNote}
              onChange={(e) => setNewNote(e.target.value)}
              style={{ width: "100%", padding: "6px 8px", border: "1px solid var(--border)", borderRadius: "3px" }}
            />
          </PetraField>

          <div style={{ display: "flex", justifyContent: "flex-end", gap: "8px", marginTop: "8px" }}>
            <PetraButton variant="secondary" onClick={() => setIsAddOpen(false)}>
              إلغاء
            </PetraButton>
            <PetraButton variant="primary" onClick={handleSaveLink} disabled={isSaving}>
              {isSaving ? "جارٍ الحفظ..." : "حفظ الرابط"}
            </PetraButton>
          </div>
        </div>
      </PetraDialog>
    </div>
  );
}
