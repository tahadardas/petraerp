import { useState, useEffect, useMemo } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { EditableGrid, type EditableColumn } from "@/components/grid/EditableGrid";
import {
  PetraDocumentToolbar,
  type DocumentPostingStatus,
} from "@/components/ui/PetraDocumentToolbar";
import {
  PetraField,
  PetraConfirm,
  PetraLoadingSkeleton,
  PetraErrorDetails,
} from "@/components/ui";
import { toast } from "@/components/ui/toastStore";

export interface PurchaseInvoiceCardScreenProps {
  params?: Readonly<Record<string, string>> | undefined;
}

interface PurchaseLineRow {
  id: string;
  itemId: string;
  itemCode: string;
  itemNameArabic: string;
  unitCode: string;
  unitId: string;
  quantity: string;
  unitCost: string;
  discountAmount: string;
  taxAmount: string;
  lineTotal: string;
  notes: string;
}

const makeBlankLine = (idx: number): PurchaseLineRow => ({
  id: `line-${idx}-${Date.now()}`,
  itemId: "",
  itemCode: "",
  itemNameArabic: "",
  unitCode: "",
  unitId: "",
  quantity: "",
  unitCost: "",
  discountAmount: "0",
  taxAmount: "0",
  lineTotal: "",
  notes: "",
});

const isBlankLine = (l: PurchaseLineRow) =>
  !l.itemCode && !l.quantity && !l.unitCost;

