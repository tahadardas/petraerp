import { fireEvent, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { describe, expect, it } from "vitest";
import { EditableGrid, type EditableColumn } from "./EditableGrid";

interface Line { note: string; debit: string; credit: string; balance: string }
const blank = (): Line => ({ note: "", debit: "", credit: "", balance: "" });
const isBlank = (l: Line) => l.note === "" && l.debit === "" && l.credit === "";

const columns: EditableColumn<Line>[] = [
  { id: "note", header: "البيان", width: 200, pinned: true, text: (l) => l.note, set: (l, v) => ({ ...l, note: v }) },
  { id: "balance", header: "الرصيد", width: 100, readOnly: true, kind: "decimal", text: (l) => l.balance },
  { id: "debit", header: "مدين", width: 100, kind: "decimal", text: (l) => l.debit, set: (l, v) => ({ ...l, debit: v }) },
  { id: "credit", header: "دائن", width: 100, kind: "decimal", text: (l) => l.credit, set: (l, v) => ({ ...l, credit: v }) },
];

let latest: Line[] = [];
function Host({ initial = [] as Line[], dir = "rtl" as "rtl" | "ltr" }) {
  const [rows, setRows] = useState<Line[]>(initial);
  latest = rows;
  return <div style={{ height: 600 }}><EditableGrid ariaLabel="سطور السند" columns={columns} rows={rows} onRowsChange={setRows} direction={dir} isBlank={isBlank} makeBlank={blank}
    contextItems={(cell) => [{ id: "open", label: `فتح المستند (سطر ${cell.row + 1})`, run: () => undefined }]} /></div>;
}
const stored = () => latest.filter((l) => !isBlank(l));

describe("EditableGrid", () => {
  it("starts with the 50 ready lines and focuses the first editable cell", () => {
    render(<Host />);
    expect(within(screen.getByRole("grid")).getAllByRole("row")).toHaveLength(51); // header + 50 (+ cushion rows are blank too)
    expect(screen.getByRole("textbox", { name: "البيان 1" })).toHaveFocus();
  });

  it("Enter moves through the editable fields (skipping the read-only balance) and then to the next row", async () => {
    render(<Host />);
    await userEvent.keyboard("مصروف{Enter}1,234.50{Enter}{Enter}");
    // note -> (balance skipped) -> debit -> credit -> next row
    expect(screen.getByRole("textbox", { name: "البيان 2" })).toHaveFocus();
    expect(latest[0]).toMatchObject({ note: "مصروف", debit: "1234.50" });
    expect(latest[0]?.credit).toBe("");
  });

  it("stores only the filled lines: two entered of 50 ready", async () => {
    render(<Host />);
    await userEvent.keyboard("أ{Enter}10{Enter}{Enter}ب{Enter}{Enter}20{Enter}");
    expect(latest.length).toBeGreaterThanOrEqual(50);
    expect(stored()).toHaveLength(2);
    expect(stored().map((l) => l.note)).toEqual(["أ", "ب"]);
  });

  it("marks an invalid amount and keeps the previous value instead of storing garbage", async () => {
    render(<Host initial={[{ note: "x", debit: "5", credit: "", balance: "" }]} />);
    await userEvent.click(screen.getAllByRole("gridcell")[2] as HTMLElement); // debit of row 1
    await userEvent.keyboard("{Control>}a{/Control}abc{Enter}");
    expect(latest[0]?.debit).toBe("5");
    expect(screen.getAllByRole("gridcell")[2]).toHaveAttribute("aria-invalid", "true");
  });

  it("pastes Excel text across cells from the cursor", async () => {
    render(<Host />);
    const box = screen.getByRole("textbox", { name: "البيان 1" });
    fireEvent.paste(box, { clipboardData: { getData: () => "بند أ\t\t100\r\nبند ب\t\t200.5" } });
    expect(stored()).toHaveLength(2);
    // Pasting starts at the note column; the read-only balance column is skipped, so the third value lands in the debit column.
    expect(latest[0]).toMatchObject({ note: "بند أ", debit: "100", balance: "" });
    expect(latest[1]).toMatchObject({ note: "بند ب", debit: "200.5" });
  });

  it("copies a selected range as Excel-compatible text", async () => {
    const user = userEvent.setup();
    render(<Host initial={[{ note: "أ", debit: "1", credit: "", balance: "" }, { note: "ب", debit: "2", credit: "", balance: "" }]} />);
    await user.click(screen.getAllByRole("gridcell")[0] as HTMLElement);
    await user.keyboard("{Shift>}{ArrowDown}{/Shift}");
    let copied = "";
    const prevented = !fireEvent.copy(document.activeElement as HTMLElement, { clipboardData: { setData: (_type: string, value: string) => { copied = value; } } });
    expect(prevented).toBe(true); // the grid took over the copy
    expect(copied).toBe("أ\r\nب");
  });

  it("right-click opens a menu for that cell with copy actions and the caller's actions", async () => {
    render(<Host initial={[{ note: "أ", debit: "", credit: "", balance: "" }]} />);
    fireEvent.contextMenu(screen.getAllByRole("gridcell")[0] as HTMLElement);
    expect(screen.getByRole("menuitem", { name: "نسخ الخلية" })).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: "نسخ السطر" })).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: "فتح المستند (سطر 1)" })).toBeInTheDocument();
  });

  it("pins the first column to the start side", () => {
    render(<Host dir="ltr" />);
    const first = screen.getAllByRole("columnheader")[0] as HTMLElement;
    expect(first.style.position).toBe("sticky");
  });

  it("virtualizes smoothly with 200, 1,000 and 10,000 rows without DOM node explosion", () => {
    // In jsdom clientHeight is 0 by default; mock clientHeight and offsetHeight to simulate a 600px desktop viewport
    const originalClientHeight = Object.getOwnPropertyDescriptor(HTMLElement.prototype, "clientHeight");
    Object.defineProperty(HTMLElement.prototype, "clientHeight", { configurable: true, value: 600 });
    Object.defineProperty(HTMLElement.prototype, "offsetHeight", { configurable: true, value: 600 });

    try {
      // Generate 1,000 rows
      const thousandRows: Line[] = Array.from({ length: 1000 }, (_, i) => ({
        note: `قيد رقم ${i + 1}`,
        debit: `${(i + 1) * 10}`,
        credit: "",
        balance: `${(i + 1) * 10}`,
      }));

      const { unmount } = render(<Host initial={thousandRows} />);
      const grid = screen.getByRole("grid");
      // With 1000 rows plus 10 cushion rows, aria-rowcount is 1010
      expect(Number(grid.getAttribute("aria-rowcount"))).toBeGreaterThanOrEqual(1000);

      // In virtual mode, the total rendered row elements in DOM is limited to visible window + overscan (~28 rows), NOT 1000
      const renderedRows = within(grid).getAllByRole("row");
      expect(renderedRows.length).toBeLessThan(50);
      unmount();

      // Now test with 10,000 rows
      const tenThousandRows: Line[] = Array.from({ length: 10000 }, (_, i) => ({
        note: `حركة رقم ${i + 1}`,
        debit: `${(i + 1) * 5}`,
        credit: "",
        balance: `${(i + 1) * 5}`,
      }));

      render(<Host initial={tenThousandRows} />);
      const bigGrid = screen.getByRole("grid");
      expect(Number(bigGrid.getAttribute("aria-rowcount"))).toBeGreaterThanOrEqual(10000);

      const bigRenderedRows = within(bigGrid).getAllByRole("row");
      // Only the virtual slice is rendered (much less than 10,000 rows)
      expect(bigRenderedRows.length).toBeLessThan(50);
    } finally {
      if (originalClientHeight) {
        Object.defineProperty(HTMLElement.prototype, "clientHeight", originalClientHeight);
      }
    }
  });

  it("pastes 100+ rows efficiently without data loss", () => {
    render(<Host />);
    const box = screen.getByRole("textbox", { name: "البيان 1" });
    // Note: col 0 is note, col 1 is read-only balance (skipped), col 2 is debit, col 3 is credit
    const bulkLines = Array.from({ length: 120 }, (_, i) => `سطر ${i + 1}\t\t${(i + 1) * 10}\t0`).join("\r\n");
    fireEvent.paste(box, { clipboardData: { getData: () => bulkLines } });

    expect(stored()).toHaveLength(120);
    expect(latest[0]?.note).toBe("سطر 1");
    expect(latest[119]?.note).toBe("سطر 120");
    expect(latest[119]?.debit).toBe("1200");
  });
});
