import { useState, useEffect } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import {
  PetraButton,
  PetraField,
  PetraDialog,
  PetraEmptyState,
  PetraLoadingSkeleton,
  PetraErrorDetails,
} from "@/components/ui";
import { toast } from "@/components/ui/toastStore";
import type { components } from "@/services/api/schema";

export interface DocumentNumbersScreenProps {
  params?: Readonly<Record<string, string>> | undefined;
}

type DocumentNumberChangeDto = components["schemas"]["DocumentNumberChangeDto"];
type DocumentNumberAuditDto = components["schemas"]["DocumentNumberAuditDto"];
type DocumentNumberPreviewResponse = components["schemas"]["DocumentNumberPreviewResponse"];
type DocumentNumberChainVerificationResponse = components["schemas"]["DocumentNumberChainVerificationResponse"];

export default function DocumentNumbersScreen({ params: _params }: DocumentNumbersScreenProps) {
  const api = useApi();
  const { companyId, branchId } = useSession((s) => ({ companyId: s.companyId, branchId: s.branchId }));

  const [activeTab, setActiveTab] = useState<"requests" | "audit">("requests");

  // Requests state
  const [requests, setRequests] = useState<DocumentNumberChangeDto[]>([]);
  const [familyFilter, setFamilyFilter] = useState<string>("");
  const [isLoadingRequests, setIsLoadingRequests] = useState(false);
  const [requestsError, setRequestsError] = useState<{ status?: number; message: string } | null>(null);

  // New Request dialog state
  const [isNewRequestOpen, setIsNewRequestOpen] = useState(false);
  const [reqFamily, setReqFamily] = useState<string>("invoices");
  const [reqDocumentId, setReqDocumentId] = useState<string>("");
  const [reqNewNumber, setReqNewNumber] = useState<string>("");
  const [reqReason, setReqReason] = useState<string>("");
  const [isPreviewingCheck, setIsPreviewingCheck] = useState(false);
  const [previewCheckResult, setPreviewCheckResult] = useState<DocumentNumberPreviewResponse | null>(null);
  const [isSubmittingReq, setIsSubmittingReq] = useState(false);

  // Decision dialog state (Approve/Reject)
  const [decisionModal, setDecisionModal] = useState<{
    open: boolean;
    requestId: string;
    action: "approve" | "reject";
    reason: string;
  }>({
    open: false,
    requestId: "",
    action: "approve",
    reason: "",
  });
  const [isDeciding, setIsDeciding] = useState(false);

  // Audit state
  const [auditEntries, setAuditEntries] = useState<DocumentNumberAuditDto[]>([]);
  const [isLoadingAudit, setIsLoadingAudit] = useState(false);
  const [auditError, setAuditError] = useState<{ status?: number; message: string } | null>(null);
  const [verificationResult, setVerificationResult] = useState<DocumentNumberChainVerificationResponse | null>(null);
  const [isVerifying, setIsVerifying] = useState(false);

  const fetchRequests = async () => {
    if (!companyId || !branchId) return;
    setIsLoadingRequests(true);
    setRequestsError(null);
    try {
      const res = await api.GET(
        "/api/v1/companies/{companyId}/branches/{branchId}/document-numbers/requests",
        {
          params: { path: { companyId, branchId } },
        }
      );
      if (res.data?.items) {
        setRequests(res.data.items);
      } else {
        setRequests([]);
      }
    } catch (err) {
      setRequestsError({
        message: err instanceof Error ? err.message : "فشل تحميل طلبات تعديل الأرقام",
      });
    } finally {
      setIsLoadingRequests(false);
    }
  };

  const fetchAudit = async () => {
    if (!companyId || !branchId) return;
    setIsLoadingAudit(true);
    setAuditError(null);
    try {
      const res = await api.GET(
        "/api/v1/companies/{companyId}/branches/{branchId}/document-numbers/audit",
        {
          params: {
            path: { companyId, branchId },
            query: { take: 100 },
          },
        }
      );
      if (res.data?.items) {
        setAuditEntries(res.data.items);
      } else {
        setAuditEntries([]);
      }
    } catch (err) {
      setAuditError({
        message: err instanceof Error ? err.message : "فشل تحميل سجل التدقيق",
      });
    } finally {
      setIsLoadingAudit(false);
    }
  };

  useEffect(() => {
    if (activeTab === "requests") {
      fetchRequests();
    } else {
      fetchAudit();
    }
  }, [companyId, branchId, activeTab]);

  const handlePreviewCollision = async () => {
    if (!companyId || !branchId || !reqFamily || !reqDocumentId || !reqNewNumber) {
      toast.warning("يرجى ملء عائلة المستند ومعرف المستند والرقم المقترح لفحص التعارض");
      return;
    }

    setIsPreviewingCheck(true);
    setPreviewCheckResult(null);
    try {
      const res = await api.GET(
        "/api/v1/companies/{companyId}/branches/{branchId}/document-numbers/preview",
        {
          params: {
            path: { companyId, branchId },
            query: {
              family: reqFamily,
              documentId: reqDocumentId,
              proposedNumber: reqNewNumber,
            },
          },
        }
      );
      if (res.data) {
        setPreviewCheckResult(res.data);
        if (res.data.canRequest) {
          toast.success("الرقم المقترح متاح ولا توجد تعارضات");
        } else {
          toast.warning(`يوجد مانع للترقيم: ${res.data.blockingCode ?? "تعارض في الأرقام"}`);
        }
      }
    } catch {
      toast.error("فشل فحص تعارض الرقم المقترح");
    } finally {
      setIsPreviewingCheck(false);
    }
  };

  const handleSubmitRequest = async () => {
    if (!companyId || !branchId) return;
    if (!reqFamily || !reqDocumentId || !reqNewNumber || !reqReason.trim()) {
      toast.warning("يرجى ملء كافة الحقول الإلزامية وسبب التعديل");
      return;
    }

    setIsSubmittingReq(true);
    try {
      const res = await api.POST(
        "/api/v1/companies/{companyId}/branches/{branchId}/document-numbers/requests",
        {
          params: { path: { companyId, branchId } },
          body: {
            family: reqFamily,
            documentId: reqDocumentId,
            newNumber: reqNewNumber,
            reason: reqReason,
          },
        }
      );

      if (res.error) {
        toast.error((res.error as { detail?: string })?.detail ?? "فشل تقديم طلب تعديل الرقم");
        return;
      }

      toast.success("تم تقديم طلب تعديل رقم المستند بنجاح");
      setIsNewRequestOpen(false);
      setReqDocumentId("");
      setReqNewNumber("");
      setReqReason("");
      setPreviewCheckResult(null);
      fetchRequests();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "خطأ أثناء تقديم الطلب");
    } finally {
      setIsSubmittingReq(false);
    }
  };

  const handleDecision = async () => {
    if (!companyId || !branchId || !decisionModal.requestId) return;
    if (!decisionModal.reason.trim()) {
      toast.warning("يرجى إدخال سبب القرار للتدقيق");
      return;
    }

    setIsDeciding(true);
    try {
      const endpoint =
        decisionModal.action === "approve"
          ? "/api/v1/companies/{companyId}/branches/{branchId}/document-numbers/requests/{requestId}/approve"
          : "/api/v1/companies/{companyId}/branches/{branchId}/document-numbers/requests/{requestId}/reject";

      const res = await api.POST(endpoint, {
        params: {
          path: { companyId, branchId, requestId: decisionModal.requestId },
        },
        body: { reason: decisionModal.reason },
      });

      if (res.error) {
        toast.error((res.error as { detail?: string })?.detail ?? "فشل تسجيل القرار");
        return;
      }

      toast.success(
        decisionModal.action === "approve"
          ? "تم اعتماد تعديل رقم المستند وتحديث الأستاذ وسجل التدقيق"
          : "تم رفض طلب تعديل الرقم"
      );
      setDecisionModal({ open: false, requestId: "", action: "approve", reason: "" });
      fetchRequests();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "خطأ أثناء تنفيذ القرار");
    } finally {
      setIsDeciding(false);
    }
  };

  const handleCancelRequest = async (requestId: string) => {
    if (!companyId || !branchId) return;
    try {
      const res = await api.POST(
        "/api/v1/companies/{companyId}/branches/{branchId}/document-numbers/requests/{requestId}/cancel",
        {
          params: { path: { companyId, branchId, requestId } },
          body: { reason: "إلغاء الطلب من قبل المستخدم" },
        }
      );
      if (res.error) {
        toast.error("فشل سحب الطلب");
        return;
      }
      toast.success("تم سحب وإلغاء الطلب بنجاح");
      fetchRequests();
    } catch {
      toast.error("خطأ أثناء سحب الطلب");
    }
  };

  const handleVerifyAuditChain = async () => {
    if (!companyId || !branchId) return;
    setIsVerifying(true);
    try {
      const res = await api.GET(
        "/api/v1/companies/{companyId}/branches/{branchId}/document-numbers/audit/verify",
        {
          params: { path: { companyId, branchId } },
        }
      );
      if (res.data) {
        setVerificationResult(res.data);
        if (res.data.intact) {
          toast.success(`سلسلة التدقيق التشفيرية سليمة تماماً (${res.data.entriesChecked} قيود مفحوصة)`);
        } else {
          toast.error(`تحذير أمني: تم رصد انكسار في السلسلة التشفيرية عند التسلسل ${res.data.firstBrokenSequence}`);
        }
      }
    } catch {
      toast.error("فشل التحقق من السلسلة التشفيرية");
    } finally {
      setIsVerifying(false);
    }
  };

  const exportAuditTsv = () => {
    if (auditEntries.length === 0) {
      toast.warning("لا توجد سجلات تدقيق للتصدير");
      return;
    }
    const headers = [
      "التسلسل",
      "معرف الطلب",
      "العائلة",
      "معرف المستند",
      "الرقم القديم",
      "الرقم الجديد",
      "المستخدم الطالب",
      "المستخدم المعتمد",
      "التاريخ UTC",
      "الهاش السابق",
      "الهاش الحالي",
    ];
    const rows = auditEntries.map((a) => [
      a.sequence ?? "",
      a.requestId ?? "",
      a.family ?? "",
      a.documentId ?? "",
      a.oldNumber ?? "",
      a.newNumber ?? "",
      a.requestedByUserId ?? "",
      a.approvedByUserId ?? "",
      a.occurredAtUtc ?? "",
      a.previousHash ?? "",
      a.hash ?? "",
    ]);

    const tsvContent = [headers.join("\t"), ...rows.map((row) => row.join("\t"))].join("\n");
    const blob = new Blob(["\uFEFF" + tsvContent], { type: "text/tab-separated-values;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const al = document.createElement("a");
    al.href = url;
    al.download = `document-number-audit-${Date.now()}.tsv`;
    al.click();
    URL.revokeObjectURL(url);
    toast.success("تم تصدير سجل التدقيق التشفيري بنجاح");
  };

  const filteredRequests = requests.filter(
    (r) => !familyFilter || r.family?.toLowerCase() === familyFilter.toLowerCase()
  );

  return (
    <div className="document-numbers-screen" style={{ padding: "16px", height: "100%", display: "flex", flexDirection: "column", gap: "16px", overflowY: "auto" }}>
      {/* Header */}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", borderBottom: "1px solid var(--border)", paddingBottom: "12px" }}>
        <div>
          <h2 style={{ margin: 0, fontSize: "1.25rem", color: "var(--header-bg)" }}>
            صيانة ترقيم المستندات وسلسلة التدقيق (Document Numbers & Audit Chain)
          </h2>
          <p style={{ margin: "4px 0 0", fontSize: "0.85rem", color: "var(--text-muted)" }}>
            إدارة طلبات تعديل أرقام المستندات، فحص التعارض، والتحقق من سلسلة التدقيق التشفيرية المانعة للتلاعب.
          </p>
        </div>
        <div style={{ display: "flex", gap: "8px" }}>
          {activeTab === "requests" ? (
            <>
              <PetraButton variant="secondary" onClick={fetchRequests} disabled={isLoadingRequests} size="sm">
                تحديث الطلبات
              </PetraButton>
              <PetraButton variant="primary" onClick={() => setIsNewRequestOpen(true)} size="sm">
                طلب تعديل رقم مستند
              </PetraButton>
            </>
          ) : (
            <>
              <PetraButton variant="secondary" onClick={exportAuditTsv} disabled={auditEntries.length === 0} size="sm">
                تصدير TSV
              </PetraButton>
              <PetraButton
                variant="primary"
                onClick={handleVerifyAuditChain}
                disabled={isVerifying}
                size="sm"
              >
                {isVerifying ? "جارٍ التحقق التشفيري..." : "فحص سلامة السلسلة التشفيرية"}
              </PetraButton>
            </>
          )}
        </div>
      </div>

      {/* Tabs */}
      <div style={{ display: "flex", gap: "8px", borderBottom: "1px solid var(--border)" }}>
        <button
          className="petra-tab-btn"
          style={{
            padding: "8px 16px",
            border: "none",
            borderBottom: activeTab === "requests" ? "3px solid var(--primary)" : "none",
            background: "transparent",
            color: activeTab === "requests" ? "var(--primary)" : "var(--text-muted)",
            fontWeight: activeTab === "requests" ? "bold" : "normal",
            cursor: "pointer",
          }}
          onClick={() => setActiveTab("requests")}
        >
          طلبات التعديل والاعتماد ({requests.length})
        </button>
        <button
          className="petra-tab-btn"
          style={{
            padding: "8px 16px",
            border: "none",
            borderBottom: activeTab === "audit" ? "3px solid var(--primary)" : "none",
            background: "transparent",
            color: activeTab === "audit" ? "var(--primary)" : "var(--text-muted)",
            fontWeight: activeTab === "audit" ? "bold" : "normal",
            cursor: "pointer",
          }}
          onClick={() => setActiveTab("audit")}
        >
          سجل التدقيق والسلسلة التشفيرية ({auditEntries.length})
        </button>
      </div>

      {/* TAB 1: Requests */}
      {activeTab === "requests" && (
        <div style={{ display: "flex", flexDirection: "column", gap: "12px", flex: 1 }}>
          <div style={{ display: "flex", gap: "12px", alignItems: "center" }}>
            <PetraField label="تصفية حسب عائلة المستند">
              <select
                className="petra-select"
                value={familyFilter}
                onChange={(e) => setFamilyFilter(e.target.value)}
                style={{ width: "160px", padding: "5px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
              >
                <option value="">كافة العائلات</option>
                <option value="invoices">فواتير (invoices)</option>
                <option value="journals">قيود (journals)</option>
                <option value="vouchers">سندات (vouchers)</option>
                <option value="inventory">مستودعات (inventory)</option>
              </select>
            </PetraField>
          </div>

          {isLoadingRequests ? (
            <PetraLoadingSkeleton rows={8} />
          ) : requestsError ? (
            <PetraErrorDetails status={requestsError.status} message={requestsError.message} onRetry={fetchRequests} />
          ) : filteredRequests.length === 0 ? (
            <PetraEmptyState title="لا توجد طلبات تعديل أرقام مستندات في هذا النطاق" />
          ) : (
            <div style={{ flex: 1, overflowX: "auto", border: "1px solid var(--border)", borderRadius: "4px", background: "var(--bg-surface)" }}>
              <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.85rem", textAlign: "right" }}>
                <thead>
                  <tr style={{ background: "var(--header-bg)", color: "#FFFFFF" }}>
                    <th style={{ padding: "8px" }}>العائلة</th>
                    <th style={{ padding: "8px" }}>معرف المستند</th>
                    <th style={{ padding: "8px" }}>الرقم الحالي</th>
                    <th style={{ padding: "8px" }}>الرقم المطلوب</th>
                    <th style={{ padding: "8px" }}>الحالة</th>
                    <th style={{ padding: "8px" }}>سبب التعديل</th>
                    <th style={{ padding: "8px" }}>تاريخ الطلب</th>
                    <th style={{ padding: "8px", textAlign: "center" }}>الإجراءات</th>
                  </tr>
                </thead>
                <tbody>
                  {filteredRequests.map((req, idx) => {
                    const isPending = req.state?.toLowerCase() === "pending";
                    return (
                      <tr
                        key={req.id || idx}
                        style={{
                          borderBottom: "1px solid var(--border)",
                          background: idx % 2 === 0 ? "transparent" : "var(--bg-app)",
                        }}
                      >
                        <td style={{ padding: "8px", fontWeight: "bold" }}>{req.family}</td>
                        <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)", fontSize: "0.75rem" }}>
                          {req.documentId?.substring(0, 8)}...
                        </td>
                        <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)" }}>
                          {req.oldNumber || "—"}
                        </td>
                        <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)", fontWeight: "bold", color: "var(--primary)" }}>
                          {req.newNumber}
                        </td>
                        <td style={{ padding: "8px" }}>
                          <span
                            style={{
                              padding: "2px 6px",
                              borderRadius: "3px",
                              fontSize: "0.75rem",
                              background:
                                req.state === "Approved"
                                  ? "#E8F5E9"
                                  : req.state === "Rejected"
                                  ? "#FFEBEE"
                                  : req.state === "Cancelled"
                                  ? "#F3F4F6"
                                  : "#FEF3C7",
                              color:
                                req.state === "Approved"
                                  ? "#2E7D32"
                                  : req.state === "Rejected"
                                  ? "#C62828"
                                  : req.state === "Cancelled"
                                  ? "#6B7280"
                                  : "#D97706",
                            }}
                          >
                            {req.state}
                          </span>
                        </td>
                        <td style={{ padding: "8px" }}>{req.reason}</td>
                        <td style={{ padding: "8px", fontSize: "0.75rem" }}>
                          {req.requestedAtUtc ? new Date(req.requestedAtUtc).toLocaleString("ar-SA") : "—"}
                        </td>
                        <td style={{ padding: "8px", textAlign: "center" }}>
                          {isPending && req.id && (
                            <div style={{ display: "flex", gap: "4px", justifyContent: "center" }}>
                              <PetraButton
                                variant="primary"
                                size="sm"
                                onClick={() =>
                                  setDecisionModal({
                                    open: true,
                                    requestId: req.id!,
                                    action: "approve",
                                    reason: "اعتماد مالي للرقم الجديد بعد التحقق من عدم وجود تعارض",
                                  })
                                }
                              >
                                اعتماد
                              </PetraButton>
                              <PetraButton
                                variant="secondary"
                                size="sm"
                                onClick={() =>
                                  setDecisionModal({
                                    open: true,
                                    requestId: req.id!,
                                    action: "reject",
                                    reason: "تعارض في الترقيم أو مخالفة سياسة التسلسل",
                                  })
                                }
                              >
                                رفض
                              </PetraButton>
                              <PetraButton
                                variant="secondary"
                                size="sm"
                                onClick={() => handleCancelRequest(req.id!)}
                              >
                                سحب
                              </PetraButton>
                            </div>
                          )}
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

      {/* TAB 2: Cryptographic Audit Chain */}
      {activeTab === "audit" && (
        <div style={{ display: "flex", flexDirection: "column", gap: "12px", flex: 1 }}>
          {verificationResult && (
            <div
              style={{
                background: verificationResult.intact ? "#E8F5E9" : "#FFEBEE",
                border: `1px solid ${verificationResult.intact ? "#A5D6A7" : "#FFCDD2"}`,
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
                    color: verificationResult.intact ? "#2E7D32" : "#C62828",
                    fontSize: "0.95rem",
                  }}
                >
                  {verificationResult.intact
                    ? "✓ السلسلة التشفيرية سليمة تماماً — لا يوجد أي تعديل غير مصرح به في قاعدة البيانات"
                    : `⚠ تحذير انكسار السلسلة التشفيرية عند التسلسل ${verificationResult.firstBrokenSequence}`}
                </div>
                <div style={{ fontSize: "0.8rem", color: "var(--text-muted)", marginTop: "4px" }}>
                  تم فحص وتحقيق {verificationResult.entriesChecked} قيدًا تدقيقيًا ضد الهاشات المتسلسلة (SHA-256 Chain).
                </div>
              </div>
            </div>
          )}

          {isLoadingAudit ? (
            <PetraLoadingSkeleton rows={8} />
          ) : auditError ? (
            <PetraErrorDetails status={auditError.status} message={auditError.message} onRetry={fetchAudit} />
          ) : auditEntries.length === 0 ? (
            <PetraEmptyState title="لا توجد سجلات تدقيق ترقيم مسجلة حتى الآن" />
          ) : (
            <div style={{ flex: 1, overflowX: "auto", border: "1px solid var(--border)", borderRadius: "4px", background: "var(--bg-surface)" }}>
              <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.8rem", textAlign: "right" }}>
                <thead>
                  <tr style={{ background: "var(--header-bg)", color: "#FFFFFF" }}>
                    <th style={{ padding: "8px", width: "60px" }}>#</th>
                    <th style={{ padding: "8px" }}>العائلة</th>
                    <th style={{ padding: "8px" }}>الرقم السابق</th>
                    <th style={{ padding: "8px" }}>الرقم الجديد</th>
                    <th style={{ padding: "8px" }}>التاريخ UTC</th>
                    <th style={{ padding: "8px" }}>الهاش السابق (Previous Hash)</th>
                    <th style={{ padding: "8px" }}>الهاش التشفيري (Hash)</th>
                  </tr>
                </thead>
                <tbody>
                  {auditEntries.map((a, idx) => (
                    <tr
                      key={a.sequence ?? idx}
                      style={{
                        borderBottom: "1px solid var(--border)",
                        background: idx % 2 === 0 ? "transparent" : "var(--bg-app)",
                      }}
                    >
                      <td style={{ padding: "8px", fontWeight: "bold" }}>{a.sequence}</td>
                      <td style={{ padding: "8px" }}>{a.family}</td>
                      <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)" }}>{a.oldNumber || "—"}</td>
                      <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)", fontWeight: "bold", color: "var(--primary)" }}>
                        {a.newNumber}
                      </td>
                      <td style={{ padding: "8px" }}>
                        {a.occurredAtUtc ? new Date(a.occurredAtUtc).toLocaleString("ar-SA") : "—"}
                      </td>
                      <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)", fontSize: "0.7rem", color: "var(--text-muted)" }}>
                        {a.previousHash ? `${a.previousHash.substring(0, 16)}...` : "Genesis"}
                      </td>
                      <td style={{ padding: "8px", fontFamily: "var(--font-mono, monospace)", fontSize: "0.7rem", fontWeight: "bold" }}>
                        {a.hash ? `${a.hash.substring(0, 16)}...` : "—"}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

      {/* New Request Dialog */}
      <PetraDialog
        open={isNewRequestOpen}
        onClose={() => setIsNewRequestOpen(false)}
        title="طلب تعديل رقم مستند رسمي"
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "16px" }}>
          <div style={{ fontSize: "0.85rem", color: "var(--text-muted)" }}>
            يتم فحص التعارض جافاً أولاً (Dry-Run Preview) للتأكد من خلو الرقم المقترح من أي تضارب مع المستندات أو الطلبات المعلقة.
          </div>

          <PetraField label="عائلة المستند (Family)">
            <select
              className="petra-select"
              value={reqFamily}
              onChange={(e) => setReqFamily(e.target.value)}
              style={{ width: "100%", padding: "6px 8px", border: "1px solid var(--border)", borderRadius: "3px" }}
            >
              <option value="invoices">فواتير (invoices)</option>
              <option value="journals">قيود (journals)</option>
              <option value="vouchers">سندات (vouchers)</option>
              <option value="inventory">مستودعات (inventory)</option>
            </select>
          </PetraField>

          <PetraField label="معرف المستند (Document ID UUID)">
            <input
              type="text"
              className="petra-input"
              placeholder="مثال: c8b0a9df-..."
              value={reqDocumentId}
              onChange={(e) => setReqDocumentId(e.target.value)}
              style={{ width: "100%", padding: "6px 8px", border: "1px solid var(--border)", borderRadius: "3px" }}
            />
          </PetraField>

          <PetraField label="الرقم الجديد المقترح (Proposed Number)">
            <div style={{ display: "flex", gap: "8px" }}>
              <input
                type="text"
                className="petra-input"
                placeholder="مثال: INV-2026-0099"
                value={reqNewNumber}
                onChange={(e) => setReqNewNumber(e.target.value)}
                style={{ flex: 1, padding: "6px 8px", border: "1px solid var(--border)", borderRadius: "3px" }}
              />
              <PetraButton
                variant="secondary"
                size="sm"
                onClick={handlePreviewCollision}
                disabled={isPreviewingCheck || !reqNewNumber || !reqDocumentId}
              >
                {isPreviewingCheck ? "فحص..." : "فحص التعارض"}
              </PetraButton>
            </div>
          </PetraField>

          {previewCheckResult && (
            <div
              style={{
                background: previewCheckResult.canRequest ? "#E8F5E9" : "#FFEBEE",
                border: `1px solid ${previewCheckResult.canRequest ? "#A5D6A7" : "#FFCDD2"}`,
                borderRadius: "4px",
                padding: "8px 12px",
                fontSize: "0.85rem",
              }}
            >
              <div style={{ fontWeight: "bold", color: previewCheckResult.canRequest ? "#2E7D32" : "#C62828" }}>
                {previewCheckResult.canRequest
                  ? "✓ متاح: لا يوجد أي تعارض للرقم المقترح ويمكن تقديم الطلب"
                  : `⚠ تعارض محظور: ${previewCheckResult.blockingCode ?? "الرقم مستخدم بالفعل"}`}
              </div>
              {previewCheckResult.collisions && previewCheckResult.collisions.length > 0 && (
                <div style={{ fontSize: "0.75rem", marginTop: "4px", color: "#C62828" }}>
                  مستندات متعارضة: {previewCheckResult.collisions.map((c) => c.documentId).join(", ")}
                </div>
              )}
            </div>
          )}

          <PetraField label="سبب ومبرر طلب التعديل (إلزامي للتدقيق والامتثال)">
            <textarea
              className="petra-input"
              rows={3}
              placeholder="بيان سبب إعادة الترقيم..."
              value={reqReason}
              onChange={(e) => setReqReason(e.target.value)}
              style={{ width: "100%", padding: "6px 8px", border: "1px solid var(--border)", borderRadius: "3px", resize: "vertical" }}
            />
          </PetraField>

          <div style={{ display: "flex", justifyContent: "flex-end", gap: "8px", marginTop: "8px" }}>
            <PetraButton variant="secondary" onClick={() => setIsNewRequestOpen(false)}>
              إلغاء
            </PetraButton>
            <PetraButton
              variant="primary"
              onClick={handleSubmitRequest}
              disabled={isSubmittingReq || (previewCheckResult !== null && !previewCheckResult.canRequest)}
            >
              {isSubmittingReq ? "جارٍ التقديم..." : "تقديم طلب التعديل"}
            </PetraButton>
          </div>
        </div>
      </PetraDialog>

      {/* Decision Dialog */}
      <PetraDialog
        open={decisionModal.open}
        onClose={() => setDecisionModal({ ...decisionModal, open: false })}
        title={decisionModal.action === "approve" ? "اعتماد تعديل رقم المستند" : "رفض طلب تعديل رقم المستند"}
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "16px" }}>
          <p style={{ margin: 0, fontSize: "0.85rem" }}>
            {decisionModal.action === "approve"
              ? "سيتم تطبيق الرقم الجديد فورياً على المستند وتحديث الأستاذ العام وتسجيل التغيير في سلسلة التدقيق التشفيرية المانعة للتلاعب."
              : "سيتم رفض الطلب ولن يتم تعديل رقم المستند مع حفظ سجل الرفض للتدقيق."}
          </p>

          <PetraField label="مبرر القرار (Decision Reason)">
            <textarea
              className="petra-input"
              rows={3}
              value={decisionModal.reason}
              onChange={(e) => setDecisionModal({ ...decisionModal, reason: e.target.value })}
              style={{ width: "100%", padding: "6px 8px", border: "1px solid var(--border)", borderRadius: "3px", resize: "vertical" }}
            />
          </PetraField>

          <div style={{ display: "flex", justifyContent: "flex-end", gap: "8px" }}>
            <PetraButton variant="secondary" onClick={() => setDecisionModal({ ...decisionModal, open: false })}>
              إلغاء
            </PetraButton>
            <PetraButton variant="primary" onClick={handleDecision} disabled={isDeciding}>
              {isDeciding ? "جارٍ الحفظ..." : decisionModal.action === "approve" ? "تأكيد الاعتماد" : "تأكيد الرفض"}
            </PetraButton>
          </div>
        </div>
      </PetraDialog>
    </div>
  );
}
