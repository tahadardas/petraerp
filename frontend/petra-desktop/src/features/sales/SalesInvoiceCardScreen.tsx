import { useState, useEffect, useMemo } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { EditableGrid, type EditableColumn } from "@/components/grid/EditableGrid";
import { PetraDocumentToolbar, type DocumentPostingStatus } from "@/components/ui/PetraDocumentToolbar";
import { PetraLoadingSkeleton, PetraErrorDetails, PetraField } from "@/components/ui";
import { toast } from "@/components/ui/toastStore";

export interface SalesInvoiceCardScreenProps {
  params?: Readonly<Record<string, string>> | undefined;
}

interface InvoiceLine {
  id: string;
  itemCode: string;
  itemNameArabic: string;
  itemId: string;
  unitCode: string;
  quantity: string;
  unitPrice: string;
  discountPercentage: string;
  taxPercentage: string;
  lineTotal: string;
  notes: string;
}

const makeBlankLine = (idx: number): InvoiceLine => ({
  id: `line-${idx}-${Date.now()}`,
  itemCode: "",
  itemNameArabic: "",
  itemId: "",
  unitCode: "",
  quantity: "",
  unitPrice: "",
  discountPercentage: "0",
  taxPercentage: "0",
  lineTotal: "",
  notes: "",
});

const isBlankLine = (l: InvoiceLine) => !l.itemCode && !l.quantity && !l.unitPrice;

