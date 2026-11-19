import { useState } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useWorkspace } from "@/workspace/workspaceStore";
import {
  PetraButton,
  PetraField,
  PetraErrorDetails,
} from "@/components/ui";
import { toast } from "@/components/ui/toastStore";
import type { components } from "@/services/api/schema";

export interface CompanyCreationWizardScreenProps {
  params?: Readonly<Record<string, string>> | undefined;
}

type ProvisioningOperationResponse = components["schemas"]["ProvisioningOperationResponse"];

export default function CompanyCreationWizardScreen({ params: _params }: CompanyCreationWizardScreenProps) {
  const api = useApi();
  const openWorkspaceWindow = useWorkspace((s) => s.open);

  const [code, setCode] = useState<string>("COMP-02");
  const [nameArabic, setNameArabic] = useState<string>("");
  const [nameEnglish, setNameEnglish] = useState<string>("");
  const [baseCurrencyCode, setBaseCurrencyCode] = useState<string>("SAR");
  const [initialYear, setInitialYear] = useState<number>(new Date().getFullYear());
  const [startDate, setStartDate] = useState<string>(`${new Date().getFullYear()}-01-01`);
  const [endDate, setEndDate] = useState<string>(`${new Date().getFullYear()}-12-31`);
  const [chartTemplateCode, setChartTemplateCode] = useState<string>("Standard");
  const [databaseName, setDatabaseName] = useState<string>("");

  const [isProvisioning, setIsProvisioning] = useState<boolean>(false);
  const [error, setError] = useState<{ status?: number; message?: string } | null>(null);
  const [result, setResult] = useState<ProvisioningOperationResponse | null>(null);

  const handleYearChange = (year: number) => {
    setInitialYear(year);
    setStartDate(`${year}-01-01`);
    setEndDate(`${year}-12-31`);
  };

  const handleCreateCompany = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!code.trim() || !nameArabic.trim() || !baseCurrencyCode.trim()) {
      toast.warning("يرجى ملء رمز الشركة واسمها العربي والعملة الأساسية");
      return;
    }

    setIsProvisioning(true);
    setError(null);
    setResult(null);

    try {
      const res = await api.POST("/api/v1/workspaces/companies", {
        body: {
          code: code.trim(),
          nameArabic: nameArabic.trim(),
          nameEnglish: nameEnglish.trim() || null,
          baseCurrencyCode: baseCurrencyCode.trim(),
          initialYear,
          startDate,
          endDate,
          chartTemplateCode,
          databaseName: databaseName.trim() || null,
        },
      });

      if (res.error || !res.data) {
        setError({
          status: res.response?.status,
          message: (res.error as { title?: string; detail?: string })?.detail ?? "فشل إنشاء الشركة وقاعدة البيانات",
        });
        return;
      }

      setResult(res.data);
      toast.success(`تم إنشاء الشركة وقاعدة البيانات بنجاح: ${res.data.targetDatabaseName}`);
    } catch (err) {
      setError({
        message: err instanceof Error ? err.message : "خطأ غير متوقع أثناء إنشاء الشركة",
      });
    } finally {
      setIsProvisioning(false);
    }
  };

  return (
    <div className="company-creation-wizard-screen" style={{ padding: "16px", height: "100%", display: "flex", flexDirection: "column", gap: "16px", overflowY: "auto" }}>
      {/* Header */}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", borderBottom: "1px solid var(--border)", paddingBottom: "12px" }}>
        <div>
          <h2 style={{ margin: 0, fontSize: "1.25rem", color: "var(--header-bg)" }}>
            معالج إنشاء شركة جديدة وتهيئة قاعدة البيانات (Company Creation Wizard)
          </h2>
          <p style={{ margin: "4px 0 0", fontSize: "0.85rem", color: "var(--text-muted)" }}>
            إنشاء كيان قانوني جديد، توليد قاعدة بيانات محاسبية معزولة، وتهيئة شجرة الحسابات والسنة المالية الأولى.
          </p>
        </div>
      </div>

      {result ? (
        /* Success Result Card */
        <div style={{ maxWidth: "700px", background: "var(--bg-surface)", border: "1px solid var(--border)", borderRadius: "4px", padding: "20px", display: "flex", flexDirection: "column", gap: "16px" }}>
          <div style={{ background: "#E8F5E9", border: "1px solid #A5D6A7", borderRadius: "4px", padding: "12px" }}>
            <h3 style={{ margin: 0, color: "#2E7D32", fontSize: "1.1rem" }}>
              ✓ تم إنشاء الشركة وتهيئة مساحة العمل بنجاح
            </h3>
            <p style={{ margin: "6px 0 0", fontSize: "0.85rem", color: "#1B5E20" }}>
              تم تأسيس الكيان القانوني وتهيئة قاعدة بيانات SQL Server المستقلة مع شجرة الحسابات والفترات المالية.
            </p>
          </div>

          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "12px", fontSize: "0.85rem" }}>
            <div>
              <strong>معرف العملية:</strong>{" "}
              <span style={{ fontFamily: "var(--font-mono, monospace)" }}>{result.operationId}</span>
            </div>
            <div>
              <strong>معرف الشركة:</strong>{" "}
              <span style={{ fontFamily: "var(--font-mono, monospace)" }}>{result.companyId}</span>
            </div>
            <div>
              <strong>قاعدة البيانات المستهدفة:</strong>{" "}
              <span style={{ fontFamily: "var(--font-mono, monospace)", color: "var(--primary)", fontWeight: "bold" }}>
                {result.targetDatabaseName}
              </span>
            </div>
            <div>
              <strong>حالة الجاهزية:</strong>{" "}
              <span style={{ color: "#2E7D32", fontWeight: "bold" }}>{result.readinessState ?? result.status}</span>
            </div>
          </div>

          {result.notice && (
            <div style={{ background: "#FEF3C7", border: "1px solid #FDE68A", padding: "8px 12px", borderRadius: "3px", fontSize: "0.8rem", color: "#92400E" }}>
              {result.notice}
            </div>
          )}

          <div style={{ display: "flex", gap: "8px", justifyContent: "flex-end", marginTop: "12px" }}>
            <PetraButton
              variant="secondary"
              onClick={() => {
                setResult(null);
                setCode(`COMP-${Date.now().toString().slice(-4)}`);
                setNameArabic("");
                setNameEnglish("");
              }}
            >
              إنشاء شركة أخرى
            </PetraButton>
            <PetraButton
              variant="primary"
              onClick={() => {
                openWorkspaceWindow({
                  key: "branches",
                  title: "إدارة وهيكلية الفروع",
                  screen: "branches",
                });
              }}
            >
              الانتقال لإعداد فروع الشركة
            </PetraButton>
          </div>
        </div>
      ) : (
        /* Form */
        <form onSubmit={handleCreateCompany} style={{ maxWidth: "750px", display: "flex", flexDirection: "column", gap: "16px" }}>
          {error && <PetraErrorDetails status={error.status} message={error.message} />}

          <div style={{ background: "var(--bg-surface)", border: "1px solid var(--border)", borderRadius: "4px", padding: "16px", display: "flex", flexDirection: "column", gap: "16px" }}>
            <h3 style={{ margin: 0, fontSize: "1rem", color: "var(--header-bg)" }}>
              1. البيانات الأساسية للشركة
            </h3>

            <div style={{ display: "grid", gridTemplateColumns: "1fr 2fr", gap: "12px" }}>
              <PetraField label="رمز الشركة (Code) *">
                <input
                  type="text"
                  className="petra-input"
                  value={code}
                  onChange={(e) => setCode(e.target.value)}
                  placeholder="مثال: COMP-02"
                  style={{ width: "100%", padding: "6px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
                  required
                />
              </PetraField>

              <PetraField label="اسم الشركة الرسمي بالعربية *">
                <input
                  type="text"
                  className="petra-input"
                  value={nameArabic}
                  onChange={(e) => setNameArabic(e.target.value)}
                  placeholder="مثال: شركة البتراء للتجارة العامة ذ.م.م"
                  style={{ width: "100%", padding: "6px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
                  required
                />
              </PetraField>
            </div>

            <div style={{ display: "grid", gridTemplateColumns: "2fr 1fr", gap: "12px" }}>
              <PetraField label="اسم الشركة بالإنجليزية (اختياري)">
                <input
                  type="text"
                  className="petra-input"
                  value={nameEnglish}
                  onChange={(e) => setNameEnglish(e.target.value)}
                  placeholder="مثال: Petra General Trading LLC"
                  style={{ width: "100%", padding: "6px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
                />
              </PetraField>

              <PetraField label="العملة الأساسية للدفاتر *">
                <select
                  className="petra-select"
                  value={baseCurrencyCode}
                  onChange={(e) => setBaseCurrencyCode(e.target.value)}
                  style={{ width: "100%", padding: "6px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
                  required
                >
                  <option value="SAR">SAR - ريال سعودي</option>
                  <option value="USD">USD - دولار أمريكي</option>
                  <option value="JOD">JOD - دينار أردني</option>
                  <option value="AED">AED - درهم إماراتي</option>
                  <option value="KWD">KWD - دينار كويتي</option>
                  <option value="SYP">SYP - ليرة سورية</option>
                </select>
              </PetraField>
            </div>
          </div>

          <div style={{ background: "var(--bg-surface)", border: "1px solid var(--border)", borderRadius: "4px", padding: "16px", display: "flex", flexDirection: "column", gap: "16px" }}>
            <h3 style={{ margin: 0, fontSize: "1rem", color: "var(--header-bg)" }}>
              2. إعدادات السنة المالية الأولى وقالب الحسابات
            </h3>

            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr", gap: "12px" }}>
              <PetraField label="السنة المالية الأولى *">
                <input
                  type="number"
                  className="petra-input"
                  value={initialYear}
                  onChange={(e) => handleYearChange(Number(e.target.value))}
                  style={{ width: "100%", padding: "6px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
                  required
                />
              </PetraField>

              <PetraField label="تاريخ بداية السنة *">
                <input
                  type="date"
                  className="petra-input"
                  value={startDate}
                  onChange={(e) => setStartDate(e.target.value)}
                  style={{ width: "100%", padding: "6px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
                  required
                />
              </PetraField>

              <PetraField label="تاريخ نهاية السنة *">
                <input
                  type="date"
                  className="petra-input"
                  value={endDate}
                  onChange={(e) => setEndDate(e.target.value)}
                  style={{ width: "100%", padding: "6px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
                  required
                />
              </PetraField>
            </div>

            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "12px" }}>
              <PetraField label="قالب شجرة الحسابات الافتراضي">
                <select
                  className="petra-select"
                  value={chartTemplateCode}
                  onChange={(e) => setChartTemplateCode(e.target.value)}
                  style={{ width: "100%", padding: "6px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
                >
                  <option value="Standard">الدليل المالي القياسي (Standard COA)</option>
                  <option value="Commercial">الدليل التجاري للمؤسسات (Commercial)</option>
                  <option value="Services">دليل الشركات الخدمية (Services)</option>
                  <option value="Empty">دليل فارغ (Empty - بدون حسابات مسبقة)</option>
                </select>
              </PetraField>

              <PetraField label="اسم قاعدة البيانات المعزولة (اختياري)">
                <input
                  type="text"
                  className="petra-input"
                  value={databaseName}
                  onChange={(e) => setDatabaseName(e.target.value)}
                  placeholder="تلقائي: PetraERP_Company_{code}_{year}"
                  style={{ width: "100%", padding: "6px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
                />
              </PetraField>
            </div>
          </div>

          <div style={{ display: "flex", justifyContent: "flex-end", gap: "8px" }}>
            <PetraButton
              variant="primary"
              type="submit"
              disabled={isProvisioning}
            >
              {isProvisioning ? "جارٍ إنشاء الشركة وقاعدة البيانات..." : "تأكيد وإنشاء الشركة (Provision Workspace)"}
            </PetraButton>
          </div>
        </form>
      )}
    </div>
  );
}
