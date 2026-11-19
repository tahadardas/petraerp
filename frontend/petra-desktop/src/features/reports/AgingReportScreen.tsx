import { useState, useEffect, useCallback } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { useWorkspace } from "@/workspace/workspaceStore";
import {
  PetraButton,
  PetraField,
  PetraEmptyState,
  PetraLoadingSkeleton,
  PetraErrorDetails,
} from "@/components/ui";
import { toast } from "@/components/ui/toastStore";

interface AgingRow {
  partyId?: string;
  partyCode?: string;
  partyName?: string;
  notDue?: string;
  days1To30?: string;
  days31To60?: string;
  days61To90?: string;
  over90?: string;
  advance?: string;
  net?: string;
}

export default function AgingReportScreen() {
  const api = useApi();
  const companyId = useSession((s) => s.companyId);
  const branchId = useSession((s) => s.branchId);
  const open = useWorkspace((s) => s.open);

  const [kind, setKind] = useState<"Customer" | "Supplier">("Customer");
  const [asOf, setAsOf] = useState(new Date().toISOString().slice(0, 10));
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [rows, setRows] = useState<AgingRow[]>([]);
  const [totals, setTotals] = useState<AgingRow | null>(null);
  const [currencyCode, setCurrencyCode] = useState("USD");

  const loadAging = useCallback(async () => {
    if (!companyId || !branchId) return;
    setLoading(true);
    setError(null);
    try {
      const res = await api.GET(
        "/api/v1/companies/{companyId}/branches/{branchId}/reports/aging",
        {
          params: {
            path: { companyId, branchId },
            query: {
              kind,
              asOf,
            },
          },
        }
      );

      if (res.error) {
        setError(JSON.stringify(res.error));
      } else if (res.data) {
        setRows(res.data.rows || []);
        setTotals(res.data.totals || null);
        setCurrencyCode(res.data.currencyCode || "USD");
      }
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setLoading(false);
    }
  }, [api, companyId, branchId, kind, asOf]);

  useEffect(() => {
    loadAging();
  }, [loadAging]);

  const handleCopyTSV = () => {
    const header =
      "رمز الطرف\tاسم الطرف\tغير مستحق\t1-30 يوم\t31-60 يوم\t61-90 يوم\tأكثر من 90\tدفعات مقدمة\tالصافي\n";
    const body = rows
      .map(
        (r) =>
          `${r.partyCode || ""}\t${r.partyName || ""}\t${r.notDue || "0"}\t${
            r.days1To30 || "0"
          }\t${r.days31To60 || "0"}\t${r.days61To90 || "0"}\t${
            r.over90 || "0"
          }\t${r.advance || "0"}\t${r.net || "0"}`
      )
      .join("\n");
    navigator.clipboard.writeText(header + body);
    toast.success("تم نسخ تقرير أعمار الذمم إلى الحافظة بتنسيق Excel.");
  };

  const handleOpenPartyStatement = (partyId?: string, partyName?: string) => {
    if (!partyId) return;
    open({
      key: `party-statement-${partyId}`,
      title: `كشف حساب: ${partyName || partyId.slice(0, 8)}`,
      screen: "party-statement",
      params: { partyId },
    });
  };

  return (
    <div className="petra-screen p-4 flex flex-col h-full bg-[#EEF3F8]">
      {/* Header Bar */}
      <div className="flex items-center justify-between bg-white p-3 border border-[#CBD8E5] rounded mb-3">
        <div className="flex items-center gap-3">
          <h2 className="text-lg font-bold text-[#17334C]">
            تقرير أعمار الذمم والديون (REP-05)
          </h2>
          <span className="text-xs text-slate-500 bg-slate-100 px-2 py-0.5 rounded font-mono">
            العملة: {currencyCode}
          </span>
        </div>
        <div className="flex items-center gap-2">
          <PetraField label="نوع الطرف">
            <select
              value={kind}
              onChange={(e) =>
                setKind(e.target.value as "Customer" | "Supplier")
              }
              className="border border-[#CBD8E5] rounded px-2.5 py-1 text-xs bg-white focus:border-[#087E87] outline-none"
            >
              <option value="Customer">ذمم الزبائن (مدينون)</option>
              <option value="Supplier">ذمم الموردين (دائنون)</option>
            </select>
          </PetraField>

          <PetraField label="كما في تاريخ">
            <input
              type="date"
              value={asOf}
              onChange={(e) => setAsOf(e.target.value)}
              className="border border-[#CBD8E5] rounded px-2.5 py-1 text-xs bg-white font-mono"
            />
          </PetraField>

          <PetraButton
            variant="secondary"
            onClick={loadAging}
            loading={loading}
          >
            تحديث
          </PetraButton>
          <PetraButton variant="secondary" onClick={handleCopyTSV}>
            نسخ Excel
          </PetraButton>
        </div>
      </div>

      {/* Main Table */}
      <div className="flex-1 bg-white border border-[#CBD8E5] rounded overflow-hidden flex flex-col">
        {loading ? (
          <PetraLoadingSkeleton rows={10} />
        ) : error ? (
          <PetraErrorDetails message={error} onRetry={loadAging} />
        ) : rows.length === 0 ? (
          <PetraEmptyState
            title="لا توجد ذمم مستحقة"
            description="لا توجد بيانات مطابقة لتاريخ وفلترة التقرير المحددة."
          />
        ) : (
          <div className="flex-1 overflow-auto">
            <table className="w-full text-sm border-collapse text-right">
              <thead className="bg-[#17334C] text-white sticky top-0 z-10 select-none text-xs">
                <tr>
                  <th className="p-2 border-l border-slate-700 w-28">الرمز</th>
                  <th className="p-2 border-l border-slate-700">اسم الطرف</th>
                  <th className="p-2 border-l border-slate-700 w-28 text-left">غير مستحق</th>
                  <th className="p-2 border-l border-slate-700 w-28 text-left">1-30 يوم</th>
                  <th className="p-2 border-l border-slate-700 w-28 text-left">31-60 يوم</th>
                  <th className="p-2 border-l border-slate-700 w-28 text-left">61-90 يوم</th>
                  <th className="p-2 border-l border-slate-700 w-28 text-left bg-rose-900">أكثر من 90</th>
                  <th className="p-2 border-l border-slate-700 w-28 text-left text-amber-300">سلف / دفعات</th>
                  <th className="p-2 w-32 text-left bg-teal-900">الصافي الإجمالي</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((r, idx) => (
                  <tr
                    key={r.partyId || idx}
                    onClick={() => handleOpenPartyStatement(r.partyId, r.partyName)}
                    className={`border-b border-[#CBD8E5] hover:bg-[#E2ECF5] cursor-pointer ${
                      idx % 2 === 1 ? "bg-[#EFF5FC]" : "bg-white"
                    }`}
                  >
                    <td className="p-2 font-mono font-bold text-[#087E87]">
                      {r.partyCode || "—"}
                    </td>
                    <td className="p-2 font-medium text-[#253447]">
                      {r.partyName || "—"}
                    </td>
                    <td className="p-2 font-mono text-left text-slate-600">
                      {r.notDue || "0.00"}
                    </td>
                    <td className="p-2 font-mono text-left text-slate-700">
                      {r.days1To30 || "0.00"}
                    </td>
                    <td className="p-2 font-mono text-left text-amber-700 font-semibold">
                      {r.days31To60 || "0.00"}
                    </td>
                    <td className="p-2 font-mono text-left text-orange-700 font-semibold">
                      {r.days61To90 || "0.00"}
                    </td>
                    <td className="p-2 font-mono text-left text-rose-700 font-bold bg-rose-50">
                      {r.over90 || "0.00"}
                    </td>
                    <td className="p-2 font-mono text-left text-amber-600">
                      {r.advance || "0.00"}
                    </td>
                    <td className="p-2 font-mono font-bold text-left text-[#17334C] bg-teal-50">
                      {r.net || "0.00"}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {/* Totals Summary Footer */}
        {totals ? (
          <div className="bg-[#17334C] text-white p-3 border-t border-slate-700 flex items-center justify-between text-xs select-none">
            <span className="font-bold">المجاميع الإجمالية لجميع الذمم:</span>
            <div className="flex items-center gap-4 font-mono">
              <span>
                غير مستحق:{" "}
                <strong className="text-slate-200">{totals.notDue || "0"}</strong>
              </span>
              <span>
                1-30:{" "}
                <strong className="text-slate-200">{totals.days1To30 || "0"}</strong>
              </span>
              <span>
                31-60:{" "}
                <strong className="text-amber-300">{totals.days31To60 || "0"}</strong>
              </span>
              <span>
                61-90:{" "}
                <strong className="text-orange-300">{totals.days61To90 || "0"}</strong>
              </span>
              <span>
                90+:{" "}
                <strong className="text-rose-300">{totals.over90 || "0"}</strong>
              </span>
              <span>
                سلف:{" "}
                <strong className="text-amber-200">{totals.advance || "0"}</strong>
              </span>
              <span className="bg-[#087E87] px-2.5 py-1 rounded text-white font-bold text-sm">
                الصافي: {totals.net || "0"} {currencyCode}
              </span>
            </div>
          </div>
        ) : null}
      </div>
    </div>
  );
}
