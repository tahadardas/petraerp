import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { LookupPicker, type LookupItem } from "./LookupPicker";

const accounts: LookupItem[] = [
  { id: "1", code: "4101", name: "المبيعات" },
  { id: "2", code: "1104", name: "المخزون" },
  { id: "3", code: "1101", name: "الصندوق" },
];

function harness(search: (q: string, s: AbortSignal) => Promise<readonly LookupItem[]>) {
  const changes: (LookupItem | null)[] = [];
  function Host() {
    const [value, setValue] = useState<LookupItem | null>(null);
    return <LookupPicker ariaLabel="الحساب" search={search} value={value} debounceMs={0} onChange={(i) => { changes.push(i); setValue(i); }} />;
  }
  render(<Host />);
  return changes;
}

const filtering = (q: string) => Promise.resolve(accounts.filter((a) => q === "" || a.code.startsWith(q) || a.name.includes(q)));

describe("LookupPicker", () => {
  it("shows matches as the user types and commits the highlighted one with Enter", async () => {
    const changes = harness(filtering);
    await userEvent.type(screen.getByRole("combobox", { name: "الحساب" }), "11");
    expect(await screen.findAllByRole("option")).toHaveLength(2);
    await userEvent.keyboard("{ArrowDown}{Enter}");
    expect(changes.at(-1)?.code).toBe("1101");
    expect(screen.getByRole("combobox")).toHaveValue("1101 الصندوق");
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
  });

  it("opens the full list with * and with Enter on an empty box", async () => {
    harness(filtering);
    const box = screen.getByRole("combobox");
    await userEvent.type(box, "*");
    expect(await screen.findAllByRole("option")).toHaveLength(3);
    await userEvent.keyboard("{Escape}");
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
    await userEvent.click(box);
    await userEvent.keyboard("{Enter}");
    expect(await screen.findAllByRole("option")).toHaveLength(3);
  });

  it("aborts the previous request when a newer keystroke arrives and ignores its late answer", async () => {
    const signals: AbortSignal[] = [];
    let release!: () => void;
    const slow = new Promise<readonly LookupItem[]>((resolve) => { release = () => resolve([accounts[0] as LookupItem]); });
    const search = vi.fn((q: string, signal: AbortSignal) => {
      signals.push(signal);
      return q === "4" ? slow : Promise.resolve([accounts[2] as LookupItem]);
    });
    harness(search);
    await userEvent.type(screen.getByRole("combobox"), "41");
    await waitFor(() => expect(signals).toHaveLength(2));
    expect(signals[0]?.aborted).toBe(true);
    release(); // the stale answer for "4" arrives late
    expect(await screen.findByRole("option", { name: /1101/ })).toBeInTheDocument();
    expect(screen.queryByRole("option", { name: /4101/ })).not.toBeInTheDocument();
  });

  it("selecting by mouse works and typing again un-selects the committed value", async () => {
    const changes = harness(filtering);
    const box = screen.getByRole("combobox");
    await userEvent.type(box, "4");
    await userEvent.click(await screen.findByRole("option", { name: /4101/ }));
    expect(changes.at(-1)?.id).toBe("1");
    await userEvent.type(box, "x");
    expect(changes.at(-1)).toBeNull();
  });

  it("does nothing when read only", async () => {
    const search = vi.fn(filtering);
    render(<LookupPicker ariaLabel="x" search={search} value={null} onChange={() => undefined} readOnly />);
    await userEvent.type(screen.getByRole("combobox"), "11");
    expect(search).not.toHaveBeenCalled();
  });
});
