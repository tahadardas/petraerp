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

interface CurrencyItem {
  code?: string;
  nameArabic?: string | null;
  nameEnglish?: string | null;
  isBase?: boolean;
}

export default function CurrenciesScreen() {
  const api = useApi();
  const companyId = useSession((s) => s.companyId);
  const branchId = useSession((s) => s.branchId);

  const [baseCurrencyCode, setBaseCurrencyCode] = useState<string>("USD");
  const [baseCurrencyLocked, setBaseCurrencyLocked] = useState<boolean>(false);
  const [currencies, setCurrencies] = useState<CurrencyItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Enable Currency Dialog
  const [isEnableDialogOpen, setIsEnableDialogOpen] = useState(false);
  const [newCode, setNewCode] = useState("");
  const [newNameAr, setNewNameAr] = useState("");
  const [newNameEn, setNewNameEn] = useState("");
  const [enableReason, setEnableReason] = useState("");
  const [enabling, setEnabling] = useState(false);

  // Exchange Rate Bulletin Dialog
  const [isBulletinDialogOpen, setIsBulletinDialogOpen] = useState(false);
  const [bulletinDate, setBulletinDate] = useState(
    new Date().toISOString().slice(0, 10)
  );
  const [bulletinSource, setBulletinSource] = useState("النشرة الرسمية");
  const [bulletinReason, setBulletinReason] = useState("تحديث الأسعار اليومي");
  const [bulletinRates, setBulletinRates] = useState<Record<string, string>>({});
  const [submittingBulletin, setSubmittingBulletin] = useState(false);

  const loadCurrencySettings = useCallback(async () => {
    if (!companyId || !branchId) return;
    setLoading(true);
    setError(null);
    try {
      const res = await api.GET(
        "/api/v1/companies/{companyId}/branches/{branchId}/currencies",
        {
          params: { path: { companyId, branchId } },
        }
      );

      if (res.error) {
        setError(JSON.stringify(res.error));
      } else if (res.data) {
        setBaseCurrencyCode(res.data.baseCurrencyCode || "USD");
        setBaseCurrencyLocked(Boolean(res.data.baseCurrencyLocked));
        setCurrencies(res.data.currencies || []);
      }
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setLoading(false);
    }
  }, [api, companyId, branchId]);

  useEffect(() => {
    loadCurrencySettings();
  }, [loadCurrencySettings]);

  const handleEnableCurrency = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!companyId || !branchId) return;
    if (!newCode.trim() || !enableReason.trim()) {
      toast.error("رمز العملة وسبب التفعيل مطلوبان");
      return;
    }

    setEnabling(true);
    try {
      const res = await api.POST(
        "/api/v1/companies/{companyId}/branches/{branchId}/currencies",
        {
          params: { path: { companyId, branchId } },
          body: {
            code: newCode.trim().toUpperCase(),
            nameArabic: newNameAr.trim() || null,
            nameEnglish: newNameEn.trim() || null,
            reason: enableReason.trim(),
          },
        }
      );

      if (res.error) {
        toast.error(JSON.stringify(res.error));
      } else {
        toast.success("تم تفعيل العملة للشركة بنجاح");
        setIsEnableDialogOpen(false);
        setNewCode("");
        setNewNameAr("");
        setNewNameEn("");
        setEnableReason("");
        loadCurrencySettings();
      }
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : String(err));
    } finally {
      setEnabling(false);
    }
  };

  const handleRegisterBulletin = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!companyId || !branchId) return;

    const rateList = Object.entries(bulletinRates)
      .filter(([_, rate]) => rate && parseFloat(rate) > 0)
      .map(([currCode, rate]) => ({
        currencyCode: currCode,
        rate: rate.trim(),
      }));

    if (rateList.length === 0) {
      toast.error("يرجى إدخال سعر لعملة واحدة على الأقل");
      return;
    }

    setSubmittingBulletin(true);
    try {
      const res = await api.POST(
        "/api/v1/companies/{companyId}/branches/{branchId}/currencies/rates/bulletin",
        {
          params: { path: { companyId, branchId } },
          body: {
            effectiveDate: bulletinDate,
            source: bulletinSource.trim(),
            reason: bulletinReason.trim(),
            rates: rateList,
          },
        }
      );

      if (res.error) {
        toast.error(JSON.stringify(res.error));
      } else {
        toast.success("تم تسجيل نشرة أسعار الصرف بنجاح");
        setIsBulletinDialogOpen(false);
        setBulletinRates({});
      }
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : String(err));
    } finally {
      setSubmittingBulletin(false);
    }
  };

  return (
    <div className="petra-screen p-4 flex flex-col h-full bg-[#EEF3F8]">
      {/* Top Header */}
      <div className="flex items-center justify-between bg-white p-3 border border-[#CBD8E5] rounded mb-3">
        <div className="flex items-center gap-3">
          <h2 className="text-lg font-bold text-[#17334C]">
            إدارة العملات وأسعار الصرف (FX-01 / FX-02)
          </h2>
          <span className="text-xs text-slate-500 bg-slate-100 px-2 py-0.5 rounded">
            عملة الأساس:{" "}
            <strong className="font-mono text-[#087E87]">
              {baseCurrencyCode}
            </strong>
            {baseCurrencyLocked ? (
              <span className="text-[10px] bg-amber-100 text-amber-800 px-1.5 py-0.5 rounded font-bold mr-2">
                مثبتة
              </span>
            ) : null}
          </span>
        </div>
        <div className="flex items-center gap-2">
          <PetraButton
            variant="secondary"
            onClick={() => loadCurrencySettings()}
            loading={loading}
          >
            تحديث
          </PetraButton>
          <PetraButton
            variant="secondary"
            onClick={() => setIsBulletinDialogOpen(true)}
          >
            نشرة أسعار الصرف
          </PetraButton>
          <PetraButton
            variant="primary"
            onClick={() => setIsEnableDialogOpen(true)}
            shortcut="F2"
          >
            + تفعيل عملة
          </PetraButton>
        </div>
      </div>

      {/* Main Table */}
      <div className="flex-1 bg-white border border-[#CBD8E5] rounded overflow-hidden flex flex-col">
        {loading ? (
          <PetraLoadingSkeleton rows={6} />
        ) : error ? (
          <PetraErrorDetails message={error} onRetry={loadCurrencySettings} />
        ) : currencies.length === 0 ? (
          <PetraEmptyState
            title="لا توجد عملات مفعلة"
            description="قم بتفعيل العملات الأجنبية للتعامل بها في الفواتير وسندات القبض والدفع."
            actionLabel="+ تفعيل عملة"
            onAction={() => setIsEnableDialogOpen(true)}
          />
        ) : (
          <div className="flex-1 overflow-auto">
            <table className="w-full text-sm border-collapse text-right">
              <thead className="bg-[#17334C] text-white sticky top-0 z-10 select-none">
                <tr>
                  <th className="p-2 border-l border-slate-700 w-28">رمز العملة</th>
                  <th className="p-2 border-l border-slate-700">الاسم العربي</th>
                  <th className="p-2 border-l border-slate-700">الاسم الأجنبي</th>
                  <th className="p-2 border-l border-slate-700 w-32 text-center">النوع</th>
                  <th className="p-2 w-32 text-center">الحالة</th>
                </tr>
              </thead>
              <tbody>
                {currencies.map((curr, idx) => {
                  const isBase = curr.code === baseCurrencyCode;
                  return (
                    <tr
                      key={curr.code || idx}
                      className={`border-b border-[#CBD8E5] hover:bg-[#E2ECF5] ${
                        idx % 2 === 1 ? "bg-[#EFF5FC]" : "bg-white"
                      }`}
                    >
                      <td className="p-2 font-mono font-bold text-[#087E87]">
                        {curr.code}
                      </td>
                      <td className="p-2 font-medium text-[#253447]">
                        {curr.nameArabic || "—"}
                      </td>
                      <td className="p-2 text-slate-500 font-mono text-xs">
                        {curr.nameEnglish || "—"}
                      </td>
                      <td className="p-2 text-center font-mono">
                        {isBase ? (
                          <span className="bg-amber-100 text-amber-900 px-2 py-0.5 rounded text-xs font-bold">
                            عملة الأساس
                          </span>
                        ) : (
                          <span className="bg-slate-100 text-slate-700 px-2 py-0.5 rounded text-xs">
                            عملة أجنبية
                          </span>
                        )}
                      </td>
                      <td className="p-2 text-center">
                        <span className="text-xs px-2 py-0.5 rounded font-bold bg-green-100 text-green-800">
                          مفعلة
                        </span>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Enable Currency Modal */}
      <PetraDialog
        open={isEnableDialogOpen}
        onClose={() => setIsEnableDialogOpen(false)}
        title="تفعيل عملة جديدة للشركة (FX-01)"
      >
        <form onSubmit={handleEnableCurrency} className="space-y-3">
          <PetraField label="رمز العملة (ISO Code)" required>
            <input
              type="text"
              maxLength={3}
              value={newCode}
              onChange={(e) => setNewCode(e.target.value.toUpperCase())}
              placeholder="مثال: EUR أو SAR"
              required
              className="w-full border border-[#CBD8E5] rounded px-3 py-1.5 text-sm bg-white focus:border-[#087E87] outline-none font-mono uppercase"
            />
          </PetraField>

          <PetraField label="الاسم العربي">
            <input
              type="text"
              value={newNameAr}
              onChange={(e) => setNewNameAr(e.target.value)}
              placeholder="مثال: يورو أوروبي"
              className="w-full border border-[#CBD8E5] rounded px-3 py-1.5 text-sm bg-white focus:border-[#087E87] outline-none"
            />
          </PetraField>

          <PetraField label="الاسم الأجنبي">
            <input
              type="text"
              value={newNameEn}
              onChange={(e) => setNewNameEn(e.target.value)}
              placeholder="Euro"
              className="w-full border border-[#CBD8E5] rounded px-3 py-1.5 text-sm bg-white focus:border-[#087E87] outline-none font-mono"
            />
          </PetraField>

          <PetraField label="سبب التفعيل" required>
            <input
              type="text"
              value={enableReason}
              onChange={(e) => setEnableReason(e.target.value)}
              placeholder="سبب إضافة العملة للتعامل المالي..."
              required
              className="w-full border border-[#CBD8E5] rounded px-3 py-1.5 text-sm bg-white focus:border-[#087E87] outline-none"
            />
          </PetraField>

          <div className="flex justify-end gap-2 pt-3 border-t border-[#CBD8E5]">
            <PetraButton
              variant="secondary"
              onClick={() => setIsEnableDialogOpen(false)}
              disabled={enabling}
            >
              إلغاء
            </PetraButton>
            <PetraButton variant="primary" type="submit" loading={enabling}>
              تفعيل العملة
            </PetraButton>
          </div>
        </form>
      </PetraDialog>

      {/* Exchange Rate Bulletin Modal */}
      <PetraDialog
        open={isBulletinDialogOpen}
        onClose={() => setIsBulletinDialogOpen(false)}
        title="تسجيل نشرة أسعار الصرف (FX-02)"
      >
        <form onSubmit={handleRegisterBulletin} className="space-y-3">
          <div className="grid grid-cols-2 gap-3">
            <PetraField label="تاريخ النشرة" required>
              <input
                type="date"
                value={bulletinDate}
                onChange={(e) => setBulletinDate(e.target.value)}
                required
                className="w-full border border-[#CBD8E5] rounded px-2.5 py-1.5 text-sm bg-white focus:border-[#087E87] outline-none font-mono"
              />
            </PetraField>

            <PetraField label="المصدر" required>
              <input
                type="text"
                value={bulletinSource}
                onChange={(e) => setBulletinSource(e.target.value)}
                placeholder="البنك المركزي / النشرة الرسمية"
                required
                className="w-full border border-[#CBD8E5] rounded px-2.5 py-1.5 text-sm bg-white focus:border-[#087E87] outline-none"
              />
            </PetraField>
          </div>

          <PetraField label="البيان / السبب" required>
            <input
              type="text"
              value={bulletinReason}
              onChange={(e) => setBulletinReason(e.target.value)}
              placeholder="نشرة الأسعار الرسمية لليوم..."
              required
              className="w-full border border-[#CBD8E5] rounded px-3 py-1.5 text-sm bg-white focus:border-[#087E87] outline-none"
            />
          </PetraField>

          <div className="border border-[#CBD8E5] rounded p-3 bg-slate-50 space-y-2">
            <span className="text-xs font-bold text-[#17334C] block mb-2">
              أسعار العملات مقابل عملة الأساس ({baseCurrencyCode}):
            </span>
            {currencies
              .filter((c) => c.code && c.code !== baseCurrencyCode)
              .map((c) => (
                <div
                  key={c.code}
                  className="flex items-center justify-between gap-3 text-sm"
                >
                  <span className="font-mono font-bold text-[#087E87] w-24">
                    1 {c.code} =
                  </span>
                  <input
                    type="text"
                    value={bulletinRates[c.code || ""] || ""}
                    onChange={(e) => {
                      const val = e.target.value;
                      setBulletinRates((prev) => ({
                        ...prev,
                        [c.code || ""]: val,
                      }));
                    }}
                    placeholder={`السعر بـ ${baseCurrencyCode}`}
                    className="flex-1 border border-[#CBD8E5] rounded px-2.5 py-1 text-sm bg-white focus:border-[#087E87] outline-none font-mono text-left"
                  />
                  <span className="font-mono text-xs text-slate-500">
                    {baseCurrencyCode}
                  </span>
                </div>
              ))}
          </div>

          <div className="flex justify-end gap-2 pt-3 border-t border-[#CBD8E5]">
            <PetraButton
              variant="secondary"
              onClick={() => setIsBulletinDialogOpen(false)}
              disabled={submittingBulletin}
            >
              إلغاء
            </PetraButton>
            <PetraButton
              variant="primary"
              type="submit"
              loading={submittingBulletin}
            >
              تثبيت النشرة
            </PetraButton>
          </div>
        </form>
      </PetraDialog>
    </div>
  );
}
