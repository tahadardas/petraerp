import { useState, useEffect } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { EditableGrid, type EditableColumn } from "@/components/grid/EditableGrid";
import { PetraDocumentToolbar, type DocumentPostingStatus } from "@/components/ui/PetraDocumentToolbar";
import { PetraLoadingSkeleton, PetraErrorDetails, PetraField } from "@/components/ui";
import { toast } from "@/components/ui/toastStore";

export interface TreasuryVoucherCardScreenProps {
  params?: Readonly<Record<string, string>> | undefined;
}

interface VoucherLine {
  id: string;
  accountCode: string;
  accountNameArabic: string;
  amount: string;
  description: string;
}

const makeBlankLine = (idx: number): VoucherLine => ({
  id: `line-${idx}-${Date.now()}`,
  accountCode: "",
  accountNameArabic: "",
  amount: "",
  description: "",
});

const isBlankLine = (l: VoucherLine) => !l.accountCode && !l.amount;

export default function TreasuryVoucherCardScreen({ params }: TreasuryVoucherCardScreenProps) {
  const voucherId = params?.voucherId;
  const initialKind = (params?.kind === "Payment" ? "Payment" : "Receipt") as "Receipt" | "Payment";
  const api = useApi();
  const { companyId, branchId } = useSession((s) => ({ companyId: s.companyId, branchId: s.branchId }));

  const [kind, setKind] = useState<"Receipt" | "Payment">(initialKind);
  const [voucherNumber, setVoucherNumber] = useState<string | null>(null);
  const [date, setDate] = useState(new Date().toISOString().substring(0, 10));
  const [partyName, setPartyName] = useState("");
  const [amount, setAmount] = useState("");
  const [status, setStatus] = useState<string>("Draft");
  const [currencyCode, setCurrencyCode] = useState("SAR");
  const [version, setVersion] = useState("1");
  const [lines, setLines] = useState<VoucherLine[]>([]);
  const [loading, setLoading] = useState(false);
  const [posting, setPosting] = useState(false);
  const [reversing, setReversing] = useState(false);
  const [error, setError] = useState<{ status?: number; message?: string } | null>(null);

  const loadVoucher = async () => {
    if (!companyId || !branchId || !voucherId) {
      setLines(Array.from({ length: 50 }, (_, i) => makeBlankLine(i)));
      return;
    }

    setLoading(true);
    setError(null);
    try {
      const res = await api.GET("/api/v1/companies/{companyId}/branches/{branchId}/treasury/vouchers/{voucherId}", {
        params: { path: { companyId, branchId, voucherId } },
      });

      if (res.error) {
        setError({ status: res.response.status, message: "فشل استرجاع بيانات السند" });
      } else if (res.data) {
        const v = res.data;
        setKind(v.kind ?? "Receipt");
        setVoucherNumber(v.voucherNumber ?? null);
        setDate(v.voucherDateUtc ? v.voucherDateUtc.substring(0, 10) : new Date().toISOString().substring(0, 10));
        setPartyName(v.partyName ?? "");
        setAmount(v.amount);
        setStatus(v.status ?? "Draft");
        setCurrencyCode(v.currencyCode ?? "SAR");
        setVersion(v.version ?? "1");

        const loadedLines: VoucherLine[] = (v.lines ?? []).map((l, i) => ({
          id: l.id ?? `l-${i}`,
          accountCode: l.accountCode ?? "",
          accountNameArabic: l.partyName ?? l.accountCode ?? "",
          amount: l.amount,
          description: l.description ?? "",
        }));

        setLines(loadedLines);
      }
    } catch (err) {
      setError({ message: err instanceof Error ? err.message : "خطأ غير متوقع" });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadVoucher();
  }, [companyId, branchId, voucherId]);

  const handlePost = async () => {
    if (!companyId || !branchId || !voucherId) {
      toast.error("يرجى حفظ السند أولاً قبل الترحيل");
      return;
    }

    setPosting(true);
    try {
      const res = await api.POST("/api/v1/companies/{companyId}/branches/{branchId}/treasury/vouchers/{voucherId}/post", {
        params: { path: { companyId, branchId, voucherId } },
        body: {
          expectedVersion: version,
          idempotencyKey: `post-v-${Date.now()}`,
        },
      });

      if (res.error) {
        toast.error("فشل ترحيل السند", (res.error as { detail?: string })?.detail);
      } else {
        toast.success(`تم ترحيل ${kind === "Receipt" ? "سند القبض" : "سند الدفع"} بنجاح`);
        loadVoucher();
      }
    } catch (err) {
      toast.error("خطأ أثناء ترحيل السند", err instanceof Error ? err.message : "خطأ غير متوقع");
    } finally {
      setPosting(false);
    }
  };

  const handleReverse = async () => {
    if (!companyId || !branchId || !voucherId) return;

    setReversing(true);
    try {
      const res = await api.POST("/api/v1/companies/{companyId}/branches/{branchId}/treasury/vouchers/{voucherId}/reverse", {
        params: { path: { companyId, branchId, voucherId } },
        body: {
          expectedVersion: version,
          reason: "عكس يدوي من واجهة المستخدم",
        },
      });

      if (res.error) {
        toast.error("فشل عكس السند", (res.error as { detail?: string })?.detail);
      } else {
        toast.success("تم عكس السند بنجاح");
        loadVoucher();
      }
    } catch (err) {
      toast.error("خطأ أثناء عكس السند", err instanceof Error ? err.message : "خطأ غير متوقع");
    } finally {
      setReversing(false);
    }
  };

  const columns: EditableColumn<VoucherLine>[] = [
    {
      id: "accountCode",
      header: "رمز الحساب",
      width: 140,
      pinned: true,
      text: (l) => l.accountCode,
      set: (l, v) => ({ ...l, accountCode: v }),
    },
    {
      id: "accountName",
      header: "اسم الحساب",
      width: 220,
      readOnly: true,
      text: (l) => l.accountNameArabic || "—",
    },
    {
      id: "amount",
      header: "المبلغ",
      width: 130,
      kind: "decimal",
      text: (l) => l.amount,
      set: (l, v) => ({ ...l, amount: v }),
    },
    {
      id: "description",
      header: "الشرح والبيان",
      width: 320,
      text: (l) => l.description,
      set: (l, v) => ({ ...l, description: v }),
    },
  ];

  const getDocStatus = (): DocumentPostingStatus => {
    switch (status) {
      case "Posted": return "posted";
      case "Reversed": return "reversed";
      case "Approved": return "pending";
      default: return "draft";
    }
  };

  return (
    <div style={{ display: "flex", flexDirection: "column", height: "100%", background: "var(--bg-app)" }}>
      {/* Document Toolbar */}
      <PetraDocumentToolbar
        status={getDocStatus()}
        canSave={status === "Draft"}
        canPost={status === "Draft" || status === "Approved"}
        canReverse={status === "Posted"}
        isPosting={posting}
        isReversing={reversing}
        onSave={() => toast.info("الحفظ يتم مع دورة الترحيل")}
        onPost={handlePost}
        onReverse={handleReverse}
        onPrint={() => window.print()}
      />

      {/* Header Fields */}
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "140px 160px 140px 1.5fr 150px",
          gap: "var(--space-3)",
          padding: "var(--space-3) var(--space-4)",
          background: "var(--bg-surface)",
          borderBottom: "1px solid var(--border)",
          alignItems: "center",
        }}
      >
        <PetraField label="رقم السند">
          <input
            type="text"
            readOnly
            value={voucherNumber ? `#${voucherNumber}` : "مسودة"}
            style={{
              height: 30,
              padding: "0 var(--space-2)",
              borderRadius: "var(--radius)",
              border: "1px solid var(--border)",
              background: "var(--bg-sunken)",
              color: "var(--text)",
              fontFamily: "var(--font-mono)",
              fontWeight: 600,
            }}
          />
        </PetraField>

        <PetraField label="تاريخ السند" required>
          <input
            type="date"
            value={date}
            onChange={(e) => setDate(e.target.value)}
            disabled={status === "Posted" || status === "Reversed"}
            style={{
              height: 30,
              padding: "0 var(--space-2)",
              borderRadius: "var(--radius)",
              border: "1px solid var(--border)",
              background: "var(--bg-input)",
              color: "var(--text)",
            }}
          />
        </PetraField>

        <PetraField label="نوع السند">
          <select
            value={kind}
            onChange={(e) => setKind(e.target.value as "Receipt" | "Payment")}
            disabled={!!voucherId}
            style={{
              height: 30,
              padding: "0 var(--space-2)",
              borderRadius: "var(--radius)",
              border: "1px solid var(--border)",
              background: "var(--bg-input)",
              color: "var(--text)",
              fontWeight: 600,
            }}
          >
            <option value="Receipt">سند قبض (+)</option>
            <option value="Payment">سند دفع (-)</option>
          </select>
        </PetraField>

        <PetraField label="الطرف / العميل">
          <input
            type="text"
            value={partyName}
            onChange={(e) => setPartyName(e.target.value)}
            disabled={status === "Posted" || status === "Reversed"}
            placeholder="اسم العميل أو الطرف..."
            style={{
              height: 30,
              padding: "0 var(--space-2)",
              borderRadius: "var(--radius)",
              border: "1px solid var(--border)",
              background: "var(--bg-input)",
              color: "var(--text)",
            }}
          />
        </PetraField>

        <PetraField label="مبلغ السند">
          <input
            type="text"
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
            disabled={status === "Posted" || status === "Reversed"}
            placeholder="0.00"
            className="numeric"
            style={{
              height: 30,
              padding: "0 var(--space-2)",
              borderRadius: "var(--radius)",
              border: "1px solid var(--border)",
              background: "var(--bg-input)",
              color: "var(--primary)",
              fontWeight: 700,
              fontSize: 15,
            }}
          />
        </PetraField>
      </div>

      {/* Lines Grid */}
      <div style={{ flex: 1, minHeight: 0, padding: "var(--space-2) var(--space-4)" }}>
        {loading ? (
          <PetraLoadingSkeleton rows={10} />
        ) : error ? (
          <PetraErrorDetails status={error.status} message={error.message} onRetry={loadVoucher} />
        ) : (
          <EditableGrid
            ariaLabel="سطور السند"
            columns={columns}
            rows={lines}
            onRowsChange={setLines}
            direction="rtl"
            isBlank={isBlankLine}
            makeBlank={makeBlankLine}
            readyRows={50}
          />
        )}
      </div>

      {/* Footer */}
      <footer
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          padding: "var(--space-2) var(--space-4)",
          background: "var(--bg-sunken)",
          borderTop: "1px solid var(--border)",
          fontSize: "var(--fs-body)",
          fontWeight: 600,
        }}
      >
        <span style={{ color: "var(--text-muted)" }}>
          العملة: <strong>{currencyCode}</strong>
        </span>

        <span style={{ fontSize: 16 }}>
          إجمالي المبلغ:{" "}
          <strong className="numeric" style={{ color: "var(--primary)", fontSize: 18 }}>
            {amount || "0.00"} {currencyCode}
          </strong>
        </span>
      </footer>
    </div>
  );
}
