import { useState, useEffect } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { PetraButton, PetraTabs, PetraLoadingSkeleton, PetraErrorDetails, PetraField } from "@/components/ui";
import { toast } from "@/components/ui/toastStore";

export interface PatternCardScreenProps {
  params?: Readonly<Record<string, string>> | undefined;
}

interface EffectiveData {
  patternId: string;
  revisionId: string;
  revisionNumber: number;
  operationKind: string;
  code: string;
  configuration: {
    schemaVersion?: number;
    voucher?: {
      formKind?: string;
      captionMode?: string;
      denyMutationsOnOrBefore?: string | null;
      perLineJournal?: boolean;
      postingMode?: string;
      autoPost?: boolean;
    } | null;
    defaultWarehouseId?: string | null;
    defaultCurrencyCode?: string | null;
    defaultCostCenterId?: string | null;
    accountMappings?: Array<{
      role?: string;
      accountId?: string;
      accountCode?: string;
      accountNameArabic?: string;
    }>;
    allowedPaymentMethods?: string[];
    allowedWarehouseIds?: string[];
    numberingPolicy?: {
      mode?: string;
      prefix?: string;
      padding?: number;
    };
    approvalPolicy?: {
      requiresApproval?: boolean;
      ceilingAmount?: string | null;
    };
    pricingPolicy?: {
      defaultPriceTier?: string;
    };
  };
}

interface EligibleAccount {
  id: string;
  code: string;
  nameArabic: string;
  nameEnglish?: string | null;
}

