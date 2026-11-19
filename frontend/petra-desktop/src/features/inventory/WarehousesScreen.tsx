import { useState, useEffect, useCallback } from "react";
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

interface WarehouseItem {
  id?: string;
  code?: string;
  nameArabic?: string;
  nameEnglish?: string | null;
  location?: string | null;
  isActive?: boolean;
}

export default function WarehousesScreen() {
  const api = useApi();
  const companyId = useSession((s) => s.companyId);
  const branchId = useSession((s) => s.branchId);

  const [warehouses, setWarehouses] = useState<WarehouseItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // New Warehouse Dialog
  const [isNewDialogOpen, setIsNewDialogOpen] = useState(false);
  const [code, setCode] = useState("");
  const [nameArabic, setNameArabic] = useState("");
  const [nameEnglish, setNameEnglish] = useState("");
  const [location, setLocation] = useState("");
  const [creating, setCreating] = useState(false);

  const loadWarehouses = useCallback(async () => {
    if (!companyId || !branchId) return;
    setLoading(true);
    setError(null);
    try {
      const res = await api.GET(
        "/api/v1/companies/{companyId}/branches/{branchId}/inventory/warehouses",
        {
          params: { path: { companyId, branchId } },
        }
      );
      if (res.error) {
        setError(JSON.stringify(res.error));
      } else if (res.data) {
        setWarehouses(res.data);
      }
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setLoading(false);
    }
  }, [api, companyId, branchId]);

  useEffect(() => {
    loadWarehouses();
  }, [loadWarehouses]);

  const handleCreateWarehouse = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!companyId || !branchId) return;
    if (!code.trim() || !nameArabic.trim()) {
      toast.error("رمز واسم المستودع مطلوبان");
      return;
    }

    setCreating(true);
    try {
      const res = await api.POST(
        "/api/v1/companies/{companyId}/branches/{branchId}/inventory/warehouses",
        {
          params: { path: { companyId, branchId } },
          body: {
            code: code.trim(),
            nameArabic: nameArabic.trim(),
            nameEnglish: nameEnglish.trim() || null,
            location: location.trim() || null,
          },
        }
      );

      if (res.error) {
        toast.error(JSON.stringify(res.error));
      } else {
        toast.success("تم إنشاء المستودع بنجاح");
        setIsNewDialogOpen(false);
        setCode("");
        setNameArabic("");
        setNameEnglish("");
        setLocation("");
        loadWarehouses();
      }
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : String(err));
    } finally {
      setCreating(false);
    }
  };

  return (
    <div className="petra-screen p-4 flex flex-col h-full bg-[#EEF3F8]">
      {/* Header Bar */}
      <div className="flex items-center justify-between bg-white p-3 border border-[#CBD8E5] rounded mb-3">
        <div className="flex items-center gap-3">
          <h2 className="text-lg font-bold text-[#17334C]">
            دليل المستودعات (INV-04)
          </h2>
          <span className="text-xs text-slate-500 bg-slate-100 px-2 py-0.5 rounded">
            العدد: {warehouses.length}
          </span>
        </div>
        <div className="flex items-center gap-2">
          <PetraButton
            variant="secondary"
            onClick={() => loadWarehouses()}
            loading={loading}
          >
            تحديث
          </PetraButton>
          <PetraButton
            variant="primary"
            onClick={() => setIsNewDialogOpen(true)}
            shortcut="F2"
          >
            + إضافة مستودع
          </PetraButton>
        </div>
      </div>

      {/* Main Table */}
      <div className="flex-1 bg-white border border-[#CBD8E5] rounded overflow-hidden flex flex-col">
        {loading ? (
          <PetraLoadingSkeleton rows={6} />
        ) : error ? (
          <PetraErrorDetails message={error} onRetry={loadWarehouses} />
        ) : warehouses.length === 0 ? (
          <PetraEmptyState
            title="لا توجد مستودعات معرّفة"
            description="قم بإضافة مستودع رئيسي لتمكين حركات التخزين والفواتير."
            actionLabel="+ إضافة مستودع جديد"
            onAction={() => setIsNewDialogOpen(true)}
          />
        ) : (
          <div className="flex-1 overflow-auto">
            <table className="w-full text-sm border-collapse text-right">
              <thead className="bg-[#17334C] text-white sticky top-0 z-10 select-none">
                <tr>
                  <th className="p-2 border-l border-slate-700 w-28">الرمز</th>
                  <th className="p-2 border-l border-slate-700">الاسم العربي</th>
                  <th className="p-2 border-l border-slate-700">الاسم الأجنبي</th>
                  <th className="p-2 border-l border-slate-700">الموقع / العنوان</th>
                  <th className="p-2 w-28 text-center">الحالة</th>
                </tr>
              </thead>
              <tbody>
                {warehouses.map((wh, idx) => (
                  <tr
                    key={wh.id || idx}
                    className={`border-b border-[#CBD8E5] hover:bg-[#E2ECF5] ${
                      idx % 2 === 1 ? "bg-[#EFF5FC]" : "bg-white"
                    }`}
                  >
                    <td className="p-2 font-mono font-bold text-[#087E87]">
                      {wh.code || "—"}
                    </td>
                    <td className="p-2 font-medium text-[#253447]">
                      {wh.nameArabic || "—"}
                    </td>
                    <td className="p-2 text-slate-500 font-mono text-xs">
                      {wh.nameEnglish || "—"}
                    </td>
                    <td className="p-2 text-slate-600 text-xs">
                      {wh.location || "—"}
                    </td>
                    <td className="p-2 text-center">
                      <span
                        className={`text-xs px-2 py-0.5 rounded font-bold ${
                          wh.isActive !== false
                            ? "bg-green-100 text-green-800"
                            : "bg-red-100 text-red-800"
                        }`}
                      >
                        {wh.isActive !== false ? "نشط" : "معطل"}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* New Warehouse Modal */}
      <PetraDialog
        open={isNewDialogOpen}
        onClose={() => setIsNewDialogOpen(false)}
        title="إضافة مستودع جديد (INV-04)"
      >
        <form onSubmit={handleCreateWarehouse} className="space-y-3">
          <PetraField label="رمز المستودع" required>
            <input
              type="text"
              value={code}
              onChange={(e) => setCode(e.target.value)}
              placeholder="مثال: WH-01"
              required
              className="w-full border border-[#CBD8E5] rounded px-3 py-1.5 text-sm bg-white focus:border-[#087E87] outline-none font-mono"
            />
          </PetraField>

          <PetraField label="الاسم العربي" required>
            <input
              type="text"
              value={nameArabic}
              onChange={(e) => setNameArabic(e.target.value)}
              placeholder="المستودع الرئيسي"
              required
              className="w-full border border-[#CBD8E5] rounded px-3 py-1.5 text-sm bg-white focus:border-[#087E87] outline-none"
            />
          </PetraField>

          <PetraField label="الاسم الأجنبي">
            <input
              type="text"
              value={nameEnglish}
              onChange={(e) => setNameEnglish(e.target.value)}
              placeholder="Main Warehouse"
              className="w-full border border-[#CBD8E5] rounded px-3 py-1.5 text-sm bg-white focus:border-[#087E87] outline-none font-mono"
            />
          </PetraField>

          <PetraField label="الموقع / العنوان">
            <input
              type="text"
              value={location}
              onChange={(e) => setLocation(e.target.value)}
              placeholder="المنطقة الصناعية - المستودع 4"
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
              حفظ المستودع
            </PetraButton>
          </div>
        </form>
      </PetraDialog>
    </div>
  );
}
