import { describe, expect, it } from "vitest";
import { parseTsv, pasteEdits, rangeToTsv, toTsv } from "./clipboard";

describe("clipboard TSV", () => {
  it("writes tabs between cells and CRLF between rows", () => {
    expect(toTsv([["a", "b"], ["c", "d"]])).toBe("a\tb\r\nc\td");
  });

  it("quotes cells with tabs, quotes or line breaks and round-trips them (including Arabic text)", () => {
    const matrix = [["بيان\tبتبويب", 'قال "نعم"'], ["سطر\nثان", "1,234.5000"]];
    expect(parseTsv(toTsv(matrix))).toEqual(matrix);
  });

  it("reads plain Excel text and ignores a trailing line break", () => {
    expect(parseTsv("1\t2\r\n3\t4\r\n")).toEqual([["1", "2"], ["3", "4"]]);
    expect(parseTsv("x")).toEqual([["x"]]);
    expect(parseTsv("")).toEqual([]);
  });

  it("keeps empty cells in place", () => {
    expect(parseTsv("a\t\tc")).toEqual([["a", "", "c"]]);
  });

  it("copies a rectangular selection with the text the user sees, whichever corner it was dragged from", () => {
    const grid = [["a0", "b0", "c0"], ["a1", "b1", "c1"], ["a2", "b2", "c2"]];
    const text = rangeToTsv({ fromRow: 2, toRow: 1, fromCol: 1, toCol: 0 }, (r, c) => grid[r]?.[c] ?? "");
    expect(text).toBe("a1\tb1\r\na2\tb2");
  });
});

describe("pasteEdits", () => {
  it("lands each pasted cell from the cursor, drops cells past the last column and reports the rows needed", () => {
    const result = pasteEdits("1\t2\t3\r\n4\t5\t6", { row: 3, col: 1 }, 3);
    expect(result.edits).toEqual([
      { row: 3, col: 1, text: "1" }, { row: 3, col: 2, text: "2" },
      { row: 4, col: 1, text: "4" }, { row: 4, col: 2, text: "5" },
    ]);
    expect(result.rowsNeeded).toBe(5);
  });
});
