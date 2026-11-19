import { useState } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { useWorkspace } from "@/workspace/workspaceStore";
import {
  PetraButton,
  PetraField,
  PetraErrorDetails,
  PetraDialog,
} from "@/components/ui";
import { toast } from "@/components/ui/toastStore";
import type { components } from "@/services/api/schema";

export interface AlAmeenMigrationScreenProps {
  params?: Readonly<Record<string, string>> | undefined;
}

type MigrationPreviewResponse = components["schemas"]["MigrationPreviewResponse"];
type MigrationExecuteResponse = components["schemas"]["MigrationExecuteResponse"];
type MigrationReconciliationResponse = components["schemas"]["MigrationReconciliationResponse"];
type MigrationCheckDto = components["schemas"]["MigrationCheckDto"];
type MigrationActivationResponse = components["schemas"]["MigrationActivationResponse"];

export default function AlAmeenMigrationScreen({ params: _params }: AlAmeenMigrationScreenProps) {
  const api = useApi();
  const { companyId } = useSession((s) => ({ companyId: s.companyId }));
  const openWorkspaceWindow = useWorkspace((s) => s.open);

  const [step, setStep] = useState<"preview" | "execute" | "reconcile" | "activate">("preview");

  // Input Parameters
  const [sourceConnectionString, setSourceConnectionString] = useState<string>(
    "Server=localhost;Database=AlAmeen_Source_2026;Integrated Security=true;TrustServerCertificate=true;"
  );
  const [targetFiscalYearCode, setTargetFiscalYearCode] = useState<string>("2026");
  const [targetFiscalYearStart, setTargetFiscalYearStart] = useState<string>("2026-01-01");
  const [targetFiscalYearEnd, setTargetFiscalYearEnd] = useState<string>("2026-12-31");
  const [targetBaseCurrencyCode, setTargetBaseCurrencyCode] = useState<string>("SAR");
  const [mode, setMode] = useState<"FastCutover" | "FullHistorical">("FastCutover");
  const [cutoffDate, setCutoffDate] = useState<string>("");

  // Step Results
  const [previewResult, setPreviewResult] = useState<MigrationPreviewResponse | null>(null);
  const [executeResult, setExecuteResult] = useState<MigrationExecuteResponse | null>(null);
  const [reconcileResult, setReconcileResult] = useState<MigrationReconciliationResponse | null>(null);
  const [activationResult, setActivationResult] = useState<MigrationActivationResponse | null>(null);

  // Loading & Error States
  const [isLoading, setIsLoading] = useState<boolean>(false);
  const [error, setError] = useState<{ status?: number; message?: string } | null>(null);

  // Execute / Activate Password Dialog
  const [passwordModalOpen, setPasswordModalOpen] = useState<boolean>(false);
  const [adminPassword, setAdminPassword] = useState<string>("Admin123$");
  const [passwordAction, setPasswordAction] = useState<"execute" | "activate">("execute");

  const handlePreview = async () => {
    if (!companyId || !sourceConnectionString.trim()) {
      toast.warning("يرجى إدخال نص الاتصال بقاعدة بيانات الأمين المصدر");
      return;
    }

    setIsLoading(true);
    setError(null);
    setPreviewResult(null);

    try {
      const res = await api.POST("/api/v1/migration/alameen/preview", {
        body: {
          sourceConnectionString: sourceConnectionString.trim(),
          targetCompanyId: companyId,
          targetFiscalYearCode,
          targetFiscalYearStart,
          targetFiscalYearEnd,
          targetBaseCurrencyCode,
          mode,
          cutoffDate: cutoffDate || null,
        },
      });

      if (res.error || !res.data) {
        setError({
          status: res.response?.status,
          message: (res.error as { title?: string; detail?: string })?.detail ?? "فشل الاتصال ومعاينة بيانات الأمين المصدر",
        });
        return;
      }

      setPreviewResult(res.data);
      setStep("execute");
      toast.success(`تم فحص ومعاينة قاعدة بيانات الأمين بنجاح (${res.data.accountsCount} حساب، ${res.data.itemsCount} مادة)`);
    } catch (err) {
      setError({
        message: err instanceof Error ? err.message : "خطأ غير متوقع أثناء معاينة الهجرة",
      });
    } finally {
      setIsLoading(false);
    }
  };

  const handleExecute = async () => {
    if (!previewResult?.migrationRunId || !adminPassword.trim()) {
      toast.warning("يرجى إدخال كلمة مرور مدير النظام للتأكيد");
      return;
    }

    setIsLoading(true);
    setError(null);
    setPasswordModalOpen(false);

    try {
      const res = await api.POST("/api/v1/migration/alameen/execute", {
        body: {
          migrationRunId: previewResult.migrationRunId,
          administratorPassword: adminPassword,
          notes: "ترحيل بيانات الأمين 10 إلى بتراء ERP مع عزل قاعدة البيانات",
        },
      });

      if (res.error || !res.data) {
        setError({
          status: res.response?.status,
          message: (res.error as { title?: string; detail?: string })?.detail ?? "فشل تنفيذ عملية الهجرة",
        });
        return;
      }

      setExecuteResult(res.data);
      setStep("reconcile");
      toast.success(`تم تنفيذ الهجرة بنجاح إلى قاعدة البيانات: ${res.data.targetDatabaseName}`);
      handleReconcile(previewResult.migrationRunId);
    } catch (err) {
      setError({
        message: err instanceof Error ? err.message : "خطأ أثناء تنفيذ الهجرة",
      });
    } finally {
      setIsLoading(false);
    }
  };

  const handleReconcile = async (runId?: string) => {
    const id = runId || previewResult?.migrationRunId;
    if (!id) return;

    setIsLoading(true);
    setError(null);

    try {
      const res = await api.GET("/api/v1/migration/alameen/{migrationRunId}/reconciliation", {
        params: { path: { migrationRunId: id } },
      });

      if (res.error || !res.data) {
        setError({
          status: res.response?.status,
          message: (res.error as { title?: string; detail?: string })?.detail ?? "فشل فحص المطابقة المحاسبية",
        });
        return;
      }

      setReconcileResult(res.data);
      if (res.data.passed) {
        toast.success("✓ اجتازت الهجرة كافة فحوصات المطابقة المحاسبية الـ 14 بنسبة 100%");
      } else {
        toast.warning("⚠ تم رصد فوارق محاسبية بين المصدر والهدف في بعض الفحوصات");
      }
    } catch (err) {
      setError({
        message: err instanceof Error ? err.message : "خطأ أثناء فحص المطابقة",
      });
    } finally {
      setIsLoading(false);
    }
  };

  const handleActivate = async () => {
    if (!previewResult?.migrationRunId || !adminPassword.trim()) {
      toast.warning("يرجى إدخال كلمة مرور مدير النظام للتأكيد");
      return;
    }

    setIsLoading(true);
    setError(null);
    setPasswordModalOpen(false);

    try {
      const res = await api.POST("/api/v1/migration/alameen/activate", {
        body: {
          migrationRunId: previewResult.migrationRunId,
          administratorPassword: adminPassword,
        },
      });

      if (res.error || !res.data) {
        setError({
          status: res.response?.status,
          message: (res.error as { title?: string; detail?: string })?.detail ?? "فشل تفعيل مساحة العمل المهجرة",
        });
        return;
      }

      setActivationResult(res.data);
      setStep("activate");
      toast.success("✓ تم تفعيل الشركة والسنة المالية المهجرة بنجاح في بتراء ERP");
    } catch (err) {
      setError({
        message: err instanceof Error ? err.message : "خطأ أثناء تفعيل الهجرة",
      });
    } finally {
      setIsLoading(false);
    }
  };

  const exportReconciliationTsv = () => {
    if (!reconcileResult?.checks) return;
    const headers = ["فحص المطابقة", "قيمة الأمين (المصدر)", "قيمة بتراء (الهدف)", "الفارق", "الحالة", "التفاصيل"];
    const rows = reconcileResult.checks.map((c: MigrationCheckDto) => [
      c.checkName,
      c.sourceValue,
      c.targetValue,
      c.difference,
      c.passed ? "مطابق ✓" : "غير مطابق ⚠",
      c.details ?? "",
    ]);

    const tsvContent = [headers.join("\t"), ...rows.map((row) => row.join("\t"))].join("\n");
    const blob = new Blob(["\uFEFF" + tsvContent], { type: "text/tab-separated-values;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `alameen-reconciliation-${previewResult?.migrationRunId}.tsv`;
    a.click();
    URL.revokeObjectURL(url);
    toast.success("تم تصدير تقرير مطابقة الهجرة بنجاح");
  };

  return (
    <div className="alameen-migration-screen" style={{ padding: "16px", height: "100%", display: "flex", flexDirection: "column", gap: "16px", overflowY: "auto" }}>
      {/* Header */}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", borderBottom: "1px solid var(--border)", paddingBottom: "12px" }}>
        <div>
          <h2 style={{ margin: 0, fontSize: "1.25rem", color: "var(--header-bg)" }}>
            معالج هجرة واستيراد بيانات الأمين (Al-Ameen 10 Migration Import)
          </h2>
          <p style={{ margin: "4px 0 0", fontSize: "0.85rem", color: "var(--text-muted)" }}>
            استيراد آلي معزول لقواعد بيانات الأمين 10، التحقق الهيكلي، فحص المطابقة عبر 14 طبقة، والتفعيل النهائي.
          </p>
        </div>
      </div>

      {/* Progress Steps Header */}
      <div style={{ display: "flex", gap: "8px", background: "var(--bg-surface)", border: "1px solid var(--border)", borderRadius: "4px", padding: "12px" }}>
        {[
          { key: "preview", label: "1. فحص ومعاينة المصدر" },
          { key: "execute", label: "2. تنفيذ الهجرة المعزولة" },
          { key: "reconcile", label: "3. المطابقة المحاسبية (14 فحصاً)" },
          { key: "activate", label: "4. تفعيل مساحة العمل" },
        ].map((s, _idx) => {
          const isActive = step === s.key;
          return (
            <div
              key={s.key}
              style={{
                flex: 1,
                textAlign: "center",
                padding: "8px 12px",
                borderRadius: "3px",
                background: isActive ? "var(--primary)" : "#F3F4F6",
                color: isActive ? "#FFFFFF" : "var(--text-muted)",
                fontWeight: isActive ? "bold" : "normal",
                fontSize: "0.85rem",
              }}
            >
              {s.label}
            </div>
          );
        })}
      </div>

      {error && <PetraErrorDetails status={error.status} message={error.message} />}

      {/* STEP 1: Parameters & Preview */}
      <div style={{ background: "var(--bg-surface)", border: "1px solid var(--border)", borderRadius: "4px", padding: "16px", display: "flex", flexDirection: "column", gap: "16px" }}>
        <h3 style={{ margin: 0, fontSize: "1rem", color: "var(--header-bg)" }}>
          إعدادات اتصال قاعدة بيانات الأمين المصدر والهدف
        </h3>

        <PetraField label="نص الاتصال بقاعدة بيانات الأمين المصدر (SQL Server Connection String) *">
          <input
            type="text"
            className="petra-input"
            value={sourceConnectionString}
            onChange={(e) => setSourceConnectionString(e.target.value)}
            disabled={isLoading || step !== "preview"}
            style={{ width: "100%", padding: "6px 8px", fontSize: "0.8rem", fontFamily: "var(--font-mono, monospace)", border: "1px solid var(--border)", borderRadius: "3px" }}
          />
        </PetraField>

        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr 1fr", gap: "12px" }}>
          <PetraField label="رمز السنة المالية الهدف">
            <input
              type="text"
              className="petra-input"
              value={targetFiscalYearCode}
              onChange={(e) => setTargetFiscalYearCode(e.target.value)}
              disabled={isLoading || step !== "preview"}
              style={{ width: "100%", padding: "6px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
            />
          </PetraField>

          <PetraField label="تاريخ بداية السنة">
            <input
              type="date"
              className="petra-input"
              value={targetFiscalYearStart}
              onChange={(e) => setTargetFiscalYearStart(e.target.value)}
              disabled={isLoading || step !== "preview"}
              style={{ width: "100%", padding: "6px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
            />
          </PetraField>

          <PetraField label="تاريخ نهاية السنة">
            <input
              type="date"
              className="petra-input"
              value={targetFiscalYearEnd}
              onChange={(e) => setTargetFiscalYearEnd(e.target.value)}
              disabled={isLoading || step !== "preview"}
              style={{ width: "100%", padding: "6px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
            />
          </PetraField>

          <PetraField label="العملة الأساسية">
            <input
              type="text"
              className="petra-input"
              value={targetBaseCurrencyCode}
              onChange={(e) => setTargetBaseCurrencyCode(e.target.value)}
              disabled={isLoading || step !== "preview"}
              style={{ width: "100%", padding: "6px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
            />
          </PetraField>
        </div>

        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "12px" }}>
          <PetraField label="نمط الهجرة (Migration Mode)">
            <select
              className="petra-select"
              value={mode}
              onChange={(e) => setMode(e.target.value as any)}
              disabled={isLoading || step !== "preview"}
              style={{ width: "100%", padding: "6px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
            >
              <option value="FastCutover">هجرة سريعة بالأرصدة الافتتاحية والذمم المفتوحة (Fast Cutover)</option>
              <option value="FullHistorical">هجرة تاريخية كاملة مع كافة قيود وفواتير اليومية (Full Historical)</option>
            </select>
          </PetraField>

          <PetraField label="تاريخ القطع (Cutoff Date - اختياري)">
            <input
              type="date"
              className="petra-input"
              value={cutoffDate}
              onChange={(e) => setCutoffDate(e.target.value)}
              disabled={isLoading || step !== "preview"}
              style={{ width: "100%", padding: "6px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
            />
          </PetraField>
        </div>

        {step === "preview" && (
          <div style={{ display: "flex", justifyContent: "flex-end", marginTop: "8px" }}>
            <PetraButton variant="primary" onClick={handlePreview} disabled={isLoading}>
              {isLoading ? "جارٍ فحص ومعاينة قاعدة بيانات الأمين..." : "بدء فحص ومعاينة المصدر"}
            </PetraButton>
          </div>
        )}
      </div>

      {/* STEP 2: Preview Results & Execute Action */}
      {previewResult && (
        <div style={{ background: "var(--bg-surface)", border: "1px solid var(--border)", borderRadius: "4px", padding: "16px", display: "flex", flexDirection: "column", gap: "16px" }}>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
            <h3 style={{ margin: 0, fontSize: "1rem", color: "var(--header-bg)" }}>
              نتائج معاينة بيانات الأمين المصدر (معرف العملية: {previewResult.migrationRunId})
            </h3>
            {step === "execute" && (
              <PetraButton
                variant="primary"
                onClick={() => {
                  setPasswordAction("execute");
                  setPasswordModalOpen(true);
                }}
                disabled={isLoading}
              >
                تنفيذ الهجرة إلى قاعدة البيانات المعزولة
              </PetraButton>
            )}
          </div>

          <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(180px, 1fr))", gap: "12px" }}>
            <div style={{ background: "#F9FAFB", border: "1px solid #E5E7EB", padding: "12px", borderRadius: "4px" }}>
              <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>الحسابات المالية</div>
              <div style={{ fontSize: "1.3rem", fontWeight: "bold", color: "var(--primary)" }}>{previewResult.accountsCount}</div>
            </div>
            <div style={{ background: "#F9FAFB", border: "1px solid #E5E7EB", padding: "12px", borderRadius: "4px" }}>
              <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>الزبائن والموردون</div>
              <div style={{ fontSize: "1.3rem", fontWeight: "bold", color: "var(--primary)" }}>{previewResult.partiesCount}</div>
            </div>
            <div style={{ background: "#F9FAFB", border: "1px solid #E5E7EB", padding: "12px", borderRadius: "4px" }}>
              <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>بطاقات المواد</div>
              <div style={{ fontSize: "1.3rem", fontWeight: "bold", color: "var(--primary)" }}>{previewResult.itemsCount}</div>
            </div>
            <div style={{ background: "#F9FAFB", border: "1px solid #E5E7EB", padding: "12px", borderRadius: "4px" }}>
              <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>المستودعات</div>
              <div style={{ fontSize: "1.3rem", fontWeight: "bold", color: "var(--primary)" }}>{previewResult.warehousesCount}</div>
            </div>
            <div style={{ background: "#F9FAFB", border: "1px solid #E5E7EB", padding: "12px", borderRadius: "4px" }}>
              <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>الذمم المفتوحة (عملاء/موردون)</div>
              <div style={{ fontSize: "1.1rem", fontWeight: "bold" }}>
                {previewResult.customerOpenItemsCount + previewResult.supplierOpenItemsCount} بند
              </div>
            </div>
            <div style={{ background: "#F9FAFB", border: "1px solid #E5E7EB", padding: "12px", borderRadius: "4px" }}>
              <div style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>تقييم بضاعة المخزون</div>
              <div style={{ fontSize: "1.1rem", fontWeight: "bold", fontFamily: "var(--font-mono, monospace)" }}>
                {previewResult.inventoryValuationTotal}
              </div>
            </div>
          </div>
        </div>
      )}

      {/* Execution Status Banner */}
      {executeResult && (
        <div style={{ background: "#E8F5E9", border: "1px solid #A5D6A7", padding: "12px 16px", borderRadius: "4px", fontSize: "0.85rem", color: "#1B5E20" }}>
          تم استكمال الهجرة المعزولة لقاعدة البيانات المؤقتة: <strong>{executeResult.targetDatabaseName}</strong> (معرف العملية: {executeResult.migrationRunId})
        </div>
      )}

      {/* STEP 3: Reconciliation Results Table */}
      {reconcileResult && (
        <div style={{ background: "var(--bg-surface)", border: "1px solid var(--border)", borderRadius: "4px", padding: "16px", display: "flex", flexDirection: "column", gap: "16px" }}>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
            <div>
              <h3 style={{ margin: 0, fontSize: "1rem", color: "var(--header-bg)" }}>
                فحص المطابقة المحاسبية الشامل (14 فحص مطابقة بين الأمين وبتراء)
              </h3>
              <div style={{ fontSize: "0.85rem", marginTop: "4px", color: reconcileResult.passed ? "#2E7D32" : "#C62828", fontWeight: "bold" }}>
                {reconcileResult.passed ? "✓ كافة الفحوصات متطابقة تماماً (صفر فوارق)" : "⚠ توجد فوارق محاسبية تحتاج مراجعة"}
              </div>
            </div>
            <div style={{ display: "flex", gap: "8px" }}>
              <PetraButton variant="secondary" onClick={exportReconciliationTsv} size="sm">
                تصدير TSV
              </PetraButton>
              {step !== "activate" && (
                <PetraButton
                  variant="primary"
                  onClick={() => {
                    setPasswordAction("activate");
                    setPasswordModalOpen(true);
                  }}
                  disabled={isLoading || !reconcileResult.passed}
                  size="sm"
                >
                  اعتماد وتفعيل مساحة العمل المهجرة
                </PetraButton>
              )}
            </div>
          </div>

          <div style={{ overflowX: "auto", border: "1px solid var(--border)", borderRadius: "4px" }}>
            <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.85rem", textAlign: "right" }}>
              <thead>
                <tr style={{ background: "var(--header-bg)", color: "#FFFFFF" }}>
                  <th style={{ padding: "8px" }}>فحص المطابقة</th>
                  <th style={{ padding: "8px" }}>قيمة الأمين (المصدر)</th>
                  <th style={{ padding: "8px" }}>قيمة بتراء (الهدف)</th>
                  <th style={{ padding: "8px" }}>الفارق المحاسبي</th>
                  <th style={{ padding: "8px", textAlign: "center" }}>حالة المطابقة</th>
                </tr>
              </thead>
              <tbody>
                {reconcileResult.checks.map((c: MigrationCheckDto, idx: number) => (
                  <tr
                    key={idx}
                    style={{
                      borderBottom: "1px solid var(--border)",
                      background: idx % 2 === 0 ? "transparent" : "var(--bg-app)",
                    }}
                  >
                    <td style={{ padding: "8px", fontWeight: "bold" }}>{c.checkName}</td>
                    <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)" }}>{c.sourceValue}</td>
                    <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)" }}>{c.targetValue}</td>
                    <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)", color: c.passed ? "#2E7D32" : "#C62828", fontWeight: "bold" }}>
                      {c.difference}
                    </td>
                    <td style={{ padding: "8px", textAlign: "center" }}>
                      <span
                        style={{
                          padding: "2px 8px",
                          borderRadius: "3px",
                          fontSize: "0.75rem",
                          fontWeight: "bold",
                          background: c.passed ? "#E8F5E9" : "#FFEBEE",
                          color: c.passed ? "#2E7D32" : "#C62828",
                        }}
                      >
                        {c.passed ? "مطابق ✓" : "غير مطابق ⚠"}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* STEP 4: Activation Success Banner */}
      {activationResult && (
        <div style={{ background: "#E8F5E9", border: "1px solid #A5D6A7", borderRadius: "4px", padding: "16px", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
          <div>
            <div style={{ fontWeight: "bold", color: "#2E7D32", fontSize: "1rem" }}>
              ✓ تم اعتماد وتفعيل مساحة العمل المهجرة من الأمين بنجاح!
            </div>
            <div style={{ fontSize: "0.85rem", color: "#1B5E20", marginTop: "4px" }}>
              {activationResult.message}
            </div>
          </div>
          <PetraButton
            variant="primary"
            onClick={() => {
              openWorkspaceWindow({
                key: "accounts",
                title: "شجرة الحسابات",
                screen: "accounts",
              });
            }}
          >
            فتح دليل الحسابات المهجر
          </PetraButton>
        </div>
      )}

      {/* Administrator Password Confirmation Dialog */}
      <PetraDialog
        open={passwordModalOpen}
        onClose={() => setPasswordModalOpen(false)}
        title={passwordAction === "execute" ? "تأكيد تنفيذ الهجرة" : "تأكيد تفعيل مساحة العمل"}
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "16px" }}>
          <p style={{ margin: 0, fontSize: "0.85rem" }}>
            {passwordAction === "execute"
              ? "سيتم استخراج البيانات من الأمين وتحويلها وإيداعها في قاعدة بيانات SQL Server جديدة ومستقلة."
              : "سيتم اعتماد مساحة العمل وتفعيلها رسمياً للتشغيل والترحيل في بتراء ERP."}
          </p>

          <PetraField label="كلمة مرور مدير النظام (Administrator Password) *">
            <input
              type="password"
              className="petra-input"
              value={adminPassword}
              onChange={(e) => setAdminPassword(e.target.value)}
              style={{ width: "100%", padding: "6px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
            />
          </PetraField>

          <div style={{ display: "flex", justifyContent: "flex-end", gap: "8px" }}>
            <PetraButton variant="secondary" onClick={() => setPasswordModalOpen(false)}>
              إلغاء
            </PetraButton>
            <PetraButton
              variant="primary"
              onClick={passwordAction === "execute" ? handleExecute : handleActivate}
              disabled={isLoading || !adminPassword.trim()}
            >
              {isLoading ? "جارٍ المعالجة..." : "تأكيد ومتابعة"}
            </PetraButton>
          </div>
        </div>
      </PetraDialog>
    </div>
  );
}
