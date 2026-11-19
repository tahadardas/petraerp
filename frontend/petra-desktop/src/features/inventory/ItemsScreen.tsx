import { useState, useEffect, useCallback } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { useWorkspace } from "@/workspace/workspaceStore";
import {
  PetraButton,
  PetraField,
  PetraDialog,
  PetraEmptyState,
  PetraLoadingSkeleton,
  PetraErrorDetails,
} from "@/components/ui";
import { toast } from "@/components/ui/toastStore";
import { useT } from "@/i18n/messages";

interface ItemRow {
  id?: string;
  code?: string;
  nameArabic?: string;
  nameEnglish?: string | null;
  baseUnitId?: string;
  currentStock: string;
  isActive?: boolean;
}

export default function ItemsScreen() {
  const t = useT();
  const api = useApi();
  const companyId = useSession((s) => s.companyId);
  const branchId = useSession((s) => s.branchId);
  const open = useWorkspace((s) => s.open);

  const [items, setItems] = useState<ItemRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const [totalCount, setTotalCount] = useState(0);

  // New Item Dialog
  const [isNewDialogOpen, setIsNewDialogOpen] = useState(false);
  const [newCode, setNewCode] = useState("");
  const [newNameAr, setNewNameAr] = useState("");
  const [newNameEn, setNewNameEn] = useState("");
  const [creating, setCreating] = useState(false);

  const loadItems = useCallback(async () => {
    if (!companyId || !branchId) return;
    setLoading(true);
    setError(null);
    try {
      const res = await api.GET(
        "/api/v1/companies/{companyId}/branches/{branchId}/inventory/items",
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
        setItems(res.data.items || []);
        setTotalCount(res.data.totalCount || 0);
      }
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setLoading(false);
    }
  }, [api, companyId, branchId, page, search]);

  useEffect(() => {
    loadItems();
  }, [loadItems]);

  const handleCreateItem = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!companyId || !branchId) return;
    if (!newCode.trim() || !newNameAr.trim()) {
      toast.error(t("required"));
      return;
    }

    setCreating(true);
    try {
      const res = await api.POST(
        "/api/v1/companies/{companyId}/branches/{branchId}/inventory/items",
        {
          params: { path: { companyId, branchId } },
          body: {
            code: newCode.trim(),
            nameArabic: newNameAr.trim(),
            nameEnglish: newNameEn.trim() || null,
          },
        }
      );

      if (res.error) {
        toast.error(JSON.stringify(res.error));
      } else {
        toast.success("تم إنشاء المادة بنجاح");
        setIsNewDialogOpen(false);
        setNewCode("");
        setNewNameAr("");
        setNewNameEn("");
        loadItems();
      }
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : String(err));
    } finally {
      setCreating(false);
    }
  };

  return (
    <div className="petra-screen p-4 flex flex-col h-full bg-[#EEF3F8]">
      {/* Top Header & Actions */}
      <div className="flex items-center justify-between bg-white p-3 border border-[#CBD8E5] rounded mb-3">
        <div className="flex items-center gap-3">
          <h2 className="text-lg font-bold text-[#17334C]">دليل المواد (INV-01)</h2>
          <span className="text-xs text-slate-500 bg-slate-100 px-2 py-0.5 rounded">
            إجمالي: {totalCount}
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
            placeholder="بحث بالرمز أو الاسم..."
            className="border border-[#CBD8E5] rounded px-3 py-1 text-sm bg-[#EEF3F8] focus:bg-white focus:border-[#087E87] outline-none w-64"
          />
          <PetraButton
            variant="secondary"
            onClick={() => loadItems()}
            loading={loading}
          >
            تحديث
          </PetraButton>
          <PetraButton
            variant="primary"
            onClick={() => setIsNewDialogOpen(true)}
            shortcut="F2"
          >
            + إضافة مادة
          </PetraButton>
        </div>
      </div>

      {/* Main Table Content */}
      <div className="flex-1 bg-white border border-[#CBD8E5] rounded overflow-hidden flex flex-col">
        {loading ? (
          <PetraLoadingSkeleton rows={10} />
        ) : error ? (
          <PetraErrorDetails message={error} onRetry={loadItems} />
        ) : items.length === 0 ? (
          <PetraEmptyState
            title="لا توجد مواد مسجلة"
            description="يمكنك البدء بإضافة المواد الجديدة لتفعيل حركات المستودعات والفواتير."
            actionLabel="+ إضافة مادة جديدة"
            onAction={() => setIsNewDialogOpen(true)}
          />
        ) : (
          <div className="flex-1 overflow-auto">
            <table className="w-full text-sm border-collapse text-right">
              <thead className="bg-[#17334C] text-white sticky top-0 z-10 select-none">
                <tr>
                  <th className="p-2 border-l border-slate-700 w-24">الرمز</th>
                  <th className="p-2 border-l border-slate-700">الاسم العربي</th>
                  <th className="p-2 border-l border-slate-700">الاسم الأجنبي</th>
                  <th className="p-2 border-l border-slate-700 w-32">المخزون الحالي</th>
                  <th className="p-2 border-l border-slate-700 w-24">الحالة</th>
                  <th className="p-2 w-32 text-center">إجراءات</th>
                </tr>
              </thead>
              <tbody>
                {items.map((item, idx) => (
                  <tr
                    key={item.id || idx}
                    className={`border-b border-[#CBD8E5] hover:bg-[#E2ECF5] cursor-pointer ${
                      idx % 2 === 1 ? "bg-[#EFF5FC]" : "bg-white"
                    }`}
                  >
                    <td className="p-2 font-mono font-bold text-[#087E87]">
                      {item.code || "—"}
                    </td>
                    <td className="p-2 font-medium text-[#253447]">
                      {item.nameArabic || "—"}
                    </td>
                    <td className="p-2 text-slate-500 font-mono text-xs">
                      {item.nameEnglish || "—"}
                    </td>
                    <td className="p-2 font-mono font-bold text-[#17334C]">
                      {item.currentStock || "0"}
                    </td>
                    <td className="p-2">
                      <span
                        className={`text-xs px-2 py-0.5 rounded font-bold ${
                          item.isActive !== false
                            ? "bg-green-100 text-green-800"
                            : "bg-red-100 text-red-800"
                        }`}
                      >
                        {item.isActive !== false ? "نشط" : "معطل"}
                      </span>
                    </td>
                    <td className="p-2 text-center">
                      <PetraButton
                        variant="ghost"
                        size="sm"
                        onClick={() => {
                          open({
                            key: `item-movements-${item.id}`,
                            title: `حركة المادة: ${item.nameArabic}`,
                            screen: "reports",
                            params: { itemId: item.id || "" },
                          });
                        }}
                      >
                        كشف الحركة
                      </PetraButton>
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
              disabled={items.length < 50}
              onClick={() => setPage((p) => p + 1)}
            >
              التالي
            </PetraButton>
          </div>
        </div>
      </div>

      {/* New Item Modal */}
      <PetraDialog
        open={isNewDialogOpen}
        onClose={() => setIsNewDialogOpen(false)}
        title="إضافة مادة جديدة (INV-01)"
      >
        <form onSubmit={handleCreateItem} className="space-y-3">
          <PetraField label="رمز المادة" required>
            <input
              type="text"
              value={newCode}
              onChange={(e) => setNewCode(e.target.value)}
              placeholder="مثال: ITEM-001"
              required
              className="w-full border border-[#CBD8E5] rounded px-3 py-1.5 text-sm bg-white focus:border-[#087E87] outline-none font-mono"
            />
          </PetraField>

          <PetraField label="الاسم العربي" required>
            <input
              type="text"
              value={newNameAr}
              onChange={(e) => setNewNameAr(e.target.value)}
              placeholder="اسم المادة بالعربية"
              required
              className="w-full border border-[#CBD8E5] rounded px-3 py-1.5 text-sm bg-white focus:border-[#087E87] outline-none"
            />
          </PetraField>

          <PetraField label="الاسم الأجنبي">
            <input
              type="text"
              value={newNameEn}
              onChange={(e) => setNewNameEn(e.target.value)}
              placeholder="Item English Name"
              className="w-full border border-[#CBD8E5] rounded px-3 py-1.5 text-sm bg-white focus:border-[#087E87] outline-none font-mono"
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
              حفظ المادة
            </PetraButton>
          </div>
        </form>
      </PetraDialog>
    </div>
  );
}
