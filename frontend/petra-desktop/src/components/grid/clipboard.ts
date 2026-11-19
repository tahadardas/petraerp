/** Clipboard text for grids and reports in the format Excel pastes into cells: tab between cells, newline between rows, quoted when needed. */

const needsQuoting = (cell: string) => /["\t\r\n]/.test(cell);

export function toTsv(matrix: readonly (readonly string[])[]): string {
  return matrix.map((row) => row.map((cell) => (needsQuoting(cell) ? `"${cell.replace(/"/g, '""')}"` : cell)).join("\t")).join("\r\n");
}

/** Inverse of `toTsv` (also reads plain Excel/browser clipboard text). A trailing line break does not add an empty row. */
export function parseTsv(text: string): string[][] {
  const rows: string[][] = [];
  let row: string[] = [];
  let cell = "";
  let inQuotes = false;
  let touched = false;
  for (let i = 0; i < text.length; i++) {
    const ch = text[i] as string;
    if (inQuotes) {
      if (ch === '"') {
        if (text[i + 1] === '"') { cell += '"'; i++; } else inQuotes = false;
      } else cell += ch;
      continue;
    }
    if (ch === '"' && cell === "") { inQuotes = true; touched = true; }
    else if (ch === "\t") { row.push(cell); cell = ""; touched = true; }
    else if (ch === "\r" || ch === "\n") {
      if (ch === "\r" && text[i + 1] === "\n") i++;
      row.push(cell); rows.push(row); row = []; cell = ""; touched = false;
    } else { cell += ch; touched = true; }
  }
  if (touched || cell !== "" || row.length > 0) { row.push(cell); rows.push(row); }
  return rows;
}

export interface CellRange {
  fromRow: number;
  toRow: number;
  fromCol: number;
  toCol: number;
}

/** The text of a rectangular selection: `cellText(row, col)` is what the user SEES in the cell (server-provided display text). */
export function rangeToTsv(range: CellRange, cellText: (row: number, col: number) => string): string {
  const matrix: string[][] = [];
  for (let r = Math.min(range.fromRow, range.toRow); r <= Math.max(range.fromRow, range.toRow); r++) {
    const line: string[] = [];
    for (let c = Math.min(range.fromCol, range.toCol); c <= Math.max(range.fromCol, range.toCol); c++) line.push(cellText(r, c));
    matrix.push(line);
  }
  return toTsv(matrix);
}

export interface PasteEdit {
  row: number;
  col: number;
  text: string;
}

/** Where each pasted cell lands, starting at the cursor. Cells past the last column are dropped; rows past the end are reported via `rowsNeeded`. */
export function pasteEdits(text: string, start: { row: number; col: number }, columnCount: number): { edits: PasteEdit[]; rowsNeeded: number } {
  const matrix = parseTsv(text);
  const edits: PasteEdit[] = [];
  matrix.forEach((line, r) => line.forEach((cell, c) => {
    if (start.col + c < columnCount) edits.push({ row: start.row + r, col: start.col + c, text: cell });
  }));
  return { edits, rowsNeeded: start.row + matrix.length };
}
