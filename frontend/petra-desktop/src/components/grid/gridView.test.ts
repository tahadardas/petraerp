import { describe, expect, it } from "vitest";
import { compareDecimal, compareText, filterRows, groupRows, sortRows } from "./gridView";

describe("compareDecimal (no floating point)", () => {
  it("orders by sign, magnitude and decimals", () => {
    expect(compareDecimal("2", "10")).toBeLessThan(0);
    expect(compareDecimal("-5", "3")).toBeLessThan(0);
    expect(compareDecimal("-5", "-3")).toBeLessThan(0);
    expect(compareDecimal("0.10", "0.1")).toBe(0);
    expect(compareDecimal("1.0005", "1.0004")).toBeGreaterThan(0);
    expect(compareDecimal("999999999999999.9999", "999999999999999.9998")).toBeGreaterThan(0); // beyond double precision
    expect(compareDecimal("-0", "0")).toBe(0);
  });
});

describe("sortRows", () => {
  it("is stable: equal rows keep their original order", () => {
    const rows = [{ n: "b", i: 1 }, { n: "a", i: 2 }, { n: "b", i: 3 }, { n: "a", i: 4 }];
    expect(sortRows(rows, { compare: (x, y) => compareText(x.n, y.n), direction: "asc" }).map((r) => r.i)).toEqual([2, 4, 1, 3]);
    expect(sortRows(rows, { compare: (x, y) => compareText(x.n, y.n), direction: "desc" }).map((r) => r.i)).toEqual([1, 3, 2, 4]);
  });

  it("sorts numbers inside text naturally and Arabic text without crashing", () => {
    expect(["حساب 10", "حساب 2", "حساب 1"].sort(compareText)).toEqual(["حساب 1", "حساب 2", "حساب 10"]);
  });

  it("returns a copy in the original order when there is no sort", () => {
    const rows = [3, 1, 2];
    const copy = sortRows(rows, null);
    expect(copy).toEqual([3, 1, 2]);
    expect(copy).not.toBe(rows);
  });
});

describe("filter and group", () => {
  it("applies every predicate", () => {
    expect(filterRows([1, 2, 3, 4, 5, 6], [(n) => n % 2 === 0, (n) => n > 2])).toEqual([4, 6]);
  });

  it("groups in first-appearance order and keeps rows inside a group in order", () => {
    const groups = groupRows([{ k: "x", v: 1 }, { k: "y", v: 2 }, { k: "x", v: 3 }], (r) => r.k);
    expect(groups.map((g) => g.key)).toEqual(["x", "y"]);
    expect(groups[0]?.rows.map((r) => r.v)).toEqual([1, 3]);
  });
});
