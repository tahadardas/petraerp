import { useState, useEffect, useMemo } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { useWorkspace } from "@/workspace/workspaceStore";
import { PetraButton, PetraLoadingSkeleton, PetraEmptyState, PetraErrorDetails } from "@/components/ui";

interface PartySummary {
  id: string;
  code: string;
  nameArabic: string;
  nameEnglish?: string | null;
  role: "Customer" | "Supplier" | "Both";
  creditLimit: string;
  currentBalance: string;
  availableCredit: string;
  receivableBalance: string;
  payableBalance: string;
  isCreditBlocked: boolean;
  isActive: boolean;
  taxNumber?: string | null;
}

export default function PartiesScreen() {
  const api = useApi();
  const { companyId, branchId } = useSession((s) => ({ companyId: s.companyId, branchId: s.branchId }));
  const openWorkspaceWindow = useWorkspace((s) => s.open);

  const [parties, setParties] = useState<PartySummary[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<{ status?: number; message?: string } | null>(null);
  const [roleFilter, setRoleFilter] = useState<string>("all");
  const [search, setSearch] = useState("");

  const loadParties = async () => {
    if (!companyId || !branchId) return;

    setLoading(true);
    setError(null);
    try {
      const res = await api.GET("/api/v1/companies/{companyId}/branches/{branchId}/parties", {
        params: {
          path: { companyId, branchId },
          query: { page: 1, pageSize: 200 },
        },
      });

      if (res.error) {
        setError({ status: res.response.status, message: "فشل تحميل دليل الأطراف التجارية" });
      } else if (res.data?.items) {
        setParties(res.data.items as PartySummary[]);
      }
    } catch (err) {
      setError({ message: err instanceof Error ? err.message : "خطأ غير متوقع" });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadParties();
  }, [companyId, branchId]);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    return parties.filter((p) => {
      const matchRole = roleFilter === "all" || p.role === roleFilter;
      const matchSearch =
        !q ||
        p.code.toLowerCase().includes(q) ||
        p.nameArabic.toLowerCase().includes(q) ||
        (p.nameEnglish && p.nameEnglish.toLowerCase().includes(q));
      return matchRole && matchSearch;
    });
  }, [parties, roleFilter, search]);

  const openStatement = (party: PartySummary) => {
    openWorkspaceWindow({
      key: `statement:${party.id}`,
      screen: "party-statement",
      title: `كشف حساب: ${party.nameArabic}`,
      params: { partyId: party.id, partyName: party.nameArabic, partyCode: party.code },
    });
  };

  const getRoleBadge = (role: PartySummary["role"]) => {
    switch (role) {
      case "Both":
        return <span style={{ padding: "2px 6px", borderRadius: 4, background: "rgba(8, 126, 135, 0.15)", color: "var(--primary)", fontWeight: 600, fontSize: "var(--fs-caption)" }}>زبون ومورد (Both)</span>;
      case "Customer":
        return <span style={{ padding: "2px 6px", borderRadius: 4, background: "rgba(30, 130, 76, 0.15)", color: "var(--success)", fontWeight: 600, fontSize: "var(--fs-caption)" }}>زبون</span>;
      case "Supplier":
        return <span style={{ padding: "2px 6px", borderRadius: 4, background: "rgba(217, 130, 43, 0.15)", color: "var(--warning)", fontWeight: 600, fontSize: "var(--fs-caption)" }}>مورد</span>;
    }
  };

  return (
    <div style={{ display: "flex", flexDirection: "column", height: "100%", background: "var(--bg-app)" }}>
      {/* Top Header */}
      <div
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          padding: "var(--space-3) var(--space-4)",
          background: "var(--bg-surface)",
          borderBottom: "1px solid var(--border)",
          gap: "var(--space-3)",
          flexWrap: "wrap",
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: "var(--space-3)" }}>
          <h2 style={{ margin: 0, fontSize: "var(--fs-title)", fontWeight: 700 }}>
            دليل الأطراف الموحد (الزبائن والموردون)
          </h2>
          <span style={{ fontSize: "var(--fs-caption)", color: "var(--text-muted)" }}>
            ({parties.length} طرف)
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

          <select
            value={roleFilter}
            onChange={(e) => setRoleFilter(e.target.value)}
            style={{
              height: "var(--control-height)",
              padding: "0 var(--space-2)",
              borderRadius: "var(--radius)",
              border: "1px solid var(--border)",
              background: "var(--bg-input)",
              color: "var(--text)",
              fontSize: "var(--fs-body)",
              fontFamily: "inherit",
            }}
          >
            <option value="all">جميع الأدوار</option>
            <option value="Both">زبون ومورد معاً (Both)</option>
            <option value="Customer">زبائن فقط</option>
            <option value="Supplier">موردون فقط</option>
          </select>

          <PetraButton variant="secondary" onClick={loadParties} loading={loading}>
            تحديث
          </PetraButton>
        </div>
      </div>

      {/* Main Table */}
      <div style={{ flex: 1, overflow: "auto", padding: "var(--space-4)" }}>
        {loading ? (
          <PetraLoadingSkeleton rows={8} />
        ) : error ? (
          <PetraErrorDetails status={error.status} message={error.message} onRetry={loadParties} />
        ) : filtered.length === 0 ? (
          <PetraEmptyState title="لا توجد أطراف" description="لم يتم العثور على أطراف مطابقة للبحث." />
        ) : (
          <div
            style={{
              background: "var(--bg-surface)",
              border: "1px solid var(--border)",
              borderRadius: "var(--radius)",
              overflow: "hidden",
            }}
          >
            <div
              style={{
                display: "grid",
                gridTemplateColumns: "120px 1.5fr 140px 140px 140px 120px 130px",
                padding: "var(--space-2) var(--space-4)",
                background: "var(--bg-sunken)",
                fontWeight: 600,
                borderBottom: "1px solid var(--border)",
                fontSize: "var(--fs-caption)",
              }}
            >
              <span>رمز الطرف</span>
              <span>الاسم التجاري</span>
              <span>الدور</span>
              <span style={{ textAlign: "end" }}>الرصيد الصافي</span>
              <span style={{ textAlign: "end" }}>رصيد المدين</span>
              <span style={{ textAlign: "end" }}>سقف الائتمان</span>
              <span style={{ textAlign: "center" }}>الإجراءات</span>
            </div>

            {filtered.map((party) => (
              <div
                key={party.id}
                style={{
                  display: "grid",
                  gridTemplateColumns: "120px 1.5fr 140px 140px 140px 120px 130px",
                  padding: "var(--space-2) var(--space-4)",
                  borderBottom: "1px solid var(--divider)",
                  alignItems: "center",
                  fontSize: "var(--fs-body)",
                }}
              >
                <span className="numeric" style={{ fontWeight: 600, color: "var(--primary)" }}>
                  {party.code}
                </span>
                <span style={{ fontWeight: 500 }}>{party.nameArabic}</span>
                <div>{getRoleBadge(party.role)}</div>
                <span className="numeric" style={{ fontWeight: 600 }}>
                  {party.currentBalance}
                </span>
                <span className="numeric" style={{ color: "var(--text-muted)" }}>
                  {party.receivableBalance}
                </span>
                <span className="numeric" style={{ color: "var(--text-muted)" }}>
                  {party.creditLimit}
                </span>
                <div style={{ textAlign: "center" }}>
                  <PetraButton
                    variant="ghost"
                    size="sm"
                    onClick={() => openStatement(party)}
                    title="كشف حساب تفصيلي زمني"
                  >
                    كشف الحساب
                  </PetraButton>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
