import { useState, useEffect, useCallback, useMemo } from "react";
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

interface CostCenterNode {
  id: string;
  code: string;
  nameArabic: string;
  nameEnglish?: string | null;
  parentId?: string | null;
  centerType?: number;
  description?: string | null;
}

export default function CostCentersScreen() {
  const api = useApi();
  const companyId = useSession((s) => s.companyId);
  const branchId = useSession((s) => s.branchId);

  const [centers, setCenters] = useState<CostCenterNode[]>([]);
  const [selectedCenter, setSelectedCenter] = useState<CostCenterNode | null>(
    null
  );
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState("");

  // New Cost Center Dialog
  const [isNewDialogOpen, setIsNewDialogOpen] = useState(false);
  const [code, setCode] = useState("");
  const [nameArabic, setNameArabic] = useState("");
  const [nameEnglish, setNameEnglish] = useState("");
  const [parentId, setParentId] = useState("");
  const [description, setDescription] = useState("");
  const [creating, setCreating] = useState(false);

  const loadCostCenters = useCallback(async () => {
    if (!companyId || !branchId) return;
    setLoading(true);
    setError(null);
    try {
      const res = await api.GET(
        "/api/v1/companies/{companyId}/branches/{branchId}/ledger/chart/cost-centers/tree",
        {
          params: { path: { companyId, branchId } },
        }
      );

      if (res.error) {
        setError(JSON.stringify(res.error));
      } else if (res.data) {
        setCenters(res.data);
        const first = res.data[0];
        if (first && !selectedCenter) {
          setSelectedCenter(first);
        }
      }
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setLoading(false);
    }
  }, [api, companyId, branchId, selectedCenter]);

  useEffect(() => {
    loadCostCenters();
  }, [loadCostCenters]);

  const filteredCenters = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return centers;
    return centers.filter(
      (c) =>
        c.code.toLowerCase().includes(q) ||
        c.nameArabic.toLowerCase().includes(q) ||
        (c.nameEnglish && c.nameEnglish.toLowerCase().includes(q))
    );
  }, [centers, search]);

  const handleCreateCenter = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!companyId || !branchId) return;
    if (!code.trim() || !nameArabic.trim()) {
      toast.error("رمز واسم مركز الكلفة مطلوبان");
      return;
    }

    setCreating(true);
    try {
      const res = await api.POST(
        "/api/v1/companies/{companyId}/branches/{branchId}/ledger/chart/cost-centers",
        {
          params: { path: { companyId, branchId } },
          body: {
            code: code.trim(),
            nameArabic: nameArabic.trim(),
            nameEnglish: nameEnglish.trim() || null,
            parentId: parentId || null,
            description: description.trim() || null,
          },
        }
      );

      if (res.error) {
        toast.error(JSON.stringify(res.error));
      } else {
        toast.success("تم إنشاء مركز الكلفة بنجاح");
        setIsNewDialogOpen(false);
        setCode("");
        setNameArabic("");
        setNameEnglish("");
        setParentId("");
        setDescription("");
        loadCostCenters();
      }
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : String(err));
    } finally {
      setCreating(false);
    }
  };

  return (
    <div className="petra-screen p-4 flex flex-col h-full bg-[#EEF3F8]">
      {/* Top Header */}
      <div className="flex items-center justify-between bg-white p-3 border border-[#CBD8E5] rounded mb-3">
        <div className="flex items-center gap-3">
          <h2 className="text-lg font-bold text-[#17334C]">
            دليل مراكز الكلفة (ACC-09)
          </h2>
          <span className="text-xs text-slate-500 bg-slate-100 px-2 py-0.5 rounded">
            إجمالي المراكز: {centers.length}
          </span>
        </div>
        <div className="flex items-center gap-2">
          <input
            type="search"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="بحث بالرمز أو الاسم..."
            className="border border-[#CBD8E5] rounded px-3 py-1 text-sm bg-[#EEF3F8] focus:bg-white focus:border-[#087E87] outline-none w-64"
          />
          <PetraButton
            variant="secondary"
            onClick={() => loadCostCenters()}
            loading={loading}
          >
            تحديث
          </PetraButton>
          <PetraButton
            variant="primary"
            onClick={() => setIsNewDialogOpen(true)}
            shortcut="F2"
          >
            + مركز كلفة جديد
          </PetraButton>
        </div>
      </div>

      {/* Main Content: Two-Pane Split */}
      <div className="flex-1 flex gap-3 overflow-hidden">
        {/* Left/Right Tree Pane */}
        <div className="w-1/2 bg-white border border-[#CBD8E5] rounded flex flex-col overflow-hidden">
          <div className="bg-[#17334C] text-white p-2.5 font-bold text-sm">
            شجرة مراكز الكلفة
          </div>
          {loading ? (
            <PetraLoadingSkeleton rows={10} />
          ) : error ? (
            <PetraErrorDetails message={error} onRetry={loadCostCenters} />
          ) : filteredCenters.length === 0 ? (
            <PetraEmptyState
              title="لا توجد مراكز كلفة"
              description="قم بإنشاء مراكز الكلفة لتوزيع المصاريف والإيرادات ومتابعة المشاريع."
              actionLabel="+ إضافة مركز كلفة"
              onAction={() => setIsNewDialogOpen(true)}
            />
          ) : (
            <div className="flex-1 overflow-auto p-2 space-y-1">
              {filteredCenters.map((c) => (
                <div
                  key={c.id}
                  onClick={() => setSelectedCenter(c)}
                  className={`p-2 rounded cursor-pointer flex items-center justify-between text-sm transition-colors border ${
                    selectedCenter?.id === c.id
                      ? "bg-[#087E87] text-white border-[#087E87]"
                      : "bg-[#EEF3F8] hover:bg-[#E2ECF5] text-[#253447] border-[#CBD8E5]"
                  }`}
                >
                  <div className="flex items-center gap-2">
                    <span className="font-mono font-bold text-xs">
                      {c.code}
                    </span>
                    <span className="font-medium">{c.nameArabic}</span>
                  </div>
                  {c.nameEnglish ? (
                    <span
                      className={`text-xs font-mono ${
                        selectedCenter?.id === c.id
                          ? "text-teal-100"
                          : "text-slate-500"
                      }`}
                    >
                      {c.nameEnglish}
                    </span>
                  ) : null}
                </div>
              ))}
            </div>
          )}
        </div>

        {/* Selected Center Details Card */}
        <div className="w-1/2 bg-white border border-[#CBD8E5] rounded flex flex-col p-4 overflow-auto">
          <h3 className="text-base font-bold text-[#17334C] border-b border-[#CBD8E5] pb-2 mb-4">
            بطاقة مركز الكلفة
          </h3>

          {selectedCenter ? (
            <div className="space-y-4">
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <span className="text-xs text-slate-500 block">
                    رمز المركز
                  </span>
                  <span className="text-sm font-mono font-bold text-[#087E87]">
                    {selectedCenter.code}
                  </span>
                </div>
                <div>
                  <span className="text-xs text-slate-500 block">
                    النوع / المستوى
                  </span>
                  <span className="text-sm font-medium text-slate-700">
                    {selectedCenter.parentId ? "مركز فرعي" : "مركز رئيسي"}
                  </span>
                </div>
              </div>

              <div>
                <span className="text-xs text-slate-500 block">
                  الاسم العربي
                </span>
                <span className="text-sm font-bold text-[#253447]">
                  {selectedCenter.nameArabic}
                </span>
              </div>

              <div>
                <span className="text-xs text-slate-500 block">
                  الاسم الأجنبي
                </span>
                <span className="text-sm font-mono text-slate-700">
                  {selectedCenter.nameEnglish || "—"}
                </span>
              </div>

              <div>
                <span className="text-xs text-slate-500 block">الوصف / البيان</span>
                <span className="text-sm text-slate-700">
                  {selectedCenter.description || "لا يوجد وصف مدخل."}
                </span>
              </div>

              <div className="pt-4 border-t border-[#CBD8E5] flex gap-2">
                <PetraButton
                  variant="primary"
                  onClick={() => {
                    setParentId(selectedCenter.id);
                    setIsNewDialogOpen(true);
                  }}
                >
                  + إضافة مركز فرعي تابع
                </PetraButton>
              </div>
            </div>
          ) : (
            <div className="text-slate-400 text-center py-12 text-sm">
              اختر مركز كلفة من القائمة لعرض تفاصيله
            </div>
          )}
        </div>
      </div>

      {/* New Cost Center Dialog */}
      <PetraDialog
        open={isNewDialogOpen}
        onClose={() => setIsNewDialogOpen(false)}
        title="إضافة مركز كلفة جديد (ACC-09)"
      >
        <form onSubmit={handleCreateCenter} className="space-y-3">
          <PetraField label="رمز مركز الكلفة" required>
            <input
              type="text"
              value={code}
              onChange={(e) => setCode(e.target.value)}
              placeholder="مثال: CC-101"
              required
              className="w-full border border-[#CBD8E5] rounded px-3 py-1.5 text-sm bg-white focus:border-[#087E87] outline-none font-mono"
            />
          </PetraField>

          <PetraField label="الاسم العربي" required>
            <input
              type="text"
              value={nameArabic}
              onChange={(e) => setNameArabic(e.target.value)}
              placeholder="مثال: مشروع برج الأمل"
              required
              className="w-full border border-[#CBD8E5] rounded px-3 py-1.5 text-sm bg-white focus:border-[#087E87] outline-none"
            />
          </PetraField>

          <PetraField label="الاسم الأجنبي">
            <input
              type="text"
              value={nameEnglish}
              onChange={(e) => setNameEnglish(e.target.value)}
              placeholder="Al-Amal Tower Project"
              className="w-full border border-[#CBD8E5] rounded px-3 py-1.5 text-sm bg-white focus:border-[#087E87] outline-none font-mono"
            />
          </PetraField>

          <PetraField label="المركز الرئيسي (الأب)">
            <select
              value={parentId}
              onChange={(e) => setParentId(e.target.value)}
              className="w-full border border-[#CBD8E5] rounded px-2.5 py-1.5 text-sm bg-white focus:border-[#087E87] outline-none"
            >
              <option value="">-- بدون أب (مركز رئيسي) --</option>
              {centers.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.code} - {c.nameArabic}
                </option>
              ))}
            </select>
          </PetraField>

          <PetraField label="الوصف / الملاحظات">
            <textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="ملاحظات توضيحية حول مركز الكلفة..."
              rows={2}
              className="w-full border border-[#CBD8E5] rounded px-3 py-1.5 text-sm bg-white focus:border-[#087E87] outline-none resize-none"
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
              حفظ مركز الكلفة
            </PetraButton>
          </div>
        </form>
      </PetraDialog>
    </div>
  );
}
