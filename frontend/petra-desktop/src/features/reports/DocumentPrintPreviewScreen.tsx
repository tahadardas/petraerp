import { useState, useEffect } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import {
  PetraButton,
  PetraField,
  PetraEmptyState,
  PetraLoadingSkeleton,
  PetraErrorDetails,
} from "@/components/ui";
import { toast } from "@/components/ui/toastStore";

export interface DocumentPrintPreviewScreenProps {
  params?: Readonly<Record<string, string>> | undefined;
}

export default function DocumentPrintPreviewScreen({ params }: DocumentPrintPreviewScreenProps) {
  const api = useApi();
  const { companyId, branchId } = useSession((s) => ({ companyId: s.companyId, branchId: s.branchId }));

  const [docType, setDocType] = useState<string>(params?.type ?? "SalesInvoice");
  const [docId, setDocId] = useState<string>(params?.id ?? "");

  const [isLoading, setIsLoading] = useState<boolean>(false);
  const [error, setError] = useState<{ status?: number; message?: string } | null>(null);
  const [documentData, setDocumentData] = useState<any>(null);

  const fetchDocument = async () => {
    if (!companyId || !branchId || !docId) return;

    setIsLoading(true);
    setError(null);
    try {
      let res: any;
      if (docType === "SalesInvoice") {
        res = await api.GET(
          "/api/v1/companies/{companyId}/branches/{branchId}/sales/invoices/{invoiceId}",
          {
            params: { path: { companyId, branchId, invoiceId: docId } },
          }
        );
      } else if (docType === "PurchaseInvoice") {
        res = await api.GET(
          "/api/v1/companies/{companyId}/branches/{branchId}/purchases/invoices/{invoiceId}",
          {
            params: { path: { companyId, branchId, invoiceId: docId } },
          }
        );
      } else if (docType === "Journal") {
        res = await api.GET(
          "/api/v1/companies/{companyId}/branches/{branchId}/ledger/journals/{journalId}",
          {
            params: { path: { companyId, branchId, journalId: docId } },
          }
        );
      } else {
        // Treasury voucher
        res = await api.GET(
          "/api/v1/companies/{companyId}/branches/{branchId}/treasury/vouchers/{voucherId}",
          {
            params: { path: { companyId, branchId, voucherId: docId } },
          }
        );
      }

      if (res.error || !res.data) {
        setError({
          status: res.response?.status,
          message: (res.error as { title?: string; detail?: string })?.detail ?? "فشل جلب بيانات المستند للطباعة",
        });
        return;
      }

      setDocumentData(res.data);
      toast.success("تم جلب بيانات المستند وتجهيز معاينة الطباعة");
    } catch (err) {
      setError({
        message: err instanceof Error ? err.message : "خطأ غير متوقع أثناء جلب المستند",
      });
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    if (params?.id) {
      setDocId(params.id);
      fetchDocument();
    }
  }, [params?.id, params?.type]);

  const handlePrint = () => {
    window.print();
  };

  return (
    <div className="document-print-preview-screen" style={{ padding: "16px", height: "100%", display: "flex", flexDirection: "column", gap: "16px", overflowY: "auto" }}>
      {/* Header Toolbar (hidden on print) */}
      <div className="no-print" style={{ display: "flex", justifyContent: "space-between", alignItems: "center", borderBottom: "1px solid var(--border)", paddingBottom: "12px" }}>
        <div>
          <h2 style={{ margin: 0, fontSize: "1.25rem", color: "var(--header-bg)" }}>
            معاينة وطباعة المستندات الرسمية (Document Print Preview)
          </h2>
          <p style={{ margin: "4px 0 0", fontSize: "0.85rem", color: "var(--text-muted)" }}>
            تنسيق طباعي قياسي متوافق مع ورق A4 للفواتير والسندات والقيود مع الترويسة والتواقيع.
          </p>
        </div>
        <div style={{ display: "flex", gap: "8px" }}>
          <PetraButton variant="secondary" onClick={fetchDocument} disabled={isLoading || !docId} size="sm">
            تحديث البيانات
          </PetraButton>
          <PetraButton variant="primary" onClick={handlePrint} disabled={!documentData} size="sm">
            🖨 طباعة المستند (Print / Ctrl+P)
          </PetraButton>
        </div>
      </div>

      {/* Selector panel (hidden on print) */}
      <div className="no-print" style={{ background: "var(--bg-surface)", border: "1px solid var(--border)", borderRadius: "4px", padding: "12px", display: "flex", flexWrap: "wrap", gap: "16px", alignItems: "flex-end" }}>
        <PetraField label="نوع المستند">
          <select
            className="petra-select"
            value={docType}
            onChange={(e) => setDocType(e.target.value)}
            style={{ width: "180px", padding: "5px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
          >
            <option value="SalesInvoice">فاتورة مبيعات (Sales Invoice)</option>
            <option value="PurchaseInvoice">فاتورة مشتريات (Purchase Invoice)</option>
            <option value="Journal">قيد يومية عام (Journal Entry)</option>
            <option value="Voucher">سند قبض / دفع (Voucher)</option>
          </select>
        </PetraField>

        <PetraField label="معرف المستند (Document ID UUID)">
          <input
            type="text"
            className="petra-input"
            placeholder="معرف المستند..."
            value={docId}
            onChange={(e) => setDocId(e.target.value)}
            style={{ width: "300px", padding: "5px 8px", fontSize: "0.85rem", border: "1px solid var(--border)", borderRadius: "3px" }}
          />
        </PetraField>

        <PetraButton variant="secondary" size="sm" onClick={fetchDocument} disabled={isLoading || !docId}>
          {isLoading ? "جارٍ التحميل..." : "جلب المستند"}
        </PetraButton>
      </div>

      {/* Main A4 Printable Sheet */}
      {isLoading ? (
        <PetraLoadingSkeleton rows={10} />
      ) : error ? (
        <PetraErrorDetails status={error.status} message={error.message} onRetry={fetchDocument} />
      ) : !documentData ? (
        <PetraEmptyState title="حدد نوع المستند ومعرفه واضغط 'جلب المستند' لعرض المعاينة الطباعية الرسمية" />
      ) : (
        <div
          className="printable-sheet"
          style={{
            maxWidth: "800px",
            margin: "0 auto",
            background: "#FFFFFF",
            color: "#000000",
            border: "1px solid #D1D5DB",
            boxShadow: "0 4px 16px rgba(0,0,0,0.08)",
            padding: "32px",
            borderRadius: "4px",
            display: "flex",
            flexDirection: "column",
            gap: "20px",
            fontSize: "14px",
          }}
        >
          {/* Header Block */}
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", borderBottom: "2px solid #17334C", paddingBottom: "16px" }}>
            <div>
              <h1 style={{ margin: 0, fontSize: "1.4rem", color: "#17334C" }}>شركة البتراء للأنظمة المحاسبية</h1>
              <div style={{ fontSize: "0.85rem", color: "#4B5563", marginTop: "4px" }}>
                فرع: {documentData.branchName ?? "المركز الرئيسي"} | الرقم الضريبي: 300998877600003
              </div>
            </div>
            <div style={{ textAlign: "left" }}>
              <div style={{ fontSize: "1.2rem", fontWeight: "bold", color: "#087E87" }}>
                {docType === "SalesInvoice"
                  ? "فاتورة مبيعات ضريبية"
                  : docType === "PurchaseInvoice"
                  ? "فاتورة مشتريات"
                  : docType === "Journal"
                  ? "سند قيد يومية"
                  : "سند مالي"}
              </div>
              <div style={{ fontSize: "0.9rem", fontWeight: "bold", marginTop: "4px", fontFamily: "monospace" }}>
                رقم: {documentData.invoiceNumber ?? documentData.journalNumber ?? documentData.voucherNumber ?? docId.substring(0, 8)}
              </div>
              <div style={{ fontSize: "0.8rem", color: "#6B7280", marginTop: "2px" }}>
                التاريخ: {documentData.invoiceDateUtc ? new Date(documentData.invoiceDateUtc).toLocaleDateString("ar-SA") : new Date().toLocaleDateString("ar-SA")}
              </div>
            </div>
          </div>

          {/* Customer / Party details */}
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "12px", background: "#F9FAFB", padding: "12px", borderRadius: "4px" }}>
            <div>
              <strong>الطرف / الحساب:</strong>{" "}
              {documentData.customerName ?? documentData.supplierName ?? documentData.partyName ?? documentData.description ?? "—"}
            </div>
            <div>
              <strong>العملة:</strong> {documentData.currencyCode ?? "SAR"}
            </div>
            <div>
              <strong>طريقة الدفع / القيد:</strong> {documentData.paymentMethod ?? (documentData.isPosted ? "مرحل ومعتمد" : "مسودة")}
            </div>
            <div>
              <strong>الملاحظات:</strong> {documentData.notes ?? "—"}
            </div>
          </div>

          {/* Lines Table */}
          <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "0.85rem", textAlign: "right" }}>
            <thead>
              <tr style={{ background: "#F3F4F6", borderBottom: "1px solid #D1D5DB" }}>
                <th style={{ padding: "8px", width: "40px" }}>#</th>
                <th style={{ padding: "8px" }}>البيان / المادة</th>
                <th style={{ padding: "8px", width: "80px", textAlign: "center" }}>الكمية</th>
                <th style={{ padding: "8px", width: "100px", textAlign: "center" }}>السعر</th>
                <th style={{ padding: "8px", width: "120px", textAlign: "center" }}>الإجمالي</th>
              </tr>
            </thead>
            <tbody>
              {(documentData.lines ?? []).map((line: any, idx: number) => (
                <tr key={idx} style={{ borderBottom: "1px solid #E5E7EB" }}>
                  <td style={{ padding: "8px", textAlign: "center" }}>{idx + 1}</td>
                  <td style={{ padding: "8px" }}>
                    {line.itemName ?? line.accountNameArabic ?? line.description ?? "بند مستند"}
                  </td>
                  <td style={{ padding: "8px", textAlign: "center", fontFamily: "monospace" }}>
                    {line.quantity ?? "1.00"}
                  </td>
                  <td style={{ padding: "8px", textAlign: "center", fontFamily: "monospace" }}>
                    {line.unitPrice ?? line.amount ?? "—"}
                  </td>
                  <td style={{ padding: "8px", textAlign: "center", fontFamily: "monospace", fontWeight: "bold" }}>
                    {line.totalPrice ?? line.amount ?? line.debit ?? line.credit ?? "0.00"}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>

          {/* Totals Block */}
          <div style={{ display: "flex", justifyContent: "flex-end", marginTop: "8px" }}>
            <div style={{ width: "260px", display: "flex", flexDirection: "column", gap: "6px", fontSize: "0.9rem" }}>
              <div style={{ display: "flex", justifyContent: "space-between", borderBottom: "1px solid #E5E7EB", paddingBottom: "4px" }}>
                <span>المجموع الفرعي:</span>
                <span style={{ fontFamily: "monospace" }}>{documentData.subTotal ?? documentData.totalAmount ?? "0.00"}</span>
              </div>
              {documentData.discountTotal && (
                <div style={{ display: "flex", justifyContent: "space-between", borderBottom: "1px solid #E5E7EB", paddingBottom: "4px" }}>
                  <span>إجمالي الخصم:</span>
                  <span style={{ fontFamily: "monospace", color: "#C62828" }}>{documentData.discountTotal}</span>
                </div>
              )}
              {documentData.taxTotal && (
                <div style={{ display: "flex", justifyContent: "space-between", borderBottom: "1px solid #E5E7EB", paddingBottom: "4px" }}>
                  <span>ضريبة القيمة المضافة:</span>
                  <span style={{ fontFamily: "monospace" }}>{documentData.taxTotal}</span>
                </div>
              )}
              <div style={{ display: "flex", justifyContent: "space-between", fontWeight: "bold", fontSize: "1.05rem", color: "#17334C", paddingTop: "4px" }}>
                <span>الصافي الإجمالي:</span>
                <span style={{ fontFamily: "monospace" }}>{documentData.grandTotal ?? documentData.totalAmount ?? "0.00"} {documentData.currencyCode ?? "SAR"}</span>
              </div>
            </div>
          </div>

          {/* Signatures Block */}
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr", gap: "24px", marginTop: "32px", paddingTop: "16px", borderTop: "1px dashed #D1D5DB", textAlign: "center" }}>
            <div>
              <div style={{ fontWeight: "bold", fontSize: "0.85rem" }}>منظم المستند</div>
              <div style={{ height: "40px" }}></div>
              <div style={{ borderTop: "1px solid #9CA3AF", width: "80%", margin: "0 auto" }}></div>
            </div>
            <div>
              <div style={{ fontWeight: "bold", fontSize: "0.85rem" }}>تدقيق الحسابات</div>
              <div style={{ height: "40px" }}></div>
              <div style={{ borderTop: "1px solid #9CA3AF", width: "80%", margin: "0 auto" }}></div>
            </div>
            <div>
              <div style={{ fontWeight: "bold", fontSize: "0.85rem" }}>المدير المالي / الاعتماد</div>
              <div style={{ height: "40px" }}></div>
              <div style={{ borderTop: "1px solid #9CA3AF", width: "80%", margin: "0 auto" }}></div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
