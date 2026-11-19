import { useState, useEffect } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { useWorkspace } from "@/workspace/workspaceStore";
import { PetraButton, PetraLoadingSkeleton, PetraEmptyState, PetraErrorDetails } from "@/components/ui";
import { toast } from "@/components/ui/toastStore";

export interface PartyStatementScreenProps {
  params?: Readonly<Record<string, string>> | undefined;
}

interface StatementLine {
  dateUtc: string;
  documentType: string;
  documentId: string;
  documentNumber?: string | null;
  description: string;
  debit: string;
  credit: string;
  balance: string;
}

interface StatementData {
  partyId: string;
  partyCode: string;
  partyName: string;
  currencyCode: string;
  from?: string;
  to?: string;
  opening: string;
  totalDebit: string;
  totalCredit: string;
  closing: string;
  lines: StatementLine[];
  truncated?: boolean;
}

export default function PartyStatementScreen({ params }: PartyStatementScreenProps) {
  const partyId = params?.partyId;
  const partyName = params?.partyName ?? "كشف الحساب";
  const api = useApi();
  const { companyId, branchId } = useSession((s) => ({ companyId: s.companyId, branchId: s.branchId }));
  const openWorkspaceWindow = useWorkspace((s) => s.open);

  const [statement, setStatement] = useState<StatementData | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<{ status?: number; message?: string } | null>(null);
  const [fromDate, setFromDate] = useState("");
  const [toDate, setToDate] = useState("");

  const loadStatement = async () => {
    if (!companyId || !branchId || !partyId) return;

    setLoading(true);
    setError(null);
    try {
      const res = await api.GET("/api/v1/companies/{companyId}/branches/{branchId}/reports/parties/{partyId}/statement", {
        params: {
          path: { companyId, branchId, partyId },
          query: {
            ...(fromDate ? { from: fromDate } : {}),
            ...(toDate ? { to: toDate } : {}),
          },
        },
      });

      if (res.error) {
        setError({ status: res.response.status, message: "فشل استخراج كشف الحساب من الخادم" });
      } else if (res.data) {
        setStatement(res.data as unknown as StatementData);
      }
    } catch (err) {
      setError({ message: err instanceof Error ? err.message : "خطأ غير متوقع" });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadStatement();
  }, [companyId, branchId, partyId]);

  const copyToExcel = () => {
    if (!statement || statement.lines.length === 0) return;
    const header = "التاريخ\tنوع المستند\tرقم المستند\tالبيان\tمدين\tدائن\tالرصيد الجاري";
    const rows = statement.lines.map((l) =>
      `${l.dateUtc.substring(0, 10)}\t${l.documentType}\t${l.documentNumber ?? ""}\t${l.description}\t${l.debit}\t${l.credit}\t${l.balance}`
    );
    const tsv = [header, ...rows].join("\r\n");
    navigator.clipboard?.writeText(tsv).then(() => {
      toast.success("تم نسخ الكشف إلى الحافظة بصيغة Excel (TSV)");
    });
  };

  const openSourceDocument = (line: StatementLine) => {
    if (!line.documentId) return;

    const docType = line.documentType.toLowerCase();
    if (docType.includes("sales")) {
      openWorkspaceWindow({
        key: `sales-invoice:${line.documentId}`,
        screen: "sales-invoice",
        title: `فاتورة مبيعات: ${line.documentNumber ?? line.documentId.substring(0, 8)}`,
        params: { invoiceId: line.documentId },
      });
    } else if (docType.includes("purchase")) {
      openWorkspaceWindow({
        key: `purchase-invoice:${line.documentId}`,
        screen: "purchase-invoice",
        title: `فاتورة مشتريات: ${line.documentNumber ?? line.documentId.substring(0, 8)}`,
        params: { invoiceId: line.documentId },
      });
    } else if (docType.includes("journal")) {
      openWorkspaceWindow({
        key: `journal:${line.documentId}`,
        screen: "journal-entry",
        title: `قيد: ${line.documentNumber ?? line.documentId.substring(0, 8)}`,
        params: { journalId: line.documentId },
      });
    } else if (docType.includes("voucher")) {
      openWorkspaceWindow({
        key: `voucher:${line.documentId}`,
        screen: "treasury-voucher",
        title: `سند: ${line.documentNumber ?? line.documentId.substring(0, 8)}`,
        params: { voucherId: line.documentId },
      });
    }
  };

  if (!partyId) {
    return <div style={{ padding: "var(--space-4)" }}>لم يتم تحديد الطرف.</div>;
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", height: "100%", background: "var(--bg-app)" }}>
      {/* Top Filter and Actions Bar */}
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
        <div style={{ display: "flex", alignItems: "center", gap: "var(--space-2)" }}>
          <h2 style={{ margin: 0, fontSize: "var(--fs-title)", fontWeight: 700 }}>
            كشف حساب تفصيلي: {partyName}
          </h2>
          {statement?.currencyCode ? (
            <span style={{ fontSize: "var(--fs-caption)", color: "var(--text-muted)" }}>
              [{statement.currencyCode}]
            </span>
          ) : null}
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: "var(--space-2)" }}>
          <label style={{ fontSize: "var(--fs-caption)", display: "flex", alignItems: "center", gap: 4 }}>
            من:
            <input
              type="date"
              value={fromDate}
              onChange={(e) => setFromDate(e.target.value)}
              style={{
                height: 28,
                padding: "0 4px",
                border: "1px solid var(--border)",
                borderRadius: "var(--radius)",
                background: "var(--bg-input)",
                color: "var(--text)",
              }}
            />
          </label>

          <label style={{ fontSize: "var(--fs-caption)", display: "flex", alignItems: "center", gap: 4 }}>
            إلى:
            <input
              type="date"
              value={toDate}
              onChange={(e) => setToDate(e.target.value)}
              style={{
                height: 28,
                padding: "0 4px",
                border: "1px solid var(--border)",
                borderRadius: "var(--radius)",
                background: "var(--bg-input)",
                color: "var(--text)",
              }}
            />
          </label>

          <PetraButton variant="secondary" size="sm" onClick={loadStatement} loading={loading}>
            عرض
          </PetraButton>

          <PetraButton variant="secondary" size="sm" onClick={copyToExcel} disabled={!statement || statement.lines.length === 0}>
            نسخ إلى Excel
          </PetraButton>
        </div>
      </div>

      {/* Summary KPI Cards */}
      {statement ? (
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(4, 1fr)",
            gap: "var(--space-3)",
            padding: "var(--space-3) var(--space-4)",
            background: "var(--bg-surface)",
            borderBottom: "1px solid var(--border)",
          }}
        >
          <div style={{ padding: "var(--space-2)", background: "var(--bg-sunken)", borderRadius: "var(--radius)" }}>
            <span style={{ fontSize: "var(--fs-caption)", color: "var(--text-muted)" }}>الرصيد الافتتاحي</span>
            <div className="numeric" style={{ fontSize: 16, fontWeight: 700 }}>
              {statement.opening}
            </div>
          </div>
          <div style={{ padding: "var(--space-2)", background: "var(--bg-sunken)", borderRadius: "var(--radius)" }}>
            <span style={{ fontSize: "var(--fs-caption)", color: "var(--text-muted)" }}>مجموع المدين (+)</span>
            <div className="numeric" style={{ fontSize: 16, fontWeight: 700, color: "var(--success)" }}>
              {statement.totalDebit}
            </div>
          </div>
          <div style={{ padding: "var(--space-2)", background: "var(--bg-sunken)", borderRadius: "var(--radius)" }}>
            <span style={{ fontSize: "var(--fs-caption)", color: "var(--text-muted)" }}>مجموع الدائن (-)</span>
            <div className="numeric" style={{ fontSize: 16, fontWeight: 700, color: "var(--danger)" }}>
              {statement.totalCredit}
            </div>
          </div>
          <div style={{ padding: "var(--space-2)", background: "var(--bg-sunken)", borderRadius: "var(--radius)" }}>
            <span style={{ fontSize: "var(--fs-caption)", color: "var(--text-muted)" }}>الرصيد الختامي الصافي</span>
            <div className="numeric" style={{ fontSize: 16, fontWeight: 700, color: "var(--primary)" }}>
              {statement.closing}
            </div>
          </div>
        </div>
      ) : null}

      {/* Movements Table */}
      <div style={{ flex: 1, overflow: "auto", padding: "var(--space-4)" }}>
        {loading ? (
          <PetraLoadingSkeleton rows={8} />
        ) : error ? (
          <PetraErrorDetails status={error.status} message={error.message} onRetry={loadStatement} />
        ) : !statement || statement.lines.length === 0 ? (
          <PetraEmptyState title="لا توجد حركات" description="لا توجد حركات مالية مسجلة لهذا الطرف في الفترة المحددة." />
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
                gridTemplateColumns: "110px 140px 110px 1fr 120px 120px 130px",
                padding: "var(--space-2) var(--space-4)",
                background: "var(--bg-sunken)",
                fontWeight: 600,
                borderBottom: "1px solid var(--border)",
                fontSize: "var(--fs-caption)",
              }}
            >
              <span>التاريخ</span>
              <span>نوع المستند</span>
              <span>رقم المستند</span>
              <span>البيان والشرح</span>
              <span style={{ textAlign: "end" }}>مدين</span>
              <span style={{ textAlign: "end" }}>دائن</span>
              <span style={{ textAlign: "end" }}>الرصيد الجاري</span>
            </div>

            {statement.lines.map((line, idx) => (
              <div
                key={idx}
                onDoubleClick={() => openSourceDocument(line)}
                title="انقر نقرًا مزدوجًا لفتح مستند المصدر"
                style={{
                  display: "grid",
                  gridTemplateColumns: "110px 140px 110px 1fr 120px 120px 130px",
                  padding: "var(--space-2) var(--space-4)",
                  borderBottom: "1px solid var(--divider)",
                  fontSize: "var(--fs-body)",
                  alignItems: "center",
                  cursor: line.documentId ? "pointer" : "default",
                }}
              >
                <span className="numeric">{line.dateUtc.substring(0, 10)}</span>
                <span>{line.documentType}</span>
                <span className="numeric" style={{ color: "var(--primary)", fontWeight: 600 }}>
                  {line.documentNumber ?? "—"}
                </span>
                <span>{line.description}</span>
                <span className="numeric" style={{ color: line.debit && line.debit !== "0" ? "var(--success)" : undefined }}>
                  {line.debit || "—"}
                </span>
                <span className="numeric" style={{ color: line.credit && line.credit !== "0" ? "var(--danger)" : undefined }}>
                  {line.credit || "—"}
                </span>
                <span className="numeric" style={{ fontWeight: 700 }}>
                  {line.balance}
                </span>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