export default function PurchaseInvoiceCardScreen({
  params,
}: PurchaseInvoiceCardScreenProps) {
  const api = useApi();
  const companyId = useSession((s) => s.companyId);
  const branchId = useSession((s) => s.branchId);

  const initialInvoiceId = params?.invoiceId;
  const isNew = params?.isNew === "true";

  // Document Header State
  const [currentInvoiceId, setCurrentInvoiceId] = useState<string | null>(
    initialInvoiceId || null
  );
  const [invoiceNumber, setInvoiceNumber] = useState<string | null>(null);
  const [supplierId, setSupplierId] = useState("");
  const [warehouseId, setWarehouseId] = useState("");
  const [patternRevisionId, setPatternRevisionId] = useState("");
  const [invoiceDate, setInvoiceDate] = useState(
    new Date().toISOString().slice(0, 10)
  );
  const [supplierReference, setSupplierReference] = useState("");
  const [notes, setNotes] = useState("");
  const [status, setStatus] = useState<DocumentPostingStatus>("draft");
  const [expectedVersion] = useState("1");

  // Options
  const [warehouses, setWarehouses] = useState<
    Array<{ id?: string; nameArabic?: string }>
  >([]);
  const [patterns, setPatterns] = useState<
    Array<{ id?: string; nameArabic?: string }>
  >([]);
  const [suppliers, setSuppliers] = useState<
    Array<{ id?: string; nameArabic?: string }>
  >([]);

  // 50 ready lines
  const [rows, setRows] = useState<PurchaseLineRow[]>(() =>
    Array.from({ length: 50 }, (_, idx) => makeBlankLine(idx))
  );

  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [isConfirmPostOpen, setIsConfirmPostOpen] = useState(false);
  const [isConfirmReverseOpen, setIsConfirmReverseOpen] = useState(false);
  const reversalReason = "إلغاء فاتورة مشتريات";

  const isReadOnly = status === "posted" || status === "reversed";

  // Load auxiliary lookups
  useEffect(() => {
    if (!companyId || !branchId) return;

    // Warehouses
    api
      .GET(
        "/api/v1/companies/{companyId}/branches/{branchId}/inventory/warehouses",
        {
          params: { path: { companyId, branchId } },
        }
      )
      .then((res) => {
        if (res.data) {
          setWarehouses(res.data);
          const firstWh = res.data[0];
          if (firstWh?.id && !warehouseId) {
            setWarehouseId(firstWh.id);
          }
        }
      });

    // Patterns
    api
      .GET(
        "/api/v1/companies/{companyId}/branches/{branchId}/patterns",
        {
          params: {
            path: { companyId, branchId },
            query: { page: 1, pageSize: 100 },
          },
        }
      )
      .then((res) => {
        if (res.data?.items) {
          setPatterns(res.data.items);
          const firstPat = res.data.items[0];
          if (firstPat?.id && !patternRevisionId) {
            setPatternRevisionId(firstPat.id);
          }
        }
      });

    // Suppliers (Parties)
    api
      .GET("/api/v1/companies/{companyId}/branches/{branchId}/parties", {
        params: {
          path: { companyId, branchId },
          query: { page: 1, pageSize: 200 },
        },
      })
      .then((res) => {
        if (res.data?.items) {
          setSuppliers(res.data.items);
          const firstSup = res.data.items[0];
          if (firstSup?.id && !supplierId) {
            setSupplierId(firstSup.id);
          }
        }
      });
  }, [api, companyId, branchId]);

  // Load invoice if viewing existing
  useEffect(() => {
    if (!currentInvoiceId || isNew || !companyId || !branchId) return;

    setLoading(true);
    api
      .GET(
        "/api/v1/companies/{companyId}/branches/{branchId}/purchases/invoices/{invoiceId}",
        {
          params: {
            path: { companyId, branchId, invoiceId: currentInvoiceId },
          },
        }
      )
      .then((res) => {
        if (res.error) {
          setError(JSON.stringify(res.error));
        } else if (res.data) {
          const inv = res.data;
          setInvoiceNumber(inv.invoiceNumber || null);
          setSupplierId(inv.supplierId || "");
          setWarehouseId(inv.warehouseId || "");
          setPatternRevisionId(inv.patternRevisionId || "");
          if (inv.invoiceDateUtc) {
            setInvoiceDate(inv.invoiceDateUtc.slice(0, 10));
          }
          setSupplierReference(inv.supplierReference || "");
          setNotes(inv.notes || "");
          setStatus(
            inv.status === "Posted"
              ? "posted"
              : inv.status === "Reversed"
              ? "reversed"
              : "draft"
          );

          if (inv.lines && inv.lines.length > 0) {
            const mapped = inv.lines.map((ln, i) => {
              const q = parseFloat(ln.quantity || "0") || 0;
              const c = parseFloat(ln.unitCost || "0") || 0;
              return {
                id: ln.id || `line-${i}`,
                itemId: ln.itemId || "",
                itemCode: ln.itemCode || "",
                itemNameArabic: ln.itemName || "",
                unitCode: "PCS",
                unitId: ln.unitId || "",
                quantity: ln.quantity || "0",
                unitCost: ln.unitCost || "0",
                discountAmount: ln.discountAmount || "0",
                taxAmount: ln.taxAmount || "0",
                lineTotal: (q * c).toFixed(2),
                notes: ln.notes || "",
              };
            });
            const padded = [...mapped];
            while (padded.length < 50) {
              padded.push(makeBlankLine(padded.length));
            }
            setRows(padded);
          }
        }
      })
      .finally(() => setLoading(false));
  }, [api, companyId, branchId, currentInvoiceId, isNew]);

  // Totals calculations
  const totals = useMemo(() => {
    let subtotal = 0;
    let totalDiscount = 0;
    let totalTax = 0;

    for (const r of rows) {
      const q = parseFloat(r.quantity) || 0;
      const c = parseFloat(r.unitCost) || 0;
      const d = parseFloat(r.discountAmount) || 0;
      const t = parseFloat(r.taxAmount) || 0;
      subtotal += q * c;
      totalDiscount += d;
      totalTax += t;
    }

    const net = subtotal - totalDiscount + totalTax;
    return {
      subtotal: subtotal.toFixed(2),
      discount: totalDiscount.toFixed(2),
      tax: totalTax.toFixed(2),
      net: net.toFixed(2),
    };
  }, [rows]);

  // Grid columns
  const columns: EditableColumn<PurchaseLineRow>[] = useMemo(
    () => [
      {
        id: "itemCode",
        header: "رمز المادة",
        width: 140,
        pinned: true,
        readOnly: isReadOnly,
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
        id: "quantity",
        header: "الكمية",
        width: 100,
        kind: "decimal",
        readOnly: isReadOnly,
        text: (l) => l.quantity,
        set: (l, v) => {
          const q = parseFloat(v) || 0;
          const c = parseFloat(l.unitCost) || 0;
          return { ...l, quantity: v, lineTotal: (q * c).toFixed(2) };
        },
      },
      {
        id: "unitCost",
        header: "السعر الإفرادي",
        width: 120,
        kind: "decimal",
        readOnly: isReadOnly,
        text: (l) => l.unitCost,
        set: (l, v) => {
          const q = parseFloat(l.quantity) || 0;
          const c = parseFloat(v) || 0;
          return { ...l, unitCost: v, lineTotal: (q * c).toFixed(2) };
        },
      },
      {
        id: "discountAmount",
        header: "الحسم",
        width: 100,
        kind: "decimal",
        readOnly: isReadOnly,
        text: (l) => l.discountAmount,
        set: (l, v) => ({ ...l, discountAmount: v }),
      },
      {
        id: "taxAmount",
        header: "الضريبة",
        width: 100,
        kind: "decimal",
        readOnly: isReadOnly,
        text: (l) => l.taxAmount,
        set: (l, v) => ({ ...l, taxAmount: v }),
      },
      {
        id: "lineTotal",
        header: "المجموع",
        width: 120,
        readOnly: true,
        text: (l) => l.lineTotal || "0.00",
      },
      {
        id: "notes",
        header: "ملاحظات",
        width: 200,
        readOnly: isReadOnly,
        text: (l) => l.notes,
        set: (l, v) => ({ ...l, notes: v }),
      },
    ],
    [isReadOnly]
  );

  // Save
  const handleSave = async () => {
    if (!companyId || !branchId) return;
    if (!supplierId || !warehouseId || !patternRevisionId) {
      toast.error("يرجى تحديد المورد والمستودع ونمط الفاتورة");
      return;
    }

    const filledLines = rows
      .filter((r) => r.itemCode.trim() !== "" && (parseFloat(r.quantity) || 0) > 0)
      .map((r) => ({
        itemId: r.itemId || "00000000-0000-0000-0000-000000000001",
        unitId: r.unitId || "00000000-0000-0000-0000-000000000001",
        quantity: r.quantity || "1",
        unitCost: r.unitCost || "0",
        discountAmount: r.discountAmount || "0",
        taxAmount: r.taxAmount || "0",
        notes: r.notes || null,
      }));

    if (filledLines.length === 0) {
      toast.error("يجب إدخال مادة وكمية واحدة على الأقل");
      return;
    }

    setSaving(true);
    try {
      const res = await api.POST(
        "/api/v1/companies/{companyId}/branches/{branchId}/purchases/invoices",
        {
          params: { path: { companyId, branchId } },
          body: {
            patternRevisionId,
            supplierId,
            warehouseId,
            invoiceDateUtc: new Date(invoiceDate).toISOString(),
            supplierReference: supplierReference || null,
            notes: notes || null,
            lines: filledLines,
          },
        }
      );

      if (res.error) {
        toast.error(JSON.stringify(res.error));
      } else if (res.data) {
        toast.success("تم حفظ فاتورة المشتريات بنجاح");
        if (res.data.id) {
          setCurrentInvoiceId(res.data.id);
          setInvoiceNumber(res.data.invoiceNumber || null);
        }
      }
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : String(err));
    } finally {
      setSaving(false);
    }
  };

  // Post
  const handlePost = async () => {
    if (!currentInvoiceId || !companyId || !branchId) return;
    setSaving(true);
    try {
      const res = await api.POST(
        "/api/v1/companies/{companyId}/branches/{branchId}/purchases/invoices/{invoiceId}/post",
        {
          params: {
            path: { companyId, branchId, invoiceId: currentInvoiceId },
          },
          body: {
            expectedVersion,
            idempotencyKey: `post-purch-${currentInvoiceId}-${Date.now()}`,
          },
        }
      );

      if (res.error) {
        toast.error(JSON.stringify(res.error));
      } else {
        toast.success("تم ترحيل فاتورة المشتريات وتحديث المخزون بنجاح");
        setStatus("posted");
        setIsConfirmPostOpen(false);
      }
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : String(err));
    } finally {
      setSaving(false);
    }
  };

  // Reverse
  const handleReverse = async () => {
    if (!currentInvoiceId || !companyId || !branchId) return;
    if (!reversalReason.trim()) {
      toast.error("سبب العكس مطلوب");
      return;
    }

    setSaving(true);
    try {
      const res = await api.POST(
        "/api/v1/companies/{companyId}/branches/{branchId}/purchases/invoices/{invoiceId}/reverse",
        {
          params: {
            path: { companyId, branchId, invoiceId: currentInvoiceId },
          },
          body: {
            expectedVersion,
            reason: reversalReason.trim(),
            idempotencyKey: `rev-purch-${currentInvoiceId}-${Date.now()}`,
          },
        }
      );

      if (res.error) {
        toast.error(JSON.stringify(res.error));
      } else {
        toast.success("تم عكس فاتورة المشتريات وإلغاء أثرها المخزني والمالي");
        setStatus("reversed");
        setIsConfirmReverseOpen(false);
      }
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : String(err));
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return <PetraLoadingSkeleton rows={10} />;
  }

  if (error) {
    return <PetraErrorDetails message={error} />;
  }

  return (
    <div className="petra-screen flex flex-col h-full bg-[#EEF3F8]">
      {/* Document Toolbar */}
      <PetraDocumentToolbar
        status={status}
        onSave={handleSave}
        onPost={() => setIsConfirmPostOpen(true)}
        onReverse={() => setIsConfirmReverseOpen(true)}
        isSaving={saving}
        canSave={!isReadOnly}
        canPost={Boolean(currentInvoiceId && !isReadOnly)}
        canReverse={status === "posted"}
      />

      {/* Header Fields Card */}
      <div className="p-3 bg-white border-b border-[#CBD8E5] grid grid-cols-4 gap-3 text-sm">
        {invoiceNumber ? (
          <div className="col-span-4 bg-slate-50 border border-slate-200 p-2 rounded flex items-center justify-between text-xs text-slate-700">
            <span>رقم الفاتورة المسجل: <strong className="font-mono text-[#087E87]">{invoiceNumber}</strong></span>
          </div>
        ) : null}
        <PetraField label="المورد" required>
          <select
            value={supplierId}
            disabled={isReadOnly}
            onChange={(e) => setSupplierId(e.target.value)}
            className="w-full border border-[#CBD8E5] rounded px-2.5 py-1 text-sm bg-white focus:border-[#087E87] outline-none"
          >
            <option value="">-- اختر المورد --</option>
            {suppliers.map((s) => (
              <option key={s.id} value={s.id}>
                {s.nameArabic}
              </option>
            ))}
          </select>
        </PetraField>

        <PetraField label="المستودع المستلم" required>
          <select
            value={warehouseId}
            disabled={isReadOnly}
            onChange={(e) => setWarehouseId(e.target.value)}
            className="w-full border border-[#CBD8E5] rounded px-2.5 py-1 text-sm bg-white focus:border-[#087E87] outline-none"
          >
            <option value="">-- اختر المستودع --</option>
            {warehouses.map((w) => (
              <option key={w.id} value={w.id}>
                {w.nameArabic}
              </option>
            ))}
          </select>
        </PetraField>

        <PetraField label="نمط الفاتورة" required>
          <select
            value={patternRevisionId}
            disabled={isReadOnly}
            onChange={(e) => setPatternRevisionId(e.target.value)}
            className="w-full border border-[#CBD8E5] rounded px-2.5 py-1 text-sm bg-white focus:border-[#087E87] outline-none"
          >
            <option value="">-- نمط المشتريات --</option>
            {patterns.map((p) => (
              <option key={p.id} value={p.id}>
                {p.nameArabic}
              </option>
            ))}
          </select>
        </PetraField>

        <PetraField label="تاريخ الفاتورة" required>
          <input
            type="date"
            value={invoiceDate}
            disabled={isReadOnly}
            onChange={(e) => setInvoiceDate(e.target.value)}
            className="w-full border border-[#CBD8E5] rounded px-2.5 py-1 text-sm bg-white focus:border-[#087E87] outline-none font-mono"
          />
        </PetraField>

        <PetraField label="رقم فاتورة المورد (مرجع)">
          <input
            type="text"
            value={supplierReference}
            disabled={isReadOnly}
            onChange={(e) => setSupplierReference(e.target.value)}
            placeholder="مثال: SUP-INV-9821"
            className="w-full border border-[#CBD8E5] rounded px-2.5 py-1 text-sm bg-white focus:border-[#087E87] outline-none font-mono"
          />
        </PetraField>

        <div className="col-span-3">
          <PetraField label="ملاحظات / بيان الفاتورة">
            <input
              type="text"
              value={notes}
              disabled={isReadOnly}
              onChange={(e) => setNotes(e.target.value)}
              placeholder="بيان الفاتورة وتفاصيل التوريد..."
              className="w-full border border-[#CBD8E5] rounded px-2.5 py-1 text-sm bg-white focus:border-[#087E87] outline-none"
            />
          </PetraField>
        </div>
      </div>

      {/* Grid: 50 lines */}
      <div className="flex-1 overflow-hidden p-2">
        <EditableGrid
          columns={columns}
          rows={rows}
          onRowsChange={setRows}
          direction="rtl"
          isBlank={isBlankLine}
          makeBlank={makeBlankLine}
          readyRows={50}
          ariaLabel="جدول أسطر فاتورة المشتريات"
        />
      </div>

      {/* Summary Footer */}
      <div className="bg-[#17334C] text-white p-3 border-t border-slate-700 flex items-center justify-between text-sm select-none">
        <div className="flex items-center gap-6">
          <span>
            المجموع الفرعي:{" "}
            <strong className="font-mono text-emerald-400">
              {totals.subtotal}
            </strong>
          </span>
          <span>
            إجمالي الحسم:{" "}
            <strong className="font-mono text-amber-300">
              {totals.discount}
            </strong>
          </span>
          <span>
            إجمالي الضريبة:{" "}
            <strong className="font-mono text-cyan-300">{totals.tax}</strong>
          </span>
        </div>
        <div className="flex items-center gap-3">
          <span className="text-slate-300">الصافي الإجمالي:</span>
          <span className="text-xl font-bold font-mono text-white bg-[#087E87] px-3 py-1 rounded">
            {totals.net}
          </span>
        </div>
      </div>

      {/* Confirmation Modals */}
      <PetraConfirm
        open={isConfirmPostOpen}
        onCancel={() => setIsConfirmPostOpen(false)}
        onConfirm={handlePost}
        title="ترحيل فاتورة المشتريات"
        message="هل أنت متأكد من ترحيل فاتورة المشتريات؟ سيتم توليد قيد اليومية الرسمي وتحديث أرصدة المخزون وحساب المورد بشكل غير قابل للتعديل المباشر."
        confirmLabel="تأكيد الترحيل"
      />

      <PetraConfirm
        open={isConfirmReverseOpen}
        onCancel={() => setIsConfirmReverseOpen(false)}
        onConfirm={handleReverse}
        title="عكس فاتورة المشتريات (Reversal)"
        message="سيؤدي العكس إلى إنشاء مستند عكسي وإلغاء الأثر المخزني والمالي مع بقاء المستند في سجل التدقيق."
        confirmLabel="تأكيد العكس"
        destructive={true}
      />
    </div>
  );
}
