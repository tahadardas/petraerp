import { useState, useEffect, useCallback } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { useWorkspace } from "@/workspace/workspaceStore";
import {
  PetraButton,
  PetraField,
  PetraLoadingSkeleton,
  PetraErrorDetails,
} from "@/components/ui";
import { toast } from "@/components/ui/toastStore";

interface StatementLine {
  accountId?: string;
  code?: string;
  nameArabic?: string;
  nameEnglish?: string | null;
  type?: string;
  amount?: string;
}

export default function FinancialStatementsScreen() {
  const api = useApi();
  const companyId = useSession((s) => s.companyId);
  const branchId = useSession((s) => s.branchId);
  const open = useWorkspace((s) => s.open);

  const [activeTab, setActiveTab] = useState<"income" | "balance">("income");

  // Income Statement State
  const [incomeFrom, setIncomeFrom] = useState(
    new Date(new Date().getFullYear(), 0, 1).toISOString().slice(0, 10)
  );
  const [incomeTo, setIncomeTo] = useState(
    new Date().toISOString().slice(0, 10)
  );
  const [incomeConsolidated, setIncomeConsolidated] = useState(false);
  const [incomeLoading, setIncomeLoading] = useState(false);
  const [incomeError, setIncomeError] = useState<string | null>(null);
  const [incomeLines, setIncomeLines] = useState<StatementLine[]>([]);
  const [totalRevenue, setTotalRevenue] = useState("0.00");
  const [totalExpenses, setTotalExpenses] = useState("0.00");
  const [netIncome, setNetIncome] = useState("0.00");
  const [incomeCurrency, setIncomeCurrency] = useState("USD");

  // Balance Sheet State
  const [balanceAsOf, setBalanceAsOf] = useState(
    new Date().toISOString().slice(0, 10)
  );
  const [balanceConsolidated, setBalanceConsolidated] = useState(false);
  const [balanceLoading, setBalanceLoading] = useState(false);
  const [balanceError, setBalanceError] = useState<string | null>(null);
  const [balanceLines, setBalanceLines] = useState<StatementLine[]>([]);
  const [totalAssets, setTotalAssets] = useState("0.00");
  const [totalLiabilities, setTotalLiabilities] = useState("0.00");
  const [totalEquity, setTotalEquity] = useState("0.00");
  const [currentEarnings, setCurrentEarnings] = useState("0.00");
  const [isBalanced, setIsBalanced] = useState(true);
  const [balanceCurrency, setBalanceCurrency] = useState("USD");

  // Load Income Statement
  const loadIncomeStatement = useCallback(async () => {
    if (!companyId || !branchId) return;
    setIncomeLoading(true);
    setIncomeError(null);
    try {
      const res = await api.GET(
        "/api/v1/companies/{companyId}/branches/{branchId}/reports/financial/income-statement",
        {
          params: {
            path: { companyId, branchId },
            query: {
              from: incomeFrom,
              to: incomeTo,
              consolidated: incomeConsolidated,
            },
          },
        }
      );

      if (res.error) {
        setIncomeError(JSON.stringify(res.error));
      } else if (res.data) {
        setIncomeLines(res.data.lines || []);
        setTotalRevenue(res.data.totalRevenue || "0.00");
        setTotalExpenses(res.data.totalExpenses || "0.00");
        setNetIncome(res.data.netIncome || "0.00");
        setIncomeCurrency(res.data.baseCurrency || "USD");
      }
    } catch (err: unknown) {
      setIncomeError(err instanceof Error ? err.message : String(err));
    } finally {
      setIncomeLoading(false);
    }
  }, [api, companyId, branchId, incomeFrom, incomeTo, incomeConsolidated]);

  // Load Balance Sheet
  const loadBalanceSheet = useCallback(async () => {
    if (!companyId || !branchId) return;
    setBalanceLoading(true);
    setBalanceError(null);
    try {
      const res = await api.GET(
        "/api/v1/companies/{companyId}/branches/{branchId}/reports/financial/balance-sheet",
        {
          params: {
            path: { companyId, branchId },
            query: {
              asOf: balanceAsOf,
              consolidated: balanceConsolidated,
            },
          },
        }
      );

      if (res.error) {
        setBalanceError(JSON.stringify(res.error));
      } else if (res.data) {
        setBalanceLines(res.data.lines || []);
        setTotalAssets(res.data.totalAssets || "0.00");
        setTotalLiabilities(res.data.totalLiabilities || "0.00");
        setTotalEquity(res.data.totalEquity || "0.00");
        setCurrentEarnings(res.data.currentEarnings || "0.00");
        setIsBalanced(Boolean(res.data.balanced));
        setBalanceCurrency(res.data.baseCurrency || "USD");
      }
    } catch (err: unknown) {
      setBalanceError(err instanceof Error ? err.message : String(err));
    } finally {
      setBalanceLoading(false);
    }
  }, [api, companyId, branchId, balanceAsOf, balanceConsolidated]);

  useEffect(() => {
    if (activeTab === "income") {
      loadIncomeStatement();
    } else {
      loadBalanceSheet();
    }
  }, [activeTab, loadIncomeStatement, loadBalanceSheet]);

  const handleCopyTSV = (lines: StatementLine[], title: string) => {
    const header = "رمز الحساب\tاسم الحساب\tالمبلغ\n";
    const body = lines
      .map((l) => `${l.code || ""}\t${l.nameArabic || ""}\t${l.amount || "0"}`)
      .join("\n");
    navigator.clipboard.writeText(header + body);
    toast.success(`تم نسخ بيانات ${title} إلى الحافظة بتنسيق Excel.`);
  };

  const handleDrilldownAccount = (accountId?: string, accountName?: string) => {
    if (!accountId) return;
    open({
      key: `ledger-${accountId}`,
      title: `دفتر الأستاذ: ${accountName || accountId.slice(0, 8)}`,
      screen: "account-ledger",
      params: { accountId },
    });
  };

  return (
    <div className="petra-screen p-4 flex flex-col h-full bg-[#EEF3F8]">
      {/* Header with Tabs */}
      <div className="flex items-center justify-between bg-white p-3 border border-[#CBD8E5] rounded mb-3">
        <div className="flex items-center gap-3">
          <h2 className="text-lg font-bold text-[#17334C]">
            القوائم والتقارير الختامية (REP-03)
          </h2>
          <div className="flex border border-[#CBD8E5] rounded overflow-hidden">
            <button
              type="button"
              onClick={() => setActiveTab("income")}
              className={`px-3 py-1 text-xs font-bold transition-colors ${
                activeTab === "income"
                  ? "bg-[#17334C] text-white"
                  : "bg-white text-[#253447] hover:bg-slate-100"
              }`}
            >
              قائمة الدخل (الأرباح والخسائر)
            </button>
            <button
              type="button"
              onClick={() => setActiveTab("balance")}
              className={`px-3 py-1 text-xs font-bold transition-colors ${
                activeTab === "balance"
                  ? "bg-[#17334C] text-white"
                  : "bg-white text-[#253447] hover:bg-slate-100"
              }`}
            >
              الميزانية العمومية
            </button>
          </div>
        </div>

        {activeTab === "income" ? (
          <div className="flex items-center gap-2">
            <PetraField label="من تاريخ">
              <input
                type="date"
                value={incomeFrom}
                onChange={(e) => setIncomeFrom(e.target.value)}
                className="border border-[#CBD8E5] rounded px-2 py-0.5 text-xs bg-white font-mono"
              />
            </PetraField>
            <PetraField label="إلى تاريخ">
              <input
                type="date"
                value={incomeTo}
                onChange={(e) => setIncomeTo(e.target.value)}
                className="border border-[#CBD8E5] rounded px-2 py-0.5 text-xs bg-white font-mono"
              />
            </PetraField>
            <label className="flex items-center gap-1 text-xs text-slate-700 select-none cursor-pointer pt-3">
              <input
                type="checkbox"
                checked={incomeConsolidated}
                onChange={(e) => setIncomeConsolidated(e.target.checked)}
                className="rounded text-[#087E87]"
              />
              <span>موحد</span>
            </label>
            <PetraButton
              variant="secondary"
              onClick={loadIncomeStatement}
              loading={incomeLoading}
            >
              تحديث
            </PetraButton>
            <PetraButton
              variant="secondary"
              onClick={() => handleCopyTSV(incomeLines, "قائمة الدخل")}
            >
              نسخ Excel
            </PetraButton>
          </div>
        ) : (
          <div className="flex items-center gap-2">
            <PetraField label="كما في تاريخ">
              <input
                type="date"
                value={balanceAsOf}
                onChange={(e) => setBalanceAsOf(e.target.value)}
                className="border border-[#CBD8E5] rounded px-2 py-0.5 text-xs bg-white font-mono"
              />
            </PetraField>
            <label className="flex items-center gap-1 text-xs text-slate-700 select-none cursor-pointer pt-3">
              <input
                type="checkbox"
                checked={balanceConsolidated}
                onChange={(e) => setBalanceConsolidated(e.target.checked)}
                className="rounded text-[#087E87]"
              />
              <span>موحد</span>
            </label>
            <PetraButton
              variant="secondary"
              onClick={loadBalanceSheet}
              loading={balanceLoading}
            >
              تحديث
            </PetraButton>
            <PetraButton
              variant="secondary"
              onClick={() => handleCopyTSV(balanceLines, "الميزانية العمومية")}
            >
              نسخ Excel
            </PetraButton>
          </div>
        )}
      </div>

      {/* Main Body */}
      {activeTab === "income" ? (
        <div className="flex-1 bg-white border border-[#CBD8E5] rounded overflow-hidden flex flex-col">
          {incomeLoading ? (
            <PetraLoadingSkeleton rows={10} />
          ) : incomeError ? (
            <PetraErrorDetails
              message={incomeError}
              onRetry={loadIncomeStatement}
            />
          ) : (
            <div className="flex-1 overflow-auto">
              <table className="w-full text-sm border-collapse text-right">
                <thead className="bg-[#17334C] text-white sticky top-0 z-10 select-none">
                  <tr>
                    <th className="p-2 border-l border-slate-700 w-32">رمز الحساب</th>
                    <th className="p-2 border-l border-slate-700">اسم الحساب</th>
                    <th className="p-2 border-l border-slate-700 w-44">النوع</th>
                    <th className="p-2 w-44 text-left">المبلغ ({incomeCurrency})</th>
                  </tr>
                </thead>
                <tbody>
                  {incomeLines.map((l, idx) => (
                    <tr
                      key={l.accountId || idx}
                      onClick={() => handleDrilldownAccount(l.accountId, l.nameArabic)}
                      className={`border-b border-[#CBD8E5] hover:bg-[#E2ECF5] cursor-pointer ${
                        idx % 2 === 1 ? "bg-[#EFF5FC]" : "bg-white"
                      }`}
                    >
                      <td className="p-2 font-mono font-bold text-[#087E87]">
                        {l.code}
                      </td>
                      <td className="p-2 font-medium text-[#253447]">
                        {l.nameArabic}
                      </td>
                      <td className="p-2 text-slate-500 text-xs">
                        {l.type === "Revenue" ? "إيرادات" : "مصروفات"}
                      </td>
                      <td className="p-2 font-mono font-bold text-[#17334C] text-left">
                        {l.amount || "0.00"}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {/* Income Summary Footer */}
          <div className="bg-[#17334C] text-white p-3 border-t border-slate-700 flex items-center justify-between text-sm select-none">
            <div className="flex items-center gap-6">
              <span>
                إجمالي الإيرادات:{" "}
                <strong className="font-mono text-emerald-400">
                  {totalRevenue}
                </strong>
              </span>
              <span>
                إجمالي المصروفات:{" "}
                <strong className="font-mono text-amber-300">
                  {totalExpenses}
                </strong>
              </span>
            </div>
            <div className="flex items-center gap-3">
              <span className="text-slate-300">صافي الدخل (الربح / الخسارة):</span>
              <span
                className={`text-xl font-bold font-mono px-3 py-1 rounded ${
                  parseFloat(netIncome) >= 0
                    ? "bg-emerald-600 text-white"
                    : "bg-red-600 text-white"
                }`}
              >
                {netIncome} {incomeCurrency}
              </span>
            </div>
          </div>
        </div>
      ) : (
        <div className="flex-1 bg-white border border-[#CBD8E5] rounded overflow-hidden flex flex-col">
          {balanceLoading ? (
            <PetraLoadingSkeleton rows={10} />
          ) : balanceError ? (
            <PetraErrorDetails
              message={balanceError}
              onRetry={loadBalanceSheet}
            />
          ) : (
            <div className="flex-1 overflow-auto">
              <table className="w-full text-sm border-collapse text-right">
                <thead className="bg-[#17334C] text-white sticky top-0 z-10 select-none">
                  <tr>
                    <th className="p-2 border-l border-slate-700 w-32">رمز الحساب</th>
                    <th className="p-2 border-l border-slate-700">اسم الحساب</th>
                    <th className="p-2 border-l border-slate-700 w-44">التبويب</th>
                    <th className="p-2 w-44 text-left">الرصيد ({balanceCurrency})</th>
                  </tr>
                </thead>
                <tbody>
                  {balanceLines.map((l, idx) => (
                    <tr
                      key={l.accountId || idx}
                      onClick={() => handleDrilldownAccount(l.accountId, l.nameArabic)}
                      className={`border-b border-[#CBD8E5] hover:bg-[#E2ECF5] cursor-pointer ${
                        idx % 2 === 1 ? "bg-[#EFF5FC]" : "bg-white"
                      }`}
                    >
                      <td className="p-2 font-mono font-bold text-[#087E87]">
                        {l.code}
                      </td>
                      <td className="p-2 font-medium text-[#253447]">
                        {l.nameArabic}
                      </td>
                      <td className="p-2 text-slate-500 text-xs">
                        {l.type || "—"}
                      </td>
                      <td className="p-2 font-mono font-bold text-[#17334C] text-left">
                        {l.amount || "0.00"}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {/* Balance Sheet Summary Footer */}
          <div className="bg-[#17334C] text-white p-3 border-t border-slate-700 flex items-center justify-between text-sm select-none">
            <div className="flex items-center gap-5 text-xs">
              <span>
                الأصول:{" "}
                <strong className="font-mono text-emerald-400 text-sm">
                  {totalAssets}
                </strong>
              </span>
              <span>
                الالتزامات:{" "}
                <strong className="font-mono text-amber-300 text-sm">
                  {totalLiabilities}
                </strong>
              </span>
              <span>
                حقوق الملكية:{" "}
                <strong className="font-mono text-cyan-300 text-sm">
                  {totalEquity}
                </strong>
              </span>
              <span>
                أرباح الفترة غير المقفلة:{" "}
                <strong className="font-mono text-white text-sm">
                  {currentEarnings}
                </strong>
              </span>
            </div>
            <div className="flex items-center gap-2">
              <span
                className={`text-xs px-2.5 py-1 rounded font-bold ${
                  isBalanced
                    ? "bg-green-500 text-white"
                    : "bg-red-500 text-white"
                }`}
              >
                {isBalanced ? "الميزانية متوازنة ✓" : "الميزانية غير متوازنة ⚠"}
              </span>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