export default function SalesInvoiceCardScreen({ params }: SalesInvoiceCardScreenProps) {
  const invoiceId = params?.invoiceId;
  const api = useApi();
  const { companyId, branchId } = useSession((s) => ({ companyId: s.companyId, branchId: s.branchId }));

  const [invoiceNumber, setInvoiceNumber] = useState<string | null>(null);
  const [date, setDate] = useState(new Date().toISOString().substring(0, 10));
  const [customerName, setCustomerName] = useState("");
  const [patternName, setPatternName] = useState("");
  const [paymentMethod, setPaymentMethod] = useState("Credit");
  const [status, setStatus] = useState<string>("Draft");
  const [currencyCode, setCurrencyCode] = useState("SAR");
  const [lines, setLines] = useState<InvoiceLine[]>([]);
  const [loading, setLoading] = useState(false);
  const [posting, setPosting] = useState(false);
  const [reversing, setReversing] = useState(false);
  const [error, setError] = useState<{ status?: number; message?: string } | null>(null);

  const loadInvoice = async () => {
    if (!companyId || !branchId || !invoiceId) {
      setLines(Array.from({ length: 50 }, (_, i) => makeBlankLine(i)));
      return;
    }

    setLoading(true);
    setError(null);
    try {
      const res = await api.GET("/api/v1/companies/{companyId}/branches/{branchId}/sales/invoices/{invoiceId}", {
        params: { path: { companyId, branchId, invoiceId } },
      });

      if (res.error) {
        setError({ status: res.response.status, message: "فشل استرجاع بيانات الفاتورة" });
      } else if (res.data) {
        const inv = res.data;
        setInvoiceNumber(inv.invoiceNumber ?? null);
        setDate(inv.invoiceDateUtc ? inv.invoiceDateUtc.substring(0, 10) : new Date().toISOString().substring(0, 10));
        setCustomerName(inv.customerName ?? "");
        setPatternName(inv.patternName ?? "");
        setPaymentMethod(inv.paymentMethod ?? "Credit");
        setStatus(inv.status ?? "Draft");
        setCurrencyCode(inv.currencyCode ?? "SAR");

        const loadedLines: InvoiceLine[] = (inv.lines ?? []).map((l, i) => ({
          id: l.id ?? `l-${i}`,
          itemCode: l.itemCode ?? "",
          itemNameArabic: l.itemNameArabic ?? "",
          itemId: l.itemId ?? "",
          unitCode: l.unitCode ?? "",
          quantity: l.quantity,
          unitPrice: l.unitPrice,
          discountPercentage: l.discountPercentage,
          taxPercentage: l.taxPercentage,
          lineTotal: l.lineTotal,
          notes: l.notes ?? "",
        }));

        setLines(loadedLines);
      }
    } catch (err) {
      setError({ message: err instanceof Error ? err.message : "خطأ غير متوقع" });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadInvoice();
  }, [companyId, branchId, invoiceId]);

  const handlePost = async () => {
    if (!companyId || !branchId || !invoiceId) {
      toast.error("يرجى حفظ الفاتورة أولاً قبل الترحيل");
      return;
    }

    setPosting(true);
    try {
      const res = await api.POST("/api/v1/companies/{companyId}/branches/{branchId}/sales/invoices/{invoiceId}/post", {
        params: { path: { companyId, branchId, invoiceId } },
        body: {
          idempotencyKey: `post-inv-${Date.now()}`,
        },
      });

      if (res.error) {
        toast.error("فشل ترحيل الفاتورة", (res.error as { detail?: string })?.detail);
      } else {
        toast.success("تم ترحيل فاتورة المبيعات بنجاح وقيد الحركات المخزنية والمالية");
        loadInvoice();
      }
    } catch (err) {
      toast.error("خطأ أثناء ترحيل الفاتورة", err instanceof Error ? err.message : "خطأ غير متوقع");
    } finally {
      setPosting(false);
    }
  };

  const handleReverse = async () => {
    if (!companyId || !branchId || !invoiceId) return;

    setReversing(true);
    try {
      const res = await api.POST("/api/v1/companies/{companyId}/branches/{branchId}/sales/invoices/{invoiceId}/reverse", {
        params: { path: { companyId, branchId, invoiceId } },
        body: {
          reason: "عكس يدوي من واجهة المستخدم",
        },
      });

      if (res.error) {
        toast.error("فشل عكس الفاتورة", (res.error as { detail?: string })?.detail);
      } else {
        toast.success("تم عكس فاتورة المبيعات بنجاح");
        loadInvoice();
      }
    } catch (err) {
      toast.error("خطأ أثناء عكس الفاتورة", err instanceof Error ? err.message : "خطأ غير متوقع");
    } finally {
      setReversing(false);
    }
  };

  // Compute live subtotal for display
  const totals = useMemo(() => {
    let sub = 0;
    for (const l of lines) {
      const q = parseFloat(l.quantity) || 0;
      const p = parseFloat(l.unitPrice) || 0;
      sub += q * p;
    }
    return {
      subTotal: sub.toFixed(2),
      grandTotal: sub.toFixed(2),
    };
  }, [lines]);

  const columns: EditableColumn<InvoiceLine>[] = [
    {
      id: "itemCode",
      header: "رمز المادة",
      width: 130,
      pinned: true,
      text: (l) => l.itemCode,
      set: (l, v) => ({ ...l, itemCode: v }),
    },
    {
      id: "itemName",
      header: "اسم المادة",
      width: 220,
      readOnly: true,
      text: (l) => l.itemNameArabic || "—",
    },
    {
      id: "unit",
      header: "الوحدة",
      width: 90,
      text: (l) => l.unitCode,
      set: (l, v) => ({ ...l, unitCode: v }),
    },
    {
      id: "quantity",
      header: "الكمية",
      width: 100,
      kind: "decimal",
      text: (l) => l.quantity,
      set: (l, v) => {
        const q = parseFloat(v) || 0;
        const p = parseFloat(l.unitPrice) || 0;
        return { ...l, quantity: v, lineTotal: (q * p).toFixed(2) };
      },
    },
    {
      id: "unitPrice",
      header: "السعر الإفرادي",
      width: 120,
      kind: "decimal",
      text: (l) => l.unitPrice,
      set: (l, v) => {
        const q = parseFloat(l.quantity) || 0;
        const p = parseFloat(v) || 0;
        return { ...l, unitPrice: v, lineTotal: (q * p).toFixed(2) };
      },
    },
    {
      id: "lineTotal",
      header: "الإجمالي",
      width: 130,
      readOnly: true,
      kind: "decimal",
      text: (l) => l.lineTotal,
    },
    {
      id: "notes",
      header: "ملاحظات السطر",
      width: 240,
      text: (l) => l.notes,
      set: (l, v) => ({ ...l, notes: v }),
    },
  ];

  const getDocStatus = (): DocumentPostingStatus => {
    switch (status) {
      case "Posted": return "posted";
      case "Reversed": return "reversed";
      case "Approved": return "pending";
      default: return "draft";
    }
  };

  return (
    <div style={{ display: "flex", flexDirection: "column", height: "100%", background: "var(--bg-app)" }}>
      {/* Document Toolbar */}
      <PetraDocumentToolbar
        status={getDocStatus()}
        canSave={status === "Draft"}
        canPost={status === "Draft" || status === "Approved"}
        canReverse={status === "Posted"}
        isPosting={posting}
        isReversing={reversing}
        onSave={() => toast.info("الحفظ يتم مع دورة الترحيل أو كمسودة")}
        onPost={handlePost}
        onReverse={handleReverse}
        onPrint={() => window.print()}
      />

      {/* Header Fields */}
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "140px 160px 1.5fr 140px 130px",
          gap: "var(--space-3)",
          padding: "var(--space-3) var(--space-4)",
          background: "var(--bg-surface)",
          borderBottom: "1px solid var(--border)",
          alignItems: "center",
        }}
      >
        <PetraField label="رقم الفاتورة">
          <input
            type="text"
            readOnly
            value={invoiceNumber ? `#${invoiceNumber}` : "مسودة"}
            style={{
              height: 30,
              padding: "0 var(--space-2)",
              borderRadius: "var(--radius)",
              border: "1px solid var(--border)",
              background: "var(--bg-sunken)",
              color: "var(--text)",
              fontFamily: "var(--font-mono)",
              fontWeight: 600,
            }}
          />
        </PetraField>

        <PetraField label="تاريخ الفاتورة" required>
          <input
            type="date"
            value={date}
            onChange={(e) => setDate(e.target.value)}
            disabled={status === "Posted" || status === "Reversed"}
            style={{
              height: 30,
              padding: "0 var(--space-2)",
              borderRadius: "var(--radius)",
              border: "1px solid var(--border)",
              background: "var(--bg-input)",
              color: "var(--text)",
            }}
          />
        </PetraField>

        <PetraField label="العميل" required>
          <input
            type="text"
            value={customerName}
            onChange={(e) => setCustomerName(e.target.value)}
            disabled={status === "Posted" || status === "Reversed"}
            placeholder="اسم العميل أو رمزه..."
            style={{
              height: 30,
              padding: "0 var(--space-2)",
              borderRadius: "var(--radius)",
              border: "1px solid var(--border)",
              background: "var(--bg-input)",
              color: "var(--text)",
            }}
          />
        </PetraField>

        <PetraField label="طريقة الدفع">
          <select
            value={paymentMethod}
            onChange={(e) => setPaymentMethod(e.target.value)}
            disabled={status === "Posted" || status === "Reversed"}
            style={{
              height: 30,
              padding: "0 var(--space-2)",
              borderRadius: "var(--radius)",
              border: "1px solid var(--border)",
              background: "var(--bg-input)",
              color: "var(--text)",
            }}
          >
            <option value="Credit">آجل (ذمم)</option>
            <option value="Cash">نقدي (صندوق)</option>
          </select>
        </PetraField>

        <PetraField label="نمط الفاتورة">
          <input
            type="text"
            readOnly
            value={patternName || "فاتورة مبيعات عامة"}
            style={{
              height: 30,
              padding: "0 var(--space-2)",
              borderRadius: "var(--radius)",
              border: "1px solid var(--border)",
              background: "var(--bg-sunken)",
              color: "var(--text)",
            }}
          />
        </PetraField>
      </div>

      {/* Lines Grid */}
      <div style={{ flex: 1, minHeight: 0, padding: "var(--space-2) var(--space-4)" }}>
        {loading ? (
          <PetraLoadingSkeleton rows={10} />
        ) : error ? (
          <PetraErrorDetails status={error.status} message={error.message} onRetry={loadInvoice} />
        ) : (
          <EditableGrid
            ariaLabel="سطور فاتورة المبيعات"
            columns={columns}
            rows={lines}
            onRowsChange={setLines}
            direction="rtl"
            isBlank={isBlankLine}
            makeBlank={makeBlankLine}
            readyRows={50}
          />
        )}
      </div>

      {/* Grand Totals Bar */}
      <footer
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          padding: "var(--space-2) var(--space-4)",
          background: "var(--bg-sunken)",
          borderTop: "1px solid var(--border)",
          fontSize: "var(--fs-body)",
          fontWeight: 600,
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: "var(--space-4)" }}>
          <span>
            المجموع قبل الضريبة: <strong className="numeric">{totals.subTotal}</strong> {currencyCode}
          </span>
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: "var(--space-3)" }}>
          <span style={{ fontSize: 16 }}>
            الإجمالي النهائي المستحق:{" "}
            <strong className="numeric" style={{ color: "var(--primary)", fontSize: 18 }}>
              {totals.grandTotal} {currencyCode}
            </strong>
          </span>
        </div>
      </footer>
    </div>
  );
}
