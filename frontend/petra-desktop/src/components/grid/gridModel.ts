/** Pure keyboard/row logic of the entry grid, kept free of React so it is fully testable. */

export type Direction = "rtl" | "ltr";

export interface GridColumn {
  id: string;
  /** Read-only columns are skipped by Enter/Tab entry flow (a real business reason, e.g. a computed total from the server). */
  readOnly?: boolean;
  /** Pinned (sticky) to the start side while scrolling. */
  pinned?: boolean;
}

export interface Cell {
  row: number;
  col: number;
}

export interface MoveResult {
  cell: Cell;
  /** True when the move went past the last row: the grid must add rows (auto-grow) before focusing it. */
  grow: boolean;
}

const editable = (columns: readonly GridColumn[], col: number) => columns[col] !== undefined && columns[col].readOnly !== true;

/**
 * Where the cursor goes for a key. Accounting entry order: Enter/Tab walk the editable fields of a row, then the next row; Shift reverses.
 * Arrow keys follow the VISUAL direction: in RTL the first column is on the right, so ArrowRight moves toward it.
 */
export function moveCell(current: Cell, key: string, shift: boolean, columns: readonly GridColumn[], rows: number, direction: Direction): MoveResult {
  const last = columns.length - 1;
  const stay: MoveResult = { cell: current, grow: false };
  if (columns.length === 0 || rows === 0) return stay;

  const forward = (from: Cell): MoveResult => {
    for (let col = from.col + 1; col <= last; col++) if (editable(columns, col)) return { cell: { row: from.row, col }, grow: false };
    const first = columns.findIndex((_, i) => editable(columns, i));
    if (first < 0) return stay;
    return from.row + 1 >= rows ? { cell: { row: from.row + 1, col: first }, grow: true } : { cell: { row: from.row + 1, col: first }, grow: false };
  };
  const backward = (from: Cell): MoveResult => {
    for (let col = from.col - 1; col >= 0; col--) if (editable(columns, col)) return { cell: { row: from.row, col }, grow: false };
    if (from.row === 0) return stay;
    let lastEditable = -1;
    for (let col = last; col >= 0; col--) if (editable(columns, col)) { lastEditable = col; break; }
    return lastEditable < 0 ? stay : { cell: { row: from.row - 1, col: lastEditable }, grow: false };
  };

  switch (key) {
    case "Enter":
    case "Tab":
      return shift ? backward(current) : forward(current);
    case "ArrowDown":
      return { cell: { row: Math.min(current.row + 1, rows - 1), col: current.col }, grow: false };
    case "ArrowUp":
      return { cell: { row: Math.max(current.row - 1, 0), col: current.col }, grow: false };
    case "ArrowRight":
    case "ArrowLeft": {
      const towardStart = (key === "ArrowRight") === (direction === "rtl");
      const step = towardStart ? -1 : 1;
      const col = Math.min(Math.max(current.col + step, 0), last);
      return { cell: { row: current.row, col }, grow: false };
    }
    case "Home":
      return { cell: { row: current.row, col: 0 }, grow: false };
    case "End":
      return { cell: { row: current.row, col: last }, grow: false };
    default:
      return stay;
  }
}

/** Pads with blank rows up to `target` (e.g. the 50 ready lines); never removes rows the user already has. */
export function ensureReadyRows<T>(rows: readonly T[], target: number, makeBlank: (index: number) => T): T[] {
  const result = [...rows];
  for (let i = result.length; i < target; i++) result.push(makeBlank(i));
  return result;
}

/** Only rows with data are stored or sent: the 50 ready lines are a screen convenience, not data. */
export function storableRows<T>(rows: readonly T[], isBlank: (row: T) => boolean): T[] {
  return rows.filter((row) => !isBlank(row));
}

/** How many blank rows stay below the last filled one when the user keeps entering (auto-grow keeps a cushion). */
export function rowsToAdd(rows: readonly unknown[], isBlank: (row: never) => boolean, cushion: number): number {
  let lastFilled = -1;
  rows.forEach((row, index) => {
    if (!isBlank(row as never)) lastFilled = index;
  });
  const blanksBelow = rows.length - 1 - lastFilled;
  return Math.max(0, cushion - blanksBelow);
}
