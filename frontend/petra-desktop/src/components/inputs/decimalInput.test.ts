import { describe, expect, it } from "vitest";
import { formatDecimal, isZeroDecimal, normalizeDecimalInput } from "./decimalInput";

const money = { maxScale: 4, allowNegative: false } as const;

describe("normalizeDecimalInput", () => {
  it("normalizes Arabic-Indic digits and the Arabic decimal separator", () => {
    expect(normalizeDecimalInput("١٢٣٫٥", money)).toEqual({ ok: true, value: "123.5" });
    expect(normalizeDecimalInput("۱۰", money)).toEqual({ ok: true, value: "10" });
  });

  it("accepts clean grouping and keeps the typed decimals without rounding", () => {
    expect(normalizeDecimalInput("1,234,567.8900", money)).toEqual({ ok: true, value: "1234567.8900" });
    expect(normalizeDecimalInput("1 234", money)).toEqual({ ok: true, value: "1234" });
    expect(normalizeDecimalInput("1٬234", money)).toEqual({ ok: true, value: "1234" });
  });

  it("refuses an ambiguous comma instead of guessing", () => {
    expect(normalizeDecimalInput("1,5", money)).toEqual({ ok: false, reason: "ambiguous-separator" });
    expect(normalizeDecimalInput("12,34,567", money)).toEqual({ ok: false, reason: "ambiguous-separator" });
  });

  it("refuses too many decimals, garbage and a sign where it is not allowed", () => {
    expect(normalizeDecimalInput("1.23456", money)).toEqual({ ok: false, reason: "too-many-decimals" });
    expect(normalizeDecimalInput("12a", money)).toEqual({ ok: false, reason: "not-a-number" });
    expect(normalizeDecimalInput("1.2.3", money)).toEqual({ ok: false, reason: "not-a-number" });
    expect(normalizeDecimalInput("-5", money)).toEqual({ ok: false, reason: "negative-not-allowed" });
  });

  it("allows negatives when asked and treats an empty box as blank, not zero", () => {
    expect(normalizeDecimalInput("-12.5", { maxScale: 4, allowNegative: true })).toEqual({ ok: true, value: "-12.5" });
    expect(normalizeDecimalInput("-0", { maxScale: 4, allowNegative: true })).toEqual({ ok: true, value: "0" });
    expect(normalizeDecimalInput("   ", money)).toEqual({ ok: true, value: "" });
  });
});

describe("formatDecimal", () => {
  it("groups the integer part and keeps the decimals exactly", () => {
    expect(formatDecimal("1234567.8900")).toBe("1,234,567.8900");
    expect(formatDecimal("-1000")).toBe("-1,000");
    expect(formatDecimal("12")).toBe("12");
    expect(formatDecimal("1234", { group: false })).toBe("1234");
  });

  it("returns text that is not a canonical decimal unchanged", () => {
    expect(formatDecimal("abc")).toBe("abc");
  });
});

describe("isZeroDecimal", () => {
  it("recognizes zero in every written form but not blank", () => {
    expect(isZeroDecimal("0")).toBe(true);
    expect(isZeroDecimal("0.0000")).toBe(true);
    expect(isZeroDecimal("-0.00")).toBe(true);
    expect(isZeroDecimal("0.01")).toBe(false);
    expect(isZeroDecimal("")).toBe(false);
  });
});
