import { useState, useEffect, useCallback } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import {
  PetraButton,
  PetraField,
  PetraDialog,
  PetraConfirm,
  PetraEmptyState,
  PetraLoadingSkeleton,
  PetraErrorDetails,
} from "@/components/ui";
import { toast } from "@/components/ui/toastStore";

interface InventoryDoc {
  id?: string;
  number?: string | null;
  kind?: "Transfer" | "Count" | "InternalIssue";
  state?: "Draft" | "Posted" | "Reversed";
  dateUtc?: string;
  reason?: string;
}

export default function InventoryOperationsScreen() {
  const api = useApi();
  const companyId = useSession((s) => s.companyId);
  const branchId = useSession((s) => s.branchId);

  const [docs, setDocs] = useState<InventoryDoc[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [page, setPage] = useState(1);
  const [totalCount, setTotalCount] = useState(0);
  const [search, setSearch] = useState("");

  // Post Confirm
  const [selectedDoc, setSelectedDoc] = useState<InventoryDoc | null>(null);
  const [isConfirmPostOpen, setIsConfirmPostOpen] = useState(false);

  // New Document Dialog
  const [isNewDialogOpen, setIsNewDialogOpen] = useState(false);
  const [newKind, setNewKind] = useState<"Transfer" | "Count" | "InternalIssue">(
    "Transfer"
  );
  const [warehouseId, setWarehouseId] = useState("");
  const [targetWarehouseId, setTargetWarehouseId] = useState("");
  const [reason, setReason] = useState("");
  const [warehouses, setWarehouses] = useState<
    Array<{ id?: string; nameArabic?: string }>
  >([]);
  const [creating, setCreating] = useState(false);

  // Load Warehouses
  useEffect(() => {
    if (!companyId || !branchId) return;
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
          if (res.data.length > 0 && res.data[0]?.id) {
            setWarehouseId(res.data[0].id);
          }
        }
      });
  }, [api, companyId, branchId]);

  const loadDocuments = useCallback(async () => {
    if (!companyId || !branchId) return;
    setLoading(true);
    setError(null);
    try {
      const res = await api.GET(
        "/api/v1/companies/{companyId}/branches/{branchId}/inventory/documents",
        {
          params: {
            path: { companyId, branchId },
            query: {
              page,
              pageSize: 50,
              ...(search.trim() ? { search: search.trim() } : {}),
            },
          },
        }
      );

      if (res.error) {
        setError(JSON.stringify(res.error));
      } else if (res.data) {
        setDocs(res.data.items || []);
        setTotalCount(res.data.totalCount || 0);
      }
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setLoading(false);
    }
  }, [api, companyId, branchId, page, search]);

  useEffect(() => {
    loadDocuments();
  }, [loadDocuments]);

  const handlePostDoc = async () => {
    if (!selectedDoc?.id || !companyId || !branchId) return;
    try {
      const res = await api.POST(
        "/api/v1/companies/{companyId}/branches/{branchId}/inventory/documents/{id}/post",
        {
          params: { path: { companyId, branchId, id: selectedDoc.id } },
          body: {
            expectedVersion: "1",
            idempotencyKey: `post-inv-${selectedDoc.id}-${Date.now()}`,
            reason: "ترحيل مستند مخزني من الواجهة",
          },
        }
      );

      if (res.error) {
        toast.error(JSON.stringify(res.error));
      } else {
        toast.success("تم ترحيل المستند المخزني وتحديث المخزون بنجاح");
        setIsConfirmPostOpen(false);
        setSelectedDoc(null);
        loadDocuments();
      }
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : String(err));
    }
  };

  const handleCreateDocument = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!companyId || !branchId) return;
    if (!warehouseId || !reason.trim()) {
      toast.error("المستودع وسبب العملية مطلوبان");
      return;
    }

    setCreating(true);
    try {
      const res = await api.POST(
        "/api/v1/companies/{companyId}/branches/{branchId}/inventory/documents",
        {
          params: { path: { companyId, branchId } },
          body: {
            kind: newKind,
            warehouseId,
            targetWarehouseId: newKind === "Transfer" ? targetWarehouseId || null : null,
            dateUtc: new Date().toISOString(),
            reason: reason.trim(),
            lines: [],
          },
        }
      );

      if (res.error) {
        toast.error(JSON.stringify(res.error));
      } else {
        toast.success("تم إنشاء المستند المخزني بنجاح");
        setIsNewDialogOpen(false);
        setReason("");
        loadDocuments();
      }
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : String(err));
    } finally {
      setCreating(false);
    }
  };

  const getKindLabel = (kind?: string) => {
    switch (kind) {
      case "Transfer":
        return "مناقلة بين مستودعين";
      case "InternalIssue":
        return "إخراج مخزني داخلي";
      case "Count":
        return "جرد وتسوية مخزنية";
      default:
        return kind || "مستند مخزني";
    }
  };

  return (
    <div className="petra-screen p-4 flex flex-col h-full bg-[#EEF3F8]">
      {/* Top Header */}
      <div className="flex items-center justify-between bg-white p-3 border border-[#CBD8E5] rounded mb-3">
        <div className="flex items-center gap-3">
          <h2 className="text-lg font-bold text-[#17334C]">
            حركات ومستندات المستودعات (INV-05)
          </h2>
          <span className="text-xs text-slate-500 bg-slate-100 px-2 py-0.5 rounded">
            إجمالي المستندات: {totalCount}
          </span>
        </div>
        <div className="flex items-center gap-2">
          <input
            type="search"
            value={search}
            onChange={(e) => {
              setSearch(e.target.value);
              setPage(1);
            }}
            placeholder="بحث في المستندات..."
            className="border border-[#CBD8E5] rounded px-3 py-1 text-sm bg-[#EEF3F8] focus:bg-white focus:border-[#087E87] outline-none w-56"
          />
          <PetraButton
            variant="secondary"
            onClick={() => loadDocuments()}
            loading={loading}
          >
            تحديث
          </PetraButton>
          <PetraButton
            variant="primary"
            onClick={() => setIsNewDialogOpen(true)}
            shortcut="F2"
          >
            + مستند مخزني جديد
          </PetraButton>
        </div>
      </div>

      {/* Main Table */}
      <div className="flex-1 bg-white border border-[#CBD8E5] rounded overflow-hidden flex flex-col">
        {loading ? (
          <PetraLoadingSkeleton rows={8} />
        ) : error ? (
          <PetraErrorDetails message={error} onRetry={loadDocuments} />
        ) : docs.length === 0 ? (
          <PetraEmptyState
            title="لا توجد مستندات مخزنية مسجلة"
            description="قم بإنشاء مستند مناقلة أو تسوية أو إخراج داخلي لتحديث أرصدة المستودعات."
            actionLabel="+ مستند جديد"
            onAction={() => setIsNewDialogOpen(true)}
          />
        ) : (
          <div className="flex-1 overflow-auto">
            <table className="w-full text-sm border-collapse text-right">
              <thead className="bg-[#17334C] text-white sticky top-0 z-10 select-none">
                <tr>
                  <th className="p-2 border-l border-slate-700 w-32">رقم المستند</th>
                  <th className="p-2 border-l border-slate-700 w-32">التاريخ</th>
                  <th className="p-2 border-l border-slate-700 w-44">نوع الحركة</th>
                  <th className="p-2 border-l border-slate-700">البيان / السبب</th>
                  <th className="p-2 border-l border-slate-700 w-28 text-center">الحالة</th>
                  <th className="p-2 w-32 text-center">إجراءات</th>
                </tr>
              </thead>
              <tbody>
                {docs.map((d, idx) => (
                  <tr
                    key={d.id || idx}
                    className={`border-b border-[#CBD8E5] hover:bg-[#E2ECF5] ${
                      idx % 2 === 1 ? "bg-[#EFF5FC]" : "bg-white"
                    }`}
                  >
                    <td className="p-2 font-mono font-bold text-[#087E87]">
                      {d.number || "—"}
                    </td>
                    <td className="p-2 font-mono text-xs text-slate-600">
                      {d.dateUtc ? d.dateUtc.slice(0, 10) : "—"}
                    </td>
                    <td className="p-2 font-medium text-[#17334C]">
                      {getKindLabel(d.kind)}
                    </td>
                    <td className="p-2 text-slate-700 text-xs">
                      {d.reason || "—"}
                    </td>
                    <td className="p-2 text-center">
                      <span
                        className={`text-xs px-2 py-0.5 rounded font-bold ${
                          d.state === "Posted"
                            ? "bg-green-100 text-green-800"
                            : d.state === "Draft"
                            ? "bg-amber-100 text-amber-800"
                            : "bg-red-100 text-red-800"
                        }`}
                      >
                        {d.state === "Posted"
                          ? "مرحل"
                          : d.state === "Draft"
                          ? "مسودة"
                          : "معكوس"}
                      </span>
                    </td>
                    <td className="p-2 text-center">
                      {d.state === "Draft" ? (
                        <PetraButton
                          variant="primary"
                          size="sm"
                          onClick={() => {
                            setSelectedDoc(d);
                            setIsConfirmPostOpen(true);
                          }}
                        >
                          ترحيل
                        </PetraButton>
                      ) : (
                        <span className="text-xs text-slate-400">مكتمل</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {/* Pagination Bar */}
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
              disabled={docs.length < 50}
              onClick={() => setPage((p) => p + 1)}
            >
              التالي
            </PetraButton>
          </div>
        </div>
      </div>

      {/* New Document Modal */}
      <PetraDialog
        open={isNewDialogOpen}
        onClose={() => setIsNewDialogOpen(false)}
        title="إنشاء مستند مخزني جديد (INV-05)"
      >
        <form onSubmit={handleCreateDocument} className="space-y-3">
          <PetraField label="نوع المستند" required>
            <select
              value={newKind}
              onChange={(e) =>
                setNewKind(e.target.value as "Transfer" | "Count" | "InternalIssue")
              }
              className="w-full border border-[#CBD8E5] rounded px-2.5 py-1.5 text-sm bg-white focus:border-[#087E87] outline-none"
            >
              <option value="Transfer">مناقلة بين مستودعين (Transfer)</option>
              <option value="InternalIssue">إخراج مخزني داخلي (Internal Issue)</option>
              <option value="Count">تسوية جردية (Inventory Count)</option>
            </select>
          </PetraField>

          <PetraField label="المستودع المصدر" required>
            <select
              value={warehouseId}
              onChange={(e) => setWarehouseId(e.target.value)}
              className="w-full border border-[#CBD8E5] rounded px-2.5 py-1.5 text-sm bg-white focus:border-[#087E87] outline-none"
            >
              <option value="">-- اختر المستودع --</option>
              {warehouses.map((w) => (
                <option key={w.id} value={w.id}>
                  {w.nameArabic}
                </option>
              ))}
            </select>
          </PetraField>

          {newKind === "Transfer" ? (
            <PetraField label="المستودع الوجهة" required>
              <select
                value={targetWarehouseId}
                onChange={(e) => setTargetWarehouseId(e.target.value)}
                className="w-full border border-[#CBD8E5] rounded px-2.5 py-1.5 text-sm bg-white focus:border-[#087E87] outline-none"
              >
                <option value="">-- اختر المستودع الوجهة --</option>
                {warehouses
                  .filter((w) => w.id !== warehouseId)
                  .map((w) => (
                    <option key={w.id} value={w.id}>
                      {w.nameArabic}
                    </option>
                  ))}
              </select>
            </PetraField>
          ) : null}

          <PetraField label="البيان / السبب" required>
            <input
              type="text"
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              placeholder="سبب المناقلة أو الصرف..."
              required
              className="w-full border border-[#CBD8E5] rounded px-3 py-1.5 text-sm bg-white focus:border-[#087E87] outline-none"
            />
          </PetraField>

          <div className="flex justify-end gap-2 pt-3 border-t border-[#CBD8E5]">
            <PetraButton
              variant="secondary"
              onClick={() => setIsNewDialogOpen(false)}
              disabled={creating}
            >
              إلغاء
            </PetraButton>
            <PetraButton variant="primary" type="submit" loading={creating}>
              إنشاء المستند
            </PetraButton>
          </div>
        </form>
      </PetraDialog>

      {/* Post Confirm Modal */}
      <PetraConfirm
        open={isConfirmPostOpen}
        onCancel={() => setIsConfirmPostOpen(false)}
        onConfirm={handlePostDoc}
        title="ترحيل المستند المخزني"
        message={`هل أنت متأكد من ترحيل المستند المخزني ${selectedDoc?.number || ""}؟ سيتم تطبيق الأثر المخزني الفعلي بشكل غير قابل للتعديل المباشر.`}
        confirmLabel="تأكيد الترحيل"
        destructive={false}
      />
    </div>
  );
}