export default function PatternCardScreen({ params }: PatternCardScreenProps) {
  const patternId = params?.patternId;
  const api = useApi();
  const { companyId, branchId } = useSession((s) => ({ companyId: s.companyId, branchId: s.branchId }));

  const [activeTab, setActiveTab] = useState("effective");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<{ status?: number; message?: string } | null>(null);
  const [effective, setEffective] = useState<EffectiveData | null>(null);
  const [eligibleAccounts, setEligibleAccounts] = useState<EligibleAccount[]>([]);
  const [publishing, setPublishing] = useState(false);

  const loadData = async () => {
    if (!companyId || !branchId || !patternId) return;

    setLoading(true);
    setError(null);
    try {
      // 1. Fetch effective configuration
      const effRes = await api.GET("/api/v1/companies/{companyId}/branches/{branchId}/patterns/{patternId}/effective", {
        params: { path: { companyId, branchId, patternId } },
      });

      if (effRes.error) {
        setError({ status: effRes.response.status, message: "تعذر تحميل إعدادات النمط الفعالة" });
      } else if (effRes.data) {
        setEffective(effRes.data as unknown as EffectiveData);
      }

      // 2. Fetch eligible accounts
      const accRes = await api.GET("/api/v1/companies/{companyId}/branches/{branchId}/patterns/{patternId}/eligible-accounts", {
        params: { path: { companyId, branchId, patternId } },
      });

      if (accRes.data) {
        setEligibleAccounts(accRes.data as EligibleAccount[]);
      }
    } catch (err) {
      setError({ message: err instanceof Error ? err.message : "خطأ غير متوقع" });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadData();
  }, [companyId, branchId, patternId]);

  const handlePublish = async () => {
    if (!companyId || !branchId || !patternId) return;
    setPublishing(true);
    try {
      const res = await api.POST("/api/v1/companies/{companyId}/branches/{branchId}/patterns/{patternId}/publish", {
        params: { path: { companyId, branchId, patternId } },
        body: { reason: "نشر التعديلات من الواجهة المكتبية" },
      });
      if (res.error) {
        toast.error("فشل نشر النمط", (res.error as { detail?: string })?.detail);
      } else {
        toast.success("تم نشر النمط بنجاح");
        loadData();
      }
    } catch (err) {
      toast.error("خطأ أثناء النشر", err instanceof Error ? err.message : "خطأ غير متوقع");
    } finally {
      setPublishing(false);
    }
  };

  if (!patternId) {
    return <div style={{ padding: "var(--space-4)" }}>لم يتم تحديد معرّف النمط.</div>;
  }

  const voucher = effective?.configuration?.voucher;
  const numbering = effective?.configuration?.numberingPolicy;
  const approval = effective?.configuration?.approvalPolicy;
  const mappings = effective?.configuration?.accountMappings ?? [];

  return (
    <div style={{ display: "flex", flexDirection: "column", height: "100%", background: "var(--bg-app)" }}>
      {/* Top Action Bar */}
      <div
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          padding: "var(--space-3) var(--space-4)",
          background: "var(--bg-surface)",
          borderBottom: "1px solid var(--border)",
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: "var(--space-2)" }}>
          <h2 style={{ margin: 0, fontSize: "var(--fs-title)", fontWeight: 700 }}>
            بطاقة النمط: {effective?.code ?? params?.title ?? patternId}
          </h2>
          {effective?.revisionNumber ? (
            <span
              style={{
                fontSize: "var(--fs-caption)",
                padding: "2px 8px",
                borderRadius: 4,
                background: "var(--bg-sunken)",
                fontWeight: 600,
              }}
            >
              المراجعة #{effective.revisionNumber}
            </span>
          ) : null}
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: "var(--space-2)" }}>
          <PetraButton variant="secondary" onClick={loadData} loading={loading}>
            تحديث
          </PetraButton>
          <PetraButton variant="primary" onClick={handlePublish} loading={publishing}>
            نشر النمط (Publish)
          </PetraButton>
        </div>
      </div>

      {/* Tabs */}
      <PetraTabs
        tabs={[
          { id: "effective", label: "الإعدادات الفعالة (Effective)" },
          { id: "voucher", label: "سياسات السندات (Voucher Policies)" },
          { id: "accounts", label: `الحسابات المؤهلة (${eligibleAccounts.length})` },
          { id: "mappings", label: `ربط الحسابات (${mappings.length})` },
        ]}
        activeId={activeTab}
        onChange={setActiveTab}
      />

      {/* Main Body */}
      <div style={{ flex: 1, overflowY: "auto", padding: "var(--space-4)" }}>
        {loading ? (
          <PetraLoadingSkeleton rows={6} />
        ) : error ? (
          <PetraErrorDetails status={error.status} message={error.message} onRetry={loadData} />
        ) : !effective ? (
          <div>لم يتم العثور على بيانات النمط.</div>
        ) : activeTab === "effective" ? (
          <div
            style={{
              background: "var(--bg-surface)",
              border: "1px solid var(--border)",
              borderRadius: "var(--radius)",
              padding: "var(--space-4)",
              display: "grid",
              gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
              gap: "var(--space-4)",
            }}
          >
            <div>
              <h4 style={{ margin: "0 0 var(--space-3) 0", color: "var(--primary)" }}>البيانات الأساسية</h4>
              <PetraField label="كود النمط">
                <input
                  type="text"
                  readOnly
                  value={effective.code}
                  style={{
                    height: "var(--control-height)",
                    padding: "0 var(--space-2)",
                    borderRadius: "var(--radius)",
                    border: "1px solid var(--border)",
                    background: "var(--bg-sunken)",
                    color: "var(--text)",
                  }}
                />
              </PetraField>

              <PetraField label="نوع العملية (Operation Kind)">
                <input
                  type="text"
                  readOnly
                  value={effective.operationKind}
                  style={{
                    height: "var(--control-height)",
                    padding: "0 var(--space-2)",
                    borderRadius: "var(--radius)",
                    border: "1px solid var(--border)",
                    background: "var(--bg-sunken)",
                    color: "var(--text)",
                  }}
                />
              </PetraField>

              <PetraField label="العملة الافتراضية">
                <input
                  type="text"
                  readOnly
                  value={effective.configuration.defaultCurrencyCode ?? "العملة الأساسية للشركة"}
                  style={{
                    height: "var(--control-height)",
                    padding: "0 var(--space-2)",
                    borderRadius: "var(--radius)",
                    border: "1px solid var(--border)",
                    background: "var(--bg-sunken)",
                    color: "var(--text)",
                  }}
                />
              </PetraField>
            </div>

            <div>
              <h4 style={{ margin: "0 0 var(--space-3) 0", color: "var(--primary)" }}>سياسة الترقيم والموافقة</h4>
              <PetraField label="طريقة الترقيم">
                <input
                  type="text"
                  readOnly
                  value={numbering?.mode ?? "تلقائي"}
                  style={{
                    height: "var(--control-height)",
                    padding: "0 var(--space-2)",
                    borderRadius: "var(--radius)",
                    border: "1px solid var(--border)",
                    background: "var(--bg-sunken)",
                    color: "var(--text)",
                  }}
                />
              </PetraField>

              <PetraField label="بادئة الترقيم (Prefix)">
                <input
                  type="text"
                  readOnly
                  value={numbering?.prefix ?? "—"}
                  style={{
                    height: "var(--control-height)",
                    padding: "0 var(--space-2)",
                    borderRadius: "var(--radius)",
                    border: "1px solid var(--border)",
                    background: "var(--bg-sunken)",
                    color: "var(--text)",
                  }}
                />
              </PetraField>

              <PetraField label="سقف الموافقة">
                <input
                  type="text"
                  readOnly
                  value={approval?.ceilingAmount ?? "بلا سقف"}
                  style={{
                    height: "var(--control-height)",
                    padding: "0 var(--space-2)",
                    borderRadius: "var(--radius)",
                    border: "1px solid var(--border)",
                    background: "var(--bg-sunken)",
                    color: "var(--text)",
                  }}
                />
              </PetraField>
            </div>
          </div>
        ) : activeTab === "voucher" ? (
          <div
            style={{
              background: "var(--bg-surface)",
              border: "1px solid var(--border)",
              borderRadius: "var(--radius)",
              padding: "var(--space-4)",
            }}
          >
            <h4 style={{ margin: "0 0 var(--space-3) 0", color: "var(--primary)" }}>سياسة السند (Voucher Policy)</h4>
            {voucher ? (
              <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))", gap: "var(--space-4)" }}>
                <PetraField label="شكل السند (VoucherFormKind)">
                  <input
                    type="text"
                    readOnly
                    value={voucher.formKind ?? "GenericVoucher"}
                    style={{
                      height: "var(--control-height)",
                      padding: "0 var(--space-2)",
                      borderRadius: "var(--radius)",
                      border: "1px solid var(--border)",
                      background: "var(--bg-sunken)",
                      color: "var(--text)",
                    }}
                  />
                </PetraField>

                <PetraField label="وضع الترحيل (Posting Mode)">
                  <input
                    type="text"
                    readOnly
                    value={voucher.postingMode ?? "InheritFromCompany"}
                    style={{
                      height: "var(--control-height)",
                      padding: "0 var(--space-2)",
                      borderRadius: "var(--radius)",
                      border: "1px solid var(--border)",
                      background: "var(--bg-sunken)",
                      color: "var(--text)",
                    }}
                  />
                </PetraField>

                <PetraField label="تاريخ منع التعديل (Deny Mutations On Or Before)">
                  <input
                    type="text"
                    readOnly
                    value={voucher.denyMutationsOnOrBefore ?? "غير محدد"}
                    style={{
                      height: "var(--control-height)",
                      padding: "0 var(--space-2)",
                      borderRadius: "var(--radius)",
                      border: "1px solid var(--border)",
                      background: "var(--bg-sunken)",
                      color: "var(--text)",
                    }}
                  />
                </PetraField>

                <PetraField label="قيد لكل قلم (PerLineJournal)">
                  <input
                    type="text"
                    readOnly
                    value={voucher.perLineJournal ? "مفعّل" : "معطل (مؤجل حتى اكتمال اختبار المطابقة)"}
                    style={{
                      height: "var(--control-height)",
                      padding: "0 var(--space-2)",
                      borderRadius: "var(--radius)",
                      border: "1px solid var(--border)",
                      background: "var(--bg-sunken)",
                      color: "var(--text)",
                    }}
                  />
                </PetraField>
              </div>
            ) : (
              <p style={{ color: "var(--text-muted)" }}>لا توجد سياسة سندات مخصصة لهذا النمط.</p>
            )}
          </div>
        ) : activeTab === "accounts" ? (
          <div
            style={{
              background: "var(--bg-surface)",
              border: "1px solid var(--border)",
              borderRadius: "var(--radius)",
              overflow: "hidden",
            }}
          >
            <div
              style={{
                display: "grid",
                gridTemplateColumns: "140px 1fr",
                padding: "var(--space-2) var(--space-4)",
                background: "var(--bg-sunken)",
                fontWeight: 600,
                borderBottom: "1px solid var(--border)",
              }}
            >
              <span>كود الحساب</span>
              <span>اسم الحساب</span>
            </div>
            {eligibleAccounts.length === 0 ? (
              <div style={{ padding: "var(--space-4)", textAlign: "center", color: "var(--text-muted)" }}>
                لا توجد حسابات مؤهلة محددة
              </div>
            ) : (
              eligibleAccounts.map((acc) => (
                <div
                  key={acc.id}
                  style={{
                    display: "grid",
                    gridTemplateColumns: "140px 1fr",
                    padding: "var(--space-2) var(--space-4)",
                    borderBottom: "1px solid var(--divider)",
                  }}
                >
                  <span className="numeric">{acc.code}</span>
                  <span>{acc.nameArabic}</span>
                </div>
              ))
            )}
          </div>
        ) : (
          <div
            style={{
              background: "var(--bg-surface)",
              border: "1px solid var(--border)",
              borderRadius: "var(--radius)",
              overflow: "hidden",
            }}
          >
            <div
              style={{
                display: "grid",
                gridTemplateColumns: "200px 140px 1fr",
                padding: "var(--space-2) var(--space-4)",
                background: "var(--bg-sunken)",
                fontWeight: 600,
                borderBottom: "1px solid var(--border)",
              }}
            >
              <span>الدور المحاسبي</span>
              <span>كود الحساب</span>
              <span>اسم الحساب</span>
            </div>
            {mappings.length === 0 ? (
              <div style={{ padding: "var(--space-4)", textAlign: "center", color: "var(--text-muted)" }}>
                لا توجد ربط حسابات مخصصة
              </div>
            ) : (
              mappings.map((m, i) => (
                <div
                  key={i}
                  style={{
                    display: "grid",
                    gridTemplateColumns: "200px 140px 1fr",
                    padding: "var(--space-2) var(--space-4)",
                    borderBottom: "1px solid var(--divider)",
                  }}
                >
                  <span style={{ fontWeight: 600 }}>{m.role ?? "—"}</span>
                  <span className="numeric">{m.accountCode ?? "—"}</span>
                  <span>{m.accountNameArabic ?? "—"}</span>
                </div>
              ))
            )}
          </div>
        )}
      </div>
    </div>
  );
}
