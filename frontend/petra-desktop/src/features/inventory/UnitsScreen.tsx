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

interface UnitItem {
  id?: string;
  code?: string;
  nameArabic?: string;
  nameEnglish?: string | null;
  symbol?: string;
  decimalPlaces?: number;
  isActive?: boolean;
}

export default function UnitsScreen() {
  const api = useApi();
  const companyId = useSession((s) => s.companyId);
  const branchId = useSession((s) => s.branchId);

  const [units, setUnits] = useState<UnitItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // New Unit Dialog
  const [isNewDialogOpen, setIsNewDialogOpen] = useState(false);
  const [code, setCode] = useState("");
  const [nameArabic, setNameArabic] = useState("");
  const [nameEnglish, setNameEnglish] = useState("");
  const [symbol, setSymbol] = useState("");
  const [decimalPlaces, setDecimalPlaces] = useState<number>(0);
  const [creating, setCreating] = useState(false);

  const loadUnits = useCallback(async () => {
    if (!companyId || !branchId) return;
    setLoading(true);
    setError(null);
    try {
      const res = await api.GET(
        "/api/v1/companies/{companyId}/branches/{branchId}/inventory/units",
        {
          params: { path: { companyId, branchId } },
        }
      );
      if (res.error) {
        setError(JSON.stringify(res.error));
      } else if (res.data) {
        setUnits(res.data);
      }
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setLoading(false);
    }
  }, [api, companyId, branchId]);

  useEffect(() => {
    loadUnits();
  }, [loadUnits]);

  const handleCreateUnit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!companyId || !branchId) return;
    if (!code.trim() || !nameArabic.trim() || !symbol.trim()) {
      toast.error("رمز واسم ورمز الوحدة المختصر مطلوبان");
      return;
    }

    setCreating(true);
    try {
      const res = await api.POST(
        "/api/v1/companies/{companyId}/branches/{branchId}/inventory/units",
        {
          params: { path: { companyId, branchId } },
          body: {
            code: code.trim(),
            nameArabic: nameArabic.trim(),
            nameEnglish: nameEnglish.trim() || null,
            symbol: symbol.trim(),
            decimalPlaces: Number(decimalPlaces) || 0,
          },
        }
      );

      if (res.error) {
        toast.error(JSON.stringify(res.error));
      } else {
        toast.success("تم إنشاء الوحدة بنجاح");
        setIsNewDialogOpen(false);
        setCode("");
        setNameArabic("");
        setNameEnglish("");
        setSymbol("");
        setDecimalPlaces(0);
        loadUnits();
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
            وحدات القياس (INV-03)
          </h2>
          <span className="text-xs text-slate-500 bg-slate-100 px-2 py-0.5 rounded">
            العدد: {units.length}
          </span>
        </div>
        <div className="flex items-center gap-2">
          <PetraButton
            variant="secondary"
            onClick={() => loadUnits()}
            loading={loading}
          >
            تحديث
          </PetraButton>
          <PetraButton
            variant="primary"
            onClick={() => setIsNewDialogOpen(true)}
            shortcut="F2"
          >
            + إضافة وحدة
          </PetraButton>
        </div>
      </div>

      {/* Main Table */}
      <div className="flex-1 bg-white border border-[#CBD8E5] rounded overflow-hidden flex flex-col">
        {loading ? (
          <PetraLoadingSkeleton rows={6} />
        ) : error ? (
          <PetraErrorDetails message={error} onRetry={loadUnits} />
        ) : units.length === 0 ? (
          <PetraEmptyState
            title="لا توجد وحدات قياس"
            description="قم بإضافة وحدات القياس (قطعة، صندوق، كغم...) لربطها بالمواد."
            actionLabel="+ إضافة وحدة قياس"
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
                  <th className="p-2 border-l border-slate-700 w-24">الرمز المختصر</th>
                  <th className="p-2 border-l border-slate-700 w-24">المنازل العشرية</th>
                  <th className="p-2 w-28 text-center">الحالة</th>
                </tr>
              </thead>
              <tbody>
                {units.map((u, idx) => (
                  <tr
                    key={u.id || idx}
                    className={`border-b border-[#CBD8E5] hover:bg-[#E2ECF5] ${
                      idx % 2 === 1 ? "bg-[#EFF5FC]" : "bg-white"
                    }`}
                  >
                    <td className="p-2 font-mono font-bold text-[#087E87]">
                      {u.code || "—"}
                    </td>
                    <td className="p-2 font-medium text-[#253447]">
                      {u.nameArabic || "—"}
                    </td>
                    <td className="p-2 text-slate-500 font-mono text-xs">
                      {u.nameEnglish || "—"}
                    </td>
                    <td className="p-2 font-mono text-slate-700">
                      {u.symbol || "—"}
                    </td>
                    <td className="p-2 font-mono text-center">
                      {u.decimalPlaces ?? 0}
                    </td>
                    <td className="p-2 text-center">
                      <span
                        className={`text-xs px-2 py-0.5 rounded font-bold ${
                          u.isActive !== false
                            ? "bg-green-100 text-green-800"
                            : "bg-red-100 text-red-800"
                        }`}
                      >
                        {u.isActive !== false ? "نشط" : "معطل"}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* New Unit Modal */}
      <PetraDialog
        open={isNewDialogOpen}
        onClose={() => setIsNewDialogOpen(false)}
        title="إضافة وحدة قياس جديدة (INV-03)"
      >
        <form onSubmit={handleCreateUnit} className="space-y-3">
          <PetraField label="رمز الوحدة" required>
            <input
              type="text"
              value={code}
              onChange={(e) => setCode(e.target.value)}
              placeholder="مثال: PCS"
              required
              className="w-full border border-[#CBD8E5] rounded px-3 py-1.5 text-sm bg-white focus:border-[#087E87] outline-none font-mono"
            />
          </PetraField>

          <PetraField label="الاسم العربي" required>
            <input
              type="text"
              value={nameArabic}
              onChange={(e) => setNameArabic(e.target.value)}
              placeholder="قطعة"
              required
              className="w-full border border-[#CBD8E5] rounded px-3 py-1.5 text-sm bg-white focus:border-[#087E87] outline-none"
            />
          </PetraField>

          <PetraField label="الاسم الأجنبي">
            <input
              type="text"
              value={nameEnglish}
              onChange={(e) => setNameEnglish(e.target.value)}
              placeholder="Piece"
              className="w-full border border-[#CBD8E5] rounded px-3 py-1.5 text-sm bg-white focus:border-[#087E87] outline-none font-mono"
            />
          </PetraField>

          <div className="grid grid-cols-2 gap-3">
            <PetraField label="الرمز المختصر" required>
              <input
                type="text"
                value={symbol}
                onChange={(e) => setSymbol(e.target.value)}
                placeholder="ق.ط"
                required
                className="w-full border border-[#CBD8E5] rounded px-3 py-1.5 text-sm bg-white focus:border-[#087E87] outline-none"
              />
            </PetraField>

            <PetraField label="المنازل العشرية">
              <input
                type="number"
                min="0"
                max="6"
                value={decimalPlaces}
                onChange={(e) => setDecimalPlaces(Number(e.target.value))}
                className="w-full border border-[#CBD8E5] rounded px-3 py-1.5 text-sm bg-white focus:border-[#087E87] outline-none font-mono"
              />
            </PetraField>
          </div>

          <div className="flex justify-end gap-2 pt-3 border-t border-[#CBD8E5]">
            <PetraButton
              variant="secondary"
              onClick={() => setIsNewDialogOpen(false)}
              disabled={creating}
            >
              إلغاء
            </PetraButton>
            <PetraButton variant="primary" type="submit" loading={creating}>
              حفظ الوحدة
            </PetraButton>
          </div>
        </form>
      </PetraDialog>
    </div>
  );
}
