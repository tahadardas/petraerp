/**
 * Decimal text handling for entry fields. Amounts are decimal STRINGS end to end (the API contract): this module only normalizes what the
 * user typed into the canonical wire text and formats it for display. It never converts to a binary float and never does arithmetic.
 */

export interface DecimalOptions {
  /** Maximum digits after the decimal point (the server also validates; this gives instant feedback). */
  maxScale: number;
  allowNegative: boolean;
}

export type DecimalResult =
  | { ok: true; value: string }
  | { ok: false; reason: "not-a-number" | "too-many-decimals" | "negative-not-allowed" | "ambiguous-separator" };

const ARABIC_INDIC = "٠١٢٣٤٥٦٧٨٩";
const EASTERN_ARABIC_INDIC = "۰۱۲۳۴۵۶۷۸۹";

function toAsciiDigits(text: string): string {
  let out = "";
  for (const ch of text) {
    const a = ARABIC_INDIC.indexOf(ch);
    const e = EASTERN_ARABIC_INDIC.indexOf(ch);
    out += a >= 0 ? String(a) : e >= 0 ? String(e) : ch;
  }
  return out;
}

/**
 * Canonical text for a typed amount, or why it was refused. Accepts Arabic-Indic digits, the Arabic decimal separator, and grouping
 * (space, NBSP, "٬", or "," only in clean groups of three: "1,5" is refused as ambiguous, never guessed).
 */
export function normalizeDecimalInput(text: string, options: DecimalOptions): DecimalResult {
  let value = toAsciiDigits(text).replace(/[‎‏؜]/g, "").trim();
  if (value === "") return { ok: true, value: "" };
  value = value.replace(/−/g, "-").replace(/٫/g, ".");

  let negative = false;
  if (value.startsWith("-")) {
    negative = true;
    value = value.slice(1).trimStart();
  }
  if (negative && !options.allowNegative) return { ok: false, reason: "negative-not-allowed" };

  value = value.replace(/[\s  ٬]/g, "");
  if (value.includes(",")) {
    const [integral = "", ...rest] = value.split(".");
    if (rest.length > 1) return { ok: false, reason: "not-a-number" };
    // Commas are grouping only when they form clean groups of three; anything else is ambiguous.
    if (!/^\d{1,3}(,\d{3})+$/.test(integral)) return { ok: false, reason: /^\d+(,\d+)+$/.test(integral) ? "ambiguous-separator" : "not-a-number" };
    value = integral.replace(/,/g, "") + (rest.length === 1 ? `.${rest[0]}` : "");
  }

  if (!/^\d+(\.\d+)?$/.test(value)) return { ok: false, reason: "not-a-number" };
  const scale = value.includes(".") ? (value.split(".")[1] ?? "").length : 0;
  if (scale > options.maxScale) return { ok: false, reason: "too-many-decimals" };
  return { ok: true, value: negative && /[1-9]/.test(value) ? `-${value}` : value };
}

/** Display text for a canonical decimal string: grouped integer part, the decimals exactly as stored. String work only. */
export function formatDecimal(value: string, options: { group: boolean } = { group: true }): string {
  if (!/^-?\d+(\.\d+)?$/.test(value)) return value;
  const negative = value.startsWith("-");
  const [integral = "", fraction] = (negative ? value.slice(1) : value).split(".");
  const grouped = options.group ? integral.replace(/\B(?=(\d{3})+(?!\d))/g, ",") : integral;
  return `${negative ? "-" : ""}${grouped}${fraction !== undefined ? `.${fraction}` : ""}`;
}

/** True when the canonical decimal string is zero (an empty cell counts as blank, not zero). */
export function isZeroDecimal(value: string): boolean {
  return /^-?0+(\.0+)?$/.test(value);
}
