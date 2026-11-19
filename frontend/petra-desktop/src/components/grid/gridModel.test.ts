import { describe, expect, it } from "vitest";
import { ensureReadyRows, moveCell, rowsToAdd, storableRows, type GridColumn } from "./gridModel";

const columns: GridColumn[] = [{ id: "account" }, { id: "balance", readOnly: true }, { id: "debit" }, { id: "credit" }];

describe("moveCell: entry order", () => {
  it("Enter walks the editable fields of a row, skipping read-only columns", () => {
    expect(moveCell({ row: 0, col: 0 }, "Enter", false, columns, 5, "rtl").cell).toEqual({ row: 0, col: 2 });
    expect(moveCell({ row: 0, col: 2 }, "Enter", false, columns, 5, "rtl").cell).toEqual({ row: 0, col: 3 });
  });

  it("Enter at the last field goes to the first editable field of the next row", () => {
    expect(moveCell({ row: 0, col: 3 }, "Enter", false, columns, 5, "ltr")).toEqual({ cell: { row: 1, col: 0 }, grow: false });
  });

  it("Enter at the end of the last row asks the grid to add a row", () => {
    expect(moveCell({ row: 4, col: 3 }, "Enter", false, columns, 5, "ltr")).toEqual({ cell: { row: 5, col: 0 }, grow: true });
  });

  it("Shift+Enter walks backwards, across rows too, and stops at the very first cell", () => {
    expect(moveCell({ row: 1, col: 0 }, "Enter", true, columns, 5, "ltr").cell).toEqual({ row: 0, col: 3 });
    expect(moveCell({ row: 0, col: 2 }, "Tab", true, columns, 5, "ltr").cell).toEqual({ row: 0, col: 0 });
    expect(moveCell({ row: 0, col: 0 }, "Enter", true, columns, 5, "ltr").cell).toEqual({ row: 0, col: 0 });
  });
});

describe("moveCell: arrows follow the visual direction", () => {
  it("in RTL the first column is on the right: ArrowLeft goes to the next column, ArrowRight to the previous", () => {
    expect(moveCell({ row: 0, col: 1 }, "ArrowLeft", false, columns, 3, "rtl").cell.col).toBe(2);
    expect(moveCell({ row: 0, col: 1 }, "ArrowRight", false, columns, 3, "rtl").cell.col).toBe(0);
  });

  it("in LTR it is the opposite", () => {
    expect(moveCell({ row: 0, col: 1 }, "ArrowRight", false, columns, 3, "ltr").cell.col).toBe(2);
    expect(moveCell({ row: 0, col: 1 }, "ArrowLeft", false, columns, 3, "ltr").cell.col).toBe(0);
  });

  it("arrows clamp at the edges and up/down move rows", () => {
    expect(moveCell({ row: 0, col: 0 }, "ArrowRight", false, columns, 3, "rtl").cell.col).toBe(0);
    expect(moveCell({ row: 2, col: 0 }, "ArrowDown", false, columns, 3, "ltr").cell.row).toBe(2);
    expect(moveCell({ row: 0, col: 0 }, "ArrowUp", false, columns, 3, "ltr").cell.row).toBe(0);
    expect(moveCell({ row: 1, col: 2 }, "Home", false, columns, 3, "ltr").cell.col).toBe(0);
    expect(moveCell({ row: 1, col: 2 }, "End", false, columns, 3, "ltr").cell.col).toBe(3);
  });
});

describe("ready rows", () => {
  it("pads to the target count with blanks and never removes existing rows", () => {
    const rows = ensureReadyRows([{ n: "a" }, { n: "b" }], 50, () => ({ n: "" }));
    expect(rows).toHaveLength(50);
    expect(rows[1]?.n).toBe("b");
    expect(ensureReadyRows(rows, 10, () => ({ n: "" }))).toHaveLength(50);
  });

  it("stores only filled rows: two entered lines out of 50 ready lines are two stored lines", () => {
    const rows = ensureReadyRows([{ n: "a" }, { n: "b" }], 50, () => ({ n: "" }));
    expect(storableRows(rows, (r) => r.n === "")).toHaveLength(2);
  });

  it("auto-grow keeps a cushion of blank rows below the last filled one", () => {
    const rows = [{ n: "a" }, { n: "b" }, { n: "" }, { n: "" }];
    expect(rowsToAdd(rows, (r: { n: string }) => r.n === "", 10)).toBe(8);
    expect(rowsToAdd(ensureReadyRows(rows, 50, () => ({ n: "" })), (r: { n: string }) => r.n === "", 10)).toBe(0);
  });
});
