import { useState, useEffect, useCallback } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { useWorkspace } from "@/workspace/workspaceStore";
import {
  PetraButton,
  PetraEmptyState,
  PetraLoadingSkeleton,
  PetraErrorDetails,
} from "@/components/ui";

interface PurchaseSummary {
  id?: string;
  invoiceNumber?: string | null;
  supplierName?: string;
  invoiceDateUtc?: string;
  totalAmount?: string;
  status?: string;
}

export default function PurchaseInvoicesScreen() {
  const api = useApi();
  const companyId = useSession((s) => s.companyId);
  const branchId = useSession((s) => s.branchId);
  const open = useWorkspace((s) => s.open);

  const [invoices, setInvoices] = useState<PurchaseSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [page, setPage] = useState(1);
  const [totalCount, setTotalCount] = useState(0);

  const loadInvoices = useCallback(async () => {
    if (!companyId || !branchId) return;
    setLoading(true);
    setError(null);
    try {
      const res = await api.GET(
        "/api/v1/companies/{companyId}/branches/{branchId}/purchases/invoices",
        {
          params: {
            path: { companyId, branchId },
            query: { page, pageSize: 50 },
          },
        }
      );
      if (res.error) {
        setError(JSON.stringify(res.error));
      } else if (res.data) {
        setInvoices(res.data.items || []);
        setTotalCount(res.data.totalCount || 0);
      }
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setLoading(false);
    }
  }, [api, companyId, branchId, page]);

  useEffect(() => {
    loadInvoices();
  }, [loadInvoices]);

  const handleOpenInvoice = (invoiceId: string, invoiceNum?: string | null) => {
    open({
      key: `purchase-invoice-${invoiceId}`,
      title: `فاتورة مشتريات: ${invoiceNum || invoiceId.slice(0, 8)}`,
      screen: "purchase-invoice-card",
      params: { invoiceId },
    });
  };

  const handleNewInvoice = () => {
    open({
      key: `purchase-invoice-new-${Date.now()}`,
      title: "فاتورة مشتريات جديدة",
      screen: "purchase-invoice-card",
      params: { isNew: "true" },
    });
  };

  return (
    <div className="petra-screen p-4 flex flex-col h-full bg-[#EEF3F8]">
      {/* Header */}
      <div className="flex items-center justify-between bg-white p-3 border border-[#CBD8E5] rounded mb-3">
        <div className="flex items-center gap-3">
          <h2 className="text-lg font-bold text-[#17334C]">
            فواتير المشتريات (PURCHASE-01)
          </h2>
          <span className="text-xs text-slate-500 bg-slate-100 px-2 py-0.5 rounded">
            إجمالي: {totalCount}
          </span>
        </div>
        <div className="flex items-center gap-2">
          <PetraButton
            variant="secondary"
            onClick={() => loadInvoices()}
            loading={loading}
          >
            تحديث
          </PetraButton>
          <PetraButton
            variant="primary"
            onClick={handleNewInvoice}
            shortcut="F2"
          >
            + فاتورة مشتريات جديدة
          </PetraButton>
        </div>
      </div>

      {/* Invoices List */}
      <div className="flex-1 bg-white border border-[#CBD8E5] rounded overflow-hidden flex flex-col">
        {loading ? (
          <PetraLoadingSkeleton rows={8} />
        ) : error ? (
          <PetraErrorDetails message={error} onRetry={loadInvoices} />
        ) : invoices.length === 0 ? (
          <PetraEmptyState
            title="لا توجد فواتير مشتريات مسجلة"
            description="يمكنك البدء بإنشاء فاتورة مشتريات جديدة وتسجيل مدخلات الموردين."
            actionLabel="+ فاتورة جديدة"
            onAction={handleNewInvoice}
          />
        ) : (
          <div className="flex-1 overflow-auto">
            <table className="w-full text-sm border-collapse text-right">
              <thead className="bg-[#17334C] text-white sticky top-0 z-10 select-none">
                <tr>
                  <th className="p-2 border-l border-slate-700 w-32">رقم الفاتورة</th>
                  <th className="p-2 border-l border-slate-700 w-32">التاريخ</th>
                  <th className="p-2 border-l border-slate-700">المورد</th>
                  <th className="p-2 border-l border-slate-700 w-32 text-left">الإجمالي</th>
                  <th className="p-2 border-l border-slate-700 w-28 text-center">الحالة</th>
                  <th className="p-2 w-28 text-center">إجراءات</th>
                </tr>
              </thead>
              <tbody>
                {invoices.map((inv, idx) => (
                  <tr
                    key={inv.id || idx}
                    onClick={() => inv.id && handleOpenInvoice(inv.id, inv.invoiceNumber)}
                    className={`border-b border-[#CBD8E5] hover:bg-[#E2ECF5] cursor-pointer ${
                      idx % 2 === 1 ? "bg-[#EFF5FC]" : "bg-white"
                    }`}
                  >
                    <td className="p-2 font-mono font-bold text-[#087E87]">
                      {inv.invoiceNumber || "—"}
                    </td>
                    <td className="p-2 font-mono text-xs text-slate-600">
                      {inv.invoiceDateUtc ? inv.invoiceDateUtc.slice(0, 10) : "—"}
                    </td>
                    <td className="p-2 font-medium text-[#253447]">
                      {inv.supplierName || "—"}
                    </td>
                    <td className="p-2 font-mono font-bold text-[#17334C] text-left">
                      {inv.totalAmount || "0.00"}
                    </td>
                    <td className="p-2 text-center">
                      <span
                        className={`text-xs px-2 py-0.5 rounded font-bold ${
                          inv.status === "Posted"
                            ? "bg-green-100 text-green-800"
                            : inv.status === "Draft"
                            ? "bg-amber-100 text-amber-800"
                            : "bg-slate-100 text-slate-800"
                        }`}
                      >
                        {inv.status || "مسودة"}
                      </span>
                    </td>
                    <td className="p-2 text-center" onClick={(e) => e.stopPropagation()}>
                      <PetraButton
                        variant="ghost"
                        size="sm"
                        onClick={() => inv.id && handleOpenInvoice(inv.id, inv.invoiceNumber)}
                      >
                        عرض
                      </PetraButton>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {/* Pagination */}
        <div className="bg-[#EEF3F8] border-t border-[#CBD8E5] p-2 flex items-center justify-between text-xs text-slate-600">
          <span>صفحة {page}</span>
          <div className="flex gap-1">
            <PetraButton
              variant="secondary"
              size="sm"
              disabled={page <= 1}
              onClick={() => setPage((p) => Math.max(1, p - 1))}
            >
              السابق
            </PetraButton>
            <PetraButton
              variant="secondary"
              size="sm"
              disabled={invoices.length < 50}
              onClick={() => setPage((p) => p + 1)}
            >
              التالي
            </PetraButton>
          </div>
        </div>
      </div>
    </div>
  );
}
