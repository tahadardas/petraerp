import { useState, useEffect } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { useWorkspace } from "@/workspace/workspaceStore";
import { PetraButton, PetraLoadingSkeleton, PetraEmptyState, PetraErrorDetails } from "@/components/ui";

interface PatternSummary {
  id: string;
  code: string;
  nameArabic: string;
  nameEnglish?: string | null;
  shortCode: string;
  group?: string | null;
  operationKind: string;
  isActive: boolean;
  isDefault: boolean;
  isRunnable: boolean;
  version: string;
}

export default function PatternManagerScreen() {
  const api = useApi();
  const { companyId, branchId } = useSession((s) => ({ companyId: s.companyId, branchId: s.branchId }));
  const openWorkspaceWindow = useWorkspace((s) => s.open);

  const [patterns, setPatterns] = useState<PatternSummary[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<{ status?: number; message?: string } | null>(null);
  const [selectedGroup, setSelectedGroup] = useState<string>("all");
  const [search, setSearch] = useState("");

  const loadPatterns = async () => {
    if (!companyId || !branchId) return;

    setLoading(true);
    setError(null);
    try {
      const res = await api.GET("/api/v1/companies/{companyId}/branches/{branchId}/patterns", {
        params: {
          path: { companyId, branchId },
          query: { page: 1, pageSize: 100 },
        },
      });

      if (res.error) {
        setError({ status: res.response.status, message: "فشل تحميل أنماط المستندات" });
      } else if (res.data?.items) {
        setPatterns(res.data.items as PatternSummary[]);
      }
    } catch (err) {
      setError({ message: err instanceof Error ? err.message : "خطأ غير متوقع" });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadPatterns();
  }, [companyId, branchId]);

  const groups = Array.from(new Set(patterns.map((p) => p.group).filter(Boolean))) as string[];

  const filtered = patterns.filter((p) => {
    const matchGroup = selectedGroup === "all" || p.group === selectedGroup;
    const matchSearch =
      !search ||
      p.nameArabic.toLowerCase().includes(search.toLowerCase()) ||
      p.code.toLowerCase().includes(search.toLowerCase()) ||
      p.shortCode.toLowerCase().includes(search.toLowerCase());
    return matchGroup && matchSearch;
  });

  const openPatternCard = (pattern: PatternSummary) => {
    openWorkspaceWindow({
      key: `pattern:${pattern.id}`,
      screen: "pattern-card",
      title: `نمط: ${pattern.nameArabic}`,
      params: { patternId: pattern.id, title: pattern.nameArabic },
    });
  };

  return (
    <div style={{ display: "flex", flexDirection: "column", height: "100%", background: "var(--bg-app)" }}>
      {/* Header & Filter Bar */}
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
            إدارة أنماط المستندات
          </h2>
          <span style={{ fontSize: "var(--fs-caption)", color: "var(--text-muted)" }}>
            ({patterns.length} نمط معرف)
          </span>
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: "var(--space-2)" }}>
          <input
            type="text"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="بحث في الأنماط... (اسم أو كود)"
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
            value={selectedGroup}
            onChange={(e) => setSelectedGroup(e.target.value)}
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
            <option value="all">جميع المجموعات</option>
            {groups.map((g) => (
              <option key={g} value={g}>
                {g}
              </option>
            ))}
          </select>

          <PetraButton variant="secondary" onClick={loadPatterns} loading={loading}>
            تحديث
          </PetraButton>
        </div>
      </div>

      {/* Main Content Area */}
      <div style={{ flex: 1, overflowY: "auto", padding: "var(--space-4)" }}>
        {loading ? (
          <PetraLoadingSkeleton rows={8} />
        ) : error ? (
          <PetraErrorDetails status={error.status} message={error.message} onRetry={loadPatterns} />
        ) : filtered.length === 0 ? (
          <PetraEmptyState
            title="لا توجد أنماط مطابقة"
            description="لم يتم العثور على أنماط مستندات مطابقة لمعايير البحث المحددة."
          />
        ) : (
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fill, minmax(320px, 1fr))",
              gap: "var(--space-3)",
            }}
          >
            {filtered.map((pattern) => (
              <div
                key={pattern.id}
                onClick={() => openPatternCard(pattern)}
                style={{
                  background: "var(--bg-surface)",
                  border: "1px solid var(--border)",
                  borderRadius: "var(--radius)",
                  padding: "var(--space-3)",
                  cursor: "pointer",
                  display: "flex",
                  flexDirection: "column",
                  gap: "var(--space-2)",
                  boxShadow: "0 1px 3px rgba(0, 0, 0, 0.05)",
                  transition: "border-color 0.15s ease, box-shadow 0.15s ease",
                }}
              >
                <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between" }}>
                  <div style={{ display: "flex", alignItems: "center", gap: "var(--space-2)" }}>
                    <span
                      style={{
                        padding: "2px 6px",
                        borderRadius: 3,
                        background: "var(--bg-sunken)",
                        fontWeight: 700,
                        fontSize: "var(--fs-caption)",
                        fontFamily: "var(--font-mono)",
                        color: "var(--primary)",
                      }}
                    >
                      {pattern.shortCode || pattern.code}
                    </span>
                    <strong style={{ fontSize: "var(--fs-body)", color: "var(--text)" }}>
                      {pattern.nameArabic}
                    </strong>
                  </div>
                  <span
                    style={{
                      fontSize: 11,
                      padding: "1px 6px",
                      borderRadius: 10,
                      background: pattern.isActive ? "rgba(30, 130, 76, 0.15)" : "var(--bg-sunken)",
                      color: pattern.isActive ? "var(--success)" : "var(--text-muted)",
                      fontWeight: 600,
                    }}
                  >
                    {pattern.isActive ? "نشط" : "معطل"}
                  </span>
                </div>

                <div
                  style={{
                    display: "flex",
                    justifyContent: "space-between",
                    fontSize: "var(--fs-caption)",
                    color: "var(--text-muted)",
                  }}
                >
                  <span>النوع: {pattern.operationKind}</span>
                  {pattern.group ? <span>المجموعة: {pattern.group}</span> : null}
                  <span>الإصدار: v{pattern.version}</span>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
