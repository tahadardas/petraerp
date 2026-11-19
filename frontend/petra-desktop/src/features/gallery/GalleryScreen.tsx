import { useCallback, useMemo, useState } from "react";
import { EditableGrid, type EditableColumn } from "@/components/grid/EditableGrid";
import { formatDecimal } from "@/components/inputs/decimalInput";
import { LookupPicker, type LookupItem } from "@/components/lookup/LookupPicker";
import { usePreferences } from "@/design-system/preferences";

/** Development gallery of the input engine on sample data (no API, no accounting). Real screens use the same components. */
const ACCOUNTS: LookupItem[] = [
  { id: "1", code: "1101", name: "الصندوق" },
  { id: "2", code: "1104", name: "المخزون" },
  { id: "3", code: "2211", name: "الموردون" },
  { id: "4", code: "4101", name: "المبيعات" },
  { id: "5", code: "5211", name: "مصاريف تشغيل" },
];

const searchAccounts = (query: string, signal: AbortSignal): Promise<readonly LookupItem[]> =>
  new Promise((resolve, reject) => {
    const timer = setTimeout(() => resolve(ACCOUNTS.filter((a) => query === "" || a.code.startsWith(query) || a.name.includes(query))), 150);
    signal.addEventListener("abort", () => { clearTimeout(timer); reject(new DOMException("aborted", "AbortError")); });
  });

interface Line { account: string; note: string; debit: string; credit: string; balance: string }
const blank = (): Line => ({ account: "", note: "", debit: "", credit: "", balance: "" });
const isBlank = (l: Line) => l.account === "" && l.note === "" && l.debit === "" && l.credit === "";

export default function GalleryScreen() {
  const direction = usePreferences((s) => (s.language === "ar" ? "rtl" : "ltr"));
  const [picked, setPicked] = useState<LookupItem | null>(null);
  const [rows, setRows] = useState<Line[]>([]);
  const [big, setBig] = useState<Line[] | null>(null);

  const columns = useMemo<EditableColumn<Line>[]>(() => [
    { id: "account", header: "الحساب", width: 220, pinned: true, text: (l) => l.account, set: (l, v) => ({ ...l, account: v }) },
    { id: "note", header: "البيان", width: 260, text: (l) => l.note, set: (l, v) => ({ ...l, note: v }) },
    { id: "balance", header: "الرصيد (من الخادم)", width: 160, readOnly: true, kind: "decimal", text: (l) => l.balance },
    { id: "debit", header: "مدين", width: 140, kind: "decimal", text: (l) => (l.debit ? formatDecimal(l.debit) : ""), set: (l, v) => ({ ...l, debit: v }) },
    { id: "credit", header: "دائن", width: 140, kind: "decimal", text: (l) => (l.credit ? formatDecimal(l.credit) : ""), set: (l, v) => ({ ...l, credit: v }) },
  ], []);

  const onRows = useCallback((next: Line[]) => setRows(next), []);
  const onBig = useCallback((next: Line[]) => setBig(next), []);
  const stored = rows.filter((l) => !isBlank(l)).length;

  return (
    <section style={{ padding: "var(--space-4)", display: "flex", flexDirection: "column", gap: "var(--space-4)", height: "100%", boxSizing: "border-box" }}>
      <div style={{ maxWidth: 420 }}>
        <label>اختيار حساب (اكتب جزءاً من الرمز أو الاسم، أو * لكل الحسابات)
          <LookupPicker ariaLabel="الحساب" search={searchAccounts} value={picked} onChange={setPicked} placeholder="1101 أو الصندوق" />
        </label>
      </div>
      <div style={{ color: "var(--text-muted)" }}>
        جدول إدخال بـ50 سطراً جاهزاً — Enter/Tab للحقل التالي، الأسهم، Shift+أسهم للتحديد، Ctrl+C/V، والزر الأيمن. المحفوظ فقط: {stored} سطر.
        {" "}<button type="button" onClick={() => setBig(Array.from({ length: 5000 }, (_, i) => ({ account: `${1000 + (i % 90)}`, note: `حركة ${i + 1}`, debit: String(i + 1), credit: "", balance: "" })))}>تحميل 5000 سطر</button>
      </div>
      <div style={{ flex: 1, minHeight: 260 }}>
        <EditableGrid ariaLabel="سطور السند" columns={columns} rows={big ?? rows} onRowsChange={big ? onBig : onRows} direction={direction} isBlank={isBlank} makeBlank={blank}
          contextItems={(cell) => [{ id: "open", label: `فتح المستند الأصلي (سطر ${cell.row + 1})`, run: () => undefined }, { id: "del", label: "حذف السطر", run: () => undefined, disabled: true, reason: "السند مرحَّل" }]} />
      </div>
    </section>
  );
}
