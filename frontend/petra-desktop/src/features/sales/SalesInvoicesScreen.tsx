import { useState, useEffect } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { useWorkspace } from "@/workspace/workspaceStore";
import { PetraButton, PetraLoadingSkeleton, PetraEmptyState, PetraErrorDetails } from "@/components/ui";

interface SalesInvoiceSummary {
  id: string;
  invoiceNumber?: string | null;
  invoiceDateUtc: string;
  customerCode: string;
  customerName: string;
  patternCode: string;
  patternName: string;
  status: "Draft" | "PendingApproval" | "Approved" | "Posted" | "Reversed";
  grandTotal: string;
  currencyCode: string;
  paymentMethod: string;
}

export default function SalesInvoicesScreen() {
  const api = useApi();
  const { companyId, branchId } = useSession((s) => ({ companyId: s.companyId, branchId: s.branchId }));
  const openWorkspaceWindow = useWorkspace((s) => s.open);

  const [invoices, setInvoices] = useState<SalesInvoiceSummary[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<{ status?: number; message?: string } | null>(null);
  const [search, setSearch] = useState("");

  const loadInvoices = async () => {
    if (!companyId || !branchId) return;

    setLoading(true);
    setError(null);
    try {
      const res = await api.GET("/api/v1/companies/{companyId}/branches/{branchId}/sales/invoices", {
        params: {
          path: { companyId, branchId },
          query: { page: 1, pageSize: 100 },
        },
      });

      if (res.error) {
        setError({ status: res.response.status, message: "فشل تحميل قائمة فواتير المبيعات" });
      } else if (res.data?.items) {
        setInvoices(res.data.items as unknown as SalesInvoiceSummary[]);
      }
    } catch (err) {
      setError({ message: err instanceof Error ? err.message : "خطأ غير متوقع" });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadInvoices();
  }, [companyId, branchId]);

  const openInvoice = (inv?: SalesInvoiceSummary) => {
    if (inv) {
      openWorkspaceWindow({
        key: `sales-invoice:${inv.id}`,
        screen: "sales-invoice",
        title: `فاتورة مبيعات: ${inv.invoiceNumber ?? inv.id.substring(0, 6)}`,
        params: { invoiceId: inv.id },
      });
    } else {
      const newKey = `sales-invoice:new-${Date.now()}`;
      openWorkspaceWindow({
        key: newKey,
        screen: "sales-invoice",
        title: "فاتورة مبيعات جديدة",
      });
    }
  };

  const filtered = invoices.filter((inv) => {
    const q = search.trim().toLowerCase();
    if (!q) return true;
    return (
      (inv.invoiceNumber && inv.invoiceNumber.toLowerCase().includes(q)) ||
      inv.customerName.toLowerCase().includes(q) ||
      inv.customerCode.toLowerCase().includes(q)
    );
  });

  const getStatusBadge = (status: SalesInvoiceSummary["status"]) => {
    switch (status) {
      case "Posted":
        return <span style={{ padding: "2px 6px", borderRadius: 4, background: "rgba(30, 130, 76, 0.15)", color: "var(--success)", fontWeight: 600, fontSize: "var(--fs-caption)" }}>مرحلة</span>;
      case "Reversed":
        return <span style={{ padding: "2px 6px", borderRadius: 4, background: "rgba(192, 57, 43, 0.15)", color: "var(--danger)", fontWeight: 600, fontSize: "var(--fs-caption)" }}>معكوسة</span>;
      case "Approved":
        return <span style={{ padding: "2px 6px", borderRadius: 4, background: "rgba(8, 126, 135, 0.15)", color: "var(--primary)", fontWeight: 600, fontSize: "var(--fs-caption)" }}>معتمدة</span>;
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
            فواتير المبيعات
          </h2>
          <span style={{ fontSize: "var(--fs-caption)", color: "var(--text-muted)" }}>
            ({invoices.length} فاتورة)
          </span>
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: "var(--space-2)" }}>
          <input
            type="text"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="بحث بالرقم أو العميل..."
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

          <PetraButton variant="secondary" onClick={loadInvoices} loading={loading}>
            تحديث
          </PetraButton>

          <PetraButton variant="primary" onClick={() => openInvoice()}>
            فاتورة جديدة (Ctrl+N)
          </PetraButton>
        </div>
      </div>

      {/* Main Invoices Table */}
      <div style={{ flex: 1, overflow: "auto", padding: "var(--space-4)" }}>
        {loading ? (
          <PetraLoadingSkeleton rows={8} />
        ) : error ? (
          <PetraErrorDetails status={error.status} message={error.message} onRetry={loadInvoices} />
        ) : filtered.length === 0 ? (
          <PetraEmptyState
            title="لا توجد فواتير مبيعات"
            description="لم يتم العثور على فواتير مبيعات مسجلة."
            actionLabel="إضافة فاتورة مبيعات جديدة"
            onAction={() => openInvoice()}
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
                gridTemplateColumns: "110px 110px 1.5fr 120px 110px 130px 100px",
                padding: "var(--space-2) var(--space-4)",
                background: "var(--bg-sunken)",
                fontWeight: 600,
                borderBottom: "1px solid var(--border)",
                fontSize: "var(--fs-caption)",
              }}
            >
              <span>رقم الفاتورة</span>
              <span>التاريخ</span>
              <span>العميل</span>
              <span>النمط</span>
              <span>الحالة</span>
              <span style={{ textAlign: "end" }}>الإجمالي النهائي</span>
              <span style={{ textAlign: "center" }}>الإجراء</span>
            </div>

            {filtered.map((inv) => (
              <div
                key={inv.id}
                onDoubleClick={() => openInvoice(inv)}
                style={{
                  display: "grid",
                  gridTemplateColumns: "110px 110px 1.5fr 120px 110px 130px 100px",
                  padding: "var(--space-2) var(--space-4)",
                  borderBottom: "1px solid var(--divider)",
                  alignItems: "center",
                  fontSize: "var(--fs-body)",
                  cursor: "pointer",
                }}
              >
                <span className="numeric" style={{ fontWeight: 600, color: "var(--primary)" }}>
                  {inv.invoiceNumber ?? "مسودة"}
                </span>
                <span className="numeric">{inv.invoiceDateUtc.substring(0, 10)}</span>
                <span style={{ fontWeight: 500 }}>{inv.customerName}</span>
                <span style={{ fontSize: "var(--fs-caption)", color: "var(--text-muted)" }}>
                  {inv.patternName}
                </span>
                <div>{getStatusBadge(inv.status)}</div>
                <span className="numeric" style={{ fontWeight: 700 }}>
                  {inv.grandTotal} {inv.currencyCode}
                </span>
                <div style={{ textAlign: "center" }}>
                  <PetraButton variant="ghost" size="sm" onClick={() => openInvoice(inv)}>
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
