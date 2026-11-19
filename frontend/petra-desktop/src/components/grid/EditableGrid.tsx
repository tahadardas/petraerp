import { useVirtualizer } from "@tanstack/react-virtual";
import { useEffect, useMemo, useRef, useState, type ClipboardEvent, type KeyboardEvent } from "react";
import { useContextMenu, type ContextMenuItem } from "@/components/menu/ContextMenu";
import { normalizeDecimalInput } from "@/components/inputs/decimalInput";
import { pasteEdits, rangeToTsv, type CellRange } from "./clipboard";
import { ensureReadyRows, moveCell, rowsToAdd, type Cell, type Direction } from "./gridModel";

export interface EditableColumn<T> {
  id: string;
  header: string;
  width: number;
  kind?: "text" | "decimal";
  readOnly?: boolean;
  /** Pinned columns stay visible at the start side while the rest scrolls. */
  pinned?: boolean;
  /** What the user sees in the cell (formatted by the caller from server data). */
  text: (row: T) => string;
  /** Returns the row with the cell edited. For decimal columns `value` is already the canonical decimal string. */
  set?: (row: T, value: string) => T;
  maxScale?: number;
}

export interface EditableGridProps<T> {
  columns: readonly EditableColumn<T>[];
  rows: readonly T[];
  onRowsChange: (rows: T[]) => void;
  direction: Direction;
  isBlank: (row: T) => boolean;
  makeBlank: (index: number) => T;
  /** The ready lines shown from the start (50 for invoices and vouchers by the approved setting). */
  readyRows?: number;
  /** Above this many rows only the visible ones are rendered. */
  virtualizeAbove?: number;
  rowHeight?: number;
  ariaLabel: string;
  /** Extra context actions for a cell (open source document, delete row...). Copy actions are always present. */
  contextItems?: (cell: Cell, row: T) => readonly ContextMenuItem[];
}

const CUSHION = 10;

/**
 * Keyboard-first entry grid: the active cell is an input; Enter/Tab/arrows move by the accounting entry order and the visual direction,
 * rows grow automatically, Shift+arrows select a range, Ctrl+C/V copy and paste Excel-compatible text, right-click opens a menu for the
 * cell. Blank ready lines are a screen convenience: callers store only `rows.filter(!isBlank)`.
 */
