import { useState, useEffect } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { useWorkspace } from "@/workspace/workspaceStore";
import { PetraButton, PetraLoadingSkeleton, PetraEmptyState, PetraErrorDetails } from "@/components/ui";

interface JournalSummary {
  id: string;
  accountingDate: string;
  description: string;
  state: "Draft" | "Validated" | "Approved" | "Posted" | "Reversed";
  stateName: string;
  postingSequence?: number | null;
  totalDebit: string;
  totalCredit: string;
  baseCurrencyCode: string;
}

export default function JournalsScreen() {
  const api = useApi();
  const { companyId, branchId } = useSession((s) => ({ companyId: s.companyId, branchId: s.branchId }));
  const openWorkspaceWindow = useWorkspace((s) => s.open);

  const [journals, setJournals] = useState<JournalSummary[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<{ status?: number; message?: string } | null>(null);
  const [search, setSearch] = useState("");

  const loadJournals = async () => {
    if (!companyId || !branchId) return;

    setLoading(true);
    setError(null);
    try {
      const res = await api.GET("/api/v1/companies/{companyId}/branches/{branchId}/ledger/journals", {
        params: {
          path: { companyId, branchId },
          query: { page: 1, pageSize: 100 },
        },
      });

      if (res.error) {
        setError({ status: res.response.status, message: "فشل تحميل القيود اليومية" });
      } else if (res.data?.items) {
        setJournals(res.data.items as JournalSummary[]);
      }
    } catch (err) {
      setError({ message: err instanceof Error ? err.message : "خطأ غير متوقع" });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadJournals();
  }, [companyId, branchId]);

  const openJournal = (journal?: JournalSummary) => {
    if (journal) {
      openWorkspaceWindow({
        key: `journal:${journal.id}`,
        screen: "journal-entry",
        title: `قيد #${journal.postingSequence ?? journal.id.substring(0, 6)}`,
        params: { journalId: journal.id },
      });
    } else {
      const newKey = `journal:new-${Date.now()}`;
      openWorkspaceWindow({
        key: newKey,
        screen: "journal-entry",
        title: "قيد يومية جديد",
      });
    }
  };

  const filtered = journals.filter((j) => {
    const q = search.trim().toLowerCase();
    if (!q) return true;
    return (
      j.description.toLowerCase().includes(q) ||
      (j.postingSequence && j.postingSequence.toString().includes(q))
    );
  });

  const getStateBadge = (state: JournalSummary["state"]) => {
    switch (state) {
      case "Posted":
        return <span style={{ padding: "2px 6px", borderRadius: 4, background: "rgba(30, 130, 76, 0.15)", color: "var(--success)", fontWeight: 600, fontSize: "var(--fs-caption)" }}>مرحل</span>;
      case "Reversed":
        return <span style={{ padding: "2px 6px", borderRadius: 4, background: "rgba(192, 57, 43, 0.15)", color: "var(--danger)", fontWeight: 600, fontSize: "var(--fs-caption)" }}>معكوس</span>;
      case "Approved":
        return <span style={{ padding: "2px 6px", borderRadius: 4, background: "rgba(8, 126, 135, 0.15)", color: "var(--primary)", fontWeight: 600, fontSize: "var(--fs-caption)" }}>معتمد</span>;
      default:
        return <span style={{ padding: "2px 6px", borderRadius: 4, background: "var(--bg-sunken)", color: "var(--text-muted)", fontWeight: 600, fontSize: "var(--fs-caption)" }}>مسودة</span>;
    }
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
          gap: "var(--space-3)",
          flexWrap: "wrap",
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: "var(--space-3)" }}>
          <h2 style={{ margin: 0, fontSize: "var(--fs-title)", fontWeight: 700 }}>
            القيود اليومية العامة
          </h2>
          <span style={{ fontSize: "var(--fs-caption)", color: "var(--text-muted)" }}>
            ({journals.length} قيد)
          </span>
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: "var(--space-2)" }}>
          <input
            type="text"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="بحث بالرقم أو البيان..."
            style={{
              height: "var(--control-height)",
              padding: "0 var(--space-2)",
              borderRadius: "var(--radius)",
              border: "1px solid var(--border)",
              background: "var(--bg-input)",
              color: "var(--text)",
              fontSize: "var(--fs-body)",
              width: 200,
              fontFamily: "inherit",
              outline: "none",
            }}
          />

          <PetraButton variant="secondary" onClick={loadJournals} loading={loading}>
            تحديث
          </PetraButton>

          <PetraButton variant="primary" onClick={() => openJournal()}>
            قيد جديد (Ctrl+N)
          </PetraButton>
        </div>
      </div>

      {/* Main Table */}
      <div style={{ flex: 1, overflow: "auto", padding: "var(--space-4)" }}>
        {loading ? (
          <PetraLoadingSkeleton rows={8} />
        ) : error ? (
          <PetraErrorDetails status={error.status} message={error.message} onRetry={loadJournals} />
        ) : filtered.length === 0 ? (
          <PetraEmptyState
            title="لا توجد قيود"
            description="لم يتم العثور على قيود يومية مسجلة."
            actionLabel="إضافة قيد جديد"
            onAction={() => openJournal()}
          />
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
                gridTemplateColumns: "100px 110px 1fr 110px 120px 120px 110px",
                padding: "var(--space-2) var(--space-4)",
                background: "var(--bg-sunken)",
                fontWeight: 600,
                borderBottom: "1px solid var(--border)",
                fontSize: "var(--fs-caption)",
              }}
            >
              <span>رقم القيد</span>
              <span>تاريخ القيد</span>
              <span>البيان والشرح</span>
              <span>الحالة</span>
              <span style={{ textAlign: "end" }}>مجموع المدين</span>
              <span style={{ textAlign: "end" }}>مجموع الدائن</span>
              <span style={{ textAlign: "center" }}>الإجراء</span>
            </div>

            {filtered.map((j) => (
              <div
                key={j.id}
                onDoubleClick={() => openJournal(j)}
                style={{
                  display: "grid",
                  gridTemplateColumns: "100px 110px 1fr 110px 120px 120px 110px",
                  padding: "var(--space-2) var(--space-4)",
                  borderBottom: "1px solid var(--divider)",
                  alignItems: "center",
                  fontSize: "var(--fs-body)",
                  cursor: "pointer",
                }}
              >
                <span className="numeric" style={{ fontWeight: 600, color: "var(--primary)" }}>
                  {j.postingSequence ?? "مسودة"}
                </span>
                <span className="numeric">{j.accountingDate}</span>
                <span style={{ overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                  {j.description}
                </span>
                <div>{getStateBadge(j.state)}</div>
                <span className="numeric" style={{ fontWeight: 600 }}>
                  {j.totalDebit}
                </span>
                <span className="numeric" style={{ fontWeight: 600 }}>
                  {j.totalCredit}
                </span>
                <div style={{ textAlign: "center" }}>
                  <PetraButton variant="ghost" size="sm" onClick={() => openJournal(j)}>
                    فتح
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
