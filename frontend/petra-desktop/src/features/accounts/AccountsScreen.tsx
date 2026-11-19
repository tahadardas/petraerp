import { useState, useEffect, useMemo } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { useWorkspace } from "@/workspace/workspaceStore";
import { PetraButton, PetraLoadingSkeleton, PetraEmptyState, PetraErrorDetails, PetraField } from "@/components/ui";

interface AccountItem {
  id: string;
  code: string;
  nameArabic: string;
  nameEnglish?: string | null;
  accountType: string;
  postingAllowed: boolean;
  costCenterPolicy: string;
  currencyMode: string;
  fixedCurrencyCode?: string | null;
  parentId?: string | null;
  level: number;
  hasChildren: boolean;
  hasJournalLines: boolean;
  isActive: boolean;
  version: string;
}

export default function AccountsScreen() {
  const api = useApi();
  const { companyId, branchId } = useSession((s) => ({ companyId: s.companyId, branchId: s.branchId }));
  const openWorkspaceWindow = useWorkspace((s) => s.open);

  const [accounts, setAccounts] = useState<AccountItem[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<{ status?: number; message?: string } | null>(null);
  const [search, setSearch] = useState("");

  const loadAccounts = async () => {
    if (!companyId || !branchId) return;

    setLoading(true);
    setError(null);
    try {
      const res = await api.GET("/api/v1/companies/{companyId}/branches/{branchId}/ledger/chart/accounts/tree", {
        params: { path: { companyId, branchId } },
      });

      if (res.error) {
        setError({ status: res.response.status, message: "فشل تحميل دليل الحسابات" });
      } else if (res.data) {
        const items = res.data as AccountItem[];
        setAccounts(items);
        const first = items[0];
        if (first && !selectedId) {
          setSelectedId(first.id);
        }
      }
    } catch (err) {
      setError({ message: err instanceof Error ? err.message : "خطأ غير متوقع" });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadAccounts();
  }, [companyId, branchId]);

  const selectedAccount = useMemo(() => {
    return accounts.find((a) => a.id === selectedId) ?? null;
  }, [accounts, selectedId]);

  const filteredAccounts = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return accounts;
    return accounts.filter(
      (a) =>
        a.code.toLowerCase().includes(q) ||
        a.nameArabic.toLowerCase().includes(q) ||
        (a.nameEnglish && a.nameEnglish.toLowerCase().includes(q))
    );
  }, [accounts, search]);

  const openLedgerReport = (account: AccountItem) => {
    openWorkspaceWindow({
      key: `ledger:${account.id}`,
      screen: "account-ledger",
      title: `دفتر أستاذ: ${account.nameArabic}`,
      params: { accountId: account.id, accountCode: account.code, accountName: account.nameArabic },
    });
  };

  return (
    <div style={{ display: "flex", flexDirection: "column", height: "100%", background: "var(--bg-app)" }}>
      {/* Header */}
      <div
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          padding: "var(--space-3) var(--space-4)",
          background: "var(--bg-surface)",
          borderBottom: "1px solid var(--border)",
          gap: "var(--space-2)",
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: "var(--space-3)" }}>
          <h2 style={{ margin: 0, fontSize: "var(--fs-title)", fontWeight: 700 }}>
            دليل الحسابات (شجرة الحسابات)
          </h2>
          <span style={{ fontSize: "var(--fs-caption)", color: "var(--text-muted)" }}>
            ({accounts.length} حساب)
          </span>
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: "var(--space-2)" }}>
          <input
            type="text"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="بحث بالكود أو الاسم..."
            style={{
              height: "var(--control-height)",
              padding: "0 var(--space-2)",
              borderRadius: "var(--radius)",
              border: "1px solid var(--border)",
              background: "var(--bg-input)",
              color: "var(--text)",
              fontSize: "var(--fs-body)",
              width: 220,
              fontFamily: "inherit",
              outline: "none",
            }}
          />
          <PetraButton variant="secondary" onClick={loadAccounts} loading={loading}>
            تحديث
          </PetraButton>
        </div>
      </div>

      {/* Two-pane layout */}
      <div style={{ display: "flex", flex: 1, minHeight: 0 }}>
        {/* Left pane: Tree/List */}
        <div
          style={{
            width: 360,
            borderInlineEnd: "1px solid var(--border)",
            background: "var(--bg-surface)",
            display: "flex",
            flexDirection: "column",
            overflowY: "auto",
          }}
        >
          {loading ? (
            <PetraLoadingSkeleton rows={10} />
          ) : error ? (
            <PetraErrorDetails status={error.status} message={error.message} onRetry={loadAccounts} />
          ) : filteredAccounts.length === 0 ? (
            <PetraEmptyState title="لا توجد حسابات" description="لم يتم العثور على حسابات مطابقة للبحث." />
          ) : (
            filteredAccounts.map((acc) => {
              const isSelected = acc.id === selectedId;
              const indent = (acc.level - 1) * 16;
              return (
                <div
                  key={acc.id}
                  onClick={() => setSelectedId(acc.id)}
                  style={{
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "space-between",
                    padding: `var(--space-2) var(--space-3)`,
                    paddingInlineStart: `calc(var(--space-3) + ${indent}px)`,
                    background: isSelected ? "var(--bg-sunken)" : "transparent",
                    borderInlineStart: isSelected ? "3px solid var(--primary)" : "3px solid transparent",
                    borderBottom: "1px solid var(--divider)",
                    cursor: "pointer",
                    fontSize: "var(--fs-body)",
                  }}
                >
                  <div style={{ display: "flex", alignItems: "center", gap: "var(--space-2)", minWidth: 0 }}>
                    <span style={{ fontSize: 14 }}>{acc.hasChildren ? "📁" : "📄"}</span>
                    <span className="numeric" style={{ fontWeight: 600, color: "var(--primary)" }}>
                      {acc.code}
                    </span>
                    <span
                      style={{
                        whiteSpace: "nowrap",
                        overflow: "hidden",
                        textOverflow: "ellipsis",
                        fontWeight: isSelected ? 600 : 400,
                      }}
                    >
                      {acc.nameArabic}
                    </span>
                  </div>
                  <span
                    style={{
                      fontSize: 10,
                      padding: "1px 4px",
                      borderRadius: 3,
                      background: acc.postingAllowed ? "rgba(30, 130, 76, 0.12)" : "var(--bg-sunken)",
                      color: acc.postingAllowed ? "var(--success)" : "var(--text-muted)",
                      fontWeight: 600,
                    }}
                  >
                    {acc.postingAllowed ? "حركة" : "رئيسي"}
                  </span>
                </div>
              );
            })
          )}
        </div>

        {/* Right pane: Account Card */}
        <div style={{ flex: 1, overflowY: "auto", padding: "var(--space-4)" }}>
          {selectedAccount ? (
            <div
              style={{
                background: "var(--bg-surface)",
                border: "1px solid var(--border)",
                borderRadius: "var(--radius)",
                padding: "var(--space-4)",
                maxWidth: 680,
              }}
            >
              <div
                style={{
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "space-between",
                  marginBottom: "var(--space-4)",
                  borderBottom: "1px solid var(--border)",
                  paddingBottom: "var(--space-3)",
                }}
              >
                <div>
                  <h3 style={{ margin: 0, fontSize: 18, fontWeight: 700 }}>
                    {selectedAccount.nameArabic}
                  </h3>
                  <span className="numeric" style={{ color: "var(--primary)", fontWeight: 600, fontSize: 14 }}>
                    رمز الحساب: {selectedAccount.code}
                  </span>
                </div>

                <PetraButton
                  variant="primary"
                  size="sm"
                  onClick={() => openLedgerReport(selectedAccount)}
                  title="فتح كشف حساب تفصيلي"
                >
                  كشف الحساب (دفتر الأستاذ)
                </PetraButton>
              </div>

              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "var(--space-3)" }}>
                <PetraField label="الاسم العربي">
                  <input
                    type="text"
                    readOnly
                    value={selectedAccount.nameArabic}
                    style={{
                      height: "var(--control-height)",
                      padding: "0 var(--space-2)",
                      borderRadius: "var(--radius)",
                      border: "1px solid var(--border)",
                      background: "var(--bg-sunken)",
                      color: "var(--text)",
                    }}
                  />
                </PetraField>

                <PetraField label="الاسم اللاتيني">
                  <input
                    type="text"
                    readOnly
                    value={selectedAccount.nameEnglish ?? "—"}
                    style={{
                      height: "var(--control-height)",
                      padding: "0 var(--space-2)",
                      borderRadius: "var(--radius)",
                      border: "1px solid var(--border)",
                      background: "var(--bg-sunken)",
                      color: "var(--text)",
                    }}
                  />
                </PetraField>

                <PetraField label="طبيعة الحساب (Account Type)">
                  <input
                    type="text"
                    readOnly
                    value={selectedAccount.accountType}
                    style={{
                      height: "var(--control-height)",
                      padding: "0 var(--space-2)",
                      borderRadius: "var(--radius)",
                      border: "1px solid var(--border)",
                      background: "var(--bg-sunken)",
                      color: "var(--text)",
                    }}
                  />
                </PetraField>

                <PetraField label="المستوى في الشجرة">
                  <input
                    type="text"
                    readOnly
                    value={`المستوى ${selectedAccount.level}`}
                    style={{
                      height: "var(--control-height)",
                      padding: "0 var(--space-2)",
                      borderRadius: "var(--radius)",
                      border: "1px solid var(--border)",
                      background: "var(--bg-sunken)",
                      color: "var(--text)",
                    }}
                  />
                </PetraField>

                <PetraField label="قابلية الترحيل المباشر">
                  <input
                    type="text"
                    readOnly
                    value={selectedAccount.postingAllowed ? "نعم (حساب حركة)" : "لا (حساب تجميعي رئيسي)"}
                    style={{
                      height: "var(--control-height)",
                      padding: "0 var(--space-2)",
                      borderRadius: "var(--radius)",
                      border: "1px solid var(--border)",
                      background: "var(--bg-sunken)",
                      color: "var(--text)",
                    }}
                  />
                </PetraField>

                <PetraField label="سياسة مركز الكلفة">
                  <input
                    type="text"
                    readOnly
                    value={selectedAccount.costCenterPolicy}
                    style={{
                      height: "var(--control-height)",
                      padding: "0 var(--space-2)",
                      borderRadius: "var(--radius)",
                      border: "1px solid var(--border)",
                      background: "var(--bg-sunken)",
                      color: "var(--text)",
                    }}
                  />
                </PetraField>

                <PetraField label="نمط العملة">
                  <input
                    type="text"
                    readOnly
                    value={selectedAccount.currencyMode}
                    style={{
                      height: "var(--control-height)",
                      padding: "0 var(--space-2)",
                      borderRadius: "var(--radius)",
                      border: "1px solid var(--border)",
                      background: "var(--bg-sunken)",
                      color: "var(--text)",
                    }}
                  />
                </PetraField>

                <PetraField label="العملة الثابتة">
                  <input
                    type="text"
                    readOnly
                    value={selectedAccount.fixedCurrencyCode ?? "عملة الشركة الأساسية"}
                    style={{
                      height: "var(--control-height)",
                      padding: "0 var(--space-2)",
                      borderRadius: "var(--radius)",
                      border: "1px solid var(--border)",
                      background: "var(--bg-sunken)",
                      color: "var(--text)",
                    }}
                  />
                </PetraField>
              </div>
            </div>
          ) : (
            <div style={{ color: "var(--text-muted)", padding: "var(--space-4)" }}>
              اختر حسابًا من الشجرة لعرض تفاصيله
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