export function EditableGrid<T>(props: EditableGridProps<T>) {
  const { columns, rows, onRowsChange, direction, isBlank, makeBlank, readyRows = 50, virtualizeAbove = 200, rowHeight = 32, ariaLabel, contextItems } = props;
  const [active, setActive] = useState<Cell>({ row: 0, col: Math.max(columns.findIndex((c) => !c.readOnly), 0) });
  const [anchor, setAnchor] = useState<Cell | null>(null);
  const [invalid, setInvalid] = useState<ReadonlySet<string>>(new Set());
  const scroller = useRef<HTMLDivElement>(null);

  // The 50 ready lines (and growth while entering): added once, never removed.
  useEffect(() => {
    const grown = ensureReadyRows(rows, readyRows, makeBlank);
    const more = rowsToAdd(grown, isBlank as (row: never) => boolean, CUSHION);
    const next = more > 0 ? ensureReadyRows(grown, grown.length + more, makeBlank) : grown;
    if (next.length !== rows.length) onRowsChange(next);
  }, [rows, readyRows, makeBlank, isBlank, onRowsChange]);

  const virtual = rows.length > virtualizeAbove;
  const virtualizer = useVirtualizer({ count: virtual ? rows.length : 0, getScrollElement: () => scroller.current, estimateSize: () => rowHeight, overscan: 8, initialRect: { width: 1200, height: 640 } });
  const indexes = virtual ? virtualizer.getVirtualItems().map((v) => v.index) : rows.map((_, i) => i);

  const offsets = useMemo(() => {
    let acc = 0;
    return columns.map((c) => { const start = acc; acc += c.width; return c.pinned ? start : null; });
  }, [columns]);

  const select = (cell: Cell, extend: boolean) => {
    setAnchor(extend ? (anchor ?? active) : null);
    setActive(cell);
    if (virtual) virtualizer.scrollToIndex(cell.row);
  };

  const commit = (cell: Cell, text: string) => {
    const column = columns[cell.col];
    const row = rows[cell.row];
    if (!column?.set || row === undefined) return;
    const key = `${cell.row}:${cell.col}`;
    let value = text;
    if (column.kind === "decimal") {
      const result = normalizeDecimalInput(text, { maxScale: column.maxScale ?? 4, allowNegative: false });
      if (!result.ok) { setInvalid((s) => new Set(s).add(key)); return; }
      value = result.value;
    }
    setInvalid((s) => { if (!s.has(key)) return s; const n = new Set(s); n.delete(key); return n; });
    if (column.text(row) !== value) onRowsChange(rows.map((r, i) => (i === cell.row ? column.set!(r, value) : r)));
  };

  const range = (): CellRange => ({ fromRow: (anchor ?? active).row, toRow: active.row, fromCol: (anchor ?? active).col, toCol: active.col });

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    const keys = ["Enter", "Tab", "ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight"];
    if (!keys.includes(event.key)) return;
    // Plain Left/Right inside the text move the caret; only at the text boundary (or with Shift) do they move cells.
    if ((event.key === "ArrowLeft" || event.key === "ArrowRight") && !event.shiftKey) {
      const el = event.currentTarget;
      const atEnd = el.selectionStart === el.value.length && el.selectionEnd === el.value.length;
      const atStart = el.selectionStart === 0 && el.selectionEnd === 0;
      const towardEnd = (event.key === "ArrowRight") === (direction === "ltr");
      if (!(towardEnd ? atEnd : atStart)) return;
    }
    event.preventDefault();
    commit(active, event.currentTarget.value);
    const move = moveCell(active, event.key, event.key === "Tab" || event.key === "Enter" ? event.shiftKey : false, columns, rows.length, direction);
    if (move.grow) onRowsChange(ensureReadyRows(rows, rows.length + CUSHION, makeBlank));
    select(move.cell, event.shiftKey && event.key.startsWith("Arrow"));
  };

  const onCopy = (event: ClipboardEvent) => {
    if (!anchor) return; // a plain copy inside the input copies the selected text
    event.preventDefault();
    event.clipboardData.setData("text/plain", rangeToTsv(range(), (r, c) => { const row = rows[r]; const col = columns[c]; return row !== undefined && col ? col.text(row) : ""; }));
  };

  const onPaste = (event: ClipboardEvent) => {
    const text = event.clipboardData.getData("text/plain");
    if (!/[\t\r\n]/.test(text)) return; // a single value pastes into the input normally
    event.preventDefault();
    const { edits, rowsNeeded } = pasteEdits(text, active, columns.length);
    let next = rowsNeeded > rows.length ? ensureReadyRows(rows, rowsNeeded + CUSHION, makeBlank) : [...rows];
    for (const edit of edits) {
      const column = columns[edit.col];
      const row = next[edit.row];
      if (!column?.set || column.readOnly || row === undefined) continue;
      const value = column.kind === "decimal" ? normalizeDecimalInput(edit.text, { maxScale: column.maxScale ?? 4, allowNegative: false }) : { ok: true as const, value: edit.text };
      if (value.ok) next[edit.row] = column.set(row, value.value);
    }
    onRowsChange(next);
  };

  const menu = useContextMenu<Cell>((cell) => {
    const row = rows[cell.row];
    if (row === undefined) return [];
    const copyCell: ContextMenuItem = { id: "copy-cell", label: "نسخ الخلية", run: () => void navigator.clipboard?.writeText(columns[cell.col]?.text(row) ?? "") };
    const copyRow: ContextMenuItem = { id: "copy-row", label: "نسخ السطر", run: () => void navigator.clipboard?.writeText(rangeToTsv({ fromRow: cell.row, toRow: cell.row, fromCol: 0, toCol: columns.length - 1 }, (_r, c) => columns[c]?.text(row) ?? "")) };
    return [copyCell, copyRow, ...(contextItems ? contextItems(cell, row).map((i, n) => (n === 0 ? { ...i, separatorBefore: true } : i)) : [])];
  });

  // The active cell's input takes focus the moment it mounts (also when rows are added after the first render).
  const focusOnMount = (element: HTMLInputElement | null) => { element?.focus(); };

  const total = columns.reduce((n, c) => n + c.width, 0);
  const inRange = (r: number, c: number) => anchor !== null && r >= Math.min(anchor.row, active.row) && r <= Math.max(anchor.row, active.row) && c >= Math.min(anchor.col, active.col) && c <= Math.max(anchor.col, active.col);

  return (
    <div ref={scroller} role="grid" aria-label={ariaLabel} aria-rowcount={rows.length} dir={direction} onCopy={onCopy} onPaste={onPaste}
      style={{ overflow: "auto", height: "100%", background: "var(--bg-input)", border: "1px solid var(--border)" }}>
      <div role="row" style={{ display: "flex", position: "sticky", top: 0, zIndex: 3, width: total, background: "var(--bg-sunken)" }}>
        {columns.map((c, i) => (
          <div key={c.id} role="columnheader" style={{ width: c.width, flex: "none", padding: "0 var(--space-2)", height: rowHeight, lineHeight: `${rowHeight}px`, fontWeight: 600, boxSizing: "border-box", borderBottom: "1px solid var(--border)", position: offsets[i] !== null ? "sticky" : "static", insetInlineStart: offsets[i] ?? undefined, background: "var(--bg-sunken)", zIndex: offsets[i] !== null ? 2 : 0 }}>{c.header}</div>
        ))}
      </div>
      <div style={{ position: "relative", width: total, height: virtual ? virtualizer.getTotalSize() : undefined }}>
        {indexes.map((r) => {
          const row = rows[r];
          if (row === undefined) return null;
          return (
            <div key={r} role="row" aria-rowindex={r + 1} style={{ display: "flex", height: rowHeight, width: total, background: r % 2 ? "var(--bg-row-alt)" : "var(--bg-input)", ...(virtual ? { position: "absolute", top: r * rowHeight } : {}) }}>
              {columns.map((c, i) => {
                const isActive = active.row === r && active.col === i;
                const key = `${r}:${i}`;
                return (
                  <div key={c.id} role="gridcell" aria-selected={isActive || inRange(r, i)} aria-invalid={invalid.has(key)}
                    onMouseDown={(e) => {
                      // The browser would move focus to the page after this click; the new active cell's input takes it instead.
                      // Clicks inside the active input keep their native behaviour (caret placement, text selection).
                      if (!(e.target instanceof HTMLInputElement)) e.preventDefault();
                      select({ row: r, col: i }, e.shiftKey);
                    }} onContextMenu={(e) => menu.onContextMenu(e, { row: r, col: i })}
                    style={{ width: c.width, flex: "none", boxSizing: "border-box", padding: "0 var(--space-2)", borderBottom: "1px solid var(--divider)", borderInlineEnd: "1px solid var(--divider)", display: "flex", alignItems: "center", position: offsets[i] !== null ? "sticky" : "static", insetInlineStart: offsets[i] ?? undefined, background: inRange(r, i) ? "var(--bg-sunken)" : "inherit", outline: isActive ? "2px solid var(--focus)" : invalid.has(key) ? "2px solid var(--danger)" : undefined, outlineOffset: -2, zIndex: offsets[i] !== null ? 1 : 0 }}>
                    {isActive && !c.readOnly ? (
                      <input ref={focusOnMount} aria-label={`${c.header} ${r + 1}`} defaultValue={c.text(row)} key={`${r}:${i}:${c.text(row)}`} onKeyDown={onKeyDown} onBlur={(e) => commit({ row: r, col: i }, e.target.value)}
                        className={c.kind === "decimal" ? "numeric" : undefined} style={{ width: "100%", border: 0, background: "transparent", color: "var(--text)", font: "inherit", outline: "none" }} />
                    ) : (
                      <span className={c.kind === "decimal" ? "numeric" : undefined} style={{ width: "100%", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{c.text(row)}</span>
                    )}
                  </div>
                );
              })}
            </div>
          );
        })}
      </div>
      {menu.menu}
    </div>
  );
}
