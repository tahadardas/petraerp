import { useState, useEffect, useMemo } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { EditableGrid, type EditableColumn } from "@/components/grid/EditableGrid";
import { PetraDocumentToolbar, type DocumentPostingStatus } from "@/components/ui/PetraDocumentToolbar";
import { PetraLoadingSkeleton, PetraErrorDetails, PetraField } from "@/components/ui";
import { toast } from "@/components/ui/toastStore";

export interface JournalEntryScreenProps {
  params?: Readonly<Record<string, string>> | undefined;
}

interface JournalLine {
  id: string;
  accountCode: string;
  accountNameArabic: string;
  accountId: string;
  debit: string;
  credit: string;
  description: string;
  costCenterCode: string;
}

const makeBlankLine = (idx: number): JournalLine => ({
  id: `line-${idx}-${Date.now()}`,
  accountCode: "",
  accountNameArabic: "",
  accountId: "",
  debit: "",
  credit: "",
  description: "",
  costCenterCode: "",
});

const isBlankLine = (l: JournalLine) =>
  !l.accountCode && !l.debit && !l.credit && !l.description;

export default function JournalEntryScreen({ params }: JournalEntryScreenProps) {
  const journalId = params?.journalId;
  const api = useApi();
  const { companyId, branchId } = useSession((s) => ({ companyId: s.companyId, branchId: s.branchId }));

  const [date, setDate] = useState(new Date().toISOString().substring(0, 10));
  const [description, setDescription] = useState("");
  const [state, setState] = useState<string>("Draft");
  const [postingSequence, setPostingSequence] = useState<number | null>(null);
  const [version, setVersion] = useState("1");
  const [lines, setLines] = useState<JournalLine[]>([]);
  const [loading, setLoading] = useState(false);
  const [posting, setPosting] = useState(false);
  const [reversing, setReversing] = useState(false);
  const [error, setError] = useState<{ status?: number; message?: string } | null>(null);

  const loadJournal = async () => {
    if (!companyId || !branchId || !journalId) {
      setLines(Array.from({ length: 50 }, (_, i) => makeBlankLine(i)));
      return;
    }

    setLoading(true);
    setError(null);
    try {
      const res = await api.GET("/api/v1/companies/{companyId}/branches/{branchId}/ledger/journals/{journalId}", {
        params: { path: { companyId, branchId, journalId } },
      });

      if (res.error) {
        setError({ status: res.response.status, message: "فشل استرجاع بيانات القيد" });
      } else if (res.data) {
        const j = res.data;
        setDate(j.accountingDate);
        setDescription(j.description);
        setState(j.state);
        setPostingSequence(j.postingSequence ?? null);
        setVersion(j.version);

        const loadedLines: JournalLine[] = (j.lines ?? []).map((l, i) => ({
          id: l.id ?? `l-${i}`,
          accountCode: l.accountCode,
          accountNameArabic: l.accountNameArabic,
          accountId: l.accountId,
          debit: l.baseDebit && l.baseDebit !== "0" ? l.baseDebit : "",
          credit: l.baseCredit && l.baseCredit !== "0" ? l.baseCredit : "",
          description: l.description,
          costCenterCode: l.costCenterCode ?? "",
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
    loadJournal();
  }, [companyId, branchId, journalId]);

  const handlePost = async () => {
    if (!companyId || !branchId || !journalId) {
      toast.error("يرجى حفظ القيد أولاً قبل الترحيل");
      return;
    }

    setPosting(true);
    try {
      const res = await api.POST("/api/v1/companies/{companyId}/branches/{branchId}/ledger/journals/{journalId}/post", {
        params: { path: { companyId, branchId, journalId } },
        body: {
          expectedVersion: version,
          reason: "ترحيل قيد يومية من الواجهة المكتبية",
        },
      });

      if (res.error) {
        toast.error("فشل ترحيل القيد", (res.error as { detail?: string })?.detail);
      } else {
        toast.success("تم ترحيل القيد بنجاح إلى دفتر اليومية والأستاذ");
        loadJournal();
      }
    } catch (err) {
      toast.error("خطأ أثناء ترحيل القيد", err instanceof Error ? err.message : "خطأ غير متوقع");
    } finally {
      setPosting(false);
    }
  };

  const handleReverse = async () => {
    if (!companyId || !branchId || !journalId) return;

    setReversing(true);
    try {
      const res = await api.POST("/api/v1/companies/{companyId}/branches/{branchId}/ledger/journals/{journalId}/post-reversal", {
        params: { path: { companyId, branchId, journalId } },
        body: {
          expectedVersion: version,
          originalJournalId: journalId,
          originalExpectedVersion: version,
          reason: "عكس يدوي من واجهة المستخدم",
        },
      });

      if (res.error) {
        toast.error("فشل عكس القيد", (res.error as { detail?: string })?.detail);
      } else {
        toast.success("تم عكس القيد بنجاح بحركة عكسية مطابقة");
        loadJournal();
      }
    } catch (err) {
      toast.error("خطأ أثناء عكس القيد", err instanceof Error ? err.message : "خطأ غير متوقع");
    } finally {
      setReversing(false);
    }
  };

  // Compute live totals for display
  const totals = useMemo(() => {
    let deb = 0;
    let cred = 0;
    for (const l of lines) {
      if (l.debit) deb += parseFloat(l.debit) || 0;
      if (l.credit) cred += parseFloat(l.credit) || 0;
    }
    const diff = deb - cred;
    return {
      debit: deb.toFixed(2),
      credit: cred.toFixed(2),
      balanced: Math.abs(diff) < 0.0001,
      difference: diff.toFixed(2),
    };
  }, [lines]);

  const columns: EditableColumn<JournalLine>[] = [
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
      width: 200,
      readOnly: true,
      text: (l) => l.accountNameArabic || "—",
    },
    {
      id: "debit",
      header: "مدين",
      width: 120,
      kind: "decimal",
      text: (l) => l.debit,
      set: (l, v) => ({ ...l, debit: v, credit: v ? "" : l.credit }),
    },
    {
      id: "credit",
      header: "دائن",
      width: 120,
      kind: "decimal",
      text: (l) => l.credit,
      set: (l, v) => ({ ...l, credit: v, debit: v ? "" : l.debit }),
    },
    {
      id: "description",
      header: "البيان والشرح",
      width: 280,
      text: (l) => l.description,
      set: (l, v) => ({ ...l, description: v }),
    },
    {
      id: "costCenter",
      header: "مركز الكلفة",
      width: 130,
      text: (l) => l.costCenterCode,
      set: (l, v) => ({ ...l, costCenterCode: v }),
    },
  ];

  const getDocStatus = (): DocumentPostingStatus => {
    switch (state) {
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
        canSave={state === "Draft"}
        canPost={state === "Draft" || state === "Approved"}
        canReverse={state === "Posted"}
        isSaving={false}
        isPosting={posting}
        isReversing={reversing}
        onSave={() => toast.info("الحفظ يتم آلياً مع الترحيل")}
        onPost={handlePost}
        onReverse={handleReverse}
        onPrint={() => window.print()}
      />

      {/* Header Fields */}
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "160px 180px 1fr",
          gap: "var(--space-3)",
          padding: "var(--space-3) var(--space-4)",
          background: "var(--bg-surface)",
          borderBottom: "1px solid var(--border)",
          alignItems: "center",
        }}
      >
        <PetraField label="رقم القيد">
          <input
            type="text"
            readOnly
            value={postingSequence ? `#${postingSequence}` : "مسودة غير مرحلة"}
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

        <PetraField label="تاريخ القيد" required>
          <input
            type="date"
            value={date}
            onChange={(e) => setDate(e.target.value)}
            disabled={state === "Posted" || state === "Reversed"}
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

        <PetraField label="البيان العام للقيد">
          <input
            type="text"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            disabled={state === "Posted" || state === "Reversed"}
            placeholder="شرح القيد المحاسبي..."
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
      </div>

      {/* Entry Grid */}
      <div style={{ flex: 1, minHeight: 0, padding: "var(--space-2) var(--space-4)" }}>
        {loading ? (
          <PetraLoadingSkeleton rows={10} />
        ) : error ? (
          <PetraErrorDetails status={error.status} message={error.message} onRetry={loadJournal} />
        ) : (
          <EditableGrid
            ariaLabel="سطور القيد اليومي"
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

      {/* Bottom Totals Bar */}
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
        <div style={{ display: "flex", alignItems: "center", gap: "var(--space-4)" }}>
          <span>
            مجموع المدين: <strong className="numeric" style={{ color: "var(--success)" }}>{totals.debit}</strong>
          </span>
          <span>
            مجموع الدائن: <strong className="numeric" style={{ color: "var(--danger)" }}>{totals.credit}</strong>
          </span>
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: "var(--space-2)" }}>
          <span style={{ fontSize: "var(--fs-caption)", color: "var(--text-muted)" }}>حالة التوازن:</span>
          {totals.balanced ? (
            <span style={{ color: "var(--success)", display: "inline-flex", alignItems: "center", gap: 4 }}>
              ✓ القيد متوازن
            </span>
          ) : (
            <span style={{ color: "var(--danger)", display: "inline-flex", alignItems: "center", gap: 4 }}>
              ✕ غير متوازن (الفارق: <span className="numeric">{totals.difference}</span>)
            </span>
          )}
        </div>
      </footer>
    </div>
  );
}
