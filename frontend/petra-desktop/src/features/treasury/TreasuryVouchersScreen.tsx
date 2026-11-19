import { useState, useEffect } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { useWorkspace } from "@/workspace/workspaceStore";
import { PetraButton, PetraLoadingSkeleton, PetraEmptyState, PetraErrorDetails } from "@/components/ui";

interface VoucherSummary {
  id: string;
  voucherNumber?: string | null;
  voucherDateUtc: string;
  kind: "Receipt" | "Payment";
  patternName: string;
  partyName?: string | null;
  amount: string;
  currencyCode: string;
  status: "Draft" | "PendingApproval" | "Approved" | "Posted" | "Reversed";
}

export default function TreasuryVouchersScreen() {
  const api = useApi();
  const { companyId, branchId } = useSession((s) => ({ companyId: s.companyId, branchId: s.branchId }));
  const openWorkspaceWindow = useWorkspace((s) => s.open);

  const [vouchers, setVouchers] = useState<VoucherSummary[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<{ status?: number; message?: string } | null>(null);
  const [kindFilter, setKindFilter] = useState<string>("all");
  const [search, setSearch] = useState("");

  const loadVouchers = async () => {
    if (!companyId || !branchId) return;

    setLoading(true);
    setError(null);
    try {
      const res = await api.GET("/api/v1/companies/{companyId}/branches/{branchId}/treasury/vouchers", {
        params: {
          path: { companyId, branchId },
          query: { page: 1, pageSize: 100 },
        },
      });

      if (res.error) {
        setError({ status: res.response.status, message: "فشل تحميل سندات الصندوق والقبض والدفع" });
      } else if (res.data?.items) {
        setVouchers(res.data.items as unknown as VoucherSummary[]);
      }
    } catch (err) {
      setError({ message: err instanceof Error ? err.message : "خطأ غير متوقع" });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadVouchers();
  }, [companyId, branchId]);

  const openVoucher = (v?: VoucherSummary, newKind?: "Receipt" | "Payment") => {
    if (v) {
      const isReceipt = v.kind === "Receipt";
      openWorkspaceWindow({
        key: `voucher:${v.id}`,
        screen: "treasury-voucher",
        title: `${isReceipt ? "سند قبض" : "سند دفع"}: ${v.voucherNumber ?? v.id.substring(0, 6)}`,
        params: { voucherId: v.id, kind: v.kind },
      });
    } else {
      const isReceipt = newKind === "Receipt";
      const newKey = `voucher:new-${Date.now()}`;
      openWorkspaceWindow({
        key: newKey,
        screen: "treasury-voucher",
        title: isReceipt ? "سند قبض جديد" : "سند دفع جديد",
        params: { kind: newKind ?? "Receipt" },
      });
    }
  };

  const filtered = vouchers.filter((v) => {
    const matchKind = kindFilter === "all" || v.kind === kindFilter;
    const q = search.trim().toLowerCase();
    const matchSearch =
      !q ||
      (v.voucherNumber && v.voucherNumber.toLowerCase().includes(q)) ||
      (v.partyName && v.partyName.toLowerCase().includes(q));
    return matchKind && matchSearch;
  });

  const getKindBadge = (kind: VoucherSummary["kind"]) => {
    if (kind === "Receipt") {
      return <span style={{ padding: "2px 6px", borderRadius: 4, background: "rgba(30, 130, 76, 0.15)", color: "var(--success)", fontWeight: 600, fontSize: "var(--fs-caption)" }}>سند قبض</span>;
    }
    return <span style={{ padding: "2px 6px", borderRadius: 4, background: "rgba(217, 130, 43, 0.15)", color: "var(--warning)", fontWeight: 600, fontSize: "var(--fs-caption)" }}>سند دفع</span>;
  };

  const getStatusBadge = (status: VoucherSummary["status"]) => {
    switch (status) {
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
            سندات الصندوق (سندات القبض والدفع)
          </h2>
          <span style={{ fontSize: "var(--fs-caption)", color: "var(--text-muted)" }}>
            ({vouchers.length} سند)
          </span>
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: "var(--space-2)" }}>
          <input
            type="text"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="بحث بالرقم أو الطرف..."
            style={{
              height: "var(--control-height)",
              padding: "0 var(--space-2)",
              borderRadius: "var(--radius)",
              border: "1px solid var(--border)",
              background: "var(--bg-input)",
              color: "var(--text)",
              fontSize: "var(--fs-body)",
              width: 180,
              fontFamily: "inherit",
              outline: "none",
            }}
          />

          <select
            value={kindFilter}
            onChange={(e) => setKindFilter(e.target.value)}
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
            <option value="all">جميع السندات</option>
            <option value="Receipt">سندات قبض فقط</option>
            <option value="Payment">سندات دفع فقط</option>
          </select>

          <PetraButton variant="secondary" onClick={loadVouchers} loading={loading}>
            تحديث
          </PetraButton>

          <PetraButton variant="primary" onClick={() => openVoucher(undefined, "Receipt")}>
            سند قبض +
          </PetraButton>

          <PetraButton variant="secondary" onClick={() => openVoucher(undefined, "Payment")}>
            سند دفع -
          </PetraButton>
        </div>
      </div>

      {/* Main Table */}
      <div style={{ flex: 1, overflow: "auto", padding: "var(--space-4)" }}>
        {loading ? (
          <PetraLoadingSkeleton rows={8} />
        ) : error ? (
          <PetraErrorDetails status={error.status} message={error.message} onRetry={loadVouchers} />
        ) : filtered.length === 0 ? (
          <PetraEmptyState
            title="لا توجد سندات"
            description="لم يتم العثور على سندات قبض أو دفع مسجلة."
            actionLabel="إضافة سند قبض جديد"
            onAction={() => openVoucher(undefined, "Receipt")}
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
                gridTemplateColumns: "110px 110px 110px 1.5fr 110px 130px 100px",
                padding: "var(--space-2) var(--space-4)",
                background: "var(--bg-sunken)",
                fontWeight: 600,
                borderBottom: "1px solid var(--border)",
                fontSize: "var(--fs-caption)",
              }}
            >
              <span>رقم السند</span>
              <span>التاريخ</span>
              <span>نوع السند</span>
              <span>الطرف المستفيد / العميل</span>
              <span>الحالة</span>
              <span style={{ textAlign: "end" }}>المبلغ</span>
              <span style={{ textAlign: "center" }}>الإجراء</span>
            </div>

            {filtered.map((v) => (
              <div
                key={v.id}
                onDoubleClick={() => openVoucher(v)}
                style={{
                  display: "grid",
                  gridTemplateColumns: "110px 110px 110px 1.5fr 110px 130px 100px",
                  padding: "var(--space-2) var(--space-4)",
                  borderBottom: "1px solid var(--divider)",
                  alignItems: "center",
                  fontSize: "var(--fs-body)",
                  cursor: "pointer",
                }}
              >
                <span className="numeric" style={{ fontWeight: 600, color: "var(--primary)" }}>
                  {v.voucherNumber ?? "مسودة"}
                </span>
                <span className="numeric">{v.voucherDateUtc.substring(0, 10)}</span>
                <div>{getKindBadge(v.kind)}</div>
                <span style={{ fontWeight: 500 }}>{v.partyName || "—"}</span>
                <div>{getStatusBadge(v.status)}</div>
                <span className="numeric" style={{ fontWeight: 700 }}>
                  {v.amount} {v.currencyCode}
                </span>
                <div style={{ textAlign: "center" }}>
                  <PetraButton variant="ghost" size="sm" onClick={() => openVoucher(v)}>
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
