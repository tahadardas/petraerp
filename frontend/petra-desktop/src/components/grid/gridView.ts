/** Sorting, filtering and grouping of already-loaded rows. Display helpers only: totals and balances always come from the API. */

export type SortDirection = "asc" | "desc";

export interface SortSpec<T> {
  compare: (a: T, b: T) => number;
  direction: SortDirection;
}

/** Stable sort: rows that compare equal keep their original (e.g. chronological ledger) order. */
export function sortRows<T>(rows: readonly T[], spec: SortSpec<T> | null): T[] {
  if (!spec) return [...rows];
  const sign = spec.direction === "asc" ? 1 : -1;
  return rows.map((row, index) => ({ row, index })).sort((x, y) => spec.compare(x.row, y.row) * sign || x.index - y.index).map((x) => x.row);
}

/** Text comparison that is correct for Arabic and mixed text (locale-aware, numeric-aware: "2" before "10"). */
export const compareText = (a: string, b: string, locale = "ar"): number => a.localeCompare(b, locale, { numeric: true, sensitivity: "base" });

/** Canonical decimal strings compared without floating point: sign, integer length, then digit by digit. */
export function compareDecimal(a: string, b: string): number {
  const parse = (v: string) => {
    const negative = v.startsWith("-");
    const [i = "0", f = ""] = (negative ? v.slice(1) : v).split(".");
    return { negative: negative && /[1-9]/.test(v), i: i.replace(/^0+(?=\d)/, ""), f: f.replace(/0+$/, "") };
  };
  const x = parse(a);
  const y = parse(b);
  if (x.negative !== y.negative) return x.negative ? -1 : 1;
  const magnitude = x.i.length !== y.i.length ? x.i.length - y.i.length : x.i < y.i ? -1 : x.i > y.i ? 1 : x.f.padEnd(y.f.length, "0") < y.f.padEnd(x.f.length, "0") ? -1 : x.f.padEnd(y.f.length, "0") > y.f.padEnd(x.f.length, "0") ? 1 : 0;
  return x.negative ? -magnitude : magnitude;
}

export function filterRows<T>(rows: readonly T[], predicates: readonly ((row: T) => boolean)[]): T[] {
  return predicates.length === 0 ? [...rows] : rows.filter((row) => predicates.every((p) => p(row)));
}

export interface RowGroup<T> {
  key: string;
  rows: T[];
}

/** Groups keep first-appearance order; a group header may show its row COUNT, never a financial total (totals come from the server). */
export function groupRows<T>(rows: readonly T[], keyOf: (row: T) => string): RowGroup<T>[] {
  const groups = new Map<string, T[]>();
  for (const row of rows) {
    const key = keyOf(row);
    const bucket = groups.get(key);
    if (bucket) bucket.push(row);
    else groups.set(key, [row]);
  }
  return [...groups].map(([key, bucket]) => ({ key, rows: bucket }));
}
